# 06: Write-Ahead Logs and Storage Engines

In Part I we treated storage nodes as abstract boxes that persist variables like `acceptedValue` or `commitIndex` to disk.
Now we open the box.
When a database server receives a write request, how does it organize bytes on an SSD or NVMe drive so that acknowledged writes survive a sudden power loss (`durability`), corrupted bits on physical media are caught before they poison application state (`integrity`), and millions of point lookups and range scans execute in milliseconds (`performance`)?
Every durable storage system starts with the same primitive: the **Write-Ahead Log (WAL)**.
Around that log, modern storage engines split into two major architectural families: page-oriented **B+Trees** (which update data in place on disk) and log-structured **LSM-Trees** (which treat disk storage as a sequence of immutable, append-only sorted files).
This module covers WAL durability, log segmentation and compaction, checksumming against silent corruption, the B+Tree versus LSM-Tree engineering trade-off, and how replicated logs use the **High-Water Mark** to govern reader visibility.

> **Prerequisite:** [Module 01: Distributed Systems Models and Failure Modes](./01-distributed-systems-models-and-failure-modes.md) (specifically the crash-recovery node model) and [Module 04: Consensus, Leases, and Split-Brain](./04-consensus-leases-and-split-brain.md).

---

## 1. The Write-Ahead Log (WAL) invariant and `fsync` durability

Suppose a database stores a customer's account record inside a 16 KB B-tree page on disk, plus two secondary index pages on different disk blocks.
If the server modifies those three pages directly on disk and the power supply fails after writing the first page but before writing the second and third pages, the on-disk data structures are left half-updated and permanently corrupted.
Even worse, updating three random disk blocks synchronously on every write turns every small update into slow random I/O.

The **Write-Ahead Log (WAL)** (also called the transaction log, redo log, or commit log) solves both problems through a single non-negotiable invariant[^ddia-ch3][^joshi-pds]:
> **The Write-Ahead Invariant:** Before any modified data page is written to its permanent location on disk (and before any transaction commit is acknowledged to the client), an append-only log record describing the mutation must be flushed to durable non-volatile storage.

```
Client Write("Set X = 42")
        |
        v
+---------------------------------------------------------------+
| Storage Engine Process                                        |
|                                                               |
|  1. Append record [LSN=105 | TxId=9 | Key="X" | Val="42" | CRC]|
|     to sequential Write-Ahead Log (WAL) + fsync() to disk     |
|                                                               |
|  2. Update in-memory data structure (MemTable / Buffer Pool)  |
|                                                               |
|  3. Return "200 OK (Committed)" to Client                     |
+---------------------------------------------------------------+
```

Why is writing to the WAL fast and safe?
1. **Sequential append vs random overwrite**: Appending bytes to the end of a sequential log file requires no random disk seeks and matches the erase-block physiology of flash SSDs as well as magnetic platters.
2. **Atomic crash recovery via replay**: When a crash-recovery node reboots after a power failure, its volatile in-memory state (`MemTable` or dirty buffer-pool pages) is gone. The recovery manager simply reads the WAL sequentially from the last known checkpoint and **reapplies (redoes)** the logged mutations in order (`Log Sequence Number`, or `LSN`, order).

### The `fsync` Trap and Group Commit

Appending bytes via `FileStream.Write` in .NET or the POSIX `write()` system call does **not** put those bytes onto the physical SSD; it merely copies them into the operating system kernel's volatile in-memory **page cache**.
If the process crashes (for example, an unhandled exception), the OS kernel will still flush the page cache to disk, so the data survives.
However, if the **machine loses power or the OS kernel panics**, everything sitting in the volatile OS page cache vanishes!

To guarantee true durability across machine power loss, the storage engine must explicitly invoke `fsync()` (or `FlushFileBuffers` / `FileStream.Flush(flushToDisk: true)` on Windows) before acknowledging a commit.
Because calling `fsync()` on every individual 100-byte record limits throughput to the round-trip latency of the storage controller (often a few thousand IOPS), production engines use **Group Commit**: a single background flusher thread batches all concurrent transactions that arrived within a 1 to 2 ms window and executes a single `fsync()` call that makes the entire batch durable in one hardware operation[^petrov-dbi].

