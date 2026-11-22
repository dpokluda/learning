# 04: Consensus, Leases, and Split-Brain

In Module 03 we saw that leader-follower replication simplifies write serialization because a single leader orders all mutations, and we saw that leaderless quorums (`R + W > N`) fail to guarantee linearizability when concurrent writes or partial failures occur.
That leaves the hardest question in distributed systems sitting right in the middle of the room: **when the leader stops responding, how does the cluster elect a new leader and agree on the exact sequence of committed operations without ending up with two active leaders corrupting the database?**
This problem is **distributed consensus**, and failing to solve it produces **split-brain**: two nodes simultaneously believing they are the sole authority and issuing conflicting writes.
In this module we walk through the mechanics of Single-Decree Paxos, Multi-Paxos, and Raft, examine how heartbeats and failure detectors trigger leadership transitions, and prove why time-bounded leases must be paired with monotonically increasing **fencing tokens** enforced by the storage layer to defeat zombie leaders.

> **Prerequisite:** [Module 01](./01-distributed-systems-models-and-failure-modes.md), [Module 02](./02-time-clocks-and-ordering-of-events.md), and [Module 03](./03-replication-quorums-and-anti-entropy.md).

---

## 1. The consensus contract and why split-brain happens

Suppose a cluster has a single leader `Leader 1` coordinating writes to a shared storage volume or replicated database.
At `12:00:00`, `Leader 1` enters a 15-second stop-the-world garbage collection pause (or its top-of-rack switch drops packets for 15 seconds).
From the perspective of the followers, `Leader 1` has stopped sending heartbeats.
Because the system must remain available, after a 10-second election timeout the followers promote `Leader 2` to take over.
Five seconds later, at `12:00:15`, `Leader 1` finishes its GC pause (or its network cable reconnects).
`Leader 1`'s in-memory state still says `isLeader = true`, and it immediately resumes flushing its queued writes to disk right on top of `Leader 2`'s writes.

This failure mode has a name: `Leader 1` is a **zombie leader**, and the cluster is in **split-brain**[^ddia-ch8][^joshi-pds].
To prevent split-brain and build fault-tolerant state machines, a consensus algorithm must satisfy four formal properties in a partially synchronous network[^ongaro-raft][^kleppmann-lectures]:
1. **Agreement (Safety)**: No two correct nodes ever decide on different values for the same log slot or decree.
2. **Validity (Integrity)**: Any decided value must have been proposed by some node in the cluster (no fabricating synthetic values).
3. **Termination (Liveness)**: Every non-faulty node eventually decides a value once the network stabilizes (assuming a majority of nodes, \(\lfloor N/2 \rfloor + 1\), are reachable and non-faulty).

To tolerate \(f\) crash-recovery failures, any consensus cluster requires at least \(N = 2f + 1\) nodes so that any two majority quorums of size \(f + 1\) are guaranteed to intersect in at least one node that remembers what happened in the previous round.

---

## 2. Single-Decree Paxos and Multi-Paxos

Leslie Lamport's **Paxos** protocol is the foundational consensus algorithm from which nearly all modern crash-tolerant consensus systems derive[^lamport-paxos][^lamport-paxos-simple].
Basic (**Single-Decree**) Paxos reaches agreement on a **single immutable value** across three logical roles (which in practice are co-located on every server in the cluster): **Proposers** (which advocate client values), **Acceptors** (which form the fault-tolerant memory of the cluster via majority quorums), and **Learners** (which read the decided outcome).

Why can a Proposer not simply send `Accept(value)` to a majority of Acceptors in one round?
Because two Proposers might concurrently write to two different majorities while a node crashes, leaving neither value with a clear lock on future rounds.
Paxos solves this by requiring every proposal to carry a **globally unique, totally ordered ballot (proposal) number \(n\)** (constructed as `(roundCounter, nodeId)`), and executing a **two-phase protocol**[^lamport-paxos-simple]:

