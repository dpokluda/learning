# 03: Replication, Quorums, and Anti-Entropy

Why do we replicate data across multiple machines?
First, to keep data geographically close to users so read latency stays low.
Second, to allow the system to continue operating when individual machines, disks, or entire racks fail.
Third, to scale out read throughput across multiple copies when a single machine's CPU or I/O bandwidth is saturated.
Yet the moment you store more than one copy of a mutable record, you create a fundamental tension: **how do you keep those replicas in agreement when writes arrive concurrently and network links drop packets?**
This module examines the three canonical replication topologies (leader-follower, multi-leader, and leaderless), derives the strict quorum intersection rule (`R + W > N`) and the subtle edge cases where it still fails linearizability, and shows how leaderless stores stay available and converge using sloppy quorums, hinted handoff, read repair, and Merkle tree anti-entropy.

> **Prerequisite:** [Module 01: Distributed Systems Models and Failure Modes](./01-distributed-systems-models-and-failure-modes.md) and [Module 02: Time, Clocks, and Ordering of Events](./02-time-clocks-and-ordering-of-events.md).

---

## 1. Redundancy vs replication and the three replication topologies

Before comparing architectures, distinguish **redundancy** from **replication**[^ddia-ch5]:
- **Redundancy** is the duplication of stateless or stateful hardware and software components (for example, running two stateless load balancers or dual power supplies) so that a component failure does not halt the system.
- **Replication** is the ongoing process of synchronizing mutable state across multiple redundant storage nodes so that replicas converge on the same data.

Every replicated database routes writes through one of three structural topologies[^ddia-ch5]:

### Leader-Follower (Active-Passive / Primary-Secondary)

One replica is designated as the **leader** (primary).
All client writes must be sent to the leader, which serializes them into its local Write-Ahead Log and streams the state changes to one or more **followers** (read replicas).
Followers never accept direct client writes; they apply the leader's replication stream in order and may optionally serve read-only queries.

The critical design choice in leader-follower replication is **when the leader acknowledges the write to the client**:
- **Synchronous replication**: The leader waits until every follower (or a designated synchronous standby) confirms it has persisted the write before returning `200 OK` to the client. Synchronous replication guarantees that a follower promotion after a leader crash loses zero acknowledged writes, but it degrades write availability: if a single required synchronous follower is slow or unreachable, writes block.
- **Asynchronous replication**: The leader appends the write locally, returns `200 OK` immediately, and ships the log to followers in the background. Asynchronous replication delivers low write latency and survives follower outages, but if the leader suffers an unrecoverable disk crash before its latest log entries reach the followers, **acknowledged writes are permanently lost** (creating a non-zero Recovery Point Objective, `RPO > 0`), and clients reading from lagging followers may observe stale state.
- **Semi-synchronous (quorum-backed) replication**: The leader waits for a subset (typically a majority quorum) of replicas to acknowledge the write before returning success, leaving the remaining followers to catch up asynchronously. This balances durability with availability and forms the backbone of consensus-replicated logs.

### Multi-Leader (Active-Active / Multi-Primary)

In a **multi-leader** topology, more than one node (typically one leader per geographic region) accepts writes concurrently.
Each regional leader acknowledges local writes quickly with low regional latency and asynchronously replicates its mutations to the leaders in other regions.
While multi-leader replication shines for globally distributed writes and survives entire regional disconnects, its Achilles' heel is **write conflict resolution**: if a user updates a record in `us-east` at the exact same moment an automated job updates the same record in `eu-west`, both regional leaders accept their respective writes locally and only discover the conflict tens of milliseconds later when their replication streams cross.
Resolving multi-leader conflicts requires deterministic convergence rules: Last-Write-Wins (which silently drops data under clock skew), custom application merge handlers, Vector Clocks, or Conflict-Free Replicated Data Types (CRDTs).

### Leaderless (Dynamo-Style) Replication

Inspired by Amazon's Dynamo paper (2007) and used in Apache Cassandra, Riak, and Voldemort, **leaderless replication** abandons the concept of a single primary node altogether[^dynamo-2007][^ddia-ch5].
A client sends its write (or read) directly to all $N$ replicas in parallel, or sends it to any node in the cluster (acting as a stateless **coordinator** for that request), which forwards the operation to the $N$ replicas that own the key.
There is no failover pause when a node dies because there is no leader to elect; instead, correctness relies on **quorum math** at read and write time, paired with background reconciliation.

---

## 2. Strict majority quorums (`R + W > N`)

If a key is replicated across $N$ nodes in a leaderless (or quorum-replicated) group and some nodes may be down or slow during a write, how many replicas must confirm a write ($W$) and how many replicas must respond to a read ($R$) so that a reader is guaranteed to see the latest write?