---

## 2. Log segmentation, checkpoints, and log compaction

If a database appends every mutation to a single WAL file forever, two bad things happen: the file eventually exhausts all disk space, and crash recovery after a reboot takes hours because the engine must replay months of history from byte zero.

Storage engines bound WAL size and recovery time using **Log Segmentation**, **Checkpoints (Low-Water Marks)**, and **Log Compaction**[^ddia-ch3][^joshi-pds]:

```
Segmented WAL on Disk:
+-------------------+   +-------------------+   +-------------------+   +-------------------+
| segment-00001.log |   | segment-00002.log |   | segment-00003.log |   | segment-00004.log |
| LSN 1 .. 50,000   |   | LSN 50,001..99,999|   | LSN 100,000..150k |   | LSN 150,001.. (RW)|
| (Immutable/Closed)|   | (Immutable/Closed)|   | (Immutable/Closed)|   | (Active Segment)  |
+-------------------+   +-------------------+   +-------------------+   +-------------------+
                                            ^
                                            |
                             Checkpoint Low-Water Mark (LSN 100,000)
                    (Segments 1 & 2 already flushed to SSTables/B+Tree -> Safe to delete!)
```

- **Log Segmentation**: Instead of one giant file, the engine splits the log into fixed-size **segments** (for example, rolling to a new file whenever the active segment reaches `1 GB` or every `4 hours`)[^joshi-pds]. Only the latest segment is open for appending (`RW`); all older segments are sealed as immutable read-only files.
- **Checkpoints (Low-Water Mark truncation)**: Once dirty in-memory state up to `LSN 100,000` has been safely flushed to the main on-disk B+Tree or SSTable files, the engine records `checkpointLSN = 100,000` (the **Low-Water Mark**). Any sealed WAL segment whose maximum LSN is strictly below `100,000` (`segment-00001.log` and `segment-00002.log`) is no longer needed for crash recovery and can be deleted cleanly without fragmenting the active log file.
- **Log Compaction (Key-Based Retention)**: In log-centric storage and streaming systems like Apache Kafka (Module 17), the log *is* the primary store rather than a temporary crash-recovery buffer. Instead of deleting old segments blindly by age, a background **compaction thread** scans sealed segments and discards superseded records for the same key, writing out a compacted segment that retains **only the most recent value for each unique key**[^ddia-ch3].

---

## 3. Checksums and defending against silent data corruption

Disks, RAID controllers, DMA channels, and RAM chips occasionally flip bits without reporting an hardware error, a phenomenon known as **silent data corruption** or **bit rot**[^ddia-ch1][^petrov-dbi].
Even more frequently, a sudden power loss in the middle of writing a 64 KB log record leaves a **torn write** at the tail of the WAL: the first 16 KB of the record hold the new bytes, and the remaining 48 KB hold zeroes or garbage from a previously deleted file.
If your WAL recovery loop blindly deserializes that torn record on reboot, it will either crash in a deserialization panic (preventing the database from starting!) or silently load garbage into your state machine.

Every production WAL record and storage page embeds a **Checksum** in its header[^joshi-pds][^petrov-dbi]:

```
+------------------+------------------+------------------+------------------+------------------+
|  CRC32C (4B)     |  LSN (8B)        |  KeyLength (4B)  |  ValLength (4B)  |  Key + Value ... |
+------------------+------------------+------------------+------------------+------------------+
```

