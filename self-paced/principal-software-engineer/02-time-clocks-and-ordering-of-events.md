# 02: Time, Clocks, and Ordering of Events

On a single machine, if Thread A writes a record to memory and then signals Thread B, both threads share the same hardware memory bus and the same CPU time-stamp counter.
If you want to know which of two events happened first, you can read the local clock.
In a distributed system, there is no global physical clock.
Every server has its own quartz crystal oscillator running at a slightly different frequency depending on manufacturing tolerances and ambient temperature, and those oscillators are periodically nudged forward or backward over an asymmetric network by the Network Time Protocol (NTP).
If Node A timestamps a write at `10:00:00.005` and Node B timestamps a conflicting write at `10:00:00.003`, you cannot conclude that Node B's write happened first in real time; Node B's clock might simply be lagging by ten milliseconds.
To build correct distributed storage engines, caches, and replication logs, you must separate **physical time** (counting elapsed seconds) from **logical time** (counting causal dependencies between events).

> **Prerequisite:** [Module 01: Distributed Systems Models and Failure Modes](./01-distributed-systems-models-and-failure-modes.md) (specifically point-to-point message passing and partial synchrony).

---

## 1. Physical clocks: UTC, TAI, NTP drift, and monotonic vs wall-clock APIs

Computers maintain two fundamentally different kinds of physical clocks in the operating system kernel, and confusing them is a classic cause of production outages[^ddia-ch8][^kleppmann-lectures]:

1. **Time-of-day (wall-clock) time** (`DateTimeOffset.UtcNow` in .NET, `clock_gettime(CLOCK_REALTIME)` in POSIX): Returns the current calendar date and time relative to the Unix epoch (`1970-01-01T00:00:00Z`), synchronized periodically against NTP servers. Crucially, **wall-clock time is not guaranteed to move forward monotonically**. If the local quartz oscillator has drifted ahead of the upstream NTP stratum server, the NTP daemon may step or slew the system clock backward, causing a timestamp taken *after* an operation to be numerically smaller than the timestamp taken *before* it.
2. **Monotonic time** (`Stopwatch.GetTimestamp()` in .NET, `clock_gettime(CLOCK_MONOTONIC)` in POSIX): Counts elapsed nanoseconds or ticks from an arbitrary starting point (such as machine boot). The OS kernel guarantees that a monotonic clock never jumps backward, even when NTP steps the wall clock. However, **monotonic clock values are meaningless across different machines** because each server boots at a different time.

Even calendar time itself contains a subtle trap that has triggered major cloud outages: the distinction between **TAI (International Atomic Time)** and **UTC (Coordinated Universal Time)**[^kleppmann-lectures].
TAI is a continuous time scale where every day is exactly \(86{,}400\) atomic seconds.
Because the Earth's rotation gradually slows and fluctuates, **UTC inserts (or theoretically removes) leap seconds** at the end of June 30 or December 31 to keep solar noon aligned with atomic clocks.
Unix timestamps explicitly ignore leap seconds by defining every day as \(86{,}400\) seconds, which forces operating systems to repeat the 60th second (`23:59:60`) or step the clock backward by one second when a leap second occurs.
To prevent Naive code from crashing when `t2 - t1 <= 0`, hyperscale cloud providers use **leap second smearing**: instead of stepping the clock by 1,000 ms all at once, their NTP servers intentionally slew the length of a second by a few parts per million over a 24-hour window around the leap second[^ddia-ch8].

What all of this means for system design is straightforward:
- Use **monotonic clocks** exclusively for measuring local durations, timeouts, and local lease expirations on a single node.
- Never assume that **wall-clock timestamps** (`DateTimeOffset.UtcNow`) from two different machines can be compared to establish the true order of events that occurred within tens of milliseconds of each other, because NTP synchronization over a congested network can easily drift by 10 to 100 ms.

---

## 2. Causality and Lamport's happens-before (`a -> b`) relation

In his seminal 1978 paper *Time, Clocks, and the Ordering of Events in a Distributed System*, Leslie Lamport observed that we rarely need to know the exact physical nanosecond at which two events occurred; what we actually need to know is whether one event could have **causally influenced** the other[^lamport-1978].

