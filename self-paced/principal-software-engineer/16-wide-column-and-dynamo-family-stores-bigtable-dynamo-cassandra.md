# 16: Wide-Column and Dynamo-Family Stores: BigTable, Dynamo, and Cassandra

Between 2006 and 2010, three papers defined the architecture of every modern distributed NoSQL database: Google's **Bigtable: A Distributed Storage System for Structured Data** (Chang et al., 2006)[^bigtable-2006], Amazon's **Dynamo: Amazon's Highly Available Key-Value Store** (DeCandia et al., 2007)[^dynamo-2007], and Facebook's **Cassandra: A Decentralized Structured Storage System** (Lakshman and Malik, 2010)[^cassandra-2010].
Studying these three systems side by side is one of the highest-leverage exercises in distributed systems engineering because they represent **three distinct points in the CAP and PACELC design space built from the exact same fundamental building blocks**:
- **BigTable (`CP` / `PC-EC`)** chooses **strong single-row consistency** and **sorted range-partitioned tablets** by assigning each key range to a **single authoritative Tablet Server** at a time, coordinated by **Chubby** and backed by **GFS** (Module 15).
- **Dynamo (`AP` / `PA-EL`)** chooses **100% write availability ("always-on")** and **zero single points of failure** by arranging nodes in a **masterless Consistent Hash Ring** with **sloppy quorums, hinted handoff, vector clocks, and Merkle tree anti-entropy** (Modules 02, 03, and 08).
- **Cassandra (Tunable `PA-EL` to Quorum)** marries **Dynamo's masterless ring, gossip membership, and tunable quorums** with **BigTable's rich wide-column data model and LSM-Tree (`CommitLog + MemTable + SSTable + Bloom Filter`) storage engine**.

This module walks through the internal architecture of all three systems and shows how to choose and model keys across them.

> **Prerequisite:** [Module 02](./02-time-clocks-and-ordering-of-events.md), [Module 03](./03-replication-quorums-and-anti-entropy.md), [Module 06](./06-write-ahead-logs-and-storage-engines.md), [Module 08](./08-data-partitioning-and-hashing-schemes.md), [Module 09](./09-probabilistic-data-structures-at-scale.md), and [Module 15](./15-distributed-file-systems-and-coordination-gfs-hdfs-chubby.md).

---

## 1. Google BigTable: CP wide-column storage on GFS and Chubby

GFS (Module 15) solved storing huge multi-gigabyte files sequentially, but Google needed a system to store and query **billions of structured records**; such as every URL on the web (`Webtable`), Google Earth satellite imagery tiles, and per-user Personalized Search histories; with low-latency random reads and writes plus high-throughput MapReduce scans.
Chang et al. open the BigTable paper with one sentence that every Principal engineer should know verbatim[^bigtable-2006]:
> *"A Bigtable is a sparse, distributed, persistent multi-dimensional sorted map."*

The map is indexed by a **row key**, a **column key**, and a **64-bit integer timestamp**, mapping to an uninterpreted array of bytes:

\[
(\text{row}: \texttt{string}, \;\text{column}: \texttt{string}, \;\text{time}: \texttt{int64}) \longrightarrow \texttt{byte[]}
\]

### Data Model: Row Keys, Tablets, Column Families, and Timestamps

Every piece of that 3-tuple carries specific architectural rules[^bigtable-2006]:
1. **Row Keys and Dynamic Tablets**: Row keys are arbitrary strings (up to 64 KB) sorted **lexicographically**. Every read or write of data under a *single row key* is **atomic** (regardless of how many columns are modified in that row), while BigTable explicitly provides no cross-row transactions. The table is dynamically range-partitioned along row-key boundaries into contiguous row ranges called **Tablets** (typically `100 MB to 200 MB` each).
   - *Row-key locality design*: Because tablets are contiguous lexicographical ranges, **how you format your row key determines your I/O locality and hotspot behavior!** In Google's `Webtable`, storing URLs as `http://www.cnn.com/index.html` would scatter pages from the same domain across different tablets alphabetically (`news.cnn.com` vs `sports.cnn.com`). Instead, BigTable **reverses the domain components**: `com.cnn.www/index.html` and `com.cnn.sports/scores.html` sort right next to each other inside the same Tablet, maximizing compression and making domain analysis a fast sequential scan!