When writing a record, the engine computes a checksum over `(LSN, KeyLength, ValLength, Key, Value)` and writes the checksum alongside the payload.
When reading the record back (either during crash recovery or during a client read), the engine recomputes the checksum over the payload and compares it against the stored header:
- **During WAL tail recovery**: If the last record in the active segment fails its checksum validation (or ends before `KeyLength + ValLength` bytes are available), the recovery engine recognizes it as an **uncommitted torn write** caused by the crash, logs a warning, **truncates the WAL back to the start of that partial record**, and finishes starting up cleanly.
- **During normal replica reads**: If an on-disk SSTable block or B+Tree page fails its checksum, the storage node returns an explicit `CorruptDataError` rather than serving corrupt bytes, allowing the coordinator to fetch a healthy copy from another replica and trigger read repair.

Notice the distinction between **cyclic redundancy checks (`CRC32C` / `xxHash64`)** and **cryptographic hashes (`SHA-256` / `BLAKE3`)**:
- `CRC32C` (hardware-accelerated on modern x86/ARM CPUs via SSE4.2/NEON instructions) detects random hardware bit flips and torn writes at tens of gigabytes per second with only 4 bytes of overhead, making it the standard choice for local WAL frames and database pages.
- Cryptographic hashes (`SHA-256`, 32 bytes) are collision-resistant against an intelligent adversary and are required when building content-addressable storage, Merkle trees across untrusted networks, or signed firmware manifests.

---

## 4. Storage engines under the hood: B+Trees vs LSM-Trees

Once a mutation is safely recorded in the WAL, how should the storage engine organize the main dataset on disk?
Almost every transactional and analytical database in the world uses one of two indexing structures[^ddia-ch3][^petrov-dbi]:

### Page-Oriented Update-in-Place: The B+Tree

Used by PostgreSQL, MySQL (InnoDB), SQL Server, Oracle, and SQLite, a **B+Tree** divides the disk into fixed-size **pages** (typically `4 KB`, `8 KB`, or `16 KB`) that correspond directly to hardware storage blocks.

```
                      [ Internal Page: Keys  100  |  250 ]
                       /                   |              \
                      v                    v               v
     [ Leaf Page: 10..99 ] <---> [ Leaf Page: 100..249 ] <---> [ Leaf Page: 250..400 ]
     (Holds inline row values    (Doubly linked leaf chain     (Updated in-place on disk
      or heap row pointers)       for fast range scans)         via Buffer Pool + WAL)
```

Unlike an in-memory binary search tree (which has a branching factor of 2 and chases pointers across scattered heap allocations), a B+Tree page holds **hundreds of keys** (a branching factor $b$ of 200 to 500).
A 4-level B+Tree with a branching factor of 500 and 16 KB leaf pages can store $500^3 = 125\text{ million}$ leaf pages; **2 terabytes of data**; while requiring **at most 4 page lookups** (and usually only 1 disk read, since the top 3 levels of internal pages fit easily in RAM).
In a B+Tree (specifically the $B^+$ variant used by all real databases, as opposed to the classic Bayer-McCreight B-Tree), **internal nodes store only routing keys and child page offsets, while all actual row payloads (or row pointers) live exclusively in the leaf pages**, which are linked together in a sequential doubly linked list for fast range scans.

When you update a key in a B+Tree, the engine modifies the 16 KB page in its in-memory **Buffer Pool**, logs the change to the WAL, and eventually overwrites that exact 16 KB page **in place** on disk.
If an insert fills a 16 KB leaf page beyond capacity, the engine **splits** the page into two 8 KB pages and writes an updated separator key into the parent page.
To recover from crashes when dirty buffer-pool pages may be flushed before commit (`STEAL`) or committed pages may still sit only in RAM at commit time (`NO-FORCE`), relational B+Tree engines use the **ARIES recovery algorithm** (Mohan et al., 1992)[^aries-1992]: **Analysis** (scan the WAL to find active transactions and dirty pages), **Redo** (replay *all* logged changes from the earliest dirty page LSN to restore the exact pre-crash state), and **Undo** (walk backward through the undo log to roll back any transaction that had not committed before the crash).

### Append-Only Log-Structured Merge: The LSM-Tree

Introduced by O'Neil et al. (1996) and used in RocksDB, LevelDB, Google BigTable, Apache Cassandra, and ScyllaDB, a **Log-Structured Merge-Tree (LSM-Tree)** eliminates random in-place disk overwrites altogether[^lsm-1996][^ddia-ch3]:

