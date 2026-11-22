# 24: Columnar Storage and Lakehouse Pipelines (Parquet, Delta Lake, and Iceberg)

In Module 06 we examined the two engines that power operational (`OLTP`) databases: **B+Trees** (which update 8 KB rows in place for fast point reads and range transactions) and **LSM-Trees** (which append key-value mutations into sequential SSTables for high write throughput).
Both B+Trees and standard LSM-Trees are **row-oriented**: all columns of a single record (`EventId, Timestamp, TenantId, DeviceId, EventType, CpuPct, MemoryMb, StackTraceJson, ...`) are stored contiguously next to each other on disk.

Row-oriented storage is unbeatable when your query asks for **all columns of one specific entity** (`SELECT * FROM DeviceTwins WHERE DeviceId = 'dev-42'`).
Wait until a security analyst, a data-lake aggregation pipeline, or a compliance dashboard runs an **analytical (`OLAP`) query** across **50 billion security and telemetry events** (`100 TB` on disk):
```sql
SELECT tenant_id, COUNT(*), AVG(latency_ms)
FROM security_audit_events
WHERE event_date = '2026-11-21' AND event_type = 'SignInFailure'
GROUP BY tenant_id;
```
Notice that this query touches only **3 columns** (`tenant_id`, `event_type`, `latency_ms`, totaling `24 bytes` per row) out of a wide **50-column telemetry schema** (`2,000 bytes` per row).
If that 100 TB table is stored in a row-oriented format, the storage engine must read and decompress **all 2,000 bytes of every single row (`100 TB` of disk I/O)** just to extract `24 bytes` per row, wasting **98.8% of disk bandwidth** on 47 columns the query immediately throws away!

This module explains how **Column-Oriented Storage (`Apache Parquet` and `Apache ORC`)** eliminates that 98.8% I/O waste, how **Columnar Encodings (`Dictionary`, `Run-Length Encoding [RLE]`, `Bit-Packing`, `Delta`)** and **Dremel Nested Repetition/Definition Levels** compress petabytes of nested JSON/Protobuf events by \(10\times\), how **Row-Group Min/Max Statistics and Bloom Filter Predicate Pushdown** skip 99% of disk blocks without decompressing a single byte, how streaming pipelines cure the **Small-File Problem**, and how **Modern Lakehouse Table Formats (`Delta Lake` and `Apache Iceberg`)** layer ACID transactions and snapshot isolation directly onto cloud object storage (`ADLS Gen2 / Amazon S3`).

> **Prerequisite:** [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md), [Module 09: Probabilistic Data Structures at Scale](./09-probabilistic-data-structures-at-scale.md), and [Module 23: Multi-Tenant Isolation and Noisy-Neighbor Economics](./23-multi-tenant-isolation-and-noisy-neighbor-economics.md).

---

## 1. Row-oriented (`OLTP`) vs column-oriented (`OLAP`) physical layout

Look at how the exact same 4-row table is laid out on disk in a **Row Store** versus a **Column Store**[^ddia-ch3][^parquet-spec]:

```
Logical Table:
  Row 0: (Tenant = "T1", EventType = "SignInOk",      LatencyMs = 12, Payload = "{...2KB...}")
  Row 1: (Tenant = "T1", EventType = "SignInOk",      LatencyMs = 14, Payload = "{...2KB...}")
  Row 2: (Tenant = "T1", EventType = "SignInFailure", LatencyMs = 85, Payload = "{...2KB...}")
  Row 3: (Tenant = "T2", EventType = "SignInOk",      LatencyMs = 11, Payload = "{...2KB...}")

1. ROW-ORIENTED LAYOUT ON DISK (PostgreSQL Heap / MySQL InnoDB / CSV / JSONL):
+-------------------------------------------------------+-------------------------------------------------------+
| [T1 | SignInOk | 12 | {...}] [T1 | SignInOk | 14 | ..]| [T1 | SignInFailure | 85 | ..] [T2 | SignInOk | 11 ..]|
+-------------------------------------------------------+-------------------------------------------------------+
  * Point lookup of Row 2: 1 single disk read!
  * Scan AVG(LatencyMs) WHERE EventType = 'SignInFailure': must read past every 2 KB Payload column!

2. COLUMN-ORIENTED LAYOUT ON DISK (Apache Parquet / ClickHouse / DuckDB):
+-----------------------------------+-----------------------------------------------+---------------------------+
| Column Chunk: Tenant              | Column Chunk: EventType                       | Column Chunk: LatencyMs   |
| ["T1", "T1", "T1", "T2"]          | ["SignInOk", "SignInOk", "SignInFailure", ..] | [12, 14, 85, 11]          |
+-----------------------------------+-----------------------------------------------+---------------------------+
  * Scan AVG(LatencyMs) WHERE EventType = 'SignInFailure': reads ONLY EventType & LatencyMs chunks!
    (The 2 KB Payload column chunk is NEVER EVEN OPENED OR READ FROM OBJECT STORAGE!)
```

Converting a table from row layout to column layout produces three compounding mechanical advantages for analytical, security-graph, and telemetry workloads[^ddia-ch3]:
1. **Projection Pushdown (Column Pruning)**: A query that references 3 out of 60 columns issues HTTP `Range: bytes=start-end` reads against cloud object storage for **only those 3 column byte ranges**, cutting network and disk I/O by \(20\times\) before reading a single value.
2. **Massive Compression Ratios (`5x` to `20x` Smaller than Row Stores)**: In a row store, adjacent bytes on disk belong to completely different data types (`UUID`, then `UTF-8 String`, then `Int64`, then `Float64`), which defeats byte-level compression. In a column store, 1,000,000 values of the *exact same column and data type* sit contiguously (`["T1", "T1", "T1", ...]`), enabling specialized columnar encodings (`Dictionary + RLE + Delta`) before general-purpose `Zstandard (Zstd)` or `Snappy` compression runs.
3. **SIMD Vectorized CPU Execution**: Because a decoded column page is a contiguous array of homogeneous primitives (`int64[4096]`), the query engine (`Spark Photon`, `DuckDB`, `Velox`, `ClickHouse`, `.NET Vector256<long>`) processes 4 to 16 rows per CPU clock cycle using AVX-2 / AVX-512 SIMD instructions with zero per-row virtual method dispatch!

---

## 2. Inside Apache Parquet: file anatomy, encodings, Dremel levels, and predicate pushdown

**Apache Parquet** is the universal open-source columnar storage format for cloud data lakes[^parquet-spec].
To design high-performance ingestion and query pipelines on Azure Data Lake Storage (`ADLS Gen2`) or Amazon S3, you must understand how a Parquet file is organized internally and why a Parquet reader **always reads the end of the file first**:

```
APACHE PARQUET FILE ANATOMY (Hybrid Horizontal Row-Group + Vertical Column-Chunk Layout)
========================================================================================
+--------------------------------------------------------------------------------------+
| Magic Bytes: "PAR1" (4 bytes)                                                        |
+--------------------------------------------------------------------------------------+
| ROW GROUP 0 (e.g., 128 MB to 512 MB / ~1,000,000 rows)                               |
|   +--------------------------+  +--------------------------+  +--------------------+ |
|   | Column Chunk: TenantId   |  | Column Chunk: EventType  |  | Column: LatencyMs  | |
|   |  * Dictionary Page       |  |  * Dictionary Page       |  |  * Data Page 0     | |
|   |  * Data Page 0 (RLE)     |  |  * Data Page 0 (RLE)     |  |  * Data Page 1     | |
|   |  * Data Page 1 (RLE)     |  |  * Bloom Filter Bitset   |  |  (Delta + BitPack) | |
|   +--------------------------+  +--------------------------+  +--------------------+ |
+--------------------------------------------------------------------------------------+
| ROW GROUP 1 (Next ~1,000,000 rows) ...                                               |
+--------------------------------------------------------------------------------------+
| FILE FOOTER (FileMetaData Thrift Struct)                                             |
|   * Schema Definition (including nested Dremel Repetition/Definition max levels)     |
|   * Row Group Directory: byte offsets of every Column Chunk                          |
|   * Per-Column-Chunk Statistics: {MinValue, MaxValue, NullCount, DistinctCount}      |
|   * Offset Index & Column Index (Page-level Min/Max bounds)                          |
| Footer Length (4 bytes, uint32)  |  Trailing Magic Bytes: "PAR1" (4 bytes)           |
+--------------------------------------------------------------------------------------+
```