2. **Column Families and Locality Groups**: Columns are grouped into `family:qualifier` sets called **Column Families** (for example, `anchor:cnnsi.com` and `anchor:my.look.ca` both belong to the `anchor` family, whereas `contents:html` belongs to the `contents` family). Column Families are small in number (dozens, created via schema DDL) and form the unit of **access control** and **Locality Groups** (BigTable flushes different column families into separate physical SSTable files so scanning small metadata columns in `anchor:*` never has to read multi-megabyte HTML blobs in `contents:*`). Within a Column Family, a row can create **millions of dynamic `qualifier` columns on the fly** with zero schema overhead and zero storage cost for empty cells (`sparse`).
3. **Timestamps (Multi-Version Cell History)**: Each cell `(row, family:qualifier)` can store multiple versions of the data indexed by 64-bit timestamps in **decreasing order** so the newest version is always read first, with automatic garbage collection policies ("keep only the last 3 versions" or "keep versions from the last 7 days").

### The Three-Level Tablet Location Hierarchy

How does a BigTable client find which **Tablet Server** currently hosts the Tablet containing a given `rowKey`, without asking the single BigTable Master on every read or write?
BigTable uses a brilliant **three-level B+Tree-like location hierarchy** rooted in **Chubby** (Module 15)[^bigtable-2006]:

```
[ Level 0: Chubby File ]  (Holds location of the single Root Tablet)
           |
           v
[ Level 1: Root Tablet ]  (1st tablet of METADATA table; NEVER splits! Points to other METADATA tablets)
           |
           +---> [ Level 2: METADATA Tablets ]  (Hold (TableId + EndRowKey) -> TabletServer locations)
                            |
                            +---> [ Level 3: User Tablets ]  (100-200 MB contiguous row ranges on Tablet Servers)
```

Let us do the capacity math from Section 5.1 of the BigTable paper[^bigtable-2006]:
- Each `METADATA` row is ~1 KB in size and points to one User Tablet.
- Limiting each `METADATA` tablet to a modest `128 MB` allows a single `METADATA` tablet to address \(2^{17} = 131{,}072\) User Tablets.
- Because the **Root Tablet** (Level 1) is also a `128 MB` `METADATA` tablet that never splits, the Root Tablet can point to \(2^{17}\) Level-2 `METADATA` tablets, which together address **\(2^{34} \approx 17\text{ billion User Tablets}\)**.
- At `128 MB` per User Tablet, this 3-level hierarchy addresses **\(2^{61}\text{ bytes}\) (2 Exabytes) of data** in at most **3 network round trips**; and because the BigTable Client Library caches `METADATA` tablet locations aggressively in RAM, almost all lookups take **0 location round trips**!

### Tablet Server Internals: Why BigTable Needs Zero Data Copying on Failover

Look at how a **BigTable Tablet Server** uses **GFS** and **Chubby** to achieve `CP` strong consistency without ever copying SSTable files between Tablet Servers during failover[^bigtable-2006]:
- **Single-Owner Authority**: At any instant, each Tablet is assigned to **at most one Tablet Server**. All reads and writes for that Tablet go through that single Tablet Server, making single-row linearizability and atomic row mutations trivial in memory!
- **Shared Commit Log + MemTable + Immutable SSTables on GFS**: When a write arrives at a Tablet Server, it appends the mutation to a shared **Commit Log (WAL)** stored as a replicated file in **GFS**, and inserts the key into the Tablet's in-memory **MemTable**. When the MemTable fills up, a **Minor Compaction** freezes it and writes a new immutable **SSTable** file directly into **GFS** (with a **Block Cache**, a **Scan Cache**, and an in-memory **Bloom Filter** per locality group to avoid disk seeks on absent keys).
- **Instant Failover via GFS**: What happens when a Tablet Server crashes? Its ephemeral lock file in **Chubby** expires! The **BigTable Master** notices the deleted Chubby file, sorts the dead server's GFS Commit Log by `(TabletId, SeqNum)` for fast replay, and assigns the orphaned Tablets to other live Tablet Servers. Because **all SSTables and Commit Logs already live on the shared, 3-way replicated GFS cluster**, the new Tablet Server simply reads the metadata pointers from `METADATA`, replays the tiny tail of the GFS Commit Log into a new MemTable, and starts serving traffic in seconds!

---

## 2. Amazon Dynamo: AP "always-writable" masterless storage

