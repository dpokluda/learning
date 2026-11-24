# 15: Distributed File Systems and Coordination: GFS, HDFS, and Chubby

Every concept we built from first principles in Parts I through III; write-ahead logs, checksums, heartbeats, leases, fencing tokens, Paxos consensus, and separating control flow from data flow; was forged in the early 2000s to solve a concrete problem: **how do you store petabytes of files across thousands of cheap commodity servers that fail every single day, without losing data or bottlenecking on a single coordinator?**
Two papers published by Google in 2003 and 2006 changed the trajectory of distributed systems engineering: Sanjay Ghemawat, Howard Gobioff, and Shun-Tak Leung's **The Google File System (GFS)**[^gfs-2003] (along with its open-source descendant, the **Hadoop Distributed File System, HDFS**[^hdfs-2010]) and Mike Burrows's **The Chubby Lock Service for Loosely-Coupled Distributed Systems**[^chubby-2006] (along with its open-source descendant, **Apache ZooKeeper**[^zookeeper-2010]).
Together, GFS and Chubby established the canonical cloud storage pattern: **pair a small, strongly consistent Paxos/Raft coordination cell (for leader election, configuration, and coarse-grained leases) with a massively scalable data plane that separates metadata control traffic from bulk byte streaming.**
This module dissects GFS, HDFS, and Chubby from their primary papers, showing both why their designs succeeded and which trade-offs forced later architectural evolutions.

> **Prerequisite:** [Module 03: Replication, Quorums, and Anti-Entropy](./03-replication-quorums-and-anti-entropy.md), [Module 04: Consensus, Leases, and Split-Brain](./04-consensus-leases-and-split-brain.md), and [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md).

---

## 1. GFS and HDFS: design assumptions and high-level architecture

Traditional POSIX distributed file systems (like NFS) were designed around assumptions that break at warehouse scale: they assumed hardware failures are rare anomalies, files are mostly kilobytes to a few megabytes, and applications mutate files via small random in-place overwrites.
GFS (and subsequently HDFS) threw out POSIX compatibility and started from four empirical observations about large-scale data-processing workloads[^gfs-2003][^hdfs-2010]:

1. **Component failures are the norm, not the exception**: In a cluster of thousands of commodity machines and tens of thousands of disks, disk failures, power-supply crashes, memory corruption, and rack switch drops happen *every hour*. Monitoring, checksum validation, fault detection, and automatic re-replication must be built into the file system core.
2. **Files are huge**: Multi-gigabyte files containing millions of application records or SSTable blocks are the common case; supporting billions of tiny 4 KB files is explicitly *not* a goal.
3. **Mutations are almost exclusively sequential appends, not random overwrites**: Once written, files are rarely modified in place; workloads consist of large streaming sequential writes (appending new data) and large streaming reads (plus occasional small random reads in indexed files like SSTables).
4. **Sustained aggregate bandwidth matters more than single-request latency**: Streaming terabytes of data at maximum network and disk throughput takes priority over shaving a millisecond off an individual seek.

To exploit those assumptions with the simplest possible control plane, a GFS cluster (and an HDFS cluster) consists of three components[^gfs-2003][^hdfs-2010]:

```
               +---------------------------------------------------+
               |       GFS Master  (HDFS NameNode)                 |
               |   In-Memory Namespace & File->Chunk Mapping       |
               |   Backed by Replicated Operation Log (EditLog)    |
               +---------------------------------------------------+
                  ^                                             ^
     1. Metadata: |  File "/logs/day1", ChunkIdx 0?             |  Periodic HeartBeat +
        Control   |  Reply: Handle 0x8F2A,                      |  Chunk/Block Reports &
        Flow      |  Locations: [CS1 (Primary), CS2, CS3]       |  Lease Management
                  v                                             v
     +--------------------+      2. Direct Data Flow       +------------------------+
     | Application Client | =============================> | ChunkServer 1 (Primary)|
     | + GFS Client Lib   |    (64MB/128MB Chunks;         +------------------------+
     +--------------------+     never touches Master!)                  |  Pipelined
                                                                        v  TCP Push
                                                           +------------------------+
                                                           | ChunkServer 2 (Replica)|
                                                           +------------------------+
                                                                        |
                                                                        v
                                                           +------------------------+
                                                           | ChunkServer 3 (Replica)|
                                                           +------------------------+
```