```
Write Path:                                     Read Path (Point Lookup for Key K):
-----------                                     -----------------------------------
1. Append to on-disk WAL (sequential)           1. Check active MemTable in RAM
2. Insert into sorted in-memory MemTable -----> 2. Check immutable MemTables flushing to disk
   (Skip List / B-Tree in RAM, e.g. 64 MB)      3. Check SSTable Bloom Filters (in RAM)
        |                                       4. Binary-search Sparse Index -> Read SSTable block
        | (When MemTable hits 64 MB,               from newest Level (L0) to oldest (L1, L2...)
        v  flush sequentially to disk!)
+-----------------------------------------------------------------------+
| Immutable SSTables on Disk (Sorted String Tables)                     |
|   L0: [SST-04 (newest)] [SST-03]  (Overlapping key ranges from flush) |
|         \                 /                                           |
|          v (Background Compaction: merge-sort + discard tombstones)   |
|   L1: [SST-10: A..G] [SST-11: H..P] [SST-12: Q..Z] (Non-overlapping)  |
+-----------------------------------------------------------------------+
```

Trace how writes, deletes, reads, and compaction work in an LSM-Tree:
1. **Writes**: Every write is appended sequentially to the on-disk **WAL** and inserted into a sorted in-memory tree called the **MemTable** (typically implemented as a concurrent **Skip List** or Red-Black Tree). There is zero random disk I/O on the write path!
2. **Deletes (Tombstones)**: Because on-disk files are immutable, deleting a key does not erase bytes on disk; it appends a special marker record called a **Tombstone** (`Key="X", Deleted=true, Timestamp=T`) to the WAL and MemTable.
3. **Flushing to SSTables**: When the MemTable reaches its size threshold (say `64 MB`), it is frozen as an immutable MemTable while a fresh MemTable immediately takes over new writes. A background thread writes the frozen MemTable sequentially to disk as an immutable **SSTable (Sorted String Table)** file containing sorted data blocks and an index block at the tail.
4. **Reads and Sparse Indexing**: Because a key's latest version might live in the MemTable, in a recently flushed `L0` SSTable, or in an older `L1..Lk` SSTable, a point lookup must check the MemTable first and then check SSTables from newest to oldest. To prevent checking every SSTable on disk from causing a dozen disk reads, engines keep two small structures in RAM per SSTable: a **Sparse Index** (storing only one key offset per 4 KB or 64 KB compressed data block, since the file is already sorted) and a **Bloom Filter** (Module 09), which proves in nanoseconds when an SSTable definitely does *not* contain the queried key.
5. **Compaction (Size-Tiered vs Leveled)**: Over time, SSTable files accumulate and hold obsolete versions and tombstones. A background **Compaction** thread runs a multi-way merge sort (identical to external merge sort in Module 20) over existing SSTables, keeping only the newest version of each key, discarding tombstones once they are older than all older levels, and writing out clean new SSTables. **Size-Tiered Compaction** (Cassandra default for write-heavy workloads) merges similarly sized SSTables into larger ones (minimizing write amplification at the cost of temporary 50% disk-space overhead); **Leveled Compaction** (RocksDB / LevelDB default) partitions levels `L1, L2, ...` into non-overlapping fixed-size SSTable ranges where each level is $10\times$ larger than the level above it (minimizing space amplification and bounding point-read latency at the cost of higher compaction I/O).

### The Three Amplification Trade-Offs (RUM Conjecture)

At the Principal level, you choose between B+Trees and LSM-Trees by evaluating **Read Amplification**, **Write Amplification**, and **Space Amplification**[^ddia-ch3][^petrov-dbi]:

| Dimension | B+Tree (InnoDB, PostgreSQL) | LSM-Tree (RocksDB, Cassandra, BigTable) |
| --- | --- | --- |
| **Write throughput & I/O pattern** | Lower on random keys; updating a 100-byte row rewrites an entire 16 KB page in place (random I/O). | **Significantly higher**; all writes are sequential appends to WAL + MemTable flush. |
| **Write Amplification** ($\frac{\text{Bytes written to SSD}}{\text{Logical bytes written}}$) | High for small random writes (a 100B update writes 16 KB + WAL); low for sequential bulk loads. | Lower on initial ingest, though Leveled Compaction rewrites data across levels (typically $10\times$ to $30\times$ total). |
| **Point & Range Read Latency** | **Faster and more predictable** (`p99`); each key exists in exactly **one** leaf page on disk. | Slower `p99` tail latency; range scans must merge-iterate across MemTable and multiple SSTable levels; compaction steals disk I/O bandwidth. |
| **Space Amplification** (Disk footprint) | Moderate page fragmentation (pages are typically $50\%$ to $70\%$ full after splits/deletes). | **Smaller footprint**; immutable SSTables have zero page fragmentation and compress tightly in 64 KB blocks. |
| **Transactional Locking** | Easy to attach row/page/range locks directly to canonical B+Tree key slots. | Harder for pessimistic locking; naturally suited to lock-free MVCC and versioned append workloads. |

---

## 5. High-Water Mark: commit index vs log end offset in replicated logs

When we combine a local Write-Ahead Log with leader-follower replication (Module 03) or consensus (Module 04), a subtle visibility hazard appears at the tail of the log[^joshi-pds]:

```
Leader WAL:     [ Idx 1 (Committed) ] [ Idx 2 (Committed) ] [ Idx 3 (Committed) ] [ Idx 4 (Uncommitted) ]
                                                                     ^                       ^
                                                                     |                       |
Follower 1 WAL: [ Idx 1 (Committed) ] [ Idx 2 (Committed) ] [ Idx 3 (Committed) ]            |
Follower 2 WAL: [ Idx 1 (Committed) ] [ Idx 2 (Committed) ]          |                       |
                                                                     |                       |
                                                          High-Water Mark (HWM = 3)    Log End Offset (LEO = 4)
                                                     (Safe to expose to client reads)
```

Suppose the Leader appends entry `Idx 4` to its local WAL (`Log End Offset = 4`), and before it can replicate `Idx 4` to `Follower 1` or `Follower 2`, a client sends a read request to the Leader, and then 1 millisecond later the Leader's power supply explodes permanently.
`Follower 1` is elected as the new Leader, and `Idx 4` is gone forever because no surviving node ever received it.
If the old Leader had allowed the client to read the state produced by `Idx 4` before `Idx 4` reached a quorum, the client would have observed a "phantom" committed state that vanished after failover; breaking read monotonicity and external consistency!

To prevent this, every replicated log system maintains two distinct pointers[^joshi-pds]:
1. **Log End Offset (LEO)**: The index of the latest entry appended to the node's local WAL on disk.
2. **High-Water Mark (HWM / Commit Index)**: The highest log index that the Leader knows has been durably replicated to a **quorum of followers** (or to all **In-Sync Replicas** in Kafka).

**The High-Water Mark Visibility Rule:** Neither the Leader nor any Follower is ever permitted to apply or expose log entries beyond the High-Water Mark (`index <= HWM`) to client reads.
The Leader propagates the updated `HWM` to Followers piggybacked on regular replication and heartbeat messages; if a Leader crashes and a Follower takes over, any uncommitted entries sitting between `HWM + 1` and `LEO` on the old Leader are truncated so all replicas and clients see a single consistent prefix.

---

## 6. Storage engines in the Atlas platform