Why is the `FileMetaData` directory stored in the **Footer at the tail of the file** rather than in the header at the top?
Because a streaming Parquet writer writes Row Group 0, Row Group 1, ..., Row Group $K$ sequentially in a single append pass without knowing in advance how many rows will arrive, what the compressed byte offsets of each column chunk will be, or what the final `Min/Max` values of each column will be!
Only when the file is closed does the writer serialize the complete metadata catalog at the end, followed by the 4-byte `FooterLength` and `"PAR1"`.
When a query engine opens a `1 GB` Parquet file on S3 or ADLS, its **very first I/O call** is a tail range read (`Range: bytes=-65536`) to fetch the Footer!

### 1. Columnar Compression Encodings (`Dictionary`, `RLE / Bit-Packing`, `Delta`)

Inside each Column Chunk, Parquet applies lightweight CPU encodings tailored to the column's statistical distribution before passing the page to `Zstd` or `Snappy`[^parquet-spec]:
- **Dictionary Encoding (for low-to-medium cardinality strings/IDs)**: If a `Column Chunk` of `1,000,000` rows contains only `400` distinct `TenantId` or `Region` strings, Parquet writes a **Dictionary Page** mapping `0 -> "tenant-alpha"`, `1 -> "tenant-beta"`, ..., `399 -> "tenant-omega"`, and replaces all 1,000,000 36-byte GUID strings in the Data Pages with tiny **9-bit integers (`0..399`)**! (If a high-cardinality column like `EventId` exceeds the dictionary size cap of ~1 MB, Parquet automatically falls back to plain encoding for subsequent pages.)
- **Run-Length Encoding (`RLE`) & Bit-Packing Hybrid**: When rows are sorted or clustered by `TenantId` or `EventType`, the integer dictionary IDs repeat in long runs (`[0, 0, 0, ..., 0]` 50,000 times). **RLE** compresses that 50,000-element run into a single pair `(Value = 0, Count = 50000)` in **8 bytes total**! When values alternate without long runs, the hybrid encoder packs integers using the exact minimum bit width $\lceil \log_2(\text{MaxVal} + 1) \rceil$ (**Bit-Packing**).
- **Delta Encoding (`DELTA_BINARY_PACKED` for timestamps and monotonic IDs)**: Instead of storing 64-bit millisecond timestamps `[1700000001000, 1700000001005, 1700000001012]`, Delta Encoding stores the base value `1700000001000` followed by the frame-of-reference deltas `[+5, +7]`, which fit in 4 bits each!

### 2. Dremel Nested Data Representation (`Definition Levels` and `Repetition Levels`)

