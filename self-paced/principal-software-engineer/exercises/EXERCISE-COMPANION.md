# Exercise Companion: Sections 01 to 29

> Read [`00-HOW-TO-USE.md`](./00-HOW-TO-USE.md) first.
> For each module, answer the **Part A (Active Recall Questionnaire)** in writing with the module closed, then complete the **Part B (Hands-On C# Coding Drill)** until your unit tests pass, and finally compare your work against [`ANSWER-BOOK.md`](./ANSWER-BOOK.md).

## Exercise Index

| Section | Module | Core invariant you will prove in Part B |
| --- | --- | --- |
| [Section 01](#section-01-distributed-systems-models-and-failure-modes) | 01 Models & Failure Modes | Sender retry + receiver idempotency deduplication upgrades a fair-loss link to a reliable link; parallel fan-out amplifies tail latency as $1 - (1 - p)^k$. |
| [Section 02](#section-02-time-clocks-and-ordering-of-events) | 02 Time, Clocks & Ordering | Lamport clocks give a causal total order; Vector Clocks detect exact concurrency ($V_1 \parallel V_2$); Hybrid Logical Clocks stay monotonic when wall-clock time steps backward. |
| [Section 03](#section-03-replication-quorums-and-anti-entropy) | 03 Replication & Quorums | Strict quorums ($R + W > N$) overlap in $\ge 1$ replica and repair stale replicas via Read Repair; Sloppy Quorums buffer hints for Hinted Handoff; Merkle Trees isolate divergent buckets in $O(\log B)$. |
| [Section 04](#section-04-consensus-leases-and-split-brain) | 04 Consensus, Leases & Split-Brain | Paxos Phase 1 discovers already-chosen values across overlapping majorities; storage-enforced monotonic Fencing Tokens reject delayed writes from GC-paused zombie leaders. |
| [Section 05](#section-05-consistency-and-transaction-isolation-models) | 05 Consistency & Isolation | Plain MVCC Snapshot Isolation permits Write Skew across disjoint keys; Serializable Snapshot Isolation (`SSI`) aborts transactions when their `ReadSet` was modified after their snapshot. |
| [Section 06](#section-06-write-ahead-logs-and-storage-engines) | 06 WAL & Storage Engines | Binary WAL frames with CRC checksums detect and truncate torn tail writes on crash recovery; LSM-Trees merge `MemTable`, immutable `SSTables`, and deletion tombstones during compaction. |
| [Section 07](#section-07-indexes-and-data-modeling-sql-vs-nosql) | 07 Indexes & Data Modeling | Covering indexes eliminate clustered bookmark lookups; `UnitOfWork` + `IdentityMap` preserve reference identity and atomic commit semantics. |
| [Section 08](#section-08-data-partitioning-and-hashing-schemes) | 08 Partitioning & Hashing | Consistent Hashing with Virtual Nodes and stateless Rendezvous (`HRW`) Hashing move only $\approx 1/N$ of keys when nodes join and select distinct physical replicas. |
| [Section 09](#section-09-probabilistic-data-structures-at-scale) | 09 Probabilistic Structures | Kirsch-Mitzenmacher Bloom Filters yield 0% false negatives at ~9.6 bits/key for 1% FP; Count-Min Sketch never underestimates frequencies and merges by matrix addition. |
| [Section 10](#section-10-networking-proxies-and-realtime-protocols) | 10 Networking & Real-Time | Power of Two Random Choices (`P2C`) steers load away from busy/unhealthy servers; Server-Sent Events (`SSE`) resume losslessly from `Last-Event-ID`. |
| [Section 11](#section-11-caching-architectures-and-failure-modes) | 11 Caching & Failure Modes | Hash-map + doubly linked list achieves $O(1)$ LRU eviction; Cache Lease Tokens prevent slow cold-miss readers from poisoning the cache after a concurrent write invalidation. |
| [Section 12](#section-12-rate-limiting-throttling-and-resiliency) | 12 Rate Limiting & Resiliency | Token Buckets allow bursts up to $B$ at rate $r$; Sliding Window Counters prevent $2\times$ boundary spikes in $O(1)$ space; Circuit Breakers transition `Closed -> Open -> Half-Open -> Closed`. |
| [Section 13](#section-13-microservices-messaging-and-event-driven-architecture) | 13 Microservices & Messaging | Pattern-matched routing selects the most specific rule; Transactional Outbox + Idempotent Inbox gives exact-once processing; Saga Orchestrators compensate in reverse order on failure. |
| [Section 14](#section-14-domain-driven-design-and-software-architecture-patterns) | 14 DDD & Architecture Patterns | Composite AST rules evaluate applicability locally without external RPCs; Regional Privacy Mapping Tables pseudonymize EU customer IDs before global logging. |
| [Section 15](#section-15-distributed-file-systems-and-coordination-gfs-hdfs-chubby) | 15 GFS, HDFS & Chubby | GFS Primary ChunkServers pad chunks at the 64 MB boundary and serialize `RecordAppend` offsets; Chubby sessions survive Master failover via the 45s Jeopardy grace period and validate Sequencers. |
| [Section 16](#section-16-wide-column-and-dynamo-family-stores-bigtable-dynamo-cassandra) | 16 BigTable, Dynamo & Cassandra | BigTable's 3-level hierarchy resolves tablets in $\le 3$ hops (0 when cached); Cassandra hashes `PartitionKey` and sorts `ClusteringKey` for intra-partition range scans. |
| [Section 17](#section-17-distributed-streaming-and-log-centric-systems-kafka) | 17 Apache Kafka | Partition Leaders expose records only up to the ISR High-Water Mark (`HWM`), deduplicate Idempotent Producer `(PID, SeqNum)` batches, and cap active group consumers at partition count. |
| [Section 18](#section-18-capacity-planning-and-asymptotic-analysis) | 18 Capacity Planning & Big-O | Little's Law ($L = \lambda W$), $30{,}000 / T_{\text{ms}}$ headroom server sizing, and $10^8$-second 3-year storage math size production fleets deterministically. |
| [Section 19](#section-19-trees-tries-and-spatial-indexing) | 19 Trees, Tries & Spatial | AVL rotations keep sorted inserts balanced in $O(\log N)$; binary trees convert to DLLs in place and print boundary perimeters; Prefix Tries resolve autocomplete, LPM, and string sorting; Quad-Trees subdivide 2D space. |
| [Section 20](#section-20-hash-tables-heaps-and-graphs) | 20 Hash Tables, Heaps & Graphs | Robin Hood open addressing preserves probes across Tombstones; Floyd's `Heapify` runs in $O(N)$; Selection, Insertion, Bubble, Merge, and Hoare/Lomuto QuickSort sort in place/buffer; Kahn's Topological Sort, Dijkstra, Union-Find, and DAG path counting solve graph dependencies. |
| [Section 21](#section-21-dynamic-programming-greedy-and-coding-patterns) | 21 DP, Greedy & Coding Patterns | Backward 1D tabulation solves 0/1 Knapsack in $O(C)$ space; Earliest-Finish Greedy and Prim's MST solve interval and spanning-tree optimization; Monotonic Stack, Sliding Window, and Two Heaps run in optimal bounds. |
| [Section 22](#section-22-control-plane-vs-data-plane-and-cell-architectures) | 22 Control/Data Plane & Cells | Zero-dependency stamp caches hydrate snapshots and replay incremental delta queues with version monotonicity; Shuffle Sharding yields $\binom{N}{k}$ virtual shards. |
| [Section 23](#section-23-multi-tenant-isolation-and-noisy-neighbor-economics) | 23 Multi-Tenant Isolation | Tenant batch coalescing eliminates 200-tenant/blob fan-out flush amplification; Deficit Round-Robin (`DRR`) prevents mega-tenants from starving small tenants. |
| [Section 24](#section-24-columnar-storage-and-lakehouse-pipelines) | 24 Columnar & Lakehouse | Columnar Dictionary + RLE encoding compresses runs and prunes Row Groups via `Min/Max` and Bloom Filter pushdown; Delta Lake logs enforce MVCC snapshot isolation. |
| [Section 25](#section-25-policy-engines-dsls-and-cross-language-runtimes) | 25 Policy Engines & Rust FFI | Bloom + Inverted-Index pre-screening prunes 99.7% of catalog rules before short-circuit AST evaluation and multi-criteria lexicographical driver ranking. |
| [Section 26](#section-26-testing-in-production-safe-rollouts-and-kill-switches) | 26 TiP, Rollouts & Kill-Switch | Multi-stage emergency kill-switches halt Scan, Download, and Pre-Install stages in `< 5s`; canary ring gates enforce minimum sample sizes, bake windows, and auto-rollback. |
| [Section 27](#section-27-production-ai-and-agentic-engineering-systems) | 27 Production AI & MCP | MCP tool schema gatekeepers block mutating calls from read-only agents and catch hallucinated/unbounded args; Reciprocal Rank Fusion (`RRF`) combines lexical and semantic search. |
| [Section 28](#section-28-system-design-methodology-and-architecture-reviews) | 28 System Design Methodology | Deterministic canary hashing (`Hash(RolloutId:DeviceId) % 100`) guarantees monotonic cohort expansion (`1% -> 10% -> 50%`), paired with Priority Load Shedding (`P0/P1/P2`). |
| [Section 29](#section-29-principal-leadership-and-behavioral-execution) | 29 Principal Leadership | Architecture governance forbids Tier-1 services from taking synchronous hot-path dependencies on Tier-2/3 services and halts negative-ROI platform convergences. |

---

## Section 01: Distributed Systems Models and Failure Modes

### Part A: Active Recall Questionnaire

1. Name the three point-to-point network link models in order from most hostile to strongest, and state the two software/protocol mechanisms that transform a link up each step of that hierarchy.
2. Contrast the **crash-stop** node failure model with the **crash-recovery** and **temporal (timing)** failure models. Why does designing a leader-election protocol assuming crash-stop fail when deployed on managed VMs running a garbage-collected runtime like .NET or the JVM?
3. Define the **synchronous**, **asynchronous**, and **partially synchronous** timing models. Which model do real datacenter networks inhabit, and why?
4. State the **FLP Impossibility Result** (Fischer, Lynch, Paterson, 1985) and explain how production consensus protocols like Paxos and Raft legally circumvent it.
5. Explain the asymmetry between **Reliability** and **Availability**: why does high reliability imply high availability, whereas a service can report `100%` availability while being completely unreliable?
6. An API gateway fans out every incoming user request in parallel to `50` backend shards and waits for all `50` to respond. If each shard independently has a `p99` latency of `500 ms` (`1%` chance of taking `> 500 ms`), derive the probability that the end-to-end user request takes `> 500 ms`.

### Part B: Hands-On C# Drill (`Chapter01.cs`)

**The invariant to implement:** A fair-loss network link that drops every $N$-th packet and delivers duplicate copies of surviving packets is transformed into an exact-once reliable link when the sender retries until acknowledged and the receiver deduplicates incoming messages by `(DeviceId, SequenceNumber)`. Separately, parallel fan-out tail probability follows $1 - (1 - p)^k$.

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter01
{
    [TestMethod]
    public void Retry_and_idempotency_deduplication_transform_fair_loss_link_into_reliable_link()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Tail_latency_amplification_grows_exponentially_with_parallel_fanout()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 02: Time, Clocks, and Ordering of Events

### Part A: Active Recall Questionnaire

1. Distinguish **wall-clock (time-of-day) time** (`DateTimeOffset.UtcNow`) from **monotonic time** (`Stopwatch.GetTimestamp()`). Which one can jump backward during an NTP synchronization step, and what is the difference between **UTC** and **TAI** regarding leap seconds?
2. State the three formal rules that define Lamport's **happens-before relation ($a \rightarrow b$)**. Why is $\rightarrow$ a strict *partial* order rather than a total order, and what does it mean mathematically when two events are **concurrent ($a \parallel b$)**?
3. State the increment and receive-merge rules for a **Lamport logical clock**. Why does $a \rightarrow b \implies L(a) < L(b)$ hold, why does the converse $L(a) < L(b) \implies a \rightarrow b$ **fail**, and how do you construct a deterministic total order $\prec$ from Lamport timestamps?
4. State the increment, send, and receive-merge rules for a **Vector Clock** of $N$ nodes, and write the exact element-wise condition under which $V(a) < V(b)$ versus $V(a) \parallel V(b)$.
5. What two operational limitations of Vector Clocks motivated the invention of **Hybrid Logical Clocks (HLC)**, and how does an HLC tuple `(pt, l, nodeId)` advance when a node receives a message whose physical timestamp `msg.pt` is ahead of the local wall clock?
6. How does Google Spanner's **TrueTime API** represent current time, and why must a transaction coordinator wait for $2\varepsilon$ (**Commit Wait**) before releasing locks?

### Part B: Hands-On C# Drill (`Chapter02.cs`)

**The invariant to implement:** Implement `LamportClock` (with `(Counter, NodeId)` tie-breaking), `VectorClock` (with exact `Equal`, `HappensBefore`, `HappensAfter`, and `Concurrent` comparison), and `HybridLogicalClock` (proving strict monotonicity even when `wallClockMs` regresses).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter02
{
    [TestMethod]
    public void Lamport_clock_preserves_causal_order_and_breaks_ties_deterministically()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Vector_clock_distinguishes_causal_succession_from_concurrent_conflict()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Hybrid_logical_clock_stays_monotonic_when_wall_clock_steps_backward()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 03: Replication, Quorums, and Anti-Entropy

### Part A: Active Recall Questionnaire

1. Compare **synchronous**, **asynchronous**, and **semi-synchronous** replication in a leader-follower topology. Which one risks losing acknowledged writes (`RPO > 0`) on an unrecoverable leader crash, and which one sacrifices write availability when a single follower disconnects?
2. State the strict quorum intersection inequality ($R + W > N$) and explain how the pigeonhole principle guarantees that a read set of size $R$ and a write set of size $W$ share at least $R + W - N \ge 1$ replica.
3. Describe **four distinct scenarios** in which a leaderless database configured with strict $R + W > N$ (`N = 3, W = 2, R = 2`) still **fails to provide linearizability**.
4. Contrast a **Strict Quorum** with a **Sloppy Quorum + Hinted Handoff**. Does a Sloppy Quorum guarantee read-your-writes consistency while the network partition is active? Why or why not?
5. Walk through how a coordinator executes **digest-based Read Repair** during an $R = 2$ or $R = 3$ read without transferring full multi-megabyte payloads from every replica when replicas are already in sync.
6. Why is Read Repair alone insufficient to keep replicas converged, how does a **Merkle Tree** isolate out-of-sync keys between two replicas in $O(\log B)$ network rounds, and why does adding/removing nodes without virtual nodes force Merkle Tree recalculation?

### Part B: Hands-On C# Drill (`Chapter03.cs`)

**The invariant to implement:** Implement `QuorumReplicaCluster` (supporting strict quorum writes, read repair of stale replicas on read, and sloppy quorum writes with `ReplayHints()`), plus `MerkleTree` (building a binary SHA-256 hash tree over leaf buckets and finding differing bucket indices in $O(\log B)$).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter03
{
    [TestMethod]
    public void Strict_quorum_reads_latest_version_and_triggers_read_repair_on_stale_replicas()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Sloppy_quorum_stores_hint_on_surrogate_and_replays_during_hinted_handoff()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Merkle_tree_isolates_divergent_key_bucket_in_logarithmic_comparisons()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 04: Consensus, Leases, and Split-Brain

### Part A: Active Recall Questionnaire

1. Walk through **Phase 1 (`Prepare / Promise`)** and **Phase 2 (`Accept / Accepted`)** of **Single-Decree Paxos**. Specifically, if an Acceptor in the Phase 1b majority quorum returns an already-accepted `(acceptedBallot, acceptedValue)`, what value is the Proposer *forced* to propose in Phase 2a, and why?
2. How does **Multi-Paxos** reduce steady-state write latency from 2 network round trips down to 1 round trip while also preventing dueling proposers from livelocking?
3. In **Raft**, what is the exact condition under which a voter denies its vote during `RequestVote` (**the Election Restriction**), and how does that restriction prove the **Leader Completeness Property**?
4. Why is a Raft Leader **forbidden** from committing log entries from a *previous* term simply by counting that those entries are now stored on a majority of servers (Raft Paper Section 5.4.2)?
5. Why is a **time-bounded lease** superior to an indefinite lock when clients can crash, and why does a time-bounded lease *still* fail to prevent split-brain writes when the lease holder experiences a stop-the-world GC pause?
6. Explain how pairing every lease or leadership term with a monotonically increasing **Fencing Token (`Generation Clock`)** checked atomically by the storage server makes zombie-leader corruption impossible. Also distinguish **Resource Fencing** from **Node Fencing (`STONITH`)**.

### Part B: Hands-On C# Drill (`Chapter04.cs`)

**The invariant to implement:** Implement `PaxosAcceptor` and `PaxosProposer` proving that once `(ballot: 10, "Rollout-v1")` is chosen on a majority `[0, 1]`, a second proposer with `ballot: 20` talking to majority `[1, 2]` discovers and preserves `"Rollout-v1"`. Also implement `LeaseLockService` and `FencedStorageServer` proving that a paused leader's delayed write with an older fencing token is rejected.

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter04
{
    [TestMethod]
    public void Paxos_phase_one_discovers_already_chosen_value_and_prevents_overwriting_it()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Monotonic_fencing_token_at_storage_layer_rejects_delayed_zombie_leader_writes()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 05: Consistency and Transaction Isolation Models

### Part A: Active Recall Questionnaire

1. Why is describing a database as a **"CA system"** under the CAP theorem a category error, and how does the **PACELC theorem** extend CAP to explain the trade-off between systems like BigTable/Spanner (`PC/EC`) and Dynamo/Cassandra (`PA/EL`) when the network is healthy?
2. Rank **Linearizability**, **Sequential Consistency**, **Causal Consistency**, and **Eventual Consistency** from strongest to weakest. What exact guarantee does Linearizability provide that Sequential Consistency does not, and which is the strongest model achievable while remaining `AP` during a partition?
3. Define the four client-centric session guarantees (**Read-Your-Writes**, **Monotonic Reads**, **Monotonic Writes**, and **Writes-Follow-Reads**) and give a concrete user-visible bug caused by violating Monotonic Reads.
4. Define the six multi-object concurrency anomalies: **Dirty Write**, **Dirty Read**, **Non-Repeatable Read (Read Skew)**, **Lost Update**, **Phantom Read**, and **Write Skew**.
5. Construct a two-transaction timeline showing how **Write Skew** occurs under **Snapshot Isolation (`MVCC`)** even though neither transaction reads uncommitted data and the two transactions update *different* rows.
6. Compare how **Strict Two-Phase Locking (`2PL` with index-range/predicate locks)** and **Serializable Snapshot Isolation (`SSI` on MVCC)** prevent Write Skew, and state the latency/concurrency trade-off between the two.

### Part B: Hands-On C# Drill (`Chapter05.cs`)

**The invariant to implement:** Implement an in-memory `MvccDatabase` supporting point-in-time snapshot reads (`ReadAtSnapshot`) and First-Committer-Wins write checks. Prove that when `enforceSerializableSsi: false`, two concurrent doctors checking on-call counts and updating disjoint keys both commit (producing **Write Skew**), and when `enforceSerializableSsi: true`, checking whether any key in `tx.ReadSet` was modified after `tx.SnapshotTxId` deterministically aborts the second committer!

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter05
{
    [TestMethod]
    public void Snapshot_isolation_permits_write_skew_when_concurrent_transactions_mutate_disjoint_keys()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Serializable_snapshot_isolation_aborts_second_committer_on_read_write_antidependency()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 06: Write-Ahead Logs and Storage Engines

### Part A: Active Recall Questionnaire

1. State the **Write-Ahead Log (WAL) Invariant**. Why is calling `FileStream.Write` without `fsync()` / `Flush(flushToDisk: true)` unsafe against machine power loss, and how does **Group Commit** amortize `fsync` latency across concurrent transactions?
2. Explain **Log Segmentation**, the checkpoint **Low-Water Mark**, and **Log Compaction**. How does deleting sealed WAL segments below the Low-Water Mark differ from key-based Log Compaction?
3. Why must every WAL record header carry a **Checksum (`CRC32C`)**, and what exact action must the WAL recovery loop take when it encounters a record at the tail of the active log whose checksum fails or whose byte length is truncated?
4. Compare the internal disk layout and read/write paths of a **B+Tree** (`4-16 KB` mutable pages, branching factor $200\text{--}500$, ARIES undo/redo recovery) against an **LSM-Tree** (`WAL + in-memory MemTable + immutable SSTables + Sparse Index + Bloom Filter + Compaction`).
5. Why does deleting a key in an LSM-Tree require writing a **Tombstone** rather than erasing bytes on disk, and how do **Size-Tiered Compaction** and **Leveled Compaction** trade off write amplification against disk-space amplification?
6. Distinguish the **Log End Offset (`LEO`)** from the **High-Water Mark (`HWM` / Commit Index)** in a replicated log. Why must a leader never expose entries between `HWM + 1` and `LEO` to client reads?

### Part B: Hands-On C# Drill (`Chapter06.cs`)

**The invariant to implement:** Implement `BinaryWal` (framing each record with `[CRC32(4B) | PayloadLen(4B) | LSN(8B) | KeyLen(4B) | ValLen(4B) | Key | Val]` and truncating corrupt/torn tail bytes during `RecoverValidRecords()`) and `MiniLsmEngine` (buffering mutations in a sorted `MemTable`, writing Tombstones on delete, flushing immutable `SSTables` when full, reading newest-to-oldest, and purging tombstones during `CompactAll()`).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter06
{
    [TestMethod]
    public void Write_ahead_log_validates_crc_and_truncates_torn_tail_record_on_recovery()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Lsm_engine_reads_across_memtable_and_sstables_and_honors_tombstone_deletes()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 07: Indexes and Data Modeling: SQL vs NoSQL

### Part A: Active Recall Questionnaire

1. Explain the physical difference between a **Clustered (Primary) Index** and a **Non-Clustered (Secondary) Index**. Why can a table have at most one Clustered Index, and what is a **Bookmark / Key Lookup**?
2. State the **Leftmost Prefix Rule** and the **Equality-Before-Range Rule** for composite B+Tree indexes. Given a query `WHERE A = 1 AND B > 10 AND C = 'Active'`, should the composite index be `(A, B, C)` or `(A, C, B)`, and why?
3. What is a **Covering Index (`INCLUDE` columns)**, and why do we place non-filter `SELECT` columns in the `INCLUDE` leaf payload rather than adding them as key columns in the internal B+Tree routing nodes?
4. Contrast the **Index Table Pattern** and the **Materialized View Pattern** in partitioned cloud NoSQL stores.
5. Compare **Relational (SQL)**, **Key-Value**, **Document**, **Wide-Column**, and **Graph** databases across schema flexibility, join support, and horizontal write partitioning. What does *"in SQL you model the data first; in NoSQL at scale you model the queries first"* mean?
6. Define Martin Fowler's **Active Record**, **Data Mapper**, **Unit of Work**, **Identity Map**, **Lazy Load** (including why implicit lazy loading causes the $N + 1$ query bug), and the three relational **Inheritance Mapping** strategies (`Single Table / TPH`, `Class Table / TPT`, `Concrete Table / TPC`).

### Part B: Hands-On C# Drill (`Chapter07.cs`)

**The invariant to implement:** Implement `IndexedTable` demonstrating that a non-covering secondary index on `(TenantId, Status)` requires $K$ clustered bookmark lookups for $K$ matching rows whereas a covering index requires `0` bookmark lookups. Also implement `UnitOfWorkSession` with an internal `IdentityMap` proving repeated loads of the same ID return the exact same object reference (`Assert.AreSame`) and changes are flushed only on `Commit()`.

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter07
{
    [TestMethod]
    public void Covering_index_eliminates_clustered_bookmark_lookups()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Identity_map_and_unit_of_work_return_same_reference_and_commit_atomically()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 08: Data Partitioning and Hashing Schemes

### Part A: Active Recall Questionnaire

1. Compare **Horizontal Partitioning (Sharding)**, **Vertical Partitioning**, and **Directory-Based Partitioning**.
2. Contrast **Range-Based Sharding** with **Hash-Based Sharding**. Why does Range-Based Sharding suffer from catastrophic write hotspots when the shard key is a monotonic timestamp or auto-increment ID, and how does Hash-Based Sharding trade away range scans to fix it?
3. Why does even a cryptographically uniform hash function fail to prevent a single shard from melting when a **Celebrity / Hotspot Key** receives 50% of all traffic? Explain how **Key Salting (`Key#00 .. Key#15`)** fixes the write path and what trade-off it imposes on the read path.
4. Why does naive `hash(key) % N` remap $\approx (N - 1)/N$ of all keys when $N$ changes, and how does **Consistent Hashing** reduce the fraction of moved keys to $\approx 1/N$ using a sorted ring and $O(\log M)$ binary search?
5. What **three production problems** occur if each physical server is placed at only a *single* token position on a Consistent Hash Ring, and how do **Virtual Nodes (`Vnodes`)** solve all three?
6. Write the formula for **Rendezvous Hashing (Highest Random Weight, `HRW`)**. Why does Rendezvous Hashing require zero ring data structures, how does it select $R = 3$ distinct physical replicas in one sort pass, and when should you prefer Rendezvous Hashing vs Consistent Hashing with Vnodes?

### Part B: Hands-On C# Drill (`Chapter08.cs`)

**The invariant to implement:** Implement `ConsistentHashRing` (with $V$ virtual nodes per physical node, binary search clockwise lookup, and distinct physical-node filtering in `GetPreferenceList`) and `RendezvousHasher` (`SelectTopK`), proving that adding a 4th node to a 3-node cluster moves only $\approx 25\%$ of keys in both schemes.

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter08
{
    [TestMethod]
    public void Consistent_hash_ring_with_virtual_nodes_moves_only_fractional_keys_and_picks_distinct_physical_replicas()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Rendezvous_hashing_achieves_minimal_disruption_without_ring_state()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 09: Probabilistic Data Structures at Scale

### Part A: Active Recall Questionnaire

1. Explain how a **Bloom Filter** executes `Add(item)` and `MightContain(item)` over an $m$-bit array using $k$ hash functions. Why are **false negatives mathematically impossible (`0%`)**, whereas **false positives** occur with probability $p > 0$?
2. Derive (or state each step of) the Bloom Filter false-positive probability $p \approx (1 - e^{-kn/m})^k$, the optimal hash count $k_{\text{opt}} = \frac{m}{n} \ln 2$, and the required bits per element $\frac{m}{n} \approx -1.44 \log_2 p$. How many bits per key and how many hash functions do you need for a `1%` false-positive rate?
3. What is the **Kirsch-Mitzenmacher Double Hashing optimization** ($h_i(x) = (g_1(x) + i \cdot g_2(x)) \bmod m$), and why can you not delete an item from a standard 1-bit Bloom Filter by clearing its $k$ bits to `0`?
4. Explain the data structure and `Increment` / `EstimateFrequency` operations of a **Count-Min Sketch (`d x w` matrix)**. Why does `EstimateFrequency` return the **minimum** across the $d$ rows, and why can a Count-Min Sketch **never underestimate** the true count?
5. How do you choose the width $w = \lceil e / \varepsilon \rceil$ and depth $d = \lceil \ln(1/\delta) \rceil$ of a Count-Min Sketch, how do you merge two sketches from different servers, and how do you pair a Count-Min Sketch with a Min-Heap to track **Top-$K$ Heavy Hitters**?
6. Explain the intuition behind **HyperLogLog (`HLL`)**: how does counting leading zeros of uniform 64-bit hashes across $m = 2^b$ registers with harmonic-mean averaging estimate `COUNT(DISTINCT)` of billions of items in `12 KB` of RAM?

### Part B: Hands-On C# Drill (`Chapter09.cs`)

**The invariant to implement:** Implement `BloomFilter` (including `CalculateOptimalSizing` and Kirsch-Mitzenmacher double hashing, proving 0% false negatives and $< 2.5\%$ empirical false positives at $p = 0.01$) and `CountMinSketch` (proving zero underestimation and element-wise matrix `MergeInPlace`).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter09
{
    [TestMethod]
    public void Bloom_filter_produces_zero_false_negatives_and_bounded_false_positives()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Count_min_sketch_never_underestimates_and_supports_distributed_matrix_merging()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 10: Networking, Proxies, and Real-Time Protocols

### Part A: Active Recall Questionnaire

1. Walk through the **TLSHandshake**, explaining why TLS uses asymmetric cryptography (`X.509` + `ECDHE`) only during setup and switches to symmetric `AEAD` (`AES-GCM`) for data transfer, what **Forward Secrecy** means, and how **TLS 1.3 (1-RTT)** eliminates a full round trip compared to **TLS 1.2 (2-RTT)**.
2. Contrast a **Forward Proxy** with a **Reverse Proxy** in terms of where each sits, whose identity it hides, and what operational responsibilities each performs.
3. Compare **Layer 4 (Transport)** and **Layer 7 (Application)** load balancers across packet inspection, TCP connection multiplexing, throughput, **Direct Server Return (`DSR`)**, and why L4 load balancing fails to balance multiplexed **HTTP/2 / gRPC** streams across backend pods.
4. Compare the load-balancing routing algorithms: **Round Robin**, **Least Connections**, **Least Response Time**, **Consistent/IP Hash**, and **Power of Two Random Choices (`P2C`)**. Why is `P2C` so effective in distributed proxy fleets where no single proxy has global queue state?
5. Compare the five real-time communication protocols: **HTTP Short Polling**, **HTTP Long-Polling ("Hanging GET")**, **Server-Sent Events (`SSE`)**, **WebSockets**, and **HTTP/2 gRPC Bidirectional Streams**.
6. How does **SSE (`text/event-stream`)** handle automatic client reconnection and missed-message recovery via the **`Last-Event-ID`** header, and when should you choose SSE over WebSockets?

### Part B: Hands-On C# Drill (`Chapter10.cs`)

**The invariant to implement:** Implement `PowerOfTwoLoadBalancer.SelectBackend` (choosing the healthier and less-loaded of two sampled servers) and `SseStreamBuffer` (replaying all events with `EventId > lastEventId` upon client reconnection).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter10
{
    [TestMethod]
    public void Power_of_two_random_choices_selects_less_loaded_healthy_backend()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Server_sent_events_stream_replays_missed_events_after_last_event_id()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 11: Caching Architectures and Failure Modes

### Part A: Active Recall Questionnaire

1. Compare the five read/write caching patterns: **Cache-Aside (Lazy Loading)**, **Read-Through**, **Write-Through**, **Write-Around**, and **Write-Back (Write-Behind)**. Which pattern risks losing acknowledged writes if a cache node crashes, and which pattern prevents bulk write-only logs from polluting the cache?
2. How do you implement an **LRU (Least Recently Used)** cache so that both `Get(key)` and `Put(key, value)` execute in strict **$O(1)$ time**?
3. What is **Scan Pollution** in a pure LRU cache, what is **Frequency Stagnation** in a pure LFU cache, and how does **Window-TinyLFU (`W-TinyLFU`)** use a **Count-Min Sketch** admission filter to solve both?
4. Define the three production cache availability disasters; **Cache Stampede (Thundering Herd)**, **Cache Penetration**, and **Cache Avalanche**; and state the exact engineering defenses for each (`Singleflight`, `Leases`, `Logical Expiration`, `Negative Caching`, `Bloom Filters`, `TTL Jitter`).
5. In **Cache-Aside**, why do we **delete (invalidate)** the cache entry after updating the database rather than **overwriting** the cache entry with the new value (`Update DB -> Set Cache`)?
6. Trace the **Slow-Reader Stale-Set Race Condition** that can still happen even under `Update DB -> Delete Cache`, and explain how Facebook/Meta's **Memcache Lease Token (`GetOrAcquireLease` + `InvalidateOnWrite` + `TrySetWithLease`)** prevents the slow reader from poisoning the cache.

### Part B: Hands-On C# Drill (`Chapter11.cs`)

**The invariant to implement:** Implement an $O(1)$ `LruCache<TKey, TValue>` (using `Dictionary` + `LinkedList`) and `LeaseProtectedCache` (proving that when a concurrent writer calls `InvalidateOnWrite(key)` while a cold-miss reader is fetching from the DB, the reader's subsequent `TrySetWithLease` is rejected).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter11
{
    [TestMethod]
    public void Lru_cache_evicts_least_recently_used_item_in_constant_time()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Cache_lease_token_prevents_slow_reader_from_poisoning_cache_after_concurrent_delete()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 12: Rate Limiting, Throttling, and Resiliency

### Part A: Active Recall Questionnaire

1. Distinguish **Rate Limiting**, **Throttling (`429 Too Many Requests`)**, and **Load Shedding (`503 Service Unavailable`)**. Why can a service with properly functioning per-tenant throttling still suffer a complete meltdown without priority-based Load Shedding?
2. Compare the five rate-limiting algorithms: **Token Bucket**, **Leaky Bucket (Queue vs Meter)**, **Fixed Window Counter**, **Sliding Window Log**, and **Sliding Window Counter**. Which one suffers from a $2\times$ boundary spike, and how does the **Sliding Window Counter** fix that boundary spike using only two integers per key?
3. Write the lazy-refill equation for a **Token Bucket** of capacity $B$ and refill rate $r$ tokens/sec when a request arrives at $t_{\text{now}}$, and explain why distributed Redis rate limiters must execute check-and-decrement inside an **atomic Redis Lua script (`EVALSHA`)** rather than separate client `GET` and `SET` calls.
4. Why does retrying failed RPCs with **deterministic exponential backoff (`1s, 2s, 4s`)** create synchronized hammer waves after an outage, how does **Full Jitter** break up those waves, and how does a **10% Retry Budget** prevent retry amplification?
5. Draw the three states of a **Circuit Breaker (`Closed`, `Open`, `Half-Open`)** and state the exact transition condition along every directed edge.
6. Explain the **Bulkhead Pattern** and **Shuffle Sharding** (including why assigning each tenant a random combination of 4 workers out of 100 isolates noisy-neighbor crashes combinatorially), and explain why a Kubernetes **Liveness Probe** must *never* check downstream database health.

### Part B: Hands-On C# Drill (`Chapter12.cs`)

**The invariant to implement:** Implement `TokenBucketLimiter` (lazy time refill), `SlidingWindowCounterLimiter` (weighted interpolation `(prev * (1 - fraction)) + curr`), and `CircuitBreakerStateMachine` (`Closed -> Open -> Half-Open -> Closed`).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter12
{
    [TestMethod]
    public void Token_bucket_allows_burst_up_to_capacity_and_refills_linearly_over_time()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Sliding_window_counter_prevents_double_burst_at_fixed_window_boundary()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Circuit_breaker_transitions_closed_to_open_to_half_open_and_back_to_closed()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 13: Microservices, Messaging, and Event-Driven Architecture

### Part A: Active Recall Questionnaire

1. What is a **Distributed Monolith**, and why does a chain of $k = 10$ synchronous microservice RPCs (each with `99.9%` availability) degrade end-to-end availability to $\approx 99.0\%$ while amplifying tail latency and thread exhaustion?
2. Draw Richard Rodger's **2x2 Message Interaction Matrix (`Synchronous vs Asynchronous` $\times$ `Consume vs Observe`)** and give a concrete microservice example of each of the four quadrants.
3. Explain **Pattern-Matched Message Routing** and **Transport Independence** from *The Tao of Microservices*. How does routing messages by matching key-value subsets (most-specific rule wins) allow you to insert a new regional or specialized microservice without modifying existing senders or receivers?
4. Define the five cloud messaging patterns: **Queue-Based Load Leveling**, **Priority Queue**, **Sequential Convoy**, **Claim Check**, and **Asynchronous Request-Reply (`202 Accepted` + `Location` + `Retry-After`)**.
5. Explain the **Dual-Write Bug** when updating a database and publishing to a message broker, and trace how the **Transactional Outbox Pattern** paired with an **Idempotent Consumer Inbox** solves it without Distributed Two-Phase Commit (`2PC`).
6. Compare **Saga Choreography** and **Saga Orchestration**, explain why Sagas require **Compensating Transactions** and **Semantic Locks** (due to lacking ACID isolation across steps), and define **CQRS** and **Event Sourcing**.

### Part B: Hands-On C# Drill (`Chapter13.cs`)

**The invariant to implement:** Implement `PatternMatchingRouter` (selecting the matching rule with the highest matched-key specificity count), `OutboxDatabase` + `IdempotentConsumerInbox` (guaranteeing exact-once side effects across duplicate relay deliveries), and `SagaOrchestrator` (running compensating actions in reverse order when step $i$ fails).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter13
{
    [TestMethod]
    public void Pattern_matched_router_selects_most_specific_microservice_rule_without_hardcoded_addresses()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Transactional_outbox_and_idempotent_inbox_deliver_exactly_once_side_effects_under_duplicate_relay()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Saga_orchestrator_runs_compensating_transactions_in_reverse_order_when_step_fails()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 14: Domain-Driven Design and Software Architecture Patterns

### Part A: Active Recall Questionnaire

1. State the five **SOLID Principles** (`SRP`, `OCP`, `LSP`, `ISP`, `DIP`), the **Law of Demeter**, and **Postel's Law**, and give a concrete architectural example of how Dependency Inversion decouples domain logic from SQL/Kafka SDKs.
2. Distinguish **Factory Method**, **Abstract Factory**, **Builder**, **Adapter**, **Bridge**, **Composite**, **Decorator**, **Facade**, **Flyweight**, **Proxy**, **Chain of Responsibility**, **Command**, **Mediator**, **State**, **Strategy**, and **Visitor** in modern backend services.
3. When should you choose Fowler's **Transaction Script** pattern over a rich **Domain Model + Service Layer**?
4. In Eric Evans's **Domain-Driven Design (DDD)**, define **Ubiquitous Language**, **Core vs Supporting vs Generic Subdomains**, and **Bounded Contexts**. Why is forcing one enterprise-wide `Device` or `Customer` class across all microservices an anti-pattern?
5. Contrast a DDD **Entity**, an immutable **Value Object**, and an **Aggregate Root**. State the three transactional rules of an **Aggregate** (and why referencing other Aggregates by ID rather than object reference is mandatory at scale).
6. Define the cloud architecture patterns: **Gateway Routing / Aggregation / Offloading**, **Backends for Frontends (`BFF`)**, **Sidecar** vs **Ambassador**, **Strangler Fig + Anti-Corruption Layer (`ACL`)**, **Valet Key**, **Geode**, and **Regional Privacy Mapping Tables**.

### Part B: Hands-On C# Drill (`Chapter14.cs`)

**The invariant to implement:** Implement a GoF **Composite** boolean applicability expression tree (`IApplicabilityRule`: `EqualsRule`, `AndRule`, `OrRule`, `NotRule`) that evaluates device eligibility locally in memory, and implement `RegionalPrivacyMappingTable` (pseudonymizing EU customer-identifiable strings into deterministic opaque `Guid` tokens).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter14
{
    [TestMethod]
    public void Composite_applicability_expression_evaluates_local_boolean_rules_without_external_service()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Regional_privacy_mapping_table_pseudonymizes_eu_customer_identifiers_before_global_logging()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 15: Distributed File Systems and Coordination: GFS, HDFS, and Chubby

### Part A: Active Recall Questionnaire

1. State the four workload and hardware assumptions of **GFS** and **HDFS**, and explain the roles of the **Master (`NameNode`)**, **ChunkServers (`DataNodes`)**, and **Client Library**.
2. Why did GFS choose a **64 MB chunk size** (`128 MB` in HDFS), and why does storing 100 million 1 KB files in GFS/HDFS exhaust the Master's RAM long before the ChunkServers' disks fill up?
3. Why does the GFS Master persist the file namespace and `file -> chunkHandle` map in its replicated **Operation Log (`EditLog`)**, but **never persists the `chunkHandle -> ChunkServer locations` map to disk**?
4. Trace how GFS separates **Control Flow** from **Pipelined Data Flow** during a write, and explain how **Primary-Chunk Leases (60 seconds)** establish a consistent mutation order across replicas.
5. How does GFS's **Atomic Record Append** work at the 64 MB chunk boundary, why does a partial write failure followed by a client retry leave replicas byte-divergent (`defined interspersed with inconsistent`), and why did **HDFS** simplify this to **Single-Writer WORM**?
6. In **Chubby** (and **ZooKeeper**), explain the difference between **Coarse-Grained** and **Fine-Grained** locks, how **Ephemeral Files + Event Watches** handle leader election without polling, how **KeepAlive Long-Polling + the 45-second Jeopardy grace period** survive a Chubby Master failover, and what a **Sequencer** is.

### Part B: Hands-On C# Drill (`Chapter15.cs`)

**The invariant to implement:** Implement `GfsChunkPrimary.RecordAppend` (assigning sequential byte offsets when a record fits, or padding the chunk to `maxChunkBytes` and signaling `RollToNextChunk: true` when it would overflow) and `ChubbyCoordinator` (enforcing the 45s `Jeopardy` grace window and verifying `ChubbySequencer` generation numbers).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter15
{
    [TestMethod]
    public void Gfs_primary_chunk_server_pads_chunk_at_64mb_boundary_and_assigns_deterministic_append_offsets()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Chubby_session_enters_jeopardy_grace_window_on_master_failover_and_validates_sequencers()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 16: Wide-Column and Dynamo-Family Stores: BigTable, Dynamo, and Cassandra

### Part A: Active Recall Questionnaire

1. Write out the formal 3-tuple data model of **Google BigTable**, explain why row keys are sorted lexicographically into dynamic **Tablets** (and why `Webtable` reversed domain names like `com.cnn.www`), and define **Column Families** and **Locality Groups**.
2. Draw BigTable's **Three-Level Tablet Location Hierarchy (`Chubby File -> Root Tablet -> METADATA Tablets -> User Tablets`)** and show why it can address 2 Exabytes ($2^{34}$ tablets) in at most 3 network hops.
3. Explain the internal read/write architecture of a **BigTable Tablet Server** (`GFS Commit Log + MemTable + SSTables + Block/Scan Cache + Bloom Filters`), and explain why failing over a crashed Tablet Server to a new machine requires **zero copying of SSTable files**.
4. Why did Amazon design **Dynamo** as an `AP` (`PA / EL`) "always-writable" store instead of copying BigTable's `CP` single-owner tablet architecture, and what **six core distributed systems mechanisms** work together inside Dynamo?
5. How does **Apache Cassandra** combine the best of **Dynamo** and **BigTable**? Specifically, in a Cassandra table with `PRIMARY KEY ((tenant_id, device_id, bucket_day), recorded_at, event_id)`, what is the exact role of the **Partition Key** versus the **Clustering Key**?
6. Compare **BigTable**, **Dynamo**, and **Cassandra** across CAP/PACELC posture, cluster symmetry, partitioning scheme, range-scan capability, and write conflict resolution (`Single Tablet Server` vs `Vector Clocks` vs `LWW / Paxos LWT`).

### Part B: Hands-On C# Drill (`Chapter16.cs`)

**The invariant to implement:** Implement `BigTableLocationHierarchy` (resolving a row key in 3 logical hops on a cold lookup and 0 hops once cached at the client) and `CassandraWideColumnStore` (hashing `partitionKey` into a partition dictionary and storing rows sorted by `clusteringTimestamp` for fast range scans).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter16
{
    [TestMethod]
    public void Bigtable_three_level_hierarchy_resolves_user_tablet_server_in_at_most_three_hops_and_caches()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Cassandra_compound_key_hashes_partition_key_and_sorts_clustering_key_for_range_queries()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 17: Distributed Streaming and Log-Centric Systems: Kafka

### Part A: Active Recall Questionnaire

1. How does **Apache Kafka** organize Topics, Partitions, and Offsets on disk? Why is message ordering guaranteed **strictly within a single Partition** rather than across a Topic, and how does Producer Key Hashing preserve per-entity causal order?
2. Explain how Kafka's **Consumer Group** abstraction unifies **Competing Consumers (Queue)** and **Publish-Subscribe (Observe)** semantics. Why does scaling a Consumer Group to 20 consumer instances on a topic with 12 partitions leave 8 instances completely idle?
3. Contrast **Eager (Stop-the-World) Rebalancing** with **Cooperative Sticky (Incremental) Rebalancing** when a consumer joins or leaves a Consumer Group.
4. Define the **Partition Leader**, **Followers**, the **In-Sync Replica (`ISR`) set**, and the **`acks = 0 / 1 / all` + `min.insync.replicas = 2`** durability contract.
5. What is the **High-Water Mark (`HWM`)** in Kafka, what was the classic **`HWM` log-truncation race condition** on follower restart/failover, and how did introducing the monotonic **Leader Epoch (`KIP-101`)** fix it?
6. Explain the **two OS/hardware optimizations** that make Kafka orders of magnitude faster than traditional brokers (`OS Pagecache` + **`sendfile()` zero-copy DMA**), how **Idempotent Producers (`PID + SeqNum`)** prevent duplicate appends on retry, and why Kafka migrated from **ZooKeeper** to **KRaft (`KIP-500`)**.

### Part B: Hands-On C# Drill (`Chapter17.cs`)

**The invariant to implement:** Implement `KafkaPartitionLeader` (deduplicating idempotent producer retries by `(producerId, sequenceNumber)` and exposing records to consumers *only* up to `_highWaterMark` as followers acknowledge replication) and `KafkaConsumerGroupAssignor` (proving consumers beyond `partitionCount` receive 0 partitions).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter17
{
    [TestMethod]
    public void Kafka_partition_exposes_records_only_up_to_high_water_mark_and_deduplicates_idempotent_producer()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Consumer_group_assignor_caps_active_consumers_at_partition_count()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 18: Capacity Planning and Asymptotic Analysis

### Part A: Active Recall Questionnaire

1. Distinguish **worst-case**, **average-case**, and **amortized** time complexity using a concrete storage or collection example, and explain why **CPU L1/L2 cache locality** often makes a contiguous array scan faster than $O(\log N)$ pointer chasing for small-to-medium $N$.
2. From memory, state the approximate physical latencies (and relative orders of magnitude) for: **L1 cache**, **Main RAM**, **LZ4 compression of 1 KB**, **NVMe 4 KB random read**, **Intra-datacenter RTT**, **HDD seek**, and **Cross-continent RTT (`US <-> EU`)**.
3. State the power-of-ten time shortcuts for **1 day** ($\approx 10^5\text{ s}$), **1 year** ($\approx 3 \times 10^7\text{ s}$), and **3 years** ($\approx 1{,}000\text{ days} \approx 10^8\text{ s}$), and convert `500 million requests/day` into average RPS in one step.
4. State **Little's Law ($L = \lambda \times W$)** and use it to compute the required connection-pool / in-flight concurrency when a service handling `10,000 RPS` calls a dependency whose average latency degrades from `10 ms` to `500 ms`.
5. Using the thread-capacity and **33% utilization headroom formula ($\text{Safe RPS} \approx 30{,}000 / T_{\text{ms}}$)**, calculate how many application servers are needed to handle a peak load of `6,000 RPS` when each request takes `150 ms`.
6. Execute a complete 3-year storage and shard-count estimate for a service ingesting `5,000 writes/sec` (average) of `4 KB` JSON records with Replication Factor $N = 3$ and `1 TB` maximum capacity per shard.

### Part B: Hands-On C# Drill (`Chapter18.cs`)

**The invariant to implement:** Implement `CapacityMath` (`ComputeLittlesLawInFlight`, `ComputeRequiredServers`, and `ComputeThreeYearReplicatedStorageBytes`) and verify the exact numerical invariants.

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter18
{
    [TestMethod]
    public void Littles_law_and_headroom_formulas_size_concurrency_servers_and_multi_year_storage()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 19: Trees, Tries, and Spatial Indexing

### Part A: Active Recall Questionnaire

1. Why does inserting sorted keys into an unbalanced **Binary Search Tree (`BST`)** degrade lookup time from $O(\log N)$ to $O(N)$, and how do **AVL Trees** (`BF in {-1, 0, +1}`, 4 rotations `LL, RR, LR, RL`) and **Red-Black Trees** (color invariants, $\le 2$ / $\le 3$ rotations) trade off lookup height against mutation cost?
2. State the visiting order and space complexity of **Preorder**, **Inorder** (including iterative stack-based Inorder), **Postorder**, and **Level-Order (`BFS`)** traversals, and explain how to **validate a BST in $O(N)$ time**, find the **Inorder Successor in $O(H)$ time**, and find the **$N$-th Highest Element** via Reverse Inorder.
3. Explain how to **convert a Binary Tree to a Doubly Linked List in-place** in $O(N)$ time and $O(1)$ heap allocations, how to **print the Tree Perimeter (Boundary Traversal)** without duplicating corner leaves, and how to **connect all level-order siblings in $O(1)$ extra space** using `current` and `last` pointers.
4. Compare a **Standard Prefix Trie** with a path-compressed **Radix (Patricia) Tree**. Why do exact lookup, `StartsWith` prefix search, **Longest-Prefix Match (`LPM`)**, counting all words, and **sorting an array of strings via a Trie** run without full-key comparisons across $N$ items?
5. Why does a standard 1D composite B+Tree index on `(Latitude, Longitude)` perform poorly on 2D bounding-box queries, and how does a **Point Quad-Tree** recursively subdivide 2D space (`NW, NE, SW, SE`) and prune non-intersecting subtrees during `QueryRange`?
6. Explain how a **Geohash** interleaves latitude and longitude bits into a 1D string prefix so any B+Tree or LSM-Tree can execute 2D spatial proximity queries, and why you must query **all 9 neighbor cells** to avoid boundary blind spots.

### Part B: Hands-On C# Drill (`Chapter19.cs`)

**The invariant to implement:** Implement `AvlTree` (with `LL, RR, LR, RL` rebalancing rotations, `IsValidBst()`, and `FindInorderSuccessor()`), `BinaryTreeAlgorithms` (`TraverseInOrderIterative`, `ConvertBinaryTreeToDoublyLinkedList`, and `GetTreeBoundaryPerimeter`), `PrefixTrie` (supporting sorted `GetWordsWithPrefix` autocomplete, `LongestPrefixMatch`, `CountWords`, and `SortArrayWithTrie`), and `QuadTree` (recursive 4-quadrant subdivision and bounding-box `QueryRange`).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter19
{
    [TestMethod]
    public void Avl_tree_maintains_logarithmic_height_on_sorted_insertions_and_finds_inorder_successor()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Prefix_trie_supports_autocomplete_and_longest_prefix_match_routing()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Quad_tree_subdivides_dense_quadrants_and_prunes_non_intersecting_regions_on_range_query()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Binary_tree_to_doubly_linked_list_and_boundary_perimeter_and_sibling_links_preserve_order()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 20: Hash Tables, Heaps, and Graphs

### Part A: Active Recall Questionnaire

1. Compare **Separate Chaining** (including how .NET's `Dictionary<TKey, TValue>` uses contiguous `buckets[] + entries[]` struct arrays instead of heap linked-list nodes) with **Open Addressing (Linear Probing)**, and explain the seven classic hashing drills (disjoint arrays, symmetric pairs, `a + b == c + d`, zero-sum subarray prefix map, first non-repeating integer, linked-list deduplication, and list union/intersection).
2. In Open Addressing with Linear Probing, why does deleting a key require writing a **`TOMBSTONE`** sentinel instead of marking the slot `EMPTY`, and how does **Robin Hood Hashing** reduce probe-length variance using Probe Sequence Length (`PSL`)?
3. What is a **Hash-Flooding Denial-of-Service (`DoS`) Attack**, and how does keyed **SipHash** (or per-process randomized hash seeds) prevent it?
4. Write the 0-based array index formulas for `Parent(i)`, `LeftChild(i)`, and `RightChild(i)` in a **Binary Heap**, and prove why **Floyd's bottom-up `Heapify`** builds a heap from an unsorted array of $N$ elements in **$O(N)$ linear time** rather than $O(N \log N)$.
5. Compare **Selection Sort**, **Insertion Sort**, **Bubble Sort**, **Merge Sort**, and **QuickSort** (contrasting **Hoare's two-pointer partition scheme** with **Lomuto's partition scheme** and median-of-three pivot selection), and explain how **External $K$-Way Merge Sort** sorts a `10 TB` file using only `4 GB` of RAM.
6. Compare **3-Color DFS Cycle Detection**, **DFS Backtracking Path Counting (`CountPaths`)**, and **Kahn's In-Degree BFS Queue Algorithm** for **Topological Sort** on a Directed Graph (`DAG`), and state when to use **BFS**, **Dijkstra**, **A\***, **Bellman-Ford**, and **Union-Find (`Disjoint Set`)**.

### Part B: Hands-On C# Drill (`Chapter20.cs`)

**The invariant to implement:** Implement `RobinHoodTable` (open addressing with `Tombstone` deletion and PSL swapping), `BinaryMinHeap.HeapifyInPlace` ($O(N)$ bottom-up `SiftDown`), `SortingAlgorithms` (`SelectionSort`, `InsertionSort`, `BubbleSort`, `MergeSort`, `QuickSortHoare`, `QuickSortLomuto`), `HashSetDrills`, `GraphAlgorithms` (`TopologicalSortKahn`, `DijkstraShortestPaths`, `CountPathsInDag`), and `DisjointSet`.

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter20
{
    [TestMethod]
    public void Robin_hood_hash_table_preserves_lookup_across_tombstone_deletions()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Floyds_bottom_up_heapify_converts_unsorted_array_to_min_heap_in_linear_time()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Kahns_topological_sort_orders_package_dag_and_detects_cycles()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Dijkstra_and_union_find_compute_shortest_paths_and_connected_components()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Classic_sorting_algorithms_and_hash_table_drills_produce_exact_results()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 21: Dynamic Programming, Greedy Algorithms, and Coding Patterns

### Part A: Active Recall Questionnaire

1. Define the two mathematical properties required for **Dynamic Programming (`Optimal Substructure` and `Overlapping Subproblems`)**, and compare **Top-Down Memoization** against **Bottom-Up Tabulation**.
2. In the **1D Space-Optimized 0/1 Knapsack** algorithm (`int[] dp = new int[capacity + 1]`), why must the inner loop iterate capacity **$c$ backward from `capacity` down to `weight[i]`**, and what problem does the algorithm solve if you iterate $c$ forward instead?
3. What is the **Greedy Choice Property**? Explain why sorting by **Earliest Finish Time** is provably optimal for **Interval / Activity Scheduling**, how **Prim's MST** (`Min-Heap` of crossing edges) and **Kruskal's MST** (`Union-Find` over sorted edges) exploit the **Greedy Cut Property**, and why Greedy fails on **0/1 Knapsack** and non-canonical **Coin Change (`[1, 3, 4]` for target `6`)**.
4. Explain the core invariant and time/space complexity of: **Two Pointers**, **Fast & Slow Pointers (Floyd's Cycle Detection)**, **Sliding Window**, **Merge Intervals**, **Cyclic Sort**, and **Bitwise XOR**.
5. Explain how the **Two Heaps (`Max-Heap + Min-Heap`)** pattern maintains the running **Median of a Number Stream** in $O(\log N)$ per insert and $O(1)$ per query, and how the **$K$-Way Merge** pattern merges $K$ sorted streams in $O(N \log K)$.
6. Explain why a **Monotonic Decreasing Stack** finds the **Next Greater Element** for all $N$ elements of an array in **$O(N)$ amortized time** despite having a `while` loop nested inside a `for` loop, and explain **Modified Binary Search on the Answer Space**.

### Part B: Hands-On C# Drill (`Chapter21.cs`)

**The invariant to implement:** Implement `SolveZeroOneKnapsack` (1D backward tabulation), `MaxNonOverlappingIntervals` (Earliest-Finish Greedy), `MergeOverlappingIntervals`, `PrimMinimumSpanningTreeWeight` (Min-Heap of crossing edges), `NextGreaterElements` (Monotonic Stack), `LongestSubstringWithAtMostKDistinct` (Sliding Window), and `StreamingMedianTracker` (Two Heaps).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter21
{
    [TestMethod]
    public void One_dimensional_zero_one_knapsack_maximizes_profit_and_greedy_fails_on_indivisible_items()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Greedy_interval_scheduling_and_merge_intervals_handle_time_windows_optimally()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Monotonic_stack_and_sliding_window_and_two_heaps_execute_in_optimal_bounds()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 22: Control Plane vs Data Plane and Cell-Based Architectures

### Part A: Active Recall Questionnaire

1. Contrast the **Control Plane** and the **Data Plane** across traffic volume (QPS), latency SLA (`p99`), CAP/PACELC posture, dependency footprint, and target availability (`99.9%` vs `99.999%`). Why is it mathematically impossible for a `99.999%` Data Plane to make synchronous hot-path calls to a `99.9%` Control Plane?
2. Define **Static Stability** for a Data Plane, and give three examples of "hidden runtime coupling" that violate static stability during a central Control Plane outage.
3. Walk through the **Zero-Dependency Stamp Data Plane pattern**: how do **Periodic Full Snapshots**, **Incremental Change Queues**, **Stamp-Local Storage**, and **Atomic In-Memory Reference Swaps** allow a newly booted Data Plane pod to hydrate 100% of its policy state even when the Control Plane is completely dead?
4. In `StampDataPlaneCache.HydrateAndApplyDeltas`, why must the hydration loop check **both** `delta.SequenceVersion <= baselineSnapshot.SnapshotVersion` and `existing.Version >= delta.SequenceVersion` when replaying delta events from the change queue?
5. What are the four engineering invariants of a **Cell-Based (Deployment Stamp) Architecture**, and how does **Shuffle Sharding** ($k = 4$ endpoints out of $N = 100$ workers) reduce co-tenant poison-pill blast radius to $\frac{1}{\binom{100}{4}} \approx 0.0000255\%$?
6. State the four Principal governance rules for **Public REST APIs and Multi-Language SDKs**: contract-first OpenAPI versioning, `Idempotency-Key` headers, asynchronous **Long-Running Operations (`HTTP 202 Accepted` + `Operation-Location` + `Retry-After`)**, and opaque continuation-token pagination wrapped in idiomatic language SDKs (`C#, Java, Python, Go, TypeScript`).

### Part B: Hands-On C# Drill (`Chapter22.cs`)

**The invariant to implement:** Implement `StampDataPlaneCache.HydrateAndApplyDeltas` (reconciling baseline snapshots with out-of-order and overlapping incremental change-queue events via monotonic version guards) and `ShuffleShardAllocator` (computing $\binom{N}{k}$ virtual shard combinations and assigning deterministic disjoint endpoint subsets per tenant).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter22
{
    [TestMethod]
    public void Zero_dependency_stamp_cache_hydrates_snapshot_and_reconciles_overlapping_change_queue_deltas_monotonically()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Shuffle_sharding_allocates_distinct_endpoints_and_reduces_full_overlap_combinatorially()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 23: Multi-Tenant Isolation and Noisy-Neighbor Economics

### Part A: Active Recall Questionnaire

1. Compare the four tiers of the **Multi-Tenancy Isolation Spectrum**: **Shared Pool (`TenantId` column)**, **Pod/Stamp-Partitioned**, **Provider-Hosted Silo**, and **Customer-Hosted Compute & Storage (`Managed Resource Group / BYOS`)**.
2. Explain how the **Customer-Hosted (`Managed Resource Group`)** model solves data sovereignty, noisy-neighbor isolation, and direct COGS cost attribution simultaneously.
3. How does a centralized SaaS Control Plane safely provision and operate containers and databases inside thousands of customer subscriptions using **Scoped Delegated Identities (`OIDC / Managed Identity`)** and **Asynchronous Provisioning Sagas (`HTTP 202`)** without storing long-lived customer credentials?
4. Explain the physics of **Multi-Tenant Fan-Out Amplification**: why does ingesting a `20 MB` telemetry blob spanning **200 distinct tenants** cause an **87% throughput drop** (`15.4 MB/s -> 2.0 MB/s`) compared to a single-tenant blob?
5. How do **Claim-Check byte-range splitting**, **in-memory tenant batch coalescing**, and **`ArrayPool<byte>` buffer pooling** work together to deliver a **$78\times$ throughput gain (`1.3 MB/s -> 102.2 MB/s`)**, and what Kafka offset-commit rule prevents data loss while batches are buffered in RAM?
6. How do **Stratified Tenant-Tier Worker Pools**, **per-tenant Token Buckets**, and **Deficit Round-Robin (`DRR`) Fair Queuing** prevent a Pareto Mega-Tenant (`60%` of traffic) from starving small and medium tenants?

### Part B: Hands-On C# Drill (`Chapter23.cs`)

**The invariant to implement:** Implement `TenantBatchCoalescer` (buffering small per-tenant payloads across multiple mixed input blobs until a byte threshold or max-age SLA is reached, eliminating per-blob fan-out flush amplification) and `DeficitRoundRobinScheduler<T>` (serving active tenant queues by per-round `Quantum` credits so a noisy mega-tenant cannot starve a quiet tenant).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter23
{
    [TestMethod]
    public void Tenant_batch_coalescer_eliminates_per_blob_fanout_flush_amplification()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Deficit_round_robin_scheduler_prevents_noisy_mega_tenant_from_starving_small_tenant()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 24: Columnar Storage and Lakehouse Pipelines

### Part A: Active Recall Questionnaire

1. Contrast **Row-Oriented (`OLTP`)** storage with **Column-Oriented (`OLAP`)** storage when scanning `3` columns out of a `50`-column telemetry table across `50 billion` rows. What three mechanical advantages make columnar storage orders of magnitude faster for analytical and security-graph queries?
2. Sketch the internal anatomy of an **Apache Parquet** file (`Header`, `Row Groups`, `Column Chunks`, `Data Pages`, and `File Footer`), and explain why a Parquet reader **always reads the Footer at the end of the file first**.
3. Explain how Parquet's three columnar encodings (**Dictionary Encoding**, **Run-Length Encoding (`RLE`) & Bit-Packing**, and **Delta Encoding**) work, and how Google Dremel's **Definition Levels (`D-Level`)** and **Repetition Levels (`R-Level`)** shred deeply nested/repeated JSON trees into flat columns losslessly.
4. Explain how **Row-Group `Min/Max` Statistics** and **Column-Chunk Split-Block Bloom Filters** enable **Predicate Pushdown** to skip `99%` of disk I/O without decompressing pages, and why Bloom Filters are essential for unsorted high-cardinality GUID columns like `DeviceId` or `TraceId`.
5. What is the **Small-File Problem** in streaming lakehouse ingestion (when writing thousands of `50 KB` Parquet files per hour from Kafka/Event Hubs), and how do **Ingestion-Time Micro-Batch Coalescing** and **Asynchronous Bin-Packing Compaction (`OPTIMIZE`)** solve it?
6. How do **Modern Lakehouse Table Formats (`Delta Lake` and `Apache Iceberg`)** use an **atomic Transaction Log (`_delta_log` / Manifest tree)**, **MVCC Snapshot Isolation**, **Optimistic Concurrency Control**, and **Z-Order Clustering** to provide ACID transactions and multi-dimensional pruning on top of cloud object storage?

### Part B: Hands-On C# Drill (`Chapter24.cs`)

**The invariant to implement:** Implement `ColumnarRleDictionaryChunk` (encoding string columns via a Dictionary + RLE runs and pruning non-matching Row Groups via `Min/Max` bounds and a Bloom Filter without decoding runs) and `DeltaTransactionLog` (proving lock-free MVCC snapshot isolation across concurrent file compaction commits).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter24
{
    [TestMethod]
    public void Columnar_dictionary_rle_chunk_compresses_runs_and_prunes_row_groups_via_minmax_and_bloom_pushdown()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Delta_lake_transaction_log_provides_snapshot_isolation_across_concurrent_file_compaction()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 25: Policy Engines, DSLs, and Cross-Language Runtimes

### Part A: Active Recall Questionnaire

1. Why does hardcoding dynamic business rules in procedural code, calling an external rules microservice over RPC on the hot path, or executing user-supplied Turing-complete scripts (`JS / Lua`) fail at Tier-1 scale?
2. Walk through the four-stage architecture that enables an in-memory **Applicability and Driver Ranking Engine** to evaluate **10,000 targeting expressions in `< 30 ms` (`< 3 ms` per 1,000 expressions)** and match **`1,000+` client hardware attributes against `130,000+` catalog rules**: Symbol Interning, Bloom Filter + Inverted Index Pre-Screening, Short-Circuit Flyweight AST Evaluation, and Multi-Criteria Lexicographical Ranking.
3. How does a **Formal Domain Grammar and Static Compiler** guardrail **multivariate ML experimentation** at publish time (`Syntactic/Type Verification`, `Static Feature-Slot Mutual Exclusion`, and `Runtime Fallback Guardrails`)?
4. What two mathematical properties do declarative **Policy-as-Code** languages (`Open Policy Agent / Rego`, `Google CEL`, `AWS Cedar`) guarantee that general-purpose scripting languages do not, and what is **Partial Evaluation**?
5. Why does embedding an in-process, zero-GC **Rust** policy engine (`Regorus`) inside a **.NET / C#** worker outperform both an out-of-process `localhost` Go sidecar and a pure managed-heap interpreter on high-QPS hot paths?
6. State the critical safety invariants of a **.NET `[LibraryImport]` <-> `Rust extern "C"` FFI boundary**: `SafeHandle` lifetime ownership, zero-copy stack-pinned `ReadOnlySpan<byte>` UTF-8 slices (vs `string` marshaling), and why every Rust `extern "C"` entrypoint must wrap its body in `std::panic::catch_unwind`.

### Part B: Hands-On C# Drill (`Chapter25.cs`)

**The invariant to implement:** Implement `HighSpeedApplicabilityEngine` (combining `_hardwareBloomFilter` + `_invertedHardwareIndex` pre-screening to prune `> 99%` of unrelated catalog rules, short-circuit `LogicalAndNode` / `AttributeCheckNode` AST evaluation, and deterministic multi-criteria lexicographical driver ranking across `FeatureScore`, `HardwareRank`, and `DriverVersionSortable`).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter25
{
    [TestMethod]
    public void Inverted_index_and_bloom_filter_prune_catalog_and_multi_criteria_ranker_selects_best_driver()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 26: Testing in Production, Safe Rollouts, and Emergency Kill-Switches

### Part A: Active Recall Questionnaire

1. Why can a pre-production staging environment never catch all planet-scale bugs across 100,000+ tenants and 1.4B devices, and why do privacy regulations (`GDPR / EUII`) prohibit simply copying production customer data into staging?
2. Explain the three pillars of **Testing in Production (`TiP`)** on streaming data planes: **Shadow Consumer Groups** on Kafka/Event Hubs, **Privacy-Safe Synthetic Workload Generation via Stratified Tenant-Tier Extrapolation**, and **Automated Cryptographic Parity Verifiers**.
3. What are the three rules governing **Progressive Delivery across Concentric Deployment Rings (`Ring 0 Canary -> Ring 1 -> Ring 2 -> Ring 3 Paired Regions`)**? Why must a ring health gate require **both** a minimum bake duration $T_{\text{bake}}$ and a minimum sample count $N_{\min}$ before advancing?
4. Walk through the three stages of a secure **Asynchronous (`HTTP 202`) Supply-Chain & Content Publishing Pipeline**: Quarantine `SHA-256` content-hash verification & deduplication, isolated container malware detonation, and HSM code signing + **Peer-to-Peer (`P2P`) Delivery Optimization** chunk metadata.
5. Why is revoking a toxic update through the **normal content publishing pipeline (`30+ minutes`)** far too slow during an active global incident?
6. Explain why an **Out-of-Band `< 5-Second` Emergency Kill-Switch** must halt all three client lifecycle phases simultaneously: **Stage A (`Scan / Discovery`)**, **Stage B (`CDN Download / Valet-Key Revocation`)**, and **Stage C (`Pre-Install / Pre-Reboot Commit Check`)**. What happens at 3:00 AM if Stage C is omitted?

### Part B: Hands-On C# Drill (`Chapter26.cs`)

**The invariant to implement:** Implement `EmergencyKillSwitchAndRingGate` (proving that triggering the kill-switch halts `ScanDiscovery`, `ContentDownload`, and `PreInstallCommit` immediately, and that `EvaluateRingProgression` auto-triggers rollback on SLI breach while holding progression until both minimum sample count and minimum bake minutes are met).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter26
{
    [TestMethod]
    public void Emergency_kill_switch_halts_scan_download_and_pre_install_commit_stages_immediately()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Canary_ring_gate_holds_until_both_minimum_sample_size_and_bake_time_are_met()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 27: Production AI and Agentic Engineering Systems

### Part A: Active Recall Questionnaire

1. Compare a traditional deterministic microservice RPC with an **LLM / Agentic Reasoning Step** across determinism, latency/cost, context capacity, failure modes, and blast radius of side effects. What deterministic systems guardrails must wrap every production LLM component?
2. Explain the architecture of the **Model Context Protocol (`MCP`)**: how do **MCP Hosts**, **MCP Clients**, and **MCP Servers** communicate over `stdio` or `HTTP/SSE`, and what are the distinct roles of the three MCP primitives (**Resources**, **Tools**, and **Prompts**)?
3. State the four Principal engineering rules for designing **hallucination-resistant, secure MCP Tools** (bounded/typed parameters vs raw unbounded query strings, self-correcting validation error messages, output token truncation/pagination ceilings, and strict Read-Only vs Mutating credential separation).
4. Why does naive fixed-token chunking of human wikis fail during automated incident investigation, and what four structural invariants turn human Troubleshooting Guides (`TSGs`) into **LLM-Optimized Structured Knowledge Bases**?
5. Why does **Dense Vector Embedding Search** alone struggle on exact hexadecimal error codes (`0x80072EE2`) and symbol names, why does **Lexical Search (`BM25`)** alone struggle on conceptual paraphrases, and how does **Reciprocal Rank Fusion (`RRF`, $\sum \frac{1}{k + \text{rank}_m(d)}$ with $k = 60$)** combine Lexical, Semantic, and Code Graph retrieval without score-calibration fragility?
6. What are the four deterministic engineering primitives of a **Specification-Driven Multi-Agent Workflow** that allow multiple autonomous coding/review agents to collaborate on a repository without working-tree collisions or context amnesia?

### Part B: Hands-On C# Drill (`Chapter27.cs`)

**The invariant to implement:** Implement `McpToolSchemaGatekeeper.ValidateToolCall` (blocking mutating tools for read-only agents, rejecting hallucinated parameter names, and enforcing `MinIntValue / MaxIntValue` bounds with self-correcting error messages) and `HybridRrfRetriever.FuseRankedLists` (computing Reciprocal Rank Fusion $\sum \frac{1}{k + \text{rank}_m(d)}$ across lexical and semantic rankings).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter27
{
    [TestMethod]
    public void Mcp_tool_schema_gatekeeper_rejects_mutating_calls_from_readonly_agents_and_catches_hallucinated_args()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Reciprocal_rank_fusion_ranks_consensus_document_above_single_retriever_outlier()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 28: System Design Methodology and Architecture Reviews

### Part A: Active Recall Questionnaire

1. List the **six phases** of the Principal System Design Methodology in order, along with the target time allocation and primary output artifact of each phase in a 45-minute session.
2. Distinguish a **Depth-Oriented System Design** from a **Breadth-Oriented System Design**, giving two concrete examples of each and explaining how your time allocation shifts between Phase 4 and Phase 5.
3. During Phase 5 (**Three-Tier Deep Dive**), what specific questions must you answer for the **Application Tier**, the **Cache Tier**, and the **Storage Tier**?
4. In the worked **Atlas** design, explain why assigning devices to phased canary cohorts via deterministic hashing (`Hash(RolloutId + ":" + DeviceId) % 100 < targetPercent`) is superior to inserting 500 million assignment rows into a database table.
5. Trace end-to-end how Atlas handles a **200 MB firmware upload and rollout to 50M European devices** while enforcing **GDPR data residency (`Privacy Mapping Tables`)**, avoiding control-plane binary bottlenecks (`Valet Key + Claim Check`), hydrating **Zero-Dependency Stamp Caches**, and preventing split-brain rollout progression (`etcd Lease + Fencing Token`).
6. How does Atlas survive a simultaneous reconnect storm of 10 million devices after a regional cellular outage without suffering a **metastable retry storm** or dropping `P0` rollback commands?

### Part B: Hands-On C# Drill (`Chapter28.cs`)

**The invariant to implement:** Implement `AtlasRolloutPlane.IsDeviceInCanaryBucket` (proving deterministic hashing `% 100` is strictly monotonic as `targetPercent` expands from `1% -> 10% -> 50%`) and `ShouldAdmitRequest` (enforcing priority load shedding across `P0`, `P1`, and `P2` tiers as host utilization climbs).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter28
{
    [TestMethod]
    public void Atlas_canary_cohort_hasher_is_monotonic_as_rollout_phase_expands()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Priority_load_shedder_preserves_p0_rollback_traffic_when_host_utilization_is_critical()
    {
        throw new NotImplementedException();
    }
}
```

---

## Section 29: Principal Leadership and Behavioral Execution

### Part A: Active Recall Questionnaire

1. Outline the four parts of the **60-Second Reverse-Chronological Career Pitch ("Tell me about yourself")** and explain why walking *backward* from your most recent Principal scope beats walking forward from university.
2. State the **Principal STAR Framework (`Situation`, `Task`, `Action`, `Result + Reflection`)**, including the **3-Pillar breakdown of the `Action` section** and how to use `"I"` vs `"we"` accurately without undercutting your leadership or taking false credit.
3. Using **Case Study 1 (`Local Applicability Expression Engine`)** and **Case Study 6 (`Zero-Dependency Stamp Data Plane & Customer-Hosted MRG`)**, explain the architectural rule governing **Control Plane / Tier-2 vs Data Plane / Tier-1 dependencies** and how to resolve cross-organizational priority deadlocks.
4. Using **Case Study 2 (`Pragmatic Descoping of Automatic File Encryption`)** and **Case Study 4 (`Service Convergence Cancellation`)**, explain how a Principal engineer uses customer evidence and ROI/risk analysis to push back on speculative scope and halt low-ROI migrations.
5. Using **Case Study 3 (`EU GDPR Privacy Mapping Tables`)**, **Case Study 5 (`Deployment Journal & ML Experimentation Grammar`)**, **Case Study 7 (`78x Telemetry Pipeline & 87% Multi-Tenant Fan-Out Analysis`)**, and **Case Study 8 (`1.4B-Device Driver Ranking & < 5s Emergency Kill-Switch`)**, explain how to turn regulatory, performance, or scale bottlenecks into durable architectural patterns.
6. State the **60-Second Direct-Then-Evidence Rule** for answering **Philosophical Questions**, the five rules of **Reverse Interviewing**, and the **Bold Closing Question** to ask at the end of an interview loop.

### Part B: Hands-On C# Drill (`Chapter29.cs`)

**The invariant to implement:** Implement `PrincipalArchitectureGovernance.IsValidDependency` (forbidding any Tier-$N$ service from taking a synchronous hot-path dependency on a lower-SLA Tier-$M$ service where $M > N$) and `ShouldProceedWithConvergence` (rejecting platform convergence proposals whose Tier-1 regression risk exceeds threshold or whose payback period exceeds the strategic horizon).

**Starter stub:**

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter29
{
    [TestMethod]
    public void Tier_dependency_validator_rejects_synchronous_hot_path_calls_to_lower_tier_services()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    public void Convergence_roi_evaluator_recommends_cancelling_migration_when_payback_exceeds_horizon()
    {
        throw new NotImplementedException();
    }
}
```
