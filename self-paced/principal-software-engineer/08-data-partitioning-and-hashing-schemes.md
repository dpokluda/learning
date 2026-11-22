# 08: Data Partitioning and Hashing Schemes

Replication (Module 03) gives you fault tolerance and read scaling, but if every replica must hold 100% of your dataset, your total storage capacity and total write throughput are still capped by what a single machine can hold and write.
When a dataset grows to tens of terabytes or write throughput climbs into hundreds of thousands of mutations per second, you must break the database into smaller subsets called **partitions** (or **shards**) and distribute them across multiple independent machines[^ddia-ch6].
The goal of partitioning is simple to state and hard to execute: **spread both data volume and read/write request load evenly across \(N\) servers so that single-key operations touch only one server, range queries touch as few servers as possible, and adding or removing a server moves only the minimum mathematical fraction (\(1/N\)) of existing data.**
This module covers horizontal, vertical, and directory-based partitioning, hot-key (celebrity-key) mitigation, **Consistent Hashing with Virtual Nodes**, and **Rendezvous Hashing (Highest Random Weight, HRW)**.

> **Prerequisite:** [Module 03: Replication, Quorums, and Anti-Entropy](./03-replication-quorums-and-anti-entropy.md) and [Module 07: Indexes and Data Modeling: SQL vs NoSQL](./07-indexes-and-data-modeling-sql-vs-nosql.md).

---

## 1. Partitioning strategies: horizontal, vertical, and directory-based

There are three structural ways to divide a database[^ddia-ch6][^azure-sharding]:

1. **Vertical Partitioning (Column / Feature Splitting)**: Splits a wide table or domain by *columns* or *functional area* according to access patterns. For example, in a `Devices` table, the small, frequently updated `Status` and `LastHeartbeatUtc` columns are placed in a hot in-memory/SSD table, while the 2 MB `DiagnosticHardwareManifestJson` blob (read only once a month during support triage) is split into a separate cold blob store or archival table. Vertical partitioning keeps hot buffer pools compact and prevents large cold columns from inflating B+Tree page I/O.
2. **Horizontal Partitioning (Sharding)**: Splits a table by *rows* across multiple independent database instances (**shards**), where every shard has the exact same schema and owns a disjoint subset of the rows determined by a **Shard Key (Partition Key)**.
3. **Directory-Based Partitioning**: Abstracts the physical placement of keys behind a dedicated **Lookup Service (Directory)**. Instead of computing the destination shard strictly from a static formula, clients (or routing proxies) consult the Directory (`GetShardForKey(key) -> ShardId`) and cache the mapping locally.

How does a horizontal sharding scheme decide which shard owns a given key?
Four criteria dominate production systems[^ddia-ch6]:

| Sharding criterion | Mechanism | Primary advantage | Primary failure mode / drawback |
| --- | --- | --- | --- |
| **Range-Based Sharding** | Assigns contiguous sorted intervals of the key space to shards (e.g., `A..D -> Shard 1`, `E..K -> Shard 2`). Used by BigTable, HBase, CockroachDB. | Preserves sort order across the cluster, making **range scans** (`WHERE Key BETWEEN X AND Y`) efficient. | **Write hotspots** when keys are monotonic (such as timestamps or auto-increment IDs): 100% of new writes hammer the single highest range shard! |
| **Hash-Based Sharding** | Applies a uniform deterministic hash function (e.g., `MurmurHash3`, `xxHash64`, `SHA-256`) to the partition key and maps the hash output to a shard. | Scatters sequential/monotonic keys uniformly across all shards, eliminating time-based hotspots. | Destroys key ordering: a range query over the partition key must scatter-gather across **all \(N\) shards**. |
| **List / Geo Partitioning** | Explicitly maps discrete attribute values (e.g., `CountryCode IN ('DE', 'FR', 'NL') -> EU-Shard`) to designated shards. | Enforces strict regulatory **data residency** (GDPR) and keeps regional users close to their shard. | Skewed lists (one country having $20\times$ more users than another) create severe shard size imbalance unless combined with hashing. |
| **Directory-Based Sharding** | Stores explicit `(KeyRange/TenantId -> ShardEndpoint)` rows in a highly available metadata coordination service (Chubby, etcd, SQL directory). | Allows **live shard migration and rebalancing** of an overloaded tenant without changing application hashing code. | The Directory itself becomes a critical-path dependency, a caching coherence problem when mappings move, and a potential single point of failure. |