- **A Single Master (`NameNode` in HDFS)**: Holds all file system metadata in **main memory (RAM)**: the file and directory namespace, the mapping from each file to its ordered list of **chunks** (`blocks` in HDFS), and the current locations of each chunk's replicas. The Master also manages system-wide background activities: granting chunk leases, garbage-collecting orphaned chunks, and re-replicating or rebalancing chunks across racks via periodic `HeartBeat` messages.
- **Multiple ChunkServers (`DataNodes` in HDFS)**: Commodity Linux machines that store each chunk as an ordinary local file on disk (alongside 32-bit checksums for every 64 KB block inside the chunk) and serve read/write byte ranges specified by a 64-bit globally unique `chunk handle`. By default, every chunk is replicated across **3 ChunkServers** (typically placed **rack-aware**: 2 replicas on different machines in the same rack for fast intra-rack bandwidth, and 1 replica in a different rack to survive a whole-rack power or switch failure).
- **The Client Library**: Linked into each application, implementing the GFS/HDFS API, caching metadata locations, and streaming data directly to and from ChunkServers.

---

## 2. Why chunks are 64 MB / 128 MB (and why small files break the Master)

Every file in GFS is partitioned into fixed-size **64 MB chunks** (and in HDFS into **128 MB or 256 MB blocks**)[^gfs-2003][^hdfs-2010].
In a traditional file system, a block size is `4 KB`; why did GFS choose a chunk size **`16,384 times` larger**?

1. **Slashing Master RAM Metadata Overhead**: Because the single Master keeps the entire namespace and `file -> chunk` table in RAM for fast, lock-free lookups, Master memory is the ceiling on cluster capacity. Each 64 MB chunk requires **less than 64 bytes of metadata in Master RAM**! That means 1 petabyte ($10^{15}$ bytes) of data stored in 64 MB chunks requires only $\frac{10^{15}}{64 \times 10^6} \times 64\text{ bytes} \approx \mathbf{1\text{ GB of Master RAM}}$!
2. **Eliminating Master Contention on the Hot Path**: A client reading or writing a multi-gigabyte file asks the Master for chunk locations *once per 64 MB/128 MB*, caches that metadata in memory, and performs thousands of subsequent operations directly against the ChunkServers without bothering the Master again.
3. **Persistent TCP Connections**: Because a client interacts with the same ChunkServer over an entire 64 MB chunk, it keeps a warm, persistent TCP connection open, amortizing TCP/TLS handshake overhead.

Now look at the flip side, which is a classic Principal design review question (**The Small-File Problem**):
Suppose a team naively stores **100 million individual 1 KB JSON files** directly in GFS or HDFS (`100 GB` of actual data; less than one laptop SSD!).
Even though a 1 KB file only uses 1 KB of disk space on the ChunkServer (because chunks are stored as lazy Linux files that allocate blocks on demand), **every single file and its chunk still consumes ~150 bytes of metadata in Master/NameNode RAM** (`100,000,000 * 150 B ~ 15 GB` of heap objects) and forces the Client to make a separate metadata RPC to the Master for every 1 KB read!
The NameNode runs out of RAM and GC-thrases to death while the cluster's disks are 99.9% empty.
Whenever you have millions of small objects on top of GFS/HDFS, you **must pack them into large container files**; which is the exact reason Google built **SSTables and BigTable** (Module 16) on top of GFS!

### Master Crash Recovery: Persisting Namespace vs Discovering Locations

What happens when the single Master crashes and restarts (or fails over to a Shadow/Standby Master)?
Look at the brilliant engineering split in how the Master manages its three metadata tables[^gfs-2003]:
1. **The file namespace** and **the `file -> chunkHandle` mapping** are persisted durably by appending mutations to a replicated **Operation Log (`EditLog` in HDFS)** flushed to local and remote disks before returning to clients, periodically compacted into a checkpoint (`FsImage`).
2. **The `chunkHandle -> [ChunkServer locations]` table is NEVER persisted to disk on the Master!** Why? Because ChunkServers crash, disks fail, and machines reboot constantly; any persisted location table on the Master would be stale the moment the Master restarted. Instead, the ChunkServers themselves are the authoritative source of truth for which chunks they actually hold on disk: when the Master starts up (and periodically via HeartBeats), it simply polls every ChunkServer for a **`BlockReport`** of its local chunk files and reconstructs the in-memory location map in seconds!

---

## 3. Separation of control and data flow, primary leases, and atomic record append

The most important architectural diagram in the GFS paper is the **Write Mutation Control Flow vs Data Flow**[^gfs-2003]:

```
Write / Record Append Flow in GFS:
==================================
1. Client asks Master: "Who holds the lease for Chunk C?"
2. Master replies: "Primary = Replica A; Secondaries = [Replica B, Replica C]" (Client caches for 60s)
3. DATA FLOW (Pipelined): Client pushes data bytes to closest Replica (say B) ---> Primary A ---> Replica C.
   (Each ChunkServer forwards bytes to the next *as they arrive* in its kernel buffer, saturating full-duplex NICs!)
4. CONTROL FLOW: Once all 3 replicas acknowledge receiving the data into their in-memory LRUs,
   Client sends "Write/Append Commit Request" to Primary A.
5. Primary A assigns a monotonic serial number to the mutation, applies it locally, and forwards
   "Apply at Serial #N" to Secondaries B and C.
6. Secondaries B and C reply "Applied #N" to Primary A.
7. Primary A replies "Success (at byte offset X)" to Client!
```

Examine the two mechanisms that make this fast and safe:
- **Network-Topology-Aware Pipelined Data Push**: Notice that in Step 3, the Client does **not** fan out three separate copies of a 10 MB write over its own outbound NIC (which would divide client egress bandwidth by 3), nor does the Primary act as a star bottleneck. Instead, the Client pushes the stream once to the closest replica, which immediately pipelines the incoming TCP stream to the next closest replica in the rack before the full payload has even finished arriving.
- **Primary-Chunk Lease Mutation Ordering**: To ensure that concurrent writes to the same chunk are applied in the **exact same byte order** on all 3 replicas without asking the central Master to sequence every write, the Master grants a **60-second chunk lease** (Module 04) to one replica, called the **Primary**. The Primary alone assigns the serial order for all mutations to that chunk during its lease.

### Why GFS Created "Atomic Record Append" (and Why HDFS Dropped It for WORM)

Suppose 50 MapReduce workers are concurrently appending output records to the same shared GFS file.
If workers used standard positional writes (`Write(file, offset, data)`), they would either overwrite each other's bytes at the same `offset` or require a slow distributed lock on every write.
Instead, GFS invented **`Record Append`**[^gfs-2003]:
- The client sends only the `data` payload (up to 1/4 of the 64 MB chunk size, i.e., `<= 16 MB`) **without specifying a file offset**.
- The **Primary ChunkServer** checks if appending the record would cause the current chunk to exceed 64 MB:
  - If so, the Primary pads the remainder of the current 64 MB chunk to full capacity, tells the Secondaries to do the same, and replies to the client: *"Retry on the next chunk!"*
  - If it fits, the Primary atomically chooses the current end-of-chunk **offset**, appends the data, instructs all Secondaries to write the record at that **exact same byte offset**, and returns the chosen `offset` to the client.

Now ask: **what happens if a `Record Append` succeeds on the Primary and Secondary B, but fails/times out on Secondary C?**
GFS does **not** roll back the write on A and B!
Instead, it returns an error to the client, and the client **retries the `Record Append`**.
As a result, the retried record is appended again (possibly in a new chunk or after padding), meaning **replicas of the same chunk are NOT byte-for-byte identical**: one replica may have a duplicate record or a padded hole where the failed write occurred!
GFS calls this **relaxed consistency ("defined interspersed with inconsistent")**: it guarantees that every record is written **at least once atomically as a contiguous byte unit (`at-least-once semantics`)**, and relies on the GFS Client Library and reader applications to embed **record checksums and unique record IDs** to skip padding and filter out duplicate records at read time[^gfs-2003].