Cloud telemetry and policy payloads are rarely flat CSV tables; they are deeply nested JSON or Protobuf trees containing optional fields and repeated arrays (for example, `Device.NetworkInterfaces[].IpAddresses[]`).
How does Parquet shred an arbitrary nested tree into flat columns without losing the ability to reconstruct which element belonged to which nested array?
Parquet uses the algorithm from Google's seminal **Dremel (BigQuery) paper (Melnik et al., 2010)**[^dremel-paper]: alongside each leaf column value, Parquet stores two tiny integer levels (typically 1 to 3 bits each, RLE-compressed):
- **Definition Level (`D-Level`)**: Counts how many optional/nullable ancestor fields along the schema path from the root to this leaf are actually *defined (non-null)*. If the maximum possible definition depth of `Device.Config.ProxyUrl` is $D_{\max} = 2$:
  - `D = 2` means the leaf value itself is present (so the value is stored in the data page).
  - `D = 1` means `Device.Config` exists, but `ProxyUrl` is `null`.
  - `D = 0` means `Device.Config` itself was `null`! (Notice that no null placeholder bytes are needed in the value stream; `D < D_max` encodes the exact level at which the null occurred!)
- **Repetition Level (`R-Level`)**: For repeated lists (`Tags[]` or `Interfaces[].Ips[]`), indicates **at which repeated field depth a new list element started** (`R = 0` means this value starts a brand-new top-level record; `R = 1` means it is a new element in the outer array of the same record; `R = 2` means it is a next element in the inner nested array).

### 3. Min/Max Statistics and Bloom Filter Predicate Pushdown (Skipping 99% of I/O)

Look back at the Parquet File Footer diagram.
For every Row Group and every Column Chunk (and every Data Page via the Column Index), the footer records **`MinValue`**, **`MaxValue`**, and **`NullCount`**; and for high-cardinality equality columns (`DeviceId`, `UserId`, `TraceId`), modern Parquet writers embed a **Split-Block Bloom Filter** (Module 09) per column chunk[^parquet-spec]!

Trace what happens when a query asks for `WHERE event_time BETWEEN '14:00' AND '14:05' AND device_id = 'dev-99182'`:
1. **Row-Group Min/Max Pruning**: The reader inspects the Footer's `Min/Max` statistics for `event_time`. If Row Group 0 has `[Min = 08:00, Max = 08:59]`, the query engine knows with 100% mathematical certainty that **zero rows in Row Group 0 can match `14:00..14:05`**, skipping the entire 256 MB Row Group without issuing a single disk read!
2. **Bloom Filter Pruning**: Suppose Row Group 14 covers `14:00..14:59` (`Min/Max` overlaps!), so `event_time` cannot prune it. What about `device_id = 'dev-99182'`? Because `device_id` is a random GUID, every Row Group has `Min = "0000..."` and `Max = "ffff..."` (`Min/Max` is useless on random GUIDs unless sorted!). However, the reader fetches the 128 KB **Column-Chunk Bloom Filter** for `device_id` in Row Group 14: if `BloomFilter.MightContain("dev-99182")` returns `false` (remember from Module 09: **zero false negatives!**), the reader skips decompressing the entire 256 MB Row Group 14 too!

Let us write out a clean, verifiable C# implementation of a **Columnar RLE + Dictionary Encoder** and a **Row-Group Min/Max + Bloom Filter Predicate Pushdown Scanner**:

```csharp
public sealed class ColumnarRleDictionaryChunk
{
    private readonly List<string> _dictionary = new List<string>();
    private readonly List<(int DictId, int RunLength)> _rleRuns = new List<(int, int)>();
    private readonly HashSet<uint> _bloomBits = new HashSet<uint>();

    public string? MinValue { get; private set; }
    public string? MaxValue { get; private set; }
    public int TotalRowCount { get; private set; }
    public int DistinctCount => _dictionary.Count;
    public int CompressedRunCount => _rleRuns.Count;

    public void EncodeColumn(ReadOnlySpan<string> values)
    {
        Dictionary<string, int> dictIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string val in values)
        {
            if (MinValue is null || string.CompareOrdinal(val, MinValue) < 0)
            {
                MinValue = val;
            }

            if (MaxValue is null || string.CompareOrdinal(val, MaxValue) > 0)
            {
                MaxValue = val;
            }

            if (!dictIndex.TryGetValue(val, out int id))
            {
                id = _dictionary.Count;
                _dictionary.Add(val);
                dictIndex[val] = id;
                _bloomBits.Add(HashString(val) % 4096u);
            }

            if (_rleRuns.Count > 0 && _rleRuns[^1].DictId == id)
            {
                _rleRuns[^1] = (id, _rleRuns[^1].RunLength + 1);
            }
            else
            {
                _rleRuns.Add((id, 1));
            }

            TotalRowCount++;
        }
    }

    // Returns false if Predicate Pushdown proves the value CANNOT exist in this Row Group
    // WITHOUT decoding any RLE runs!
    public bool CanRowGroupMatchEquality(string targetValue)
    {
        if (TotalRowCount == 0 || MinValue is null || MaxValue is null)
        {
            return false;
        }

        // 1. Min/Max Range Check
        if (string.CompareOrdinal(targetValue, MinValue) < 0 ||
            string.CompareOrdinal(targetValue, MaxValue) > 0)
        {
            return false;
        }

        // 2. Bloom Filter Check (0% false negatives)
        uint bit = HashString(targetValue) % 4096u;
        return _bloomBits.Contains(bit);
    }

    public int CountMatchesDecoded(string targetValue, out bool wasSkippedByPushdown)
    {
        if (!CanRowGroupMatchEquality(targetValue))
        {
            wasSkippedByPushdown = true;
            return 0;
        }

        wasSkippedByPushdown = false;
        int matches = 0;
        foreach ((int dictId, int runLength) in _rleRuns)
        {
            if (string.Equals(_dictionary[dictId], targetValue, StringComparison.Ordinal))
            {
                matches += runLength;
            }
        }

        return matches;
    }

    private static uint HashString(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text)
        {
            h ^= c;
            h *= 16777619u;
        }

        return h;
    }
}
```

---

## 3. The Small-File Problem in streaming ingestion and Bin-Packing Compaction

Why does Parquet require large Row Groups (`128 MB` to `512 MB` per file) to achieve its compression and I/O efficiency?
Suppose a team wires an Azure Event Hub or Kafka consumer directly to ADLS Gen2 / S3 and flushes a new Parquet file every 5 seconds across 128 partitions.
In a single day (`86,400 seconds`), that job writes:
\[
\frac{86{,}400}{5} \times 128 = \mathbf{2{,}211{,}840 \text{ tiny Parquet files per day (each only } 50\text{ KB to } 200\text{ KB)!}}
\]
This is the infamous **Small-File Problem**, and it breaks cloud analytics in three ways:
1. **Object-Store Metadata Listing Bottleneck**: Before Spark, Trino, or DuckDB can read a day's partition, it must call `ListBlobs` on 2.2 million files and issue 2.2 million separate HTTP `GET` requests to read 2.2 million Parquet footers! A query that should take 3 seconds on two hundred `256 MB` files takes **25 minutes** just waiting on HTTP round-trip overhead.
2. **Destroyed Columnar Compression & RLE**: A `50 KB` Parquet file contains only a few hundred rows, so Dictionary Pages are larger than the data itself and RLE runs are tiny.
3. **Throttled Storage Account IOPS**: Cloud object stores enforce per-prefix request-rate limits (for example, `3,500 PUT` and `5,500 GET` requests/sec per S3 prefix or `20,000 IOPS` per ADLS storage account); millions of small files hit HTTP `503 Slow Down` throttling long before saturating network bandwidth.

### Two-Stage Defense Against Small Files