Just one year after Google published BigTable (`CP`), Werner Vogels, Giuseppe DeCandia, and the Amazon platform team published **Dynamo** (`AP`)[^dynamo-2007].
Why did Amazon build a radically different architecture?
Because in Amazon's retail platform, **a customer must *always* be able to add an item to their shopping cart, even if a hurricane severs cross-datacenter fibers, a database rack loses power, or a network partition splits the cluster in half**!
In BigTable (`CP`), if a Tablet Server dies or is isolated from Chubby, that Tablet is **unavailable for writes and reads** until the Chubby session lease expires and the Master reassigns the Tablet.
For a shopping cart, rejecting a customer's write costs real revenue; accepting two concurrent writes on isolated nodes and merging the two shopping carts later (at worst resurrected a deleted item) is far preferable.

Dynamo achieved its **"always-writable" (`PA / EL`), zero-master** design by combining six techniques we studied in Parts I and II into one cohesive system[^dynamo-2007]:

```
               Dynamo Masterless Consistent Hash Ring (N = 3, W = 2, R = 2)
               ============================================================
                                  [ Node A (Vnodes) ]
                                    /             \
                     (Gossip 1s)   /               \   Preference List for Key K:
                                  v                 v  [Node A, Node B, Node C]
                       [ Node F ]                     [ Node B ]
                         ^                                 |
            (Sloppy      |                                 | (Replicate K + Vector Clock [A:1])
             Hinted      |                                 v
             Handoff)    +--------------------------- [ Node C ]
```

1. **Partitioning via Consistent Hashing with Virtual Nodes (Module 08)**: There is no central Master and no Chubby location table. Nodes and keys map onto a **128-bit MD5 Consistent Hash Ring**, where each physical machine owns multiple **Virtual Nodes (tokens)** based on hardware capacity.
2. **Membership and Routing via Gossip (`O(1)` Routing)**: Every node gossips ring membership changes with random peers every second so every node holds a full copy of the token ring in memory. A client can send a `get(key)` or `put(key, context, value)` to any node in the key's **Preference List** (the top \(N\) distinct physical nodes walking clockwise around the ring) or through a load balancer; the receiving node acts as the **Coordinator** for that request.
3. **Replication via Tunable Quorums (`R + W > N`, Module 03)**: Normally, the Coordinator sends writes to the top \(N\) healthy nodes in the key's Preference List and waits for \(W\) responses (and reads from \(R\) nodes, typically \(N=3, R=2, W=2\)).
4. **High Write Availability via Sloppy Quorums and Hinted Handoff (Module 03)**: If primary nodes in the Preference List are unreachable during a network partition or node crash, Dynamo **never rejects the write**; it walks further clockwise around the ring, writes the record to healthy surrogate nodes with a local metadata **hint**, and hands the data back when the primary nodes recover.
5. **Version Branching and Conflict Detection via Vector Clocks (Module 02)**: Because sloppy quorums and concurrent multi-coordinator writes can create divergent versions of a key, Dynamo tags every mutation with a **Vector Clock** `[(CoordinatorNode, Counter), ...]`. If a read returns two causally ordered versions (`V1 < V2`), Dynamo automatically discards `V1`. If a read returns two **concurrent** versions (`V1 || V2`), Dynamo returns **both sibling objects** to the client application, which performs **Client-Side Semantic Reconciliation** (for example, taking the union of two shopping-cart item sets) and writes the merged version back to collapse the vector branches!
6. **Replica Convergence via Read Repair and Merkle Tree Anti-Entropy (Module 03)**: Hot keys are reconciled automatically during `get()` calls via **Read Repair**; cold key ranges are synchronized in the background by exchanging **Merkle Tree** hashes per token range.

---

## 3. Apache Cassandra: marrying Dynamo's ring with BigTable's data model

In 2008, Avinash Lakshman (one of the co-authors of the Amazon Dynamo paper!) and Prashant Malik at Facebook built **Apache Cassandra** to power Facebook Inbox Search across hundreds of millions of users[^cassandra-2010].
They observed that **Dynamo's masterless ring and tunable quorums** gave unbeatable operational simplicity and multi-datacenter availability (zero NameNode/Master/Chubby single points of failure), while **BigTable's sorted wide-column data model and LSM-Tree storage engine** was vastly superior to Dynamo's opaque key-value blobs for structured time-series and range queries!

### How Cassandra Combines the Two Architectures

