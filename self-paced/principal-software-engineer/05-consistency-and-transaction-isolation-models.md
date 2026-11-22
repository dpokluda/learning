# 05: Consistency and Transaction Isolation Models

The word "consistency" is the single most overloaded term in software engineering.
When a distributed systems engineer talks about the **C** in the **CAP theorem**, they mean **linearizability** (every read across a replicated cluster observes the most recent completed write of a single object).
When a database engineer talks about the **C** in **ACID**, they mean **application-level invariant preservation** (a bank transfer preserves the rule that account balances never drop below zero).
And when they talk about the **I** in **ACID** (**Isolation**), they mean controlling how concurrently executing **multi-object transactions** see each other's intermediate changes.
Confusing single-object replication consistency with multi-object transaction isolation will derail both your architecture reviews and your database choices.
This module untangles CAP and PACELC, maps the hierarchy of single-object consistency models from linearizability down to eventual consistency and session guarantees, and then dissects the multi-object ACID isolation levels, their concurrency anomalies, and the locking or multi-version concurrency control (MVCC) mechanisms that enforce them.

> **Prerequisite:** [Module 02: Time, Clocks, and Ordering of Events](./02-time-clocks-and-ordering-of-events.md), [Module 03: Replication, Quorums, and Anti-Entropy](./03-replication-quorums-and-anti-entropy.md), and [Module 04: Consensus, Leases, and Split-Brain](./04-consensus-leases-and-split-brain.md).

---

## 1. CAP theorem and PACELC theorem

In 2000, Eric Brewer conjectured (and in 2002, Seth Gilbert and Nancy Lynch formally proved) the **CAP Theorem**[^gilbert-lynch-2002]:
> A distributed data store cannot simultaneously guarantee **Consistency (Linearizability)**, **Availability** (every request received by a non-failing node returns a non-error response), and **Partition Tolerance** (the system continues to operate despite arbitrary message loss between network partitions).

```
                 When a Network Partition (P) Occurs:
                 ====================================
            Node A (US-East)     X  (Cut)  X     Node B (EU-West)
           [ Balance = $100 ]                   [ Balance = $100 ]
                   ^                                    ^
                   |                                    |
        Client 1: Write($50)                 Client 2: Read(Balance)
                                                        |
                                   +--------------------+--------------------+
                                   |                                         |
                          Choose CP (Consistency):                  Choose AP (Availability):
                     Return Error / Block until P heals        Return stale $100 immediately
```

One misconception about CAP refuses to die: the idea that you can "pick 2 out of 3: CA, CP, or AP" and classify a single-node or clustered RDBMS as a **"CA system"**[^ddia-ch9].
As Gilbert and Lynch and Martin Kleppmann point out, **network partitions are not a design choice you can opt out of; they are a physical fault that happens whether you like it or not**[^gilbert-lynch-2002][^ddia-ch9].
When the network is healthy (no partition), a system can provide *both* Consistency and Availability simultaneously.
Therefore, the accurate statement of the CAP theorem is conditional: **During a network partition (`P`), a distributed system must choose between cancelling operations on the minority side to preserve Linearizable Consistency (`CP`) or proceeding with operations on both sides to preserve Availability at the cost of stale/divergent data (`AP`).**

### The PACELC Theorem: What Happens When There Is No Partition?

CAP has a major blind spot: network partitions are relatively rare, yet distributed databases make huge performance trade-offs **every single millisecond during normal, partition-free operation**.
To capture both regimes, Daniel Abadi formulated the **PACELC Theorem** (2012)[^abadi-pacelc]:
- **If there is a Partition (`P`)**, how does the system trade off **Availability (`A`)** versus **Consistency (`C`)**?
- **Else (`E`)**, when the network is running normally without partitions, how does the system trade off **Latency (`L`)** versus **Consistency (`C`)**?

Look at why the `Else (E)` half matters so much in production: even when all network links are healthy, ensuring that every read sees the latest write (`EC`) requires replicating writes across a quorum or routing reads through a consensus leader, which costs cross-node or cross-region network round-trip latency (`L`).
If a database instead acknowledges writes locally or serves reads from any local replica without waiting for synchronous coordination (`EL`), it achieves single-digit millisecond latency at the cost of returning slightly stale data during normal operation.