A Principal data-plane architect prevents small files at two stages:
1. **Stage 1 (Ingestion-Time Micro-Batch Coalescing)**: As we derived in Module 23, stream consumers buffer incoming events by partition (`ArrayPool<byte>` + local NVMe spillover) until the partition buffer hits at least `32 MB to 128 MB` (or a `1 to 5 minute` SLA flush timer), committing the Kafka offset only after the coalesced Parquet file is durably closed.
2. **Stage 2 (Asynchronous Bin-Packing Compaction)**: For low-volume tenants or low-traffic hours where the 5-minute SLA timer forces smaller `5 MB` files to be written, a background **Bin-Packing Compaction Job** (`OPTIMIZE` in Delta Lake / `rewrite_data_files` in Iceberg) periodically merges dozens of small files in the same partition into right-sized **`256 MB` to `512 MB` Parquet files**.

---

## 4. Modern Lakehouse Table Formats: Delta Lake and Apache Iceberg

Wait: if a background compaction job merges 50 small Parquet files (`part-01.parquet .. part-50.parquet`) into a single `compacted-01.parquet` file and deletes the 50 old files **while a production Spark or Trino query is halfway through reading `part-27.parquet`**, what happens on raw object storage?
The reader crashes mid-query with **`FileNotFoundException (HTTP 404)`**!
Worse still, if a writer job crashes after writing 4 out of 10 output Parquet files, raw directory listing exposes a **corrupt partial write** to every downstream reader (violating ACID Atomicity and Isolation!).

**Modern Lakehouse Table Formats (`Delta Lake` and `Apache Iceberg`)** turn raw Parquet files on cloud object storage into a full **ACID Transactional Database with MVCC Snapshot Isolation**[^delta-lake-paper][^iceberg-spec]:

```
LAKEHOUSE TABLE ON CLOUD OBJECT STORAGE (ADLS Gen2 / S3)
========================================================
/security_events/
  +-- _delta_log/ (Atomic Ordered Transaction Log: The Single Source of Truth!)
  |     +-- 00000000000000000000.json  --> AddFile(part-A.parquet), AddFile(part-B.parquet)
  |     +-- 00000000000000000001.json  --> AddFile(part-C.parquet)
  |     +-- 00000000000000000002.json  --> RemoveFile(part-A), RemoveFile(part-B), AddFile(compacted-AB.parquet)
  |     +-- 00000000000000000010.checkpoint.parquet (Compacted state of commits 0..10!)
  +-- data/
        +-- part-A.parquet        (Physically retained on disk until VACUUM retention expires!)
        +-- part-B.parquet        (Readers at Snapshot v1 still read part-A and part-B safely!)
        +-- part-C.parquet
        +-- compacted-AB.parquet  (Readers at Snapshot v2 read part-C and compacted-AB!)
```

Look at how the **Transaction Log (`_delta_log` in Delta Lake / `Metadata -> Manifest List -> Manifest Files` tree in Apache Iceberg)** solves every failure mode of raw object storage[^delta-lake-paper]:

1. **Never `LIST` Data Directories; Read the Atomic Log**:
   Readers **never** rely on eventually consistent or non-atomic directory listings of `/data/*.parquet`. Instead, a reader inspects the latest committed transaction log version \(V\) (for example, `000002.json`), which defines the exact set of active Parquet files at Snapshot \(V\). If a writer crashes after uploading `uncommitted-temp.parquet` to `/data/` before committing `000003.json`, `uncommitted-temp.parquet` is not referenced in the log and is **completely invisible to all readers (100% Atomicity)**!
2. **Lock-Free MVCC Snapshot Isolation for Concurrent Compaction (`OPTIMIZE`) and Deletes**:
   When a compaction job merges `part-A.parquet` and `part-B.parquet` into `compacted-AB.parquet`, it writes `compacted-AB.parquet` first and then appends a single atomic commit `000002.json` containing `[RemoveFile(part-A), RemoveFile(part-B), AddFile(compacted-AB)]`.
   Crucially, **it does NOT immediately delete `part-A.parquet` and `part-B.parquet` from object storage!**
   Any long-running query that started at Snapshot `v1` continues reading `part-A` and `part-B` without error, while any new query starting at Snapshot `v2` reads `compacted-AB`. Only after a configurable retention period (default `7 days`, via `VACUUM` / `expire_snapshots`) are unreferenced tombstone files physically deleted from S3/ADLS!
   That same `RemoveFile + AddFile` log commit also enables **Time Travel (`SELECT * FROM events VERSION AS OF 1`)** and instant zero-copy rollback of bad pipeline runs!