```
Proposer                        Acceptors (Majority Quorum Q)
   |                                        |
   |--- Phase 1a: Prepare(n) -------------->|  If n > max_promised:
   |                                        |    max_promised = n
   |<-- Phase 1b: Promise(n, n_a, v_a) -----|    return highest accepted (n_a, v_a)
   |                                        |
   |  [If any Acceptor returned a v_a,      |
   |   Proposer MUST set v = v_a of         |
   |   the highest n_a; else use own v]     |
   |                                        |
   |--- Phase 2a: Accept(n, v) ------------>|  If n >= max_promised:
   |                                        |    max_promised = n
   |                                        |    n_a = n; v_a = v
   |<-- Phase 2b: Accepted(n, v) -----------|    confirm acceptance
```

Look closely at the interaction between **Phase 1 (Prepare/Promise)** and **Phase 2 (Accept/Accepted)**, because it contains the entire magic of distributed consensus:
- **Phase 1a (`Prepare(n)`)**: A Proposer picks a ballot number \(n\) higher than any it has used before and sends `Prepare(n)` to a majority of Acceptors.
- **Phase 1b (`Promise(n, highestAcceptedBallot, highestAcceptedValue)`)**: Each Acceptor maintains two durable variables on disk: `maxPromisedBallot` (the highest ballot it has ever promised to consider) and `(acceptedBallot, acceptedValue)` (the highest-numbered proposal it has actually accepted in Phase 2). If \(n > \text{maxPromisedBallot}\), the Acceptor updates `maxPromisedBallot = n` (promising **never** to accept any future proposal with a ballot strictly less than \(n\)) and replies with its current `(acceptedBallot, acceptedValue)` if any. If \(n \le \text{maxPromisedBallot}\), the Acceptor rejects (`NACK`) the Prepare.
- **Phase 2a (`Accept(n, v)`)**: Once the Proposer collects `Promise` responses from a majority quorum \(Q\), it selects the value \(v\) to propose. **This is the golden safety rule of Paxos**: if *any* Acceptor in the majority quorum returned a previously accepted value in Phase 1b, the Proposer **must abandon its own client value and propose the `acceptedValue` corresponding to the highest `acceptedBallot` among the responses**. Only if every Acceptor in the quorum returned empty (`null`) is the Proposer free to propose its own new value. It then sends `Accept(n, v)` to the Acceptors.
- **Phase 2b (`Accepted(n, v)`)**: Each Acceptor accepts `(n, v)` if and only if \(n \ge \text{maxPromisedBallot}\), persisting `acceptedBallot = n` and `acceptedValue = v` to disk before acknowledging. Once a majority of Acceptors acknowledge Phase 2b, the value \(v\) is **chosen (committed)** forever!

Why is a chosen value immutable forever?
Suppose value \(v_1\) was chosen at ballot \(n_1\) by majority quorum \(Q_1\).
Any later Proposer using ballot \(n_2 > n_1\) must collect promises from some majority quorum \(Q_2\) in Phase 1b.
Because \(Q_1\) and \(Q_2\) are both majorities of the same \(N\) nodes, **\(Q_1 \cap Q_2\) contains at least one Acceptor**.
That overlapping Acceptor already accepted \((n_1, v_1)\) before promising \(n_2 > n_1\), so it will report \((n_1, v_1)\) in Phase 1b, forcing the new Proposer to propose \(v_1\) again in Phase 2a!

**Dueling Proposers and Multi-Paxos**: Basic Paxos has two practical drawbacks.
First, if two Proposers interleave Phase 1a (`P1` sends `Prepare(1)`, `P2` sends `Prepare(2)` before `P1` sends `Accept(1)`, forcing `P1` to retry with `Prepare(3)`, and so on), they can livelock indefinitely.
Second, running two full network round trips (Phase 1 + Phase 2) for every single log entry doubles write latency.
**Multi-Paxos** solves both problems at once: instead of running Phase 1 for every log slot independently, a single node runs Phase 1 once with a ballot number covering the *entire future sequence of log slots* to become the stable **Leader**.
As long as that Leader remains healthy (verified via heartbeats/leases), it skips Phase 1 completely and executes **only Phase 2 (`Accept / Accepted`, 1 RTT)** for every subsequent client write!