| System family | PACELC classification | Behavior during Partition (`P`) | Behavior during Normal Operation (`E`) |
| --- | --- | --- | --- |
| **Google Spanner, CockroachDB, HBase, BigTable** | `PC / EC` | Refuses reads/writes on minority partitions to preserve linearizability. | Waits for consensus quorum / lease check, paying coordination latency for strong consistency. |
| **Amazon Dynamo, Apache Cassandra (default), Riak** | `PA / EL` | Accepts reads/writes on any reachable partition (sloppy quorum / local quorum). | Keeps latency low via asynchronous replication or tunable local reads, accepting eventual consistency. |
| **MongoDB (default majority write + local read)** | `PC / EL` (or tunable) | Only the primary partition with a majority of nodes accepts writes (`PC`). | If reads are served from secondary replicas without linearizable read concern, trades consistency for latency (`EL`). |
| **Azure Cosmos DB** | Tunable (`PA/EL` to `PC/EC`) | Offers five explicit consistency levels (`Strong`, `Bounded Staleness`, `Session`, `Consistent Prefix`, `Eventual`). | Lets architects select the exact point on the Latency-vs-Consistency spectrum per workload. |

---

## 2. The single-object consistency hierarchy

When we say a system is "more consistent" or "less consistent" on individual records (keys, documents, or rows), we are positioning it on a strict hierarchy of **single-object consistency models**, ordered from strongest (most restrictive and expensive) to weakest[^ddia-ch9][^petrov-dbi]:

```
               +-----------------------------------------------+
               |        Linearizability (Strong / Atomic)      |
               |   (Single global order matching real time)    |
               +-----------------------------------------------+
                                       |
                                       v
               +-----------------------------------------------+
               |             Sequential Consistency            |
               |  (Single global order preserving each node's  |
               |   program order, without real-time recency)   |
               +-----------------------------------------------+
                                       |
                                       v
               +-----------------------------------------------+
               |               Causal Consistency              |
               |    (All nodes agree on the order of causally  |
               |     related operations; concurrent ops vary)  |
               +-----------------------------------------------+
                                       |
                                       v
               +-----------------------------------------------+
               |              Eventual Consistency             |
               |   (Replicas converge eventually if writes     |
               |    stop; no ordering guarantees in flight)    |
               +-----------------------------------------------+
```

Let us make the difference between each rung concrete:

### Linearizability (Strong / Atomic Consistency)

Under **linearizability** (Herlihy and Wing, 1990)[^herlihy-wing-1990], the distributed system behaves as if there is **only a single copy of the data** and every operation takes effect atomically at a single point in real time between the moment the client sends the request and the moment the client receives the response.
This gives you the **real-time recency guarantee**: once *any* client completes a write `Write(x = 2)` (or once *any* client read returns `2`), **every subsequent read across the entire cluster (in real physical time) must return `2` (or a later write)**; never the old value `1`.

### Sequential Consistency

Defined by Lamport (1979)[^lamport-1979], **sequential consistency** drops the real-time recency requirement while preserving a single global order:
> The result of any execution is the same as if the operations of all nodes were executed in some single sequential order, and the operations of each individual node appear in this sequence in the order specified by its program.

How does sequential consistency differ from linearizability in practice?
Suppose Client A writes `x = 1` and then `x = 2` on the leader, and those updates stream asynchronously in exact log order (`1`, then `2`) to a read replica in Tokyo.
A reader in Tokyo is guaranteed to see `x` transition from `0 -> 1 -> 2` (never `2 -> 1`, preserving program order and a single global timeline), even if the Tokyo replica is 50 ms behind the leader in real time!
Linearizability forbids that 50 ms lag across clients; sequential consistency allows it as long as every replica applies the global timeline in the exact same order and each client's own operations never reorder.

### Causal Consistency

**Causal consistency** weakens sequential consistency one step further: it does **not** require a single total order across all operations in the system.
Instead, it requires only that **operations that are causally related (`a -> b`) must be seen in the same order by every node in the cluster**, whereas concurrent operations (`a || b`) may be observed in different orders by different nodes[^ddia-ch9].
For example, if Alice posts a question (`Event A`) and Bob reads Alice's question and posts an answer (`Event B`, so \(A \rightarrow B\)), causal consistency forbids any third user Charlie from ever seeing Bob's answer before Alice's question.
Crucially, **causal consistency is the strongest consistency model that can remain 100% available during a network partition (`AP`)**!