An **event** is a single atomic step at a single node: executing a local state transition, sending a message \(m\), or receiving a message \(m\).
Lamport defined the **happens-before relation** (written \(a \rightarrow b\)) using three rules[^lamport-1978][^kleppmann-lectures]:

1. **Local program order**: If events \(a\) and \(b\) occur at the same node, and \(a\) occurs before \(b\) in that node's local execution order, then \(a \rightarrow b\).
2. **Message transmission**: If event \(a\) is the sending of a message \(m\) by one node, and event \(b\) is the receipt of that same unique message \(m\) by another node, then \(a \rightarrow b\).
3. **Transitivity**: If there exists an event \(c\) such that \(a \rightarrow c\) and \(c \rightarrow b\), then \(a \rightarrow b\).

```
Node P1:   [ a: Write X=1 ] -------------( send m1 )------------------------->
                                              \
Node P2:   -------------------------> [ b: recv m1; Read X ] --( send m2 )--->
                                                                     \
Node P3:   ---------------- [ d: Write Y=9 ] -------------------------> [ c: recv m2 ]
```

Trace the diagram above using the three rules:
- Event \(a\) (sending \(m_1\) on `P1`) happens before \(b\) (receiving \(m_1\) on `P2`), so \(a \rightarrow b\).
- On `P2`, receiving \(m_1\) occurs before sending \(m_2\), and sending \(m_2\) happens before \(c\) on `P3`, so by transitivity \(a \rightarrow b \rightarrow c\).
- Now compare event \(d\) on `P3` (`Write Y=9`) with event \(a\) on `P1` (`Write X=1`). Did \(a\) happen before \(d\)? No message path leads from \(a\) to \(d\). Did \(d\) happen before \(a\)? No message path leads from \(d\) to \(a\) either (since \(d\) occurred before `P3` received \(m_2\) at \(c\)).

Mathematically, the happens-before relation \(\rightarrow\) is a **strict partial order** (it is irreflexive, meaning never \(a \rightarrow a\), and transitive).
Because it is a partial order rather than a total order, there exist pairs of events where neither \(a \rightarrow b\) nor \(b \rightarrow a\).
When neither event happened before the other, we say that \(a\) and \(b\) are **concurrent**, written \(a \parallel b\)[^lamport-1978].
Concurrency in distributed systems does not mean "occurred at the exact same physical picosecond"; it means **neither event knew about the other when it occurred**.

---

## 3. Lamport logical clocks and total ordering

How do we encode the happens-before relation into compact metadata that travels with messages?
The simplest mechanism is the **Lamport logical clock**[^lamport-1978].

Each node \(i\) maintains a single integer counter \(L_i\), initialized to `0`, and obeys three rules:
1. **Local event or send**: Before executing a local event or sending a message, node \(i\) increments its counter:
   \[
   L_i \leftarrow L_i + 1
   \]
2. **Piggybacking**: When node \(i\) sends a message \(m\), it attaches its updated counter value \(t_m = L_i\) to the message.
3. **Receive merge**: When node \(j\) receives a message \((m, t_m)\), it advances its counter past both its own current value and the message's timestamp, and then increments by 1 for the receive event:
   \[
   L_j \leftarrow \max(L_j, t_m) + 1
   \]

By induction over the three happens-before rules, every Lamport clock satisfies the **Clock Consistency Condition**[^lamport-1978]:

\[
(a \rightarrow b) \implies (L(a) < L(b))
\]

If event \(a\) causally preceded event \(b\), \(a\)'s Lamport timestamp is strictly smaller than \(b\)'s Lamport timestamp.
Furthermore, because two concurrent events on different nodes might have the exact same integer counter \(L(a) = L(b)\), we can turn Lamport timestamps into a deterministic **total order** (\(\prec\)) by breaking ties with each node's unique identifier \(N(e)\):

\[
a \prec b \iff \bigl(L(a) < L(b)\bigr) \lor \bigl(L(a) = L(b) \land N(a) < N(b)\bigr)
\]

This total order is foundational: it allows a distributed state machine to sort all operations across all nodes into a single deterministic sequence that never violates causal dependencies.

However, Lamport clocks have one critical limitation you must never forget: **the converse of the clock condition is false**:

\[
(L(a) < L(b)) \centernot\implies (a \rightarrow b)
\]

If you look solely at two Lamport timestamps `(L=2, Node=P1)` and `(L=5, Node=P3)`, you **cannot tell** whether `(2, P1)` causally preceded `(5, P3)` or whether they were completely independent concurrent operations (`a || b`) where `P3` simply happened to execute more local events first.
When your storage engine needs to distinguish causal overwrites from concurrent conflicts, a single scalar integer is not enough.

---

## 4. Vector clocks and exact causal conflict detection

To make the implication go in both directions so we can distinguish \(a \rightarrow b\) from \(a \parallel b\) simply by inspecting timestamps, we need a **Vector Clock** (independently developed by Fidge and Mattern in 1988)[^fidge-1988][^mattern-1988].

In a cluster (or replica group) of \(N\) nodes indexed \(0, 1, \dots, N-1\), each node \(i\) maintains an array of \(N\) integers \(V_i[0 \dots N-1]\), initially all zeros.
Entry \(V_i[k]\) represents the latest logical event counter of node \(k\) that node \(i\) is causally aware of:

1. **Local event or send on node \(i\)**: Increment only node \(i\)'s own slot in the vector:
   \[
   V_i[i] \leftarrow V_i[i] + 1
   \]
2. **Piggybacking**: When node \(i\) sends a message \(m\), it attaches a copy of its entire vector \(V_m = V_i\).
3. **Receive merge on node \(j\)**: Upon receiving \((m, V_m)\), node \(j\) takes the element-wise maximum across all \(N\) slots and increments its own slot \(j\):
   \[
   \forall k \in [0, N-1]: \quad V_j[k] \leftarrow \max(V_j[k], V_m[k]), \qquad V_j[j] \leftarrow V_j[j] + 1
   \]