In **Atlas**, B+Trees and LSM-Trees are deployed where their amplification profiles match the workload:
- **Tenant, Device Group, and Rollout Metadata (`PostgreSQL B+Tree`)**: Queries here are low-volume writes (`~500 RPS`) and high-volume transactional reads requiring predictable `< 3 ms` `p99` point lookups and unique index enforcement. B+Trees with WAL + ARIES recovery give deterministic read latency without compaction spikes.
- **High-Velocity Device Telemetry and Audit History (`Cassandra / RocksDB LSM-Tree`)**: Ingesting `1.5 million writes/sec` of `(DeviceId, Timestamp, Metrics)` across 500 million devices would thrash a B+Tree into a wall of random 16 KB page writes. Atlas lands telemetry in an LSM-Tree engine keyed by `PartitionKey = (TenantId, DeviceIdBucket, Day)` and `ClusteringKey = Timestamp DESC`, turning 1.5M updates per second into sequential WAL appends and immutable compressed SSTables with **Time-Window Compaction Strategy (TWCS)** so entire expired daily SSTable files can be dropped in `O(1)` filesystem unlinks without tombstone scans.

---

## Before you move on

A storage engine's first job is **durability and integrity**: every mutation is written sequentially to an append-only **Write-Ahead Log (WAL)** with a **CRC32C checksum** and flushed via `fsync` (often batched via **Group Commit**) before commit acknowledgment.
To keep the WAL bounded, the engine rolls closed files into immutable **Log Segments** and deletes segments older than the storage checkpoint **Low-Water Mark** (or compacts keys in place), while using the **High-Water Mark** to ensure readers never see unreplicated tail entries above the quorum commit index.
Above the WAL, **B+Trees** optimize for predictable point and range read latency by updating fixed-size disk pages in place (managed via ARIES undo/redo recovery), whereas **LSM-Trees** optimize for massive write throughput and compression by buffering sorted writes in an in-memory **MemTable**, flushing immutable **SSTables** guarded by **Bloom filters** and **sparse indexes**, and merging them in the background via **Compaction**.

To test your understanding before moving to Module 07, consider this changed case: suppose an engineer notices that deleting 10 million expired rows from an LSM-Tree table (such as Cassandra or RocksDB) actually caused disk usage to *increase* immediately after the `DELETE` job finished, and caused subsequent range scans over that table to become $5\times$ slower for the next two hours.
Can you explain from first principles why deleting rows in an LSM-Tree writes **tombstones** into new SSTables rather than freeing disk space immediately, why a range scan must read and filter across all those tombstones until background compaction runs, and when a tombstone is finally safe for compaction to discard?
If that mechanism is not crystal clear, review **Section 4** before continuing.

---

### Sources

[^ddia-ch3]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 3: "Storage and Retrieval"**: Hash indexes, log segmentation and compaction, SSTables and LSM-Trees, B-Trees, WAL crash recovery, and read/write amplification trade-offs.
[^joshi-pds]: **Tier 2: Unmesh Joshi, *Patterns of Distributed Systems* (Addison-Wesley, 2023)**: Write-Ahead Log, Segmented Log, Low-Water Mark, High-Water Mark, and Singular Update Queue patterns.
[^petrov-dbi]: **Tier 2: Alex Petrov, *Database Internals* (O'Reilly, 2019), Chapters 2–7**: B-Tree page layout, page splits, buffer pool management, WAL checksums, ARIES recovery, LSM-Tree MemTables, SSTables, and Size-Tiered vs Leveled compaction.
[^ddia-ch1]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 1**: Hardware faults, silent data corruption, and end-to-end checksumming.
[^aries-1992]: **Tier 1: C. Mohan et al., "ARIES: A Transaction Recovery Method Supporting Fine-Granularity Locking and Partial Rollbacks Using Write-Ahead Logging" (*ACM Transactions on Database Systems*, Vol. 17, No. 1, 1992, pp. 94–162)**: The canonical Analysis, Redo, and Undo crash-recovery protocol for page-oriented storage engines.
[^lsm-1996]: **Tier 1: Patrick O'Neil, Edward Cheng, Dieter Gawlick, and Elizabeth O'Neil, "The Log-Structured Merge-Tree (LSM-Tree)" (*Acta Informatica*, Vol. 33, No. 4, 1996, pp. 351–385)**: Original paper introducing multi-component log-structured merge trees and rolling merge compaction.