---

## 2. The operational hazards of sharding and mitigating hotspots

Once you shard a database across machines, three operational constraints immediately bite you[^ddia-ch6]:
- **Cross-Shard Joins and Referential Integrity**: Foreign keys cannot be enforced cheaply across network boundaries, and joining two tables partitioned on different keys requires pulling millions of rows into the application layer. Sharded systems denormalize data or co-locate child records using the **same Partition Key** as their parent aggregate (for instance, partitioning both `TenantRollouts` and `TenantDevices` by `TenantId` when queries are always tenant-scoped).
- **Distributed Secondary Indexes**: You must choose between **Document-Partitioned (Local) Secondary Indexes** (each shard indexes only its own local rows, so writes touch 1 shard while secondary reads must scatter-gather across all \(N\) shards) and **Term-Partitioned (Global) Secondary Indexes** (the secondary index itself is partitioned by the indexed term across shards, so secondary reads touch 1 shard while every write must update multiple shards asynchronously or via distributed transactions)[^ddia-ch6].
- **Hotspots (Celebrity / Skewed Keys)**: Even perfect cryptographic hashing cannot save you if **80% of your requests target the exact same partition key**! Because `hash(key)` is deterministic, every single request for a viral celebrity post, a flash-sale SKU, or a mega-tenant with 50 million devices hashes to the **exact same single shard**, melting its CPU while the other 99 shards sit idle.

### How to Neutralize a Hotspot Key in Production

When a single key \(K_{\text{hot}}\) exceeds the write or read capacity of one physical shard, Principal engineers use **Key Salting (Write Sharding)**:
1. Append a random (or round-robin / device-hashed) numeric suffix from `0` to \(S - 1\) (say \(S = 16\), producing `K_hot#00`, `K_hot#01`, ..., `K_hot#15`).
2. Because the salted strings are distinct keys, the hash function spreads those 16 sub-keys across 16 different shards, multiplying write throughput by \(16\times\)!
3. The trade-off is paid on the **read path**: to read the total state of `K_hot`, the reader must issue a parallel 16-way scatter-gather read across `K_hot#00 .. K_hot#15` and merge the results.
To avoid paying that 16-way read penalty on the 99.9% of small keys that are *not* hot, sophisticated platforms maintain an adaptive **Hot-Key Registry** (often driven by a Count-Min Sketch from Module 09) and apply salting *only* to keys flagged as hot!

---

## 3. Why `hash(key) % N` fails and how Consistent Hashing solves it

Suppose you have \(N = 10\) cache or storage nodes, and you route keys using naive modular arithmetic:

\[
\text{nodeIndex} = \text{hash}(\text{key}) \bmod N
\]

As long as \(N = 10\) never changes, this works fine.
Now suppose traffic grows and you add an 11th node (\(N = 11\)), or Node 4 crashes and is removed (\(N = 9\)).
Suddenly, for almost every key in the universe, \(\text{hash}(\text{key}) \bmod 10 \ne \text{hash}(\text{key}) \bmod 11\)!
Roughly **\(\frac{N - 1}{N} \approx 90\%\) of all keys change their assigned server simultaneously**.
In a caching tier, 90% of your cache instantly misses and stampedes the database into a catastrophic outage; in a storage tier, terabytes of data must be reshuffled across the network at once.

In 1997, David Karger et al. at MIT introduced **Consistent Hashing** to bound key movement to the theoretical minimum (**only \(\approx 1/N\) of keys move** when a node joins or leaves)[^karger-1997][^ddia-ch6]:

```
                        0 / 2^32 - 1
                             |
                   (k4)   [Node A: Token 500]
                         .       .
                      .             .
       [Node D: 3100]                 (k1: Hash 900 -> walks clockwise to Node B!)
                    .                 .
                    .                 .
                      .             .
                         .       .
             [Node C: 2200] ... [Node B: Token 1400]
```

Here is the exact mechanics of the **Consistent Hash Ring**:
1. Treat the output range of a uniform 32-bit (or 64-bit / 128-bit) hash function as a circular ring wrapping around from \(2^{32} - 1\) back to `0`.
2. Map each server node onto the ring by hashing its identifier (`token = Hash(nodeId)`), and keep the sorted array of tokens in memory.
3. To find which node owns `key`, compute `h = Hash(key)` and use **Binary Search** (`Array.BinarySearch` in C#) on the sorted token array to find the first token \(\ge h\) (wrapping around to index `0` if \(h\) is greater than the largest token on the ring). That lookup takes \(O(\log M)\) time (where \(M\) is the number of ring tokens) and requires zero network calls.
4. When a new node `Node E` is inserted at token `1800` (between `Node B` at `1400` and `Node C` at `2200`), **only the keys in the arc `(1400, 1800]` are reassigned from `Node C` to `Node E`**. Every other key on the ring stays on its exact existing node!

### Why Basic Consistent Hashing Needs Virtual Nodes (Vnodes)

If you place each physical server at only a **single token position** on the ring, three serious problems arise in production[^dynamo-2007]:
1. **Severe load imbalance (variance)**: With a small number of random points on a circle, arc lengths vary wildly! One node can easily end up owning an arc \(5\times\) larger than its neighbor's arc.
2. **Rebalancing hot spots on failure**: When `Node B` dies, **100% of `Node B`'s load dumps onto its single clockwise successor `Node C`**, potentially overloading `Node C` into a cascading crash.
3. **Inability to weight heterogeneous hardware**: A new server with 64 cores and 8 TB NVMe gets only one token arc, just like a legacy server with 8 cores.

The solution used by Dynamo, Cassandra, and Riak is **Virtual Nodes (Vnodes)**[^dynamo-2007]:
Instead of assigning 1 token per physical server, each physical server \(S\) is assigned \(V\) distinct tokens (typically \(V = 128\) or \(V = 256\) virtual nodes, hashed as `Hash("NodeA#vnode0")`, `Hash("NodeA#vnode1")`, ...), interleaved randomly around the ring:
- **Uniform statistical distribution**: With \(V = 256\) vnodes per server, the standard deviation of key load across servers drops to \(\approx \frac{1}{\sqrt{V}} = \frac{1}{16} \approx 6\%\).
- **Distributed failure recovery and fast bootstrap**: When `Node B` dies, its 256 small vnode arcs are handed off across **dozens of different physical nodes** around the ring rather than crushing a single neighbor. Similarly, when a new node joins, it streams small slices of data in parallel from all existing nodes.
- **Heterogeneous capacity weighting**: A machine with \(2\times\) hardware capacity is simply assigned \(2V = 512\) vnodes.

*(Note: For workloads where even a single hot key can overload a ring segment, Mirrokni et al. introduced **Consistent Hashing with Bounded Loads**, which places an explicit capacity cap \((1 + \varepsilon) \times \text{averageLoad}\) on each node and overflows excess keys clockwise to the next node with spare capacity[^mirrokni-bounded-2018].)*

---

## 4. Rendezvous Hashing (Highest Random Weight, HRW)

Consistent hashing with virtual nodes is fast and effective, but maintaining a shared ring data structure with thousands of virtual tokens, preventing two replicas of the same key from landing on the same physical node or rack when walking clockwise, and adjusting vnode counts can be fiddly.
In 1998, David Thaler and Chinya Ravishankar published an remarkably clean alternative: **Rendezvous Hashing**, also known as **Highest Random Weight (HRW) Hashing**[^hrw-1998].

### How Rendezvous Hashing Works

Suppose you have a set of candidate nodes \(\{n_1, n_2, \dots, n_N\}\) and a key \(k\).
Instead of placing nodes and keys onto a shared coordinate ring, you compute a deterministic combined hash score (weight) for each `(node, key)` pair and pick the node that yields the **maximum weight**[^hrw-1998]:

\[
\text{Owner}(k) = \arg\max_{n_i \in \text{Nodes}} \text{Hash}(n_i \parallel k)
\]

```
Key k = "device-9812"
  +-- Weight(NodeA, "device-9812") = Murmur64("NodeA" + "device-9812") = 0x4A19...  (2nd: Replica 1)
  +-- Weight(NodeB, "device-9812") = Murmur64("NodeB" + "device-9812") = 0xF83C...  (MAX: Primary Owner!)
  +-- Weight(NodeC, "device-9812") = Murmur64("NodeC" + "device-9812") = 0x1102...
  +-- Weight(NodeD, "device-9812") = Murmur64("NodeD" + "device-9812") = 0x39B0...  (3rd: Replica 2)
```

Look at the properties that fall out of that single formula with **zero ring data structures and zero virtual nodes**:
1. **Minimal \(1/N\) Disruption**: Because the hash function is uniform and independent across `(node, k)` pairs, each of the \(N\) nodes has an exact \(1/N\) probability of having the highest weight for key \(k\). If `Node B` is removed, only the \(1/N\) keys where `Node B` was the winner move to their second-highest-weight node; **every key owned by `Node A`, `Node C`, or `Node D` still has the exact same maximum winner and never moves!**
2. **Trivial \(k\)-Replica Selection**: Need \(R = 3\) distinct physical replicas for key \(k\)? Simply sort the nodes by their weight \(\text{Hash}(n_i \parallel k)\) in descending order and take the **top 3 nodes**! Unlike a vnode ring (where walking clockwise can hit 3 vnodes belonging to the same physical machine unless you write skip-filtering logic), every candidate in the HRW list is already a distinct physical node (or distinct rack/AZ if you pick the max per fault domain).
3. **Exact Weighted Capacity**: If node \(n_i\) has relative capacity weight \(w_i > 0\), weighted Rendezvous Hashing computes the score as \(\frac{-w_i}{\ln(U(n_i, k))}\) (where \(U \in (0, 1)\) is the normalized uniform hash value), distributing load in exact mathematical proportion to \(w_i\) without creating hundreds of virtual tokens!

**When to choose Consistent Hashing with Vnodes vs Rendezvous (HRW) Hashing**:
- Choose **Rendezvous (HRW) Hashing** when the number of nodes or shards \(N\) is small to moderate (say \(N \le 200\) cache servers, storage shards, or regional gateways), because evaluating \(N\) fast 64-bit hashes takes less than a microsecond (`O(N)`) and the code is 15 lines of bug-free stateless math with trivial multi-replica and rack-aware placement.
- Choose **Consistent Hashing with Virtual Nodes** (or **Jump Consistent Hash** for numbered buckets `0..N-1`) when \(N\) is very large (thousands of nodes), where \(O(\log(N \cdot V))\) binary search on a ring beats \(O(N)\) linear weight evaluation per lookup.

---

## 5. Partitioning and hashing in the Atlas platform

In **Atlas**, three different partitioning schemes cooperate across the ingestion and storage tiers:
1. **Geo-List + Hash Composite Sharding for Data Residency**: Every tenant belongs to a sovereign geography (`EU`, `US`, `APAC`). The top-level router applies **List Partitioning** on `GeoRegion` first (guaranteeing EU tenant bytes never leave EU datacenters), and within each region partitions the database across shards using **Consistent Hashing with 256 Vnodes** on `Hash(TenantId + ":" + DeviceId)`.
2. **Automatic Salting for Mega-Tenants**: An automotive customer in Atlas has 40 million connected vehicles under a single `TenantId`. For fleet-level rollouts keyed by `RolloutId`, Atlas appends a deterministic bucket suffix `RolloutId#(Hash(DeviceId) % 32)` so rollout status writes spread uniformly across 32 partition shards rather than bottlenecking on a single shard.
3. **Rendezvous Hashing at the Stateful Edge Gateway**: Atlas runs 64 stateful WebSocket notification gateways per availability zone. To route a command for `DeviceId` to the exact gateway holding its connection (with an automatic fallback to a secondary gateway), the internal router uses **Rendezvous Hashing (HRW)** over the healthy gateway list.

---

## Before you move on

Partitioning breaks a dataset across independent shards so storage and write throughput scale horizontally.
**Range-based sharding** preserves sort order for range scans but suffers from monotonic write hotspots unless ranges split dynamically; **hash-based sharding** spreads writes uniformly at the cost of scatter-gather range queries; and **directory-based sharding** decouples keys from physical topology via a lookup service.
Even with hashing, a single viral or mega-tenant key creates a **hotspot** that requires **key salting (`0..S-1`)** to spread writes across \(S\) shards.
To prevent adding or removing a node from remapping \((N-1)/N\) of all keys (`hash(k) % N`), **Consistent Hashing** places nodes and keys on a sorted ring for \(O(\log M)\) binary search lookup and uses **Virtual Nodes (Vnodes)** to eliminate load variance and spread failover load.
Alternatively, **Rendezvous (HRW) Hashing** computes \(\arg\max_{n} \text{Hash}(n \parallel k)\) in \(O(N)\) time with zero ring state, delivering exact \(1/N\) minimal disruption and effortless top-\(R\) replica selection.

To test yourself before moving to Module 09, consider this changed case: suppose you have a Consistent Hash Ring with 4 physical nodes (`A, B, C, D`), each given \(V = 100\) virtual nodes (400 tokens total on the ring), and replication factor \(R = 3\).
When you hash a key `K` and walk clockwise from `Hash(K)` to pick the first 3 tokens on the ring, why is it mathematically possible for two of those three tokens to belong to the **same physical node `A`**, and what specific rule must your ring coordinator enforce while walking clockwise so that `K` actually survives the failure of any 2 physical machines?
Compare that with how **Rendezvous Hashing** selects 3 replicas.
If the difference is not immediately clear, review **Section 3** and **Section 4**.

---

### Sources

[^ddia-ch6]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 6: "Partitioning"**: Key-range vs hash partitioning, skewed workloads and relieving hot spots via key salting, local vs global secondary indexes, and rebalancing partitions.
[^azure-sharding]: **Tier 3: Microsoft Azure Architecture Center, *Sharding Pattern***: Range, hash, and directory-based sharding strategies and trade-offs ([https://learn.microsoft.com/en-us/azure/architecture/patterns/sharding](https://learn.microsoft.com/en-us/azure/architecture/patterns/sharding)).
[^karger-1997]: **Tier 1: David Karger et al., "Consistent Hashing and Random Trees: Distributed Caching Protocols for Relieving Hot Spots on the World Wide Web" (*ACM STOC*, 1997, pp. 654–663)**: Original paper introducing consistent hashing and logarithmic virtual-node replication.
[^dynamo-2007]: **Tier 1: Giuseppe DeCandia et al., "Dynamo: Amazon's Highly Available Key-Value Store" (*ACM SOSP*, 2007, pp. 205–220)**: Virtual nodes (tokens), physical node deduplication in preference lists, and equal-sized partition slices for fast bootstrapping and anti-entropy.
[^mirrokni-bounded-2018]: **Tier 1: Vahab Mirrokni, Mikkel Thorup, and Morteza Zadimoghaddam, "Consistent Hashing with Bounded Loads" (*ACM-SIAM SODA*, 2018, pp. 587–604)**: Formal analysis of capacity-capped consistent hashing with overflow forwarding.
[^hrw-1998]: **Tier 1: David G. Thaler and Chinya V. Ravishankar, "Using Name-Based Mappings to Increase Hit Rates" (*IEEE/ACM Transactions on Networking*, Vol. 6, No. 1, 1998, pp. 1–14)**: Original specification and minimal-disruption proofs for Rendezvous / Highest Random Weight (HRW) hashing.