Now compare two vector timestamps \(V(a)\) and \(V(b)\):
- **Equal (\(V(a) = V(b)\))**: For all \(k\), \(V(a)[k] = V(b)[k]\).
- **Strictly less than (\(V(a) < V(b)\))**: For every slot \(k\), \(V(a)[k] \le V(b)[k]\), **and** there is at least one slot \(k'\) where \(V(a)[k'] < V(b)[k']\).
- **Concurrent (\(V(a) \parallel V(b)\))**: Neither \(V(a) \le V(b)\) nor \(V(b) \le V(a)\); that is, \(V(a)\) is larger in at least one slot, and \(V(b)\) is larger in at least one other slot (for example, `[2, 1, 0]` vs `[1, 3, 0]`).

Unlike Lamport clocks, Vector clocks provide an **isomorphism** with causality[^fidge-1988]:

\[
(a \rightarrow b) \iff \bigl(V(a) < V(b)\bigr) \qquad \text{and} \qquad (a \parallel b) \iff \bigl(V(a) \parallel V(b)\bigr)
\]

This property is why Amazon Dynamo (Module 16) uses vector clocks (and why Riak evolved them into dotted version vectors): when a client reads a shopping cart or device configuration from multiple replicas and receives version `[NodeA: 2, NodeB: 1]` from one replica and `[NodeA: 2, NodeB: 2]` from another, the client immediately knows the second version causally supersedes the first and can silently discard `[2, 1]`.
Conversely, if the client receives `[NodeA: 3, NodeB: 1]` and `[NodeA: 2, NodeB: 2]`, neither vector dominates the other; two writers modified the object concurrently without seeing each other's changes, and the system must preserve both **sibling versions** for semantic reconciliation rather than silently dropping data with a wall-clock Last-Write-Wins (LWW) rule[^dynamo-2007].

---

## 5. Hybrid Logical Clocks (HLC) and Google TrueTime

Vector clocks solve causal conflict detection cleanly, but they carry two operational costs: the timestamp size grows linearly with the number of actors \(O(N)\), and the integers inside the vector bear no relationship to human wall-clock time (you cannot query a vector-clocked database for "state as of 14:30 UTC yesterday").
Two modern clock architectures bridge physical time and causal ordering in production databases:

### Hybrid Logical Clocks (HLC)

Introduced by Kulkarni et al. (2014) and used in CockroachDB and MongoDB, a **Hybrid Logical Clock** packs a monotonic tuple `(pt, l, nodeId)` into a fixed-size timestamp (often 64 or 96 bits)[^hlc-2014]:
- `pt` is the highest physical wall-clock time (`physicalTime`) seen by the node so far (`l.pt = max(l.pt, wallClockNow, msg.pt)`).
- `l` is a logical counter that resets to `0` whenever `l.pt` advances, and increments by `1` whenever multiple causal events occur within the same physical clock tick or when a received message carries a `msg.pt` that is ahead of the local wall clock due to NTP skew.

HLC gives you a fixed-size scalar timestamp that satisfies Lamport's causal condition (\(a \rightarrow b \implies \text{HLC}(a) < \text{HLC}(b)\)), stays within NTP drift bounds of physical UTC time for time-travel queries, and never moves backward even if the OS wall clock is stepped backward by NTP.

### Google Spanner's TrueTime API

What if you need timestamps that respect **external linearizability** across the entire planet without nodes exchanging causal messages first?
In Google Spanner, every datacenter deploys GPS receivers and rubidium atomic clocks, and exposes time not as a single deceptive integer, but as an explicit uncertainty interval[^spanner-2012]:

\[
\text{TT.now()} = [t_{\text{earliest}}, t_{\text{latest}}], \qquad \text{where } \varepsilon = \frac{t_{\text{latest}} - t_{\text{earliest}}}{2}
\]

The hardware and daemon guarantee that true physical time \(t_{\text{absolute}}\) lies inside \([t_{\text{earliest}}, t_{\text{latest}}]\) (typically \(\varepsilon \le 1\text{ to }7\text{ ms}\)).
To ensure that if Transaction \(T_2\) starts in Tokyo after Transaction \(T_1\) commits in Virginia, \(T_2\)'s commit timestamp is strictly greater than \(T_1\)'s commit timestamp, Spanner applies **Commit Wait**: after assigning a commit timestamp \(s = t_{\text{latest}}\), the coordinator simply **waits until \(\text{TT.now().earliest} > s\)** (a pause of at most \(2\varepsilon\)) before releasing locks or making the data visible to clients.
By waiting out the clock uncertainty window, physical intervals are guaranteed not to overlap for causally ordered transactions.

| Clock mechanism | Timestamp size | Captures `a -> b => T(a) < T(b)`? | Detects concurrency (`a \|\| b`)? | Tied to wall-clock UTC? | Primary production use case |
| --- | --- | --- | --- | --- | --- |
| **Wall clock (NTP)** | 64 bits | No (can invert under skew) | No | Yes | Logging, approximate TTLs (unsafe for causal ordering) |
| **Monotonic clock** | 64 bits | Single node only | No | No (since boot) | Local timeouts, latency histograms, local lease duration |
| **Lamport clock** | 64 bits + Node ID | Yes (one-way) | No | No | Total-order broadcast, state-machine replication |
| **Vector clock** | \(O(N)\) integers | Yes (two-way iff) | Yes (exact) | No | Leaderless conflict detection (Dynamo, Riak) |
| **Hybrid Logical Clock** | 64–96 bits | Yes (one-way) | No | Yes (within NTP drift) | Distributed SQL MVCC snapshots (CockroachDB, MongoDB) |
| **TrueTime + Commit Wait** | 64 bits + \(\varepsilon\) bound | Yes (global external) | No | Yes (GPS + Atomic) | Globally linearizable transactions (Google Spanner) |

---

## 6. Clocks in the Atlas platform

In **Atlas**, all three logical-time mechanisms work side by side in the subsystems where their trade-offs fit:
- **Edge telemetry ingestion uses Hybrid Logical Clocks**: An edge gateway in a factory often loses internet connectivity for hours while its cheap internal RTC drifts by seconds. When the gateway reconnects, Atlas attaches an HLC timestamp to batch records so that causal chains of device alerts stay strictly ordered without corrupting time-series partitioning.
- **Multi-region device shadow state uses 3-node Vector Clocks**: Device configuration twins ("desired state" vs "reported state") can be updated concurrently by an operator in Europe and a field technician in North America during a cross-region fiber cut. Instead of using wall-clock Last-Write-Wins (which would silently erase one update if the two regional clocks differed by 15 ms), the 3 replica coordinators maintain a per-region vector clock `[EU, US, APAC]`. Dominated versions are overwritten automatically; concurrent sibling vectors trigger a deterministic schema merge of disjoint JSON configuration paths.

---

## Before you move on

Physical clocks measure the passage of seconds on imperfect local crystals and cannot be trusted to order distributed events that happen closer together than the network's NTP synchronization error.
To reason about order, we define the **happens-before** partial order (\(a \rightarrow b\)) over local program steps and message send/receive pairs, treating events with no causal path between them as **concurrent** (\(a \parallel b\)).
**Lamport clocks** map that partial order into a scalar counter (and a total order when paired with Node IDs) such that \(a \rightarrow b \implies L(a) < L(b)\), while **Vector clocks** track an array of counters per replica so that \(V(a) < V(b) \iff a \rightarrow b\), allowing exact detection of concurrent writes.
When wall-clock proximity is also required, **Hybrid Logical Clocks** combine physical time with a logical tie-breaker, and **TrueTime** bounds physical uncertainty \(\varepsilon\) and waits out \(2\varepsilon\) at commit time.

Before moving to Module 03, test yourself on this changed case: suppose a 3-replica store (`R1, R2, R3`) stores an item with Vector Clock `[2, 0, 1]`.
Client A reads `[2, 0, 1]` and writes back an update coordinated by `R2`, producing `[2, 1, 1]`.
Simultaneously, Client B (who had earlier read an older version `[1, 0, 1]`) writes an update coordinated by `R3`, producing `[1, 0, 2]`.
When a subsequent reader fetches both `[2, 1, 1]` and `[1, 0, 2]`, does one version causally overwrite the other, or are they concurrent siblings, and why?
If you cannot explain why `[2, 1, 1] || [1, 0, 2]` even though the sum of counters in `[2, 1, 1]` is larger, review **Section 4** before continuing.

---

### Sources

[^ddia-ch8]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 8: "Unreliable Clocks"**: Time-of-day vs monotonic clocks, NTP synchronization skew, leap seconds, smearing, and confidence intervals.
[^kleppmann-lectures]: **Tier 4: Martin Kleppmann, *Distributed Systems Lecture Series* (University of Cambridge, Lectures 3–4: Time, Clocks, and Ordering of Events)**: UTC vs TAI, happens-before partial order definition, Lamport clocks, and Vector clocks.
[^lamport-1978]: **Tier 1: Leslie Lamport, "Time, Clocks, and the Ordering of Events in a Distributed System" (*Communications of the ACM*, Vol. 21, No. 7, 1978, pp. 558–565)**: Original definition of the happens-before relation (\(\rightarrow\)), logical clocks, and total ordering of distributed events.
[^fidge-1988]: **Tier 1: Colin J. Fidge, "Timestamps in Message-Passing Systems That Preserve the Partial Ordering" (*Proceedings of the 11th Australian Computer Science Conference*, 1988, pp. 56–66)**: Vector clock algorithm and exact causal comparison properties.
[^mattern-1988]: **Tier 1: Friedemann Mattern, "Virtual Time and Global States of Distributed Systems" (*Parallel and Distributed Algorithms*, 1988, pp. 215–226)**: Independent derivation of vector time and consistent global snapshots.
[^dynamo-2007]: **Tier 1: Giuseppe DeCandia et al., "Dynamo: Amazon's Highly Available Key-Value Store" (*ACM SOSP*, 2007, pp. 205–220)**: Use of vector clocks for version branching and semantic reconciliation of concurrent shopping-cart updates.
[^hlc-2014]: **Tier 1: Sandeep S. Kulkarni et al., "Logical Physical Clocks and Consistent Snapshots in Globally Distributed Databases" (*OPODIS*, 2014)**: Formal specification and bounded-drift proofs for Hybrid Logical Clocks (HLC).
[^spanner-2012]: **Tier 1: James C. Corbett et al., "Spanner: Google's Globally-Distributed Database" (*ACM OSDI*, 2012)**: TrueTime API \([t_{\text{earliest}}, t_{\text{latest}}]\) and the \(2\varepsilon\) commit-wait rule for external consistency.