1. **Masterless Ring + Partitioner**: Like Dynamo, Cassandra is 100% peer-to-peer: every node is identical, nodes exchange cluster state via a **Gossip Protocol** with a **\(\varphi\) Accrual Failure Detector** (Module 04), and any node can act as the **Coordinator** for any client query.
2. **Compound Primary Key (`PartitionKey + ClusteringKey`)**: This is the single most important data-modeling concept in Cassandra[^cassandra-2010]:
   ```sql
   CREATE TABLE DeviceTelemetry (
       tenant_id      uuid,
       device_id      text,
       bucket_day     date,
       recorded_at    timestamp,
       event_id       uuid,
       metrics_json   text,
       PRIMARY KEY ((tenant_id, device_id, bucket_day), recorded_at, event_id)
   ) WITH CLUSTERING ORDER BY (recorded_at DESC, event_id ASC);
   ```
   Look closely at the two halves of `PRIMARY KEY ((tenant_id, device_id, bucket_day), recorded_at, event_id)`:
   - **The Partition Key `(tenant_id, device_id, bucket_day)`** (enclosed in the inner parentheses): Cassandra hashes the entire Partition Key using `Murmur3Partitioner` to compute the **64-bit Ring Token**, which determines **which physical nodes on the Consistent Hash Ring store this partition**! All rows with the same `(tenant_id, device_id, bucket_day)` live together on the exact same replica set.
   - **The Clustering Key `recorded_at DESC, event_id ASC`**: Inside each node's local **LSM-Tree (`CommitLog + MemTable + SSTables`)**, all rows sharing that Partition Key are stored **physically sorted by the Clustering Key**! Therefore, querying *"Give me the latest 50 telemetry events for Device X today between 10:00 and 11:00"* hits **one partition on the ring** and reads a **single contiguous sorted slice** of the SSTable in milliseconds!