---

## 3. Raft: understandable state-machine consensus

While Multi-Paxos is conceptually simple in outline, filling in the details of log gaps, reconfiguration, and leader catch-up is notoriously subtle.
In 2014, Diego Ongaro and John Ousterhout designed **Raft** (used in etcd, CockroachDB, Consul, and Kafka's KRaft controller) specifically to decompose replicated-log consensus into three explicit subproblems: **Leader Election**, **Log Replication**, and **Safety**[^ongaro-raft].

### Terms and Server States

Raft divides time into monotonically increasing **terms** of arbitrary length, numbered with consecutive integers (`term 1`, `term 2`, ...).
A Raft term acts as a logical clock and a **generation clock**[^joshi-pds].
At any moment, each server is in one of three states: **Follower** (passive, responds to leader/candidate RPCs), **Candidate** (actively campaigning to become leader in a new term), or **Leader** (handles all client interactions and replicates log entries).

```
                     times out,                   receives votes from
   startup           starts election              majority of servers
     |               (term = term + 1)                    |
     v               ---------------->                    v
+----------+                                        +-----------+
| Follower | <------------------------------------- | Candidate |
+----------+      discovers current leader          +-----------+
     ^                or higher term                    |   ^
     |                                                  |   |
     |            discovers server with                 |   | times out,
     |                 higher term                      v   | new election
     |               +--------------+                   |   |
     +-------------- |    Leader    | <-----------------+   +---+
                     +--------------+
```

Every RPC in Raft (`RequestVote` and `AppendEntries`) carries the sender's current `term`:
- Whenever any server receives an RPC with a `term > currentTerm`, it immediately updates its `currentTerm` to that higher number and steps down to **Follower**.
- Whenever any server receives an RPC with a `term < currentTerm`, it immediately rejects the RPC so the stale sender discovers it has been deposed.

### Leader Election and the Log Up-to-Dateness Restriction

If a Follower receives no `AppendEntries` heartbeat from a valid Leader before its **randomized election timeout** expires (typically chosen uniformly at random between `150 ms` and `300 ms` to prevent split votes), it increments its `currentTerm`, transitions to **Candidate**, votes for itself, and sends `RequestVote(term, candidateId, lastLogIndex, lastLogTerm)` to all peers.

How does Raft ensure that a newly elected Leader already holds every previously committed log entry without having to copy entries from followers during election?
By enforcing the **Election Restriction** inside `RequestVote`[^ongaro-raft]:
> A voter denies its vote if its own log is more "up-to-date" than the candidate's log, where log \(A\) is more up-to-date than log \(B\) iff:
> \[
> (A.\text{lastLogTerm} > B.\text{lastLogTerm}) \lor \bigl(A.\text{lastLogTerm} = B.\text{lastLogTerm} \land A.\text{lastLogIndex} \ge B.\text{lastLogIndex}\bigr)
> \]

Because a committed entry must reside on a majority of servers, and a Candidate must win votes from a majority of servers, the two majorities overlap in at least one server.
That overlapping server holds every committed entry and will refuse to vote for any Candidate whose log is less up-to-date than its own; therefore, **every elected Raft Leader is guaranteed to possess all previously committed entries from the moment it wins election** (the **Leader Completeness Property**).

### Log Replication and the Subtle Previous-Term Commit Rule

During normal operation, the Leader appends a client command to its local log as `(term, index, command)` and sends `AppendEntries(term, leaderId, prevLogIndex, prevLogTerm, entries[], leaderCommit)` to all Followers.
Each Follower verifies the **Log Matching Property**: it checks that its own log contains an entry at `prevLogIndex` whose term equals `prevLogTerm`.
If not, the Follower rejects the append, and the Leader decrements `nextIndex` for that Follower until they find the exact point where their logs agree, overwriting any uncommitted suffix on the Follower.

Once a log entry created in the Leader's **current term** is replicated to a majority of servers, the Leader marks it **committed** (`commitIndex` advances, which is Raft's **High-Water Mark**), applies it to its local state machine, returns success to the client, and notifies Followers of the new `leaderCommit` in the next `AppendEntries` heartbeat.