### Eventual Consistency and Client-Centric Session Guarantees

At the bottom sits **eventual consistency**: if no new updates are made to a given data item, all replicas will *eventually* converge to the same value.
On its own, pure eventual consistency is jarring for human users because a user's HTTP requests may bounce across different behind-the-scenes replicas.
To make eventual or causal systems usable without paying the cost of global linearizability, platforms implement four **client-centric session guarantees** (Terry et al., Bayou, 1994)[^bayou-1994], typically enforced by pinning a client session to a replica or passing a lightweight version token (such as a log offset or HLC timestamp) in the client's session cookie/header:
1. **Read-Your-Writes**: If a client writes a value \(v\), any subsequent read by *that same client* is guaranteed to see \(v\) (or a newer write).
2. **Monotonic Reads**: If a client reads value \(v_1\) at time \(t_1\), a later read by *that same client* at time \(t_2 > t_1\) will never return an older version \(v_0 < v_1\) (preventing "time-traveling backward" when a load balancer switches replicas).
3. **Monotonic Writes**: Writes issued by the same client session are serialized and applied in the exact order the client issued them on all replicas.
4. **Writes-Follow-Reads (Session Causality)**: If a client reads value \(v_1\) and then issues a write \(v_2\), \(v_2\) is ordered after \(v_1\) on every replica.

---

## 3. Multi-object ACID transactions and concurrency anomalies

Now shift from single objects across replicas to **multiple objects inside a database transaction** (`BEGIN TRANSACTION ... COMMIT`).
In **ACID**[^ddia-ch7]:
- **Atomicity** means abortability: if a transaction writes five rows across three tables and fails on the fifth, the database rolls back the first four so no partial state remains.
- **Consistency** means that application-defined invariants (foreign keys, uniqueness constraints, balance invariants) hold before and after the transaction.
- **Isolation** means concurrently executing transactions cannot corrupt each other via race conditions.
- **Durability** means once `COMMIT` returns success, the transaction's changes survive power loss or crashes (via WAL or replicated commit).

Full **Serializable** isolation (making concurrent transactions behave as if they ran one after another with zero concurrency) is expensive.
For forty years, databases have offered weaker isolation levels that trade away protection against specific **concurrency anomalies** in exchange for higher throughput.
As a Principal engineer, you must know the six anomalies cold[^ddia-ch7][^berenson-1995]:

1. **Dirty Write**: Transaction \(T_2\) overwrites a row that has been modified by an *uncommitted* Transaction \(T_1\). If \(T_1\) later rolls back or commits out of order, the database ends up with a corrupted mix of \(T_1\) and \(T_2\)'s updates across rows.
2. **Dirty Read**: Transaction \(T_2\) reads a row modified by an *uncommitted* Transaction \(T_1\). If \(T_1\) subsequently aborts (`ROLLBACK`), \(T_2\) has made a business decision based on data that never officially existed.
3. **Non-Repeatable Read (Read Skew)**: Transaction \(T_1\) reads row \(X\) (`$500`), then Transaction \(T_2\) transfers `$100` from \(Y\) to \(X\) (`Y = $400, X = $600`) and commits, and then \(T_1\) reads row \(Y\) (`$400`). \(T_1\) observes a total of `$500 + $400 = $900` even though the total was `$1,000` both before and after \(T_2\)! If \(T_1\) re-reads \(X\), it sees a different value (`$600`) within the same transaction.
4. **Lost Update**: Two transactions \(T_1\) and \(T_2\) concurrently execute a **read-modify-write** cycle on the same counter or JSON document: both read `count = 10`, both compute `10 + 1 = 11` in application memory, and both write `11`. One increment is silently erased.
5. **Phantom Read**: Transaction \(T_1\) executes a range query (`SELECT COUNT(*) FROM Shifts WHERE Day = 'Monday'`) and sees `2` rows; concurrently, \(T_2\) inserts a new row matching `Day = 'Monday'` and commits; when \(T_1\) repeats the range query, a "phantom" third row appears.
6. **Write Skew**: The generalization of phantom/read skew to concurrent writes on **disjoint rows** governed by a shared invariant[^berenson-1995][^ddia-ch7]. Here is the classic hospital on-call example: at least 1 doctor must remain on call for a shift. Both Alice and Bob are currently on call (`count = 2`). Concurrently, Alice and Bob both click "Go Off Call":