3. **Tunable Consistency Per Query (Not Fixed at Table Creation)**: Unlike BigTable (always `CP`) or classic Dynamo, a Cassandra client specifies the consistency level **on every individual read or write request**:
   - Write with `LOCAL_QUORUM` and Read with `LOCAL_QUORUM` inside a single datacenter (\(R + W > N_{\text{local}}\)) for strong overlapping quorum consistency without paying cross-continent WAN latency.
   - Write with `ONE` or `ANY` (allowing Hinted Handoff) for ultra-fast fire-and-forget telemetry ingestion.
   *(Note: Unlike Dynamo's vector clocks, Cassandra resolves concurrent cell mutations using **Last-Write-Wins (LWW) microsecond timestamps** per cell, and uses lightweight **Paxos (`IF NOT EXISTS` Lightweight Transactions)** when true linearizable compare-and-set is required.)*

### Architectural Comparison: BigTable vs Dynamo vs Cassandra

| Architectural dimension | Google BigTable (2006) | Amazon Dynamo (2007) | Apache Cassandra (2010) |
| --- | --- | --- | --- |
| **CAP / PACELC posture** | `CP` (`PC / EC`) | `AP` (`PA / EL`) | Tunable `AP` (`PA / EL` to Quorum) |
| **Cluster topology** | Asymmetric: 1 Master + Chubby + Tablet Servers + GFS | Symmetric Masterless Ring (P2P Gossip) | Symmetric Masterless Ring (P2P Gossip) |
| **Data model** | Wide-Column (`Row, ColFamily:Qualifier, Time`) | Opaque Key-Value Blob | Wide-Column (`PartitionKey + ClusteringKey`) |
| **Partitioning scheme** | Dynamic **Range Partitioning** (sorted Tablets) | **Consistent Hashing** with Virtual Nodes | **Consistent Hashing** with Virtual Nodes |
| **Cross-partition range scans** | Fast (Tablets are sorted by RowKey) | Not supported (hash order) | Expensive (PartitionKeys are hashed; only intra-partition `ClusteringKey` ranges are sorted!) |
| **Write path & local storage** | GFS Commit Log + MemTable + SSTables on GFS | Pluggable local engine (BDB / MySQL / in-memory) | Local LSM-Tree (`CommitLog + MemTable + SSTables`) |
| **Conflict resolution** | None needed (single Tablet Server serializes all writes to a Tablet!) | **Vector Clocks** + Client Semantic Merge (or LWW) | Cell-level **Last-Write-Wins (LWW)** timestamps (or Paxos LWT) |

---

## 4. Wide-column and Dynamo patterns in the Atlas platform

In **Atlas**, the Device Telemetry and Audit History database uses **Apache Cassandra** across 6 global regions, applying the exact lessons of BigTable and Dynamo:
- **Time-Bucketed Partition Keys to Prevent Unbounded Wide Partitions**: If Atlas defined its Cassandra Partition Key simply as `(tenant_id, device_id)`, a noisy industrial device emitting 10 events per second for 3 years would accumulate `946 million rows` inside a single Cassandra partition (exceeding the safe ~100 MB partition limit and crushing compaction). By adding `bucket_day` to the Partition Key (`(tenant_id, device_id, bucket_day)`), each partition is bounded to at most 24 hours of data (~8 MB) while keeping full `ClusteringKey = recorded_at DESC` range scans within each day.
- **`LOCAL_QUORUM` + Multi-Region Asynchronous Replication**: Telemetry writes use `LOCAL_QUORUM` (\(N=3, W=2\) within the local region) for low-latency durability, while audit queries read with `LOCAL_QUORUM` (\(R=2\)) paired with Bloom filters and Merkle tree anti-entropy.

---

## Before you move on

**BigTable**, **Dynamo**, and **Cassandra** show how the primitives of Parts I and II combine into complete distributed databases.
**BigTable** achieves `CP` single-row consistency and fast range scans over sorted **Tablets** by assigning each Tablet to a single authoritative **Tablet Server** tracked through a **3-level Chubby + `METADATA` location hierarchy**, storing its **Commit Log** and immutable **SSTables** (with **Bloom filters** and **Locality Groups**) on **GFS** so failover requires zero SSTable copying.
**Dynamo** achieves `AP` "always-writable" availability with zero masters by combining a **Consistent Hash Ring with Virtual Nodes**, **Gossip membership**, **Sloppy Quorums + Hinted Handoff**, **Vector Clocks** with semantic conflict resolution, **Read Repair**, and **Merkle Trees**.
**Cassandra** fuses **Dynamo's masterless ring and tunable quorums** with **BigTable's LSM-Tree storage engine and wide-column model**, splitting the primary key into a hashed **`PartitionKey`** (for ring placement) and a sorted **`ClusteringKey`** (for fast intra-partition range scans).

To test your understanding before moving to Module 17, consider this changed case: suppose an engineer creates a Cassandra table with `PRIMARY KEY (device_id, recorded_at)` (where `device_id` alone is the Partition Key and `recorded_at` is the Clustering Key) and then runs a global monitoring query: `SELECT * FROM DeviceTelemetry WHERE recorded_at >= '2026-11-21T14:00:00Z' ALLOW FILTERING;`.
Why does that query work efficiently on **BigTable** if the BigTable row key starts with `recorded_at` (creating a write hotspot on the last tablet unless salted!), yet in **Cassandra** `WHERE recorded_at >= ...` without specifying `device_id` forces the Coordinator to scatter-gather scan **every single node and partition in the entire cluster**?
How would you redesign the Cassandra Partition Key (using a time bucket plus a small shard-salt number `0..15`) to support global time-window queries without hotspotting a single node or scanning the whole cluster?
If that Partition Key vs Clustering Key distinction is not automatic, review **Section 3** before continuing.

---

### Sources

[^bigtable-2006]: **Tier 1: Fay Chang et al., "Bigtable: A Distributed Storage System for Structured Data" (*USENIX OSDI*, 2006, pp. 205–218)**: Sparse multi-dimensional sorted map `(row, column, time)`, dynamic Tablets, Column Families and Locality Groups, 3-level Chubby/Root/METADATA location hierarchy, and Tablet Server MemTable/SSTable/GFS Commit Log architecture.
[^dynamo-2007]: **Tier 1: Giuseppe DeCandia et al., "Dynamo: Amazon's Highly Available Key-Value Store" (*ACM SOSP*, 2007, pp. 205–220)**: Always-writable SLA design, Consistent Hashing with Virtual Nodes, Preference Lists, Sloppy Quorums, Hinted Handoff, Vector Clocks, Read Repair, Merkle Trees, and Gossip membership.
[^cassandra-2010]: **Tier 1: Avinash Lakshman and Prashant Malik, "Cassandra: A Decentralized Structured Storage System" (*ACM SIGOPS Operating Systems Review*, Vol. 44, No. 2, 2010, pp. 35–40)**: Synthesis of Dynamo's ring/gossip/quorums with BigTable's column families and LSM-Tree storage, PartitionKey vs ClusteringKey semantics, and tunable consistency.