By the pigeonhole principle, if the write set of size $W$ and the read set of size $R$ are both chosen from the same canonical set of $N$ replicas, the two subsets are **guaranteed to overlap in at least one node** if and only if[^ddia-ch5][^petrov-dbi]:

$$
R + W > N
$$

```
                    N = 5 Replicas for Key K
     +---------+   +---------+   +---------+   +---------+   +---------+
     | Node 1  |   | Node 2  |   | Node 3  |   | Node 4  |   | Node 5  |
     | (v = 2) |   | (v = 2) |   | (v = 2) |   | (v = 1) |   | (v = 1) |
     +---------+   +---------+   +---------+   +---------+   +---------+
     \_____________________________/
         Write Quorum W = 3
                       \_____________________________/
                               Read Quorum R = 3
                       \___________/
                       Overlap >= 1 (Node 3 holds latest v = 2)
```

In the diagram above, $N = 5$, $W = 3$, and $R = 3$ ($3 + 3 = 6 > 5$).
When a client writes version `v = 2`, the coordinator waits for acknowledgments from any $W = 3$ replicas (`Node 1`, `Node 2`, `Node 3`) before returning success; `Node 4` and `Node 5` might still hold the old version `v = 1` because their network packets were delayed.
When a subsequent client reads from any $R = 3$ replicas (say `Node 3`, `Node 4`, and `Node 5`), at least $R + W - N = 3 + 3 - 5 = 1$ replica (`Node 3`) is guaranteed to be in both sets.
By comparing version numbers (or vector clocks) across the $R = 3$ responses, the reader picks `v = 2` over `v = 1`.

A **strict majority quorum** sets $W = \lfloor N/2 \rfloor + 1$ and $R = \lfloor N/2 \rfloor + 1$ (for instance, $N=3, W=2, R=2$, tolerating 1 unavailable node, or $N=5, W=3, R=3$, tolerating 2 unavailable nodes).
However, `R + W > N` is tunable to match workload patterns:
- **Read-heavy workload (`W = N, R = 1`)**: Every write must succeed on all $N$ replicas, allowing any single replica (`R = 1`) to serve a fast, consistent local read. The trade-off is that write availability drops to zero if even one of the $N$ replicas is unreachable.
- **Write-heavy workload (`W = 1, R = N`)**: Writes return as soon as a single replica acknowledges, maximizing write speed and availability, while reads must query all $N$ replicas.

---

## 3. Why `R + W > N` is NOT sufficient for linearizability

One of the most common pitfalls in senior and Staff system design interviews is claiming that configuring Cassandra or Dynamo with `QUORUM` (`R + W > N`) gives you **linearizability** (strong atomic consistency).
**It does not**[^ddia-ch5][^petrov-dbi].
Even with strict `R + W > N` (`N = 3, W = 2, R = 2`), four distinct scenarios break linearizability in a leaderless quorum store unless additional consensus or read-write lock phases are added:

1. **Concurrent writes with Last-Write-Wins (LWW) physical clocks**: Suppose Client A and Client B concurrently write to the same key, and the database resolves ties using client or coordinator wall-clock timestamps. If Client A's clock is 20 ms ahead of Client B's clock, and Client B's write happens *after* Client A's write in real time (within that 20 ms window), Client B's write will be silently ignored by all replicas because its timestamp is numerically smaller. An acknowledged write vanishes without a trace.
2. **Failed writes that partially succeed without rollback**: Suppose a write (`v = 2`) with $W = 2$ succeeds on `Node 1` but fails/times out on `Node 2` and `Node 3`. The coordinator returns an error to the writer (`WriteFailed`). However, leaderless stores do not roll back the uncommitted value on `Node 1`. A subsequent read with $R = 2$ that hits `Node 1` and `Node 2` sees `v = 2` on `Node 1` and treats it as the newest truth, exposing an aborted write.
3. **Reads concurrent with an in-flight write (violating real-time recency)**: Suppose a write (`v = 2`) has reached `Node 1`, is still in flight to `Node 2`, and has not reached `Node 3`. Reader A queries `{Node 1, Node 2}` while `Node 2` still has `v = 1`; Reader A sees `v = 2` (from `Node 1`) and returns `v = 2` to its caller. Immediately after Reader A finishes (before `Node 2` receives the write or read-repair), Reader B queries `{Node 2, Node 3}`, sees `{v = 1, v = 1}`, and returns the stale value `v = 1` *after* Reader A already observed `v = 2`. That inversion directly violates linearizability! To prevent it, the reader must synchronously perform **read repair** across a write quorum *before* returning the read result to the client (an algorithm known as **ABD register emulation**, after Attiya, Bar-Noy, and Dolev)[^abd-1995].
4. **Sloppy quorums during network partitions**: If the system falls back to a sloppy quorum (discussed next), the $W$ nodes written and the $R$ nodes read are no longer drawn from the same $N$ nodes, so their intersection can be empty.