```
Transaction T1 (Alice)                           Transaction T2 (Bob)
----------------------------------------------   ----------------------------------------------
BEGIN;                                           BEGIN;
SELECT COUNT(*) FROM Doctors                     SELECT COUNT(*) FROM Doctors
  WHERE Shift = 123 AND OnCall = true;             WHERE Shift = 123 AND OnCall = true;
  --> Returns 2 (Safe to leave!)                   --> Returns 2 (Safe to leave!)

UPDATE Doctors SET OnCall = false                UPDATE Doctors SET OnCall = false
  WHERE Name = 'Alice' AND Shift = 123;            WHERE Name = 'Bob' AND Shift = 123;
COMMIT;                                          COMMIT;

        [ Final State: BOTH Alice and Bob are off call! Invariant (count >= 1) broken! ]
```

Notice why **Write Skew** is so subtle: neither \(T_1\) nor \(T_2\) experienced a dirty read, neither modified the same row (`Alice` vs `Bob`, so row-level lost-update detection does not fire!), and under **Snapshot Isolation**, both transactions read from a clean, immutable point-in-time snapshot where `count = 2`.
Yet their combined commits violated the multi-row invariant because **the premise each transaction read (`count >= 2`) was invalidated by the other transaction's write before commit**!

---

## 4. Isolation levels and how storage engines enforce them (2PL vs MVCC)

How do databases prevent these anomalies?
They combine row locks, **Multi-Version Concurrency Control (MVCC)** snapshots, predicate/range locks, and optimistic conflict detection[^ddia-ch7][^berenson-1995]:

| Isolation level | Dirty Write | Dirty Read | Non-Repeatable Read (Read Skew) | Lost Update | Phantom Read (Read-Only) | Write Skew | Typical implementation mechanism |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **Read Uncommitted** | Prevented | Allowed | Allowed | Allowed | Allowed | Allowed | Row-level write locks held until commit; reads ignore locks. |
| **Read Committed** | Prevented | **Prevented** | Allowed | Allowed | Allowed | Allowed | Write locks held until commit; each `SELECT` statement reads a fresh committed MVCC snapshot (or acquires/releases short read locks). |
| **Repeatable Read / Snapshot Isolation (SI)** | Prevented | **Prevented** | **Prevented** | Engine-dependent* | **Prevented** (in MVCC SI) | Allowed | Transaction reads from a single consistent MVCC snapshot taken at `BEGIN` (`txid` visibility rules). *PostgreSQL/Oracle SI automatically aborts concurrent writes to the *same* row (first-committer-wins), preventing Lost Updates; MySQL InnoDB requires explicit `FOR UPDATE` or atomic `SET x = x + 1`. |
| **Serializable** | Prevented | **Prevented** | **Prevented** | **Prevented** | **Prevented** | **Prevented** | Either **Two-Phase Locking (2PL)** with index-range/predicate locks, **Serializable Snapshot Isolation (SSI)**, or deterministic single-threaded execution. |

Let us compare the two dominant mechanisms for achieving true **Serializability** in production databases:

### Pessimistic Concurrency Control: Two-Phase Locking (2PL)

Used for decades in SQL Server, DB2, and MySQL InnoDB (at `SERIALIZABLE`), **Strict Two-Phase Locking (SS2PL)** enforces a strict rule:
- **Readers block writers, and writers block readers.**
- Every `SELECT` acquires a shared (`S`) lock on the rows it reads, and every `INSERT / UPDATE / DELETE` acquires an exclusive (`X`) lock on the rows it modifies (upgrading `S` to `X` if needed). All locks are held until the transaction finishes (`COMMIT` or `ROLLBACK`).
- To prevent **Phantoms** and **Write Skew** (where a row does not exist yet at `SELECT` time), 2PL uses **Predicate Locks** or, in practice, **Index-Range (Next-Key) Locks**: locking the index gap around the queried `WHERE` range so no concurrent transaction can insert or mutate a row into that range.