3. **Optimistic Concurrency Control (`OCC`) via Atomic Put-If-Absent**:
   If two writers both read Snapshot `v1` and try to commit `000002.json` concurrently, the cloud object store's atomic `PutBlob(If-None-Match: "*")` (or catalog CAS in Iceberg) lets exactly one writer win `000002.json`. The second writer catches the conflict, reads `000002.json`, and checks whether their operations commute: if both writers simply appended new disjoint files (or modified disjoint date partitions), the second writer automatically rebases and commits as `000003.json` without rewriting its Parquet files!
4. **Multi-Dimensional Clustering (`Z-Ordering` and `Liquid / Hilbert Clustering`)**:
   Wait: if you partition a table physically by `event_date`, then queries filtering by `event_date` are fast, **but what if analysts query by `tenant_id` OR `device_id` OR `event_type`?**
   If you create nested folders `/date=.../tenant=.../device=.../type=...`, you re-create millions of tiny 10 KB files (**over-partitioning**)!
   Instead, partition directories coarsely (say by `event_date`), and within each date partition run **Z-Order Clustering** (interleaving the bits of `(tenant_id, event_type, device_id)` along the space-filling Z-curve from Module 19 before writing the `256 MB` Parquet files!).
   Because Z-Ordering co-locates multi-dimensional values into tightly bounded Row Groups, **Parquet Min/Max Predicate Pushdown skips `90% to 99%` of files across ALL clustered columns simultaneously** without creating small files!

---

## 5. Columnar storage and lakehouse pipelines in the Atlas platform

In **Atlas**, the security and fleet-analytics data plane is built as a **Delta Lake / Parquet Lakehouse on cloud object storage**:
- **Coalesced Parquet Ingestion with Dremel Nested Columns**: Atlas's `1.67M RPS` telemetry stream (containing variable-depth nested sensor arrays and diagnostic tags) is micro-batched in memory by our Claim-Check coalescing workers (Module 23) and written out as **`256 MB` Zstd-compressed Parquet files**. Using **Dictionary Encoding** on `TenantId`/`Sku`, **Delta Encoding** on `RecordedAtUtc`, and **Split-Block Bloom Filters** on `DeviceId`, `50 TB/day` of raw JSON shrinks to **`4.2 TB/day` on disk (`11.9x` compression)**.
- **Sub-Second Fleet Security Hunts via Z-Ordering + Bloom Pushdown**: When a customer's security team runs a threat-hunting query (*"Find all connection events for `DeviceId = dev-881` or `CertThumbprint = sha256:...` across the past 30 days"*), **Z-Ordering** on `(TenantId, EventType)` and **Parquet Bloom Filters** on `DeviceId` allow the query engine to prune `99.4%` of Row Groups from the footer metadata alone, returning results over 120 TB of historical telemetry in `< 1.8 seconds`.
- **Zero-Downtime GDPR Right-to-Erasure (`DELETE` + `VACUUM`)**: When a tenant exercises a GDPR deletion request, Atlas runs a Delta Lake `DELETE WHERE tenant_id = '...'` transaction that rewrites only the affected Parquet files and records a new `_delta_log` commit, preserving snapshot isolation for live dashboards before purging physical files during the scheduled `VACUUM` window.

---

## Before you move on