One crucial safety detail in Section 5.4.2 of the Raft paper catches even experienced engineers off guard[^ongaro-raft]: **a Raft Leader never commits log entries from a previous term simply by counting that they are now on a majority of replicas.**
Why? Because an old entry from `term 2` sitting on a majority of nodes could still be overwritten by a Candidate elected in `term 3` whose last log entry has `term 3` (which beats `term 2` in the Election Restriction) if that Candidate gets votes from nodes that did not have the `term 2` entry!
Instead, a Raft Leader only commits entries from its **own current term** by counting replicas; once an entry from the current term is committed on a majority, the Log Matching Property indirectly commits all preceding entries from earlier terms automatically.

---

## 4. Heartbeats, failure detection, and time-bounded leases

How do nodes know when to trigger an election or revoke a lock?
In a small consensus group (3 to 7 nodes), the Leader sends periodic **Heartbeats** (empty `AppendEntries` RPCs) to every follower every `heartbeatInterval` (for example, every `50 ms`)[^joshi-pds].
In a large cluster of hundreds or thousands of nodes (such as Cassandra or Dynamo), all-to-all heartbeating requires \(O(N^2)\) messages per tick, which saturates the network; those systems use a **Gossip Protocol** where each node exchanges a heartbeat counter list with 1 to 3 randomly chosen peers every second, propagating membership state across \(N\) nodes in \(O(\log N)\) rounds[^dynamo-2007].

Fixed heartbeat timeouts (`if silence > 300 ms, declare dead`) struggle on variable cloud networks: set the timeout too short and transient TCP retransmissions trigger disruptive false-positive elections; set it too long and real crashes take minutes to fail over.
Production systems like Akka and Cassandra solve this with the **\(\varphi\) (Phi) Accrual Failure Detector** (Hayashibara et al., 2004)[^phi-accrual]: instead of outputting a binary `Alive / Dead` boolean, the detector models the historical inter-arrival times of heartbeats from a peer as a probability distribution (typically normal or exponential) and computes a continuous suspicion score \(\varphi(t) = -\log_{10}(P(\text{heartbeat arrives after } t_{\text{now}} - t_{\text{last}}))\).
When network jitter increases gradually, the mean and variance of the distribution adapt automatically; when \(\varphi\) exceeds a threshold (for example, \(\varphi > 8\), corresponding to a \(10^{-8}\) chance the delay is normal jitter), the node is marked unreachable.

### Time-Bounded Leases

When a single node (a master, a cache updater, or a chunk primary in GFS) needs exclusive permission to mutate a resource or serve authoritative local reads without querying a quorum on every read, a permanent lock is dangerous: if the lock holder crashes, the resource is wedged forever.
To prevent indefinite lock starvation, distributed systems grant **time-bounded leases** (Gray and Cheriton, 1989)[^leases-1989]:
- The lock service grants exclusive ownership for a bounded duration \(T_{\text{lease}}\) (say, 10 seconds), using a conservative bound on monotonic clock drift.
- The lease grantor promises **not** to issue a conflicting lease to any other node until at least \(T_{\text{lease}}\) has elapsed on the grantor's clock.
- A healthy lease holder periodically renews (extends) its lease via `KeepAlive` heartbeats before expiration.

---

## 5. Defeating zombie leaders with monotonic fencing tokens