In the hospital on-call Write Skew example, under 2PL, both \(T_1\) and \(T_2\) acquire shared (`S`) range locks on `Shift = 123` during their `SELECT COUNT(*)`.
When \(T_1\) tries to `UPDATE` Alice's row, it needs an exclusive (`X`) lock, which blocks waiting for \(T_2\) to release its `S` lock; simultaneously, \(T_2\) tries to acquire an `X` lock to `UPDATE` Bob's row and blocks waiting for \(T_1\)'s `S` lock.
The database lock manager immediately detects the cycle (**deadlock**) and aborts one transaction, which retries cleanly and sees `count = 1`.
The downside of 2PL is operational tail latency: one slow analytical query holding shared locks blocks concurrent OLTP writes, and lock contention causes frequent deadlocks.

### Optimistic Concurrency Control on MVCC: Serializable Snapshot Isolation (SSI)

Introduced by Cahill et al. (2008) and implemented as the `SERIALIZABLE` engine in **PostgreSQL** and **CockroachDB**[^cahill-ssi-2009], **Serializable Snapshot Isolation (SSI)** runs on top of lock-free **MVCC**:
- Every write creates a new version of a tuple tagged with `xmin` (creating transaction ID) and `xmax` (deleting/updating transaction ID).
- Readers never block writers and writers never block readers; every transaction reads from its consistent point-in-time snapshot.
- In the background, the SSI engine tracks **rw-antidependencies** (where Transaction \(T_1\) reads a row or index predicate range via lightweight `SIREAD` locks, and a concurrent Transaction \(T_2\) modifies that range after \(T_1\)'s snapshot).
- Whenever a dangerous structure of rw-antidependencies forms between concurrent transactions before commit, the database aborts one of the transactions at `COMMIT` time with `SQLSTATE 40001 (serialization_failure)`.

SSI gives you the best of both worlds for low-to-moderate contention workloads: read-only queries never block and never lock out writers, while Write Skew and Phantoms are caught deterministically at commit time.

---

## 5. Consistency and isolation in the Atlas platform

In **Atlas**, choosing the wrong isolation level in the **Rollout Approval Service** caused a subtle design review catch before launch.
Atlas enforces a tenant safety rule: *at most one active firmware rollout may target a given Device Group at any time*.
Originally, an engineer wrote the rollout activation handler under default `READ COMMITTED` (and even tested it under `SNAPSHOT ISOLATION`):
1. `SELECT COUNT(*) FROM Rollouts WHERE DeviceGroupId = @GroupId AND Status = 'Active'`
2. If `count == 0`, `INSERT INTO Rollouts (RolloutId, DeviceGroupId, Status) VALUES (@Id, @GroupId, 'Active')`.

Look at that two-step sequence: it is the exact **Write Skew / Phantom** pattern!
If two release pipelines concurrently triggered rollouts for the same `DeviceGroupId` within the same millisecond, both `SELECT` queries observed `0` active rollouts in their snapshots, and both `INSERT` statements succeeded on different `RolloutId` primary keys, leaving the device group with two conflicting active firmware rollouts.
Atlas fixed this in two ways:
- In the relational PostgreSQL metadata store, it added a **filtered unique index** (`CREATE UNIQUE INDEX UX_Active_Rollout_Per_Group ON Rollouts(DeviceGroupId) WHERE Status = 'Active'`), turning a multi-row predicate invariant into a single-index uniqueness constraint that the storage engine enforces atomically even at `READ COMMITTED`.
- For complex multi-group rollout invariants that cannot be expressed as a single unique index, Atlas executes the approval transaction at `SERIALIZABLE` isolation (SSI) with automatic client retry on `40001` conflict errors.

---

## Before you move on

Consistency in distributed systems divides cleanly into two axes: **single-object replication consistency** (how replicas across the network present the order and recency of reads and writes) and **multi-object transaction isolation** (how concurrent transactions reading and writing multiple records are shielded from race conditions).
**CAP** tells you that during a network partition you must choose between linearizable consistency (`CP`) and 100% availability (`AP`), while **PACELC** adds that even when the network is healthy (`E`), you must choose between coordination latency (`L`) and consistency (`C`).
On single objects, **linearizability** enforces real-time recency, **sequential consistency** enforces a single global order without real-time recency, and **causal consistency** preserves `a -> b` ordering while remaining available during partitions.
Inside a database, **Snapshot Isolation (MVCC)** prevents dirty reads, read skew, and read-only phantoms without blocking readers, but permits **Write Skew** unless you upgrade to **Serializable** isolation (via 2PL index-range locks or SSI rw-antidependency tracking) or materialize the invariant into a locked row or unique constraint.

Before moving on to Part II (Storage Engines), test yourself on this changed case: suppose an e-commerce voucher table stores `(VoucherCode, ClaimCount, MaxClaims = 100)`.
Fifty users click "Claim Voucher" at the exact same millisecond, and each transaction runs `SELECT ClaimCount FROM Vouchers WHERE VoucherCode = 'SAVE20'`, checks `if (ClaimCount < 100)`, and runs `UPDATE Vouchers SET ClaimCount = ClaimCount + 1 WHERE VoucherCode = 'SAVE20'`.
First, which specific concurrency anomaly is this (Write Skew on disjoint rows, or Lost Update / Predicate race on a single row)?
Second, why will running the `SELECT` under ordinary `READ COMMITTED` allow more than 100 claims to succeed even though the `UPDATE` itself is `ClaimCount = ClaimCount + 1`, and how does rewriting it as a single predicate update (`UPDATE Vouchers SET ClaimCount = ClaimCount + 1 WHERE VoucherCode = 'SAVE20' AND ClaimCount < 100`) or adding `SELECT ... FOR UPDATE` fix it?
If you cannot explain the difference between where the `SELECT` reads its value and when the `UPDATE` acquires its row lock, revisit **Section 3** and **Section 4**.

---

### Sources

[^gilbert-lynch-2002]: **Tier 1: Seth Gilbert and Nancy Lynch, "Brewer's Conjecture and the Feasibility of Consistent, Available, Partition-Tolerant Web Services" (*ACM SIGACT News*, Vol. 33, No. 2, 2002, pp. 51–59)**: Formal proof of the CAP theorem in asynchronous and partially synchronous network models.
[^ddia-ch9]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 9: "Consistency and Consensus"**: Why "CA" is a misleading classification, linearizability vs serializability, causal consistency, and total order broadcast.
[^abadi-pacelc]: **Tier 1: Daniel J. Abadi, "Consistency Tradeoffs in Modern Distributed Database System Design: CAP is Only Part of the Story" (*IEEE Computer*, Vol. 45, No. 2, 2012, pp. 37–42)**: Formulation of the PACELC theorem and classification of distributed databases (`PA/EL`, `PC/EC`, `PC/EL`).
[^petrov-dbi]: **Tier 2: Alex Petrov, *Database Internals* (O'Reilly, 2019), Chapter 5 & Chapter 11**: MVCC, two-phase locking, SSI, and consistency models.
[^herlihy-wing-1990]: **Tier 1: Maurice P. Herlihy and Jeannette M. Wing, "Linearizability: A Correctness Condition for Concurrent Objects" (*ACM Transactions on Programming Languages and Systems*, Vol. 12, No. 3, 1990, pp. 463–492)**: Formal definition of linearizability (atomic consistency).
[^lamport-1979]: **Tier 1: Leslie Lamport, "How to Make a Multiprocessor Computer That Correctly Executes Multiprocess Programs" (*IEEE Transactions on Computers*, Vol. C-28, No. 9, 1979, pp. 690–691)**: Original definition of sequential consistency.
[^bayou-1994]: **Tier 1: Douglas B. Terry et al., "Session Guarantees for Weakly Consistent Replicated Data" (*IEEE PDIS*, 1994, pp. 140–149)**: Formalization of Read-Your-Writes, Monotonic Reads, Monotonic Writes, and Writes-Follow-Reads in the Bayou system.
[^ddia-ch7]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 7: "Transactions"**: ACID semantics, Read Committed, Snapshot Isolation (MVCC), Dirty Reads/Writes, Read Skew, Lost Updates, Write Skew, Phantoms, 2PL, and SSI.
[^berenson-1995]: **Tier 1: Hal Berenson et al., "A Critique of ANSI SQL Isolation Levels" (*ACM SIGMOD*, 1995, pp. 1–10)**: Seminal paper identifying Snapshot Isolation, Lost Updates, and Write Skew (`A5B`) anomalies omitted from the 1992 ANSI SQL standard.
[^cahill-ssi-2009]: **Tier 1: Michael J. Cahill, Uwe Röhm, and Alan D. Fekete, "Serializable Isolation for Snapshot Databases" (*ACM Transactions on Database Systems*, Vol. 34, No. 4, 2009)**: Design and correctness proof of Serializable Snapshot Isolation (SSI) on top of MVCC.