**Column-Oriented Storage (`Apache Parquet`)** accelerates analytical (`OLAP`) and security-graph queries by storing each column contiguously inside horizontal **Row Groups (`128–512 MB`)**, reading the **File Footer** first to enable **Projection Pushdown** (reading only requested columns), **Columnar Compression (`Dictionary`, `RLE + Bit-Packing`, `Delta`)**, **Dremel `Definition` and `Repetition` Levels** for nested arrays/structs, and **Predicate Pushdown** via **Row-Group `Min/Max` Statistics** and **Column-Chunk Bloom Filters**.
To prevent streaming pipelines from creating millions of tiny 50 KB files (**the Small-File Problem**), ingestion workers coalesce micro-batches in memory and run background **Bin-Packing Compaction (`OPTIMIZE`)**.
Finally, **Modern Lakehouse Formats (`Delta Lake` and `Apache Iceberg`)** wrap Parquet files on object storage in an **atomic Transaction Log (`_delta_log` / Manifest tree)**, providing ACID atomicity, lock-free **MVCC Snapshot Isolation** during compaction and deletes, **Optimistic Concurrency Control**, **Time Travel**, and **Z-Order multi-dimensional clustering**.

To test your mastery before moving to Module 25, trace this concrete Parquet pushdown scenario: suppose a `100 GB` Parquet table of `1,000,000,000` log rows is divided into `400` Row Groups (`250 MB` each), sorted physically by `TimestampUtc`, and each Row Group contains random `TraceId` UUID strings (`"0000..."` to `"ffff..."`).
An engineer runs `SELECT * FROM logs WHERE TraceId = 'a1b2-c3d4-...'` without a `TimestampUtc` filter, and complains that **Parquet `Min/Max` Predicate Pushdown skipped `0` out of `400` Row Groups** (`100%` full-table scan!).
1. Why is `MinValue / MaxValue` statistics pushdown completely useless (`0%` pruning) on an unsorted high-cardinality column like `TraceId`?
2. What two Lakehouse/Parquet features (`Column-Chunk Bloom Filters` and `Z-Order Clustering`) fix this so the query skips `99%` of Row Groups even without a `TimestampUtc` filter?
If you can explain both the `Min/Max` range span of uniform UUIDs and how Bloom Filters / Z-Ordering solve it, you are ready for Module 25.

---

### Sources

[^ddia-ch3]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 3 ("Storage and Retrieval: Column-Oriented Storage")**: OLTP vs OLAP access patterns, column compression (bitmap, RLE), and vectorized processing.
[^parquet-spec]: **Tier 1: Apache Parquet Official Format Specification ([https://parquet.apache.org/docs/file-format/](https://parquet.apache.org/docs/file-format/))**: Row Groups, Column Chunks, Data Pages, Thrift FileMetaData Footer, Dictionary/RLE/Bit-Packing/Delta encodings, Column Index, and Split-Block Bloom Filters.
[^dremel-paper]: **Tier 1: Sergey Melnik, Andrey Gubarev, Jing Jing Long, Geoffrey Romer, Shiva Shivakumar, Matt Tolton, and Theo Vassilakis, "Dremel: Interactive Analysis of Web-Scale Datasets," *Proceedings of the VLDB Endowment (PVLDB)*, 3(1), 330-339 (2010)**: Seminal derivation of **Definition Levels** and **Repetition Levels** for lossless columnar shredding of arbitrary nested records.
[^delta-lake-paper]: **Tier 1: Michael Armbrust et al., "Delta Lake: High-Performance ACID Table Storage over Cloud Object Stores," *Proceedings of the VLDB Endowment (PVLDB)*, 13(12), 3411-3424 (2020)**: Architecture of the `_delta_log` JSON/Parquet transaction log, Optimistic Concurrency Control, MVCC snapshot isolation, Z-Ordering, and bin-packing compaction.
[^iceberg-spec]: **Tier 1: Apache Iceberg Table Specification ([https://iceberg.apache.org/spec/](https://iceberg.apache.org/spec/))**: Hierarchical snapshot metadata (`Catalog -> Metadata File -> Manifest List -> Manifest Files`), hidden partitioning, and schema/partition evolution.