Now we reach the critical failure mode: **a time-bounded lease alone does NOT prevent split-brain writes when the lease holder suffers a temporal pause!**
Trace this four-step disaster (Kleppmann's classic storage lock scenario)[^ddia-ch8]:

```
Client 1 (Old Leader)               Lock / Lease Service             Storage Server
        |                                    |                             |
        |--- 1. Acquire Lease -------------->|                             |
        |<-- Grant Lease (Token = 33) -------|                             |
        |                                    |                             |
   [ Enters 15s GC Pause! ]                  |                             |
   [ Lease expires at 10s ]                  |                             |
        |                                    |<-- 2. Acquire Lease --------| Client 2 (New Leader)
        |                                    |--- Grant Lease (Token = 34)>|
        |                                    |                             |<-- 3. Write(Token = 34, "New")
        |                                    |                             |--- 200 OK (MaxToken = 34)
   [ Wakes up from GC! ]                     |                             |
        |--- 4. Delayed Write(Token = 33, "Stale") ----------------------->|
                                                                     [ REJECTED! 33 < 34 ]
```

Look at what happened to `Client 1`:
1. `Client 1` acquires a 10-second lease, checks its local clock (`5 seconds remaining!`), constructs a write packet, and right before the thread pushes the bytes to the NIC, the runtime pauses all threads for 15 seconds for a full garbage collection (or the packet is delayed in a congested switch queue).
2. While `Client 1` is frozen, its 10-second lease expires at the Lock Service, and `Client 2` acquires the new lease and writes `"New"` to the Storage Server.
3. `Client 1` wakes up from its GC pause and completes the write instruction that was already past its local `if (lease.IsValid)` check, sending `"Stale"` to the Storage Server.

If the Storage Server does not verify who holds the current authority, `Client 1`'s zombie write silently overwrites `Client 2`'s committed state!
How do we make that corruption mathematically impossible?
By combining two mechanisms[^ddia-ch8][^joshi-pds]:
1. **Monotonic Generation Clock / Fencing Token**: Every time the Lock Service (or Raft/Paxos election) grants a lease or elects a leader, it increments a strictly monotonic 64-bit integer counter (`33`, then `34`, then `35`; in Chubby this is called a *sequencer*, in ZooKeeper the `zxid` or `epoch`, and in Kafka the `LeaderEpoch`).
2. **Storage-Layer Fencing Enforcement**: Every write request sent to the downstream Storage Server must include the writer's `fencingToken`. The Storage Server persists `maxSeenToken` for each resource/partition and enforces one simple invariant on every mutation:
   \[
   \text{if } (\text{request.fencingToken} < \text{resource.maxSeenToken}) \implies \text{Reject}(\text{StaleFencingTokenException})
   \]

In bare-metal or shared-SAN clusters where the storage controller cannot check application tokens, administrators use coarse physical equivalents: **Resource Fencing** (revoking the old leader's SAN/NFS mount permissions or switch port via SNMP) or **Node Fencing / STONITH ("Shoot The Other Node In The Head")** (issuing an out-of-band IPMI/BMC hardware power-off command to forcibly cut power to the old machine before activating the new leader).
In cloud-native software architectures, **token-based resource fencing at the storage layer** is far cleaner, faster, and deterministic.

---

## 6. Consensus and fencing in the Atlas platform

In **Atlas**, our global device management platform, every regional firmware rollout is driven by a **Rollout Coordinator** worker that computes batch progression (advancing a rollout from 1% canary to 10% to 50% as health metrics pass).
If two Rollout Coordinators ever ran simultaneously for the same rollout (`split-brain`), one coordinator seeing a regional spike could issue a `HaltAndRollback` command while a zombie coordinator wakes up from a GC pause 200 ms later and overwrites the rollout state back to `ProceedTo100Percent`.

Atlas prevents this using the exact pattern from Section 5:
1. Rollout Coordinator workers acquire shard ownership leases from a regional 5-node **etcd (Raft)** coordination cluster.
2. Each lease assignment increments a 64-bit `CoordinatorEpoch` fencing token.
3. Every state mutation written to the Atlas Rollout State database (`UPDATE RolloutShards SET Phase = @Phase, MaxEpoch = @Epoch WHERE ShardId = @ShardId AND @Epoch >= MaxEpoch`) checks the fencing token atomically inside the storage engine. If a paused coordinator wakes up after losing its lease, its database update affects `0 rows`, immediately triggering local lease-loss self-termination.

---

## Before you move on

Consensus protocols solve the problem of getting a fault-tolerant group of nodes to agree on a single leader and a single sequence of operations without split-brain.
**Single-Decree Paxos** uses two phases (`Prepare/Promise` and `Accept/Accepted`) with ballot numbers so that any new proposer must discover and adopt any value that might already have been chosen by a prior majority; **Multi-Paxos** and **Raft** amortize Phase 1 across an entire leadership term (`Generation Clock`), requiring only a single round trip per write during steady state.
Raft guarantees **Leader Completeness** by refusing votes to candidates with less up-to-date logs and by only committing log entries from the leader's current term via majority counting.
Finally, because **time-bounded leases** can be violated on the client side by GC pauses or clock drift, every distributed lock or leadership lease that mutates external storage must pass a monotonically increasing **fencing token** that the storage server validates on every write.

Before moving to Module 05, test your understanding on this changed case: suppose a developer implements a distributed lock using a single Redis node (`SET resource_name my_random_value NX PX 10000`) so the lock automatically expires after 10 seconds, and the worker deletes the key when done if `my_random_value` still matches.
Does this Redis lock protect the downstream database from split-brain corruption if Worker 1 experiences a 12-second GC pause right before executing its SQL `UPDATE`?
What specific field would you need to add both to the lock service and to the SQL `UPDATE ... WHERE` clause to make it safe?
If you cannot write down that exact condition, revisit **Section 5** before continuing.

---

### Sources

[^ddia-ch8]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 8 & Chapter 9**: Distributed locks, lease expiration under process pauses, monotonic fencing tokens, and consensus equivalence to total-order broadcast.
[^joshi-pds]: **Tier 2: Unmesh Joshi, *Patterns of Distributed Systems* (Addison-Wesley, 2023)**: Generation Clock, High-Water Mark, Leader and Followers, Lease, HeartBeat, and Paxos/Raft implementation patterns.
[^ongaro-raft]: **Tier 1: Diego Ongaro and John Ousterhout, "In Search of an Understandable Consensus Algorithm (Extended Version)" (*USENIX ATC*, 2014)**: Raft state transitions, randomized election timeouts, Election Restriction (Section 5.4.1), and committing entries from previous terms (Section 5.4.2).
[^kleppmann-lectures]: **Tier 4: Martin Kleppmann, *Distributed Systems Lecture Series* (University of Cambridge, Lectures 5–6: Broadcast Protocols and Consensus)**: Formal properties of consensus (Agreement, Validity, Termination) and Raft walkthrough.
[^lamport-paxos]: **Tier 1: Leslie Lamport, "The Part-Time Parliament" (*ACM Transactions on Computer Systems*, Vol. 16, No. 2, 1998, pp. 133–169)**: Original Paxos consensus specification.
[^lamport-paxos-simple]: **Tier 1: Leslie Lamport, "Paxos Made Simple" (*ACM SIGACT News*, Vol. 32, No. 4, 2001, pp. 51–58)**: Concise derivation of Phase 1 (Prepare/Promise) and Phase 2 (Accept/Accepted) safety invariants and Multi-Paxos optimization.
[^dynamo-2007]: **Tier 1: Giuseppe DeCandia et al., "Dynamo: Amazon's Highly Available Key-Value Store" (*ACM SOSP*, 2007)**: Gossip-based membership and failure detection in large clusters.
[^phi-accrual]: **Tier 1: Naohiro Hayashibara et al., "The \(\varphi\) Accrual Failure Detector" (*IEEE SRDS*, 2004, pp. 66–78)**: Adaptive continuous suspicion level calculation for heartbeat inter-arrival times.
[^leases-1989]: **Tier 1: Cary G. Gray and David R. Cheriton, "Leases: An Efficient Fault-Tolerant Mechanism for Distributed File Cache Consistency" (*ACM SOSP*, 1989, pp. 202–210)**: Foundational paper introducing time-bounded leases for fault-tolerant coordination.