---

## 4. Sloppy quorums and hinted handoff

Strict quorums prioritize consistency over write availability: if a network partition isolates a client coordinator from two of the three canonical replicas on the consistent hash ring for key $K$, a strict $W = 2$ write must fail.
For workloads like Amazon's shopping cart or Atlas's device telemetry ingestion, rejecting a write is worse than dealing with temporary divergence.

To make the system "always writable," Dynamo introduced **Sloppy Quorums** paired with **Hinted Handoff**[^dynamo-2007]:

```
Preference List for Key K: [Node A, Node B, Node C]   (Required W = 2)

  Coordinator ----(1. Write v=2)----> Node A  (OK)
       |
       +---------(2. Write v=2)----> Node B  (X - Unreachable / Timeout)
       |
       +---------(3. Write v=2)----> Node C  (X - Unreachable / Timeout)
       |
       +---------(4. Sloppy Write + Hint: "For Node B")----> Node D (Surrogate OK!)
```

Here is the exact mechanism:
- In a **strict quorum**, the $W$ writes and $R$ reads must be performed strictly on the first $N$ healthy nodes in the key's canonical preference list on the hash ring (`[Node A, Node B, Node C]`).
- In a **sloppy quorum**, if the canonical nodes `Node B` and `Node C` are unreachable, the coordinator walks further clockwise around the ring and writes the data to a healthy surrogate node (`Node D`) so that $W = 2$ durable copies (`Node A` and `Node D`) exist on separate machines before acknowledging the write.
- On `Node D`, the data is stored in a local hints table alongside a metadata **hint** specifying that the record actually belongs to `Node B`.
- **Hinted Handoff**: `Node D` periodically probes `Node B` (or listens to gossip state). As soon as `Node B` recovers, `Node D` replays the hinted write to `Node B` and deletes the local hint.

Be honest about what a sloppy quorum buys you and what it costs: a sloppy quorum **guarantees write durability across $W$ physical disks**, NOT read-your-writes consistency. Until hinted handoff finishes replaying the hints back to `Node B` and `Node C`, a read quorum querying `{Node B, Node C}` after they reconnect will completely miss the write sitting on `Node D`.

---

## 5. Healing divergence: read repair and Merkle tree anti-entropy

Because nodes crash, hints can be lost if a surrogate crashes, and network packets get dropped, replicas in a leaderless or asynchronously replicated system will inevitably drift apart over time.
Production databases use two complementary mechanisms to bring replicas back into sync[^dynamo-2007][^petrov-dbi]:

### Read Repair (Active Reconciliation on the Hot Path)

Whenever a client reads a key with $R \ge 2$, the coordinator has a free opportunity to check whether the replicas agree.
To avoid saturating internal network bandwidth by transferring full multi-megabyte values from all $R$ replicas on every read, the coordinator sends a **full data read request** to one replica (usually the closest/fastest one) and **digest read requests** (requesting only a cryptographic or CRC checksum and version vector of the value) to the remaining $R - 1$ replicas:
1. If all $R$ digests match, the coordinator immediately returns the data payload from the first replica.
2. If any digest mismatches, the coordinator fetches the full payload from the conflicting replicas, determines the newest version (or merges concurrent vector-clock siblings), returns that authoritative result to the client, and sends asynchronous **Read Repair** write requests to overwrite the stale replicas with the latest version.

Read repair is fast and cheap for frequently read keys, but it has a blind spot: **what about cold keys that are written once and not read again for six months?**
If one replica loses a cold key or misses a write, read repair will never touch it.

### Background Anti-Entropy with Merkle Trees

To synchronize cold data in the background without transferring gigabytes of keys across the network to see what differs, systems like Dynamo and Cassandra build **Merkle Trees** (hash trees) over each key range (token subrange) owned by a replica[^dynamo-2007]:

```
                        [ Root Hash: H(H01 + H23) ]
                         /                       \
            [ H01 = H(H0 + H1) ]           [ H23 = H(H2 + H3) ]
               /            \                 /            \
           [ H0 ]          [ H1 ]         [ H2 ]          [ H3 ]
         Keys [0..25)   Keys [25..50)  Keys [50..75)   Keys [75..100)
```