After years of operational experience showing that handling byte-divergent replicas and duplicate record fragments was painful for upstream applications, **HDFS simplified the contract to strict Single-Writer, Write-Once-Read-Many (WORM)** (and Google's next-generation file system, Colossus, similarly tightened replication consistency)[^hdfs-2010].
In HDFS, only one writer may hold a lease to create or append to a file at a time, and replicas remain strictly byte-identical.

---

## 4. Chubby and ZooKeeper: Paxos-backed locks, sessions, and sequencers

In both GFS (for electing the active Master from shadow masters) and BigTable (for electing the active Master, tracking live Tablet Servers, and storing the root tablet location), Google needed a rock-solid service that answered coordination questions with **strict linearizability (`CP`)**.
Rather than forcing every engineering team to embed and debug their own Paxos library, Mike Burrows built **Chubby**: a **coarse-grained distributed lock and small-file coordination service**[^chubby-2006].

### Coarse-Grained vs Fine-Grained Locking

Burrows opens the Chubby paper with a crucial architectural distinction[^chubby-2006]:
- **Coarse-grained locks** are acquired rarely and held for **hours or days** (for example, electing which single server is the active Master for a GFS cluster or which Tablet Server owns a BigTable tablet). Because lock acquisition rate is low, losing the lock service for a few seconds during a Paxos election barely affects clients, and throughput does not scale with data transaction rate.
- **Fine-grained locks** are acquired for **milliseconds** on every individual row mutation thousands of times per second. A centralized Paxos cell over the network is far too slow for fine-grained row locking; instead, you use Chubby to elect the **coarse-grained owner** of a partition, and that owner handles fine-grained locking locally in its own memory!

### Chubby Architecture: Paxos Cell, Ephemeral Nodes, and Event Watches

A **Chubby cell** consists of **5 replicas** that use **Paxos** (Module 04) to elect a single **Chubby Master** (backed by a replicated write-ahead database log) and maintain a small, in-memory **UNIX-like file system tree** of tiny files and directories (for example, `/ls/heb/bigtable/master` or `/ls/heb/bigtable/servers/tablet-server-42`)[^chubby-2006]:
- **Every node/file can act as a reader-writer lock** and hold small configuration metadata (such as the IP/port of the current leader).
- **Ephemeral Files/Nodes**: When a Tablet Server boots up, it opens a Chubby session and creates an **ephemeral file** `/ls/heb/bigtable/servers/ts-42` locked in exclusive mode. As long as the Tablet Server's Chubby session stays alive via periodic `KeepAlive` leases, the file exists; if the Tablet Server crashes and its lease expires, Chubby automatically unlocks and deletes the ephemeral file, triggering an **Event Watch** notification to the BigTable Master!
- **Avoiding the Herd Effect on Lock Acquisition**: Look at the classic Chubby lock acquisition loop from the wiki notes:
  ```text
  ACQUIRE LOCK:
  1: IF TRY_ACQUIRE(/ls/cell/service/leader_lock, ephemeral=true) SUCCEEDS:
  2:   RETURN (I am the new Leader!)
  3: IF EXISTS(/ls/cell/service/leader_lock, set_watch=true):
  4:   WAIT_FOR_WATCH_EVENT()   // Blocks passively without polling!
  5: GOTO 1
  ```
  Notice why Step 3 & 4 use an **Event Watch** instead of aggressive polling: 100 standby servers wait passively for Chubby to push an invalidation/deletion event when the old leader's session expires. (In **Apache ZooKeeper**, where nodes can create **Sequential Ephemeral ZNodes** `/locks/leader-00001`, `/locks/leader-00002`, each waiting follower sets a watch **only on the immediately preceding sequence number `N-1`** rather than all 100 followers watching node `00001`, completely eliminating the 100-node wakeup **herd effect** when the leader dies![^zookeeper-2010])

### KeepAlive Long-Polling, Client Caching, and the "Jeopardy" Grace Period

Because thousands of clients read small configuration files from a 5-node Chubby cell, Chubby relies on **aggressive, strictly consistent client-side caching**[^chubby-2006]:
1. Clients cache file contents and metadata in memory under a **time-bounded lease** granted by the Chubby Master.
2. Whenever a writer wants to update a file or lock, the Chubby Master **blocks the write** until it sends **cache invalidation notifications** to every client caching that file and either receives their invalidation ACKs or waits for their read leases to expire; guaranteeing **linearizable reads from the local client cache**!
3. **KeepAlive Long-Polling**: Instead of bombarding the Master with short heartbeat packets, a Chubby client sends a `KeepAlive` RPC immediately, and the Chubby Master **holds the `KeepAlive` request open (Long-Polling, Module 10)** until right before the client's lease expires; unless the Master needs to push a cache invalidation or watch event, in which case it replies immediately!
4. **The Jeopardy Grace Period**: What happens if the Chubby Master itself crashes and the 5-node cell takes 12 seconds to elect a new Paxos Master, causing a client's local lease timer to hit zero? Instead of immediately crashing all 10,000 client applications in the datacenter, the Chubby client library enters a special state called **Jeopardy** when its local lease expires, extending a **45-second grace period** while freezing cached reads and trying to contact the newly elected Chubby Master. If the new Master responds within the 45-second Jeopardy window and confirms the session was not invalidated, the client resumes transparently with zero application disruption!

### Chubby Sequencers (Fencing Tokens in Action)

Finally, because Chubby locks are **advisory** by default, how does Chubby prevent a paused zombie lock holder (Module 04) from corrupting a downstream file server after losing its lock?
A lock holder can request a **Sequencer** from Chubby: an opaque byte string containing the lock name, lock acquisition mode, and the **64-bit monotonic lock generation number**[^chubby-2006].
The client passes this Sequencer on every write to the storage server, which validates it either by comparing the generation number directly or by calling `Chubby.CheckSequencer()`!

---

## 5. Distributed file storage and coordination in the Atlas platform

In **Atlas**, the architecture of the **Firmware Archive and Rollout Coordination Tier** mirrors the GFS + Chubby separation of duties:
- **Control Plane Coordination (`etcd / Raft`, the modern Chubby equivalent)**: Atlas uses a 5-node Raft cluster strictly for coarse-grained metadata: electing the active Rollout Coordinator per shard, storing active shard assignments with **Ephemeral Leases**, and issuing 64-bit monotonic **Sequencer / Fencing Tokens**. Zero telemetry payloads or firmware bytes ever touch the etcd cluster.
- **Immutable Large-Chunk Storage (`Blob / HDFS-style Store`)**: Multi-hundred-megabyte firmware images and compacted daily telemetry archives are stored as immutable **128 MB blocks** with 64 KB `CRC32C` block checksums and rack-aware 3-way replication, streamed directly between clients and storage nodes via **Valet Key** URLs without passing through the metadata master.

---

## Before you move on

**GFS** and **HDFS** proved that you can build petabyte-scale fault-tolerant storage on cheap commodity hardware by optimizing for **large files (64 MB / 128 MB chunks)** and **sequential appends**, keeping compact namespace metadata in a **Master / NameNode's RAM** (backed by a replicated `Operation Log / EditLog` while discovering chunk locations dynamically from ChunkServer `BlockReports`), and **separating control flow from pipelined data flow**.
GFS ordered concurrent chunk writes via **60-second Primary-Chunk Leases** and offered high-throughput **Atomic Record Append** with at-least-once relaxed consistency, whereas HDFS simplified the model to strict **Single-Writer WORM** consistency.
Complementing the bulk file system, **Chubby** (and **ZooKeeper**) provides a **5-node Paxos/Zab linearizable coordination service** for **coarse-grained locking**, leader election via **ephemeral nodes**, **watch notifications**, **KeepAlive long-polling with Jeopardy failover tolerance**, and **Sequencer fencing tokens**.

To test your understanding before moving to Module 16, consider this changed case: suppose an engineer designing a new microservice decides to store every customer's real-time shopping cart (updated 5,000 times per second across the fleet) as a file inside **Chubby / ZooKeeper / etcd** *"because Chubby is strongly consistent and already replicated via Paxos."*
Using Burrows's distinction between **coarse-grained** and **fine-grained** state and the mechanics of **Chubby's single-Master Paxos cell and lease-invalidation broadcasts**, explain **two concrete reasons** why using a coordination lock service as a high-write-throughput transactional database will bring down the coordination cell for the entire datacenter.
If that boundary is not crystal clear, revisit **Section 4** before continuing.

---

### Sources

[^gfs-2003]: **Tier 1: Sanjay Ghemawat, Howard Gobioff, and Shun-Tak Leung, "The Google File System" (*ACM SOSP*, 2003, pp. 29–43)**: 64 MB chunk design, single Master in-memory metadata, non-persistence of chunk location maps, separation of control and pipelined data flow, primary-chunk leases, and atomic record append semantics.
[^hdfs-2010]: **Tier 1: Konstantin Shvachko, Hairong Kuang, Sanjay Radia, and Robert Chansler, "The Hadoop Distributed File System" (*IEEE MSST*, 2010, pp. 1–10)**: NameNode + DataNode architecture, 128 MB blocks, EditLog + FsImage, rack-aware replica placement, and single-writer WORM semantics.
[^chubby-2006]: **Tier 1: Mike Burrows, "The Chubby Lock Service for Loosely-Coupled Distributed Systems" (*USENIX OSDI*, 2006, pp. 335–350)**: Coarse-grained vs fine-grained locking, 5-node Paxos cell, ephemeral files, event watches, client-side consistent caching, KeepAlive long-polling, Jeopardy grace period, and Sequencer fencing tokens.
[^zookeeper-2010]: **Tier 1: Patrick Hunt, Mahadev Konar, Flavio P. Junqueira, and Benjamin Reed, "ZooKeeper: Wait-Free Coordination for Internet-Scale Systems" (*USENIX ATC*, 2010)**: Sequential ephemeral znodes, herd-effect elimination on lock watches, and linearizable writes + FIFO client-ordered reads.