In a Merkle tree, the key range `[0, 100)` is partitioned into leaf buckets.
Each leaf stores the cryptographic hash of the sorted `(Key, Version, ValueHash)` entries inside its subrange, and each internal node stores the hash of its two children concatenated together:
1. Two peer replicas responsible for the same key range exchange only the **32-byte Root Hash** of their Merkle trees.
2. **If the root hashes are equal, the replicas are 100% identical across the entire range**, and the anti-entropy exchange terminates immediately with almost zero network traffic.
3. If the root hashes differ, the nodes exchange the left and right child hashes (`H01` and `H23`). Suppose `H01` matches on both nodes while `H23` differs: the nodes skip the entire left half of the key space (`[0..50)`) and recurse only down the right subtree (`H2` and `H3`), isolating the exact out-of-sync leaf bucket in $O(\log B)$ network rounds (where $B$ is the number of leaf buckets).

The primary operational caveat of Merkle trees is that **the leaf bucket boundaries must align identically between the two replicas being compared**.
If nodes join or leave a ring without virtual nodes, key range boundaries shift and force expensive Merkle tree recalculations; that is why modern systems construct one Merkle tree per fixed **virtual node (vnode) token range**.

---

## 6. Replication and anti-entropy in the Atlas platform

In **Atlas**, replication topologies are matched deliberately to each subsystem's consistency and availability contract:
- **Rollout Policy and Tenant Metadata (`CP / Leader-Follower with Quorum`)**: When an enterprise administrator defines a safety policy ("halt firmware rollout if error rate exceeds 0.5%"), Atlas cannot tolerate sloppy quorums or silent LWW overwrites. Policy mutations go through a single Raft-elected leader per partition and replicate synchronously to a majority quorum before acknowledgment.
- **Device Last-Known-Status Store (`AP / Leaderless Dynamo Ring`)**: Half a billion devices report battery voltage, signal strength, and installed firmware build every few minutes. Atlas stores these status records in a leaderless ring with $N = 3, W = 2, R = 2$. During a top-of-rack switch reboot, coordinators use **sloppy quorums and hinted handoff** so ingestion never backs up at the edge gateways. Frequent dashboard reads trigger digest-based **read repair**, while a nightly throttled **Merkle tree anti-entropy** job reconciles any cold dormant devices per virtual-node token range.

---

## Before you move on

Replication forces you to choose who accepts writes (a single leader, multiple regional leaders, or any coordinator node) and how many replicas must participate before an operation succeeds.
In a leaderless or quorum system, requiring $R + W > N$ guarantees that the read set and write set overlap in at least one replica on the canonical ring, but **`R + W > N` alone does not provide linearizability** due to LWW clock skew, un-rolled-back failed writes, and concurrent reads seeing new and then old values unless a write-back phase is completed before reads return.
When availability during a partition matters more than immediate read-your-writes consistency, **sloppy quorums** write to surrogate nodes with **hinted handoff**, while **read repair** fixes hot keys on the read path and **Merkle trees** synchronize cold key ranges in $O(\log B)$ comparisons.

To verify your command of these mechanics, work through this changed case: suppose an Atlas cluster has $N = 5$ replicas per keyrange and is configured with $W = 2$ and $R = 3$ (`2 + 3 = 5`, which is *not* strictly greater than `5`).
Can you construct a concrete placement of a write (`v = 2`) across two nodes and a subsequent read across three nodes where the reader returns `v = 1` even when all five nodes are healthy and no sloppy quorum is used?
And if you raise $W$ to `3` (`3 + 3 = 6 > 5`), how many simultaneous node failures can the system tolerate for **writes** versus **reads**?
If you hesitate on either answer, review **Section 2** and **Section 3** before moving to Module 04.

---

### Sources

[^ddia-ch5]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 5: "Replication"**: Leader-based, multi-leader, and leaderless replication; synchronous vs asynchronous followers; quorum condition $R + W > N$; and limitations of quorum consistency.
[^dynamo-2007]: **Tier 1: Giuseppe DeCandia et al., "Dynamo: Amazon's Highly Available Key-Value Store" (*ACM SOSP*, 2007, pp. 205–220)**: Coordinator-based leaderless replication, preference lists, sloppy quorums, hinted handoff, read repair, and Merkle tree anti-entropy.
[^petrov-dbi]: **Tier 2: Alex Petrov, *Database Internals* (O'Reilly, 2019), Chapter 11: "Replication and Consistency"**: Quorum anomalies, digest reads, read repair, and Merkle tree range synchronization.
[^abd-1995]: **Tier 1: Hagit Attiya, Amotz Bar-Noy, and Danny Dolev, "Sharing Memory Robustly in Message-Passing Systems" (*Journal of the ACM*, Vol. 42, No. 1, 1995, pp. 124–142)**: Proof that emulating a linearizable atomic register over message-passing quorums requires a two-phase read (querying a read quorum and writing back the highest timestamped value to a write quorum before returning).
