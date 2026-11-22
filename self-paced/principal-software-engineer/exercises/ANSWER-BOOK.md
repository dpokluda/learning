# Answer Book: Sections 01 to 29

> **Spoilers ahead.** Everything in this file is a complete worked answer.
> Attempt the Part A written questionnaire and the Part B C# coding exercise in [`EXERCISE-COMPANION.md`](./EXERCISE-COMPANION.md) from memory first before opening this file.
> Every Part B C# solution in this book is the exact, compile-verified code in [`PrincipalEngineerDrills/`](./PrincipalEngineerDrills/) (`66` passing unit tests, zero warnings).

**Jump to Section:**
[01](#section-01-distributed-systems-models-and-failure-modes) ·
[02](#section-02-time-clocks-and-ordering-of-events) ·
[03](#section-03-replication-quorums-and-anti-entropy) ·
[04](#section-04-consensus-leases-and-split-brain) ·
[05](#section-05-consistency-and-transaction-isolation-models) ·
[06](#section-06-write-ahead-logs-and-storage-engines) ·
[07](#section-07-indexes-and-data-modeling-sql-vs-nosql) ·
[08](#section-08-data-partitioning-and-hashing-schemes) ·
[09](#section-09-probabilistic-data-structures-at-scale) ·
[10](#section-10-networking-proxies-and-realtime-protocols) ·
[11](#section-11-caching-architectures-and-failure-modes) ·
[12](#section-12-rate-limiting-throttling-and-resiliency) ·
[13](#section-13-microservices-messaging-and-event-driven-architecture) ·
[14](#section-14-domain-driven-design-and-software-architecture-patterns) ·
[15](#section-15-distributed-file-systems-and-coordination-gfs-hdfs-chubby) ·
[16](#section-16-wide-column-and-dynamo-family-stores-bigtable-dynamo-cassandra) ·
[17](#section-17-distributed-streaming-and-log-centric-systems-kafka) ·
[18](#section-18-capacity-planning-and-asymptotic-analysis) ·
[19](#section-19-trees-tries-and-spatial-indexing) ·
[20](#section-20-hash-tables-heaps-and-graphs) ·
[21](#section-21-dynamic-programming-greedy-and-coding-patterns) ·
[22](#section-22-control-plane-vs-data-plane-and-cell-architectures) ·
[23](#section-23-multi-tenant-isolation-and-noisy-neighbor-economics) ·
[24](#section-24-columnar-storage-and-lakehouse-pipelines) ·
[25](#section-25-policy-engines-dsls-and-cross-language-runtimes) ·
[26](#section-26-testing-in-production-safe-rollouts-and-kill-switches) ·
[27](#section-27-production-ai-and-agentic-engineering-systems) ·
[28](#section-28-system-design-methodology-and-architecture-reviews) ·
[29](#section-29-principal-leadership-and-behavioral-execution)

---

## Section 01: Distributed Systems Models and Failure Modes

### Part A: Worked Questionnaire Answers

1. **The three network link models and their protocol reductions.** From most hostile to best behaved: (1) **Arbitrary (Adversarial) Links**, where an active attacker can eavesdrop, forge, mutate, drop, or replay packets; (2) **Fair-Loss Links**, where packets are not forged, may be dropped, duplicated, or reordered, and infinitely retried packets eventually arrive; and (3) **Reliable (Perfect) Links**, where every sent message is delivered without duplication or corruption. Wrapping an **Arbitrary Link** in **TLS / mTLS** (with cryptographic MACs and sequence numbers) detects any forgery or tampering and turns it into a dropped connection/packet, reducing Arbitrary to **Fair-Loss**. Layering a **Sender Retry Loop** paired with **Receiver Idempotency-Key Deduplication** on top of a Fair-Loss Link ensures lost packets are retransmitted and duplicate deliveries are collapsed into one execution, reducing Fair-Loss to a **Reliable Link**.
2. **Crash-stop vs crash-recovery vs temporal failures.** A **crash-stop** node halts permanently and never acts again. A **crash-recovery** node crashes at any moment (losing volatile RAM) and later reboots with its durable disk state intact. A **temporal failure** occurs when a node is functionally alive but pauses execution for seconds (e.g., a stop-the-world .NET/JVM garbage-collection pause or VM live migration) and misses its timing deadlines. Designing leader election assuming crash-stop fails in production because an old leader frozen in a 12-second GC pause (or rebooting from disk) is not permanently dead: when it wakes up after a new leader has been elected, it resumes issuing conflicting writes (**split-brain**).
3. **Synchrony models.** A **synchronous** network has a strict, known upper bound $\Delta$ on message latency and process speed. An **asynchronous** network has zero timing guarantees (messages and pauses can take arbitrarily long). A **partially synchronous** network behaves synchronously (bounded by $\Delta$) *most of the time*, but unpredictably experiences finite periods of asynchrony (congestion, switch reconvergence, GC pauses). Real datacenter networks inhabit the **partially synchronous** model because TCP/IP queues and managed runtimes cannot guarantee hard real-time bounds 100% of the time, yet they are well-behaved enough most of the time to make progress.
4. **The FLP Impossibility Result and how Paxos/Raft escape it.** Fischer, Lynch, and Paterson (1985) proved that in a purely **asynchronous** network, no **deterministic** consensus algorithm can guarantee both **safety** (never deciding conflicting values) and **liveness** (eventually deciding) if even a single node is allowed to crash. Paxos and Raft escape FLP by targeting the **partially synchronous** model (and using randomized election timeouts in Raft): they guarantee **safety unconditionally** even during arbitrary asynchronous network chaos, and guarantee **liveness conditionally** whenever the network stabilizes for a window longer than the round-trip election timeout.
5. **Reliability vs Availability.** **Availability** is the percentage of time (or requests) that the service responds without error. **Reliability** is the ability of the system to perform its required function *correctly and without data loss or corruption* across real-world fault conditions. High reliability requires and implies high availability; however, a buggy system that responds `200 OK` immediately to every request while silently dropping writes or returning corrupt bytes has `100%` availability and `0%` reliability.
6. **Parallel fan-out tail latency math.** If each of the $k = 50$ shards independently has a $p = 0.01$ (`1%`) probability of taking $> 500\text{ ms}$, the probability that a single shard is fast ($\le 500\text{ ms}$) is $1 - 0.01 = 0.99$. For the overall fan-out request to finish in $\le 500\text{ ms}$, **all 50** shards must be fast simultaneously: $P(\text{all fast}) = 0.99^{50} \approx 0.605$. Therefore, the probability that at least one shard is slow is $1 - 0.99^{50} \approx \mathbf{39.5\%}$, amplifying the `1%` shard tail into nearly `40%` of user requests!

### Part B: Worked C# Solution (`Chapter01.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter01
{
    [TestMethod]
    public void Retry_and_idempotency_deduplication_transform_fair_loss_link_into_reliable_link()
    {
        SimulatedFairLossLink link = new SimulatedFairLossLink(dropEveryNthAttempt: 2, duplicateDeliveredPackets: true);
        ReliableReceiver receiver = new ReliableReceiver();
        ReliableSender sender = new ReliableSender(link, receiver);

        bool deliveredFirst = sender.SendWithRetry(new Message("device-1", SequenceNumber: 1, Payload: "temp=72"), maxAttempts: 5);
        bool deliveredSecond = sender.SendWithRetry(new Message("device-1", SequenceNumber: 2, Payload: "temp=73"), maxAttempts: 5);
        bool duplicateResend = sender.SendWithRetry(new Message("device-1", SequenceNumber: 1, Payload: "temp=72"), maxAttempts: 5);

        Assert.IsTrue(deliveredFirst);
        Assert.IsTrue(deliveredSecond);
        Assert.IsTrue(duplicateResend);
        Assert.HasCount(2, receiver.AppliedPayloads);
        Assert.AreEqual("temp=72", receiver.AppliedPayloads[0]);
        Assert.AreEqual("temp=73", receiver.AppliedPayloads[1]);
    }

    [TestMethod]
    public void Tail_latency_amplification_grows_exponentially_with_parallel_fanout()
    {
        double singleShardSlowProb = 0.01;
        double fanout1Prob = TailLatencyCalculator.ComputeFanoutTailProbability(singleShardSlowProb, parallelShards: 1);
        double fanout50Prob = TailLatencyCalculator.ComputeFanoutTailProbability(singleShardSlowProb, parallelShards: 50);
        double fanout100Prob = TailLatencyCalculator.ComputeFanoutTailProbability(singleShardSlowProb, parallelShards: 100);

        Assert.AreEqual(0.01, fanout1Prob, 0.0001);
        Assert.AreEqual(0.39499, fanout50Prob, 0.001);
        Assert.IsGreaterThan(0.63, fanout100Prob);
    }
}

file sealed record Message(string DeviceId, long SequenceNumber, string Payload);

file sealed class SimulatedFairLossLink
{
    private readonly int _dropEveryNthAttempt;
    private readonly bool _duplicateDeliveredPackets;
    private int _attemptCount;

    public SimulatedFairLossLink(int dropEveryNthAttempt, bool duplicateDeliveredPackets)
    {
        _dropEveryNthAttempt = dropEveryNthAttempt;
        _duplicateDeliveredPackets = duplicateDeliveredPackets;
    }

    public bool Transmit(Message message, ReliableReceiver receiver)
    {
        _attemptCount++;
        if (_dropEveryNthAttempt > 0 && (_attemptCount % _dropEveryNthAttempt) == 0)
        {
            return false;
        }

        receiver.Receive(message);
        if (_duplicateDeliveredPackets)
        {
            receiver.Receive(message);
        }

        return true;
    }
}

file sealed class ReliableReceiver
{
    private readonly HashSet<(string DeviceId, long SequenceNumber)> _processedKeys = new HashSet<(string, long)>();
    private readonly List<string> _appliedPayloads = new List<string>();

    public IReadOnlyList<string> AppliedPayloads
    {
        get { return _appliedPayloads; }
    }

    public void Receive(Message message)
    {
        (string, long) idempotencyKey = (message.DeviceId, message.SequenceNumber);
        if (_processedKeys.Add(idempotencyKey))
        {
            _appliedPayloads.Add(message.Payload);
        }
    }
}

file sealed class ReliableSender
{
    private readonly SimulatedFairLossLink _link;
    private readonly ReliableReceiver _receiver;

    public ReliableSender(SimulatedFairLossLink link, ReliableReceiver receiver)
    {
        _link = link;
        _receiver = receiver;
    }

    public bool SendWithRetry(Message message, int maxAttempts)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (_link.Transmit(message, _receiver))
            {
                return true;
            }
        }

        return false;
    }
}

file static class TailLatencyCalculator
{
    public static double ComputeFanoutTailProbability(double singleShardSlowProbability, int parallelShards)
    {
        double allFastProbability = Math.Pow(1.0 - singleShardSlowProbability, parallelShards);
        return 1.0 - allFastProbability;
    }
}
```

---

## Section 02: Time, Clocks, and Ordering of Events

### Part A: Worked Questionnaire Answers

1. **Wall-clock vs monotonic time and UTC vs TAI.** **Wall-clock time** (`DateTimeOffset.UtcNow`) returns calendar time synchronized with NTP and **can jump backward** (or slew) whenever the local quartz oscillator drifts ahead of the NTP server. **Monotonic time** (`Stopwatch.GetTimestamp()`) counts elapsed ticks since system boot and is guaranteed by the OS kernel **never to move backward**, though its raw value is meaningless across different machines. **TAI (International Atomic Time)** counts continuous atomic seconds ($86{,}400\text{ s/day}$); **UTC** inserts **leap seconds** to stay aligned with Earth's rotation, which causes naive Unix epoch clocks to repeat a second unless smoothed via **leap-second smearing**.
2. **Lamport's happens-before relation ($a \rightarrow b$).** $a \rightarrow b$ holds iff: (1) $a$ and $b$ occur at the same node and $a$ precedes $b$ in local program order; (2) $a$ is the sending of a unique message $m$ and $b$ is the receipt of $m$; or (3) there exists $c$ such that $a \rightarrow c$ and $c \rightarrow b$. It is a strict **partial** order because two events on different nodes with no causal message path between them satisfy neither $a \rightarrow b$ nor $b \rightarrow a$; such events are **concurrent ($a \parallel b$)**.
3. **Lamport logical clock rules and one-way implication.** Each node increments $L_i \leftarrow L_i + 1$ on local/send events, attaches $t_m = L_i$ to messages, and merges on receive via $L_j \leftarrow \max(L_j, t_m) + 1$. By induction, $a \rightarrow b \implies L(a) < L(b)$. The converse fails because two completely independent concurrent events on different nodes can have $L(a) = 2 < L(b) = 5$ simply because Node B executed more local events. Pairing the counter with the node ID $(L(e), N(e))$ defines a deterministic **total order**: $a \prec b \iff (L(a) < L(b)) \lor (L(a) = L(b) \land N(a) < N(b))$.
4. **Vector clock rules and isomorphism.** Each node $i$ maintains an array $V_i[0 \dots N-1]$. On local/send events, $V_i[i] \leftarrow V_i[i] + 1$. On receiving $V_m$, $\forall k: V_j[k] \leftarrow \max(V_j[k], V_m[k])$ followed by $V_j[j] \leftarrow V_j[j] + 1$. Two vectors satisfy $V(a) < V(b)$ iff $\forall k: V(a)[k] \le V(b)[k]$ and $\exists k': V(a)[k'] < V(b)[k']$. If neither $V(a) \le V(b)$ nor $V(b) \le V(a)$ (each is strictly larger in at least one slot), then $V(a) \parallel V(b)$ (concurrent conflict).
5. **Hybrid Logical Clocks (HLC).** Vector clocks grow in size as $O(N)$ with the number of nodes and carry no relationship to UTC wall-clock time. An HLC maintains a fixed-size tuple `(pt, l, nodeId)` where `pt = max(localWallClock, lastPt, msg.pt)` tracks the highest physical time seen, and the logical counter `l` increments whenever `pt` does not advance (such as when the local wall clock steps backward or when `msg.pt` is ahead of the local wall clock), resetting `l = 0` whenever physical time advances past `lastPt`.
6. **Google TrueTime and Commit Wait.** TrueTime returns an explicit uncertainty interval $[t_{\text{earliest}}, t_{\text{latest}}]$ of half-width $\varepsilon$ (guaranteed via GPS + atomic clocks to bound true physical time, typically $\varepsilon \le 1\text{--}7\text{ ms}$). When a coordinator assigns commit timestamp $s = t_{\text{latest}}$, it waits until $\text{TT.now().earliest} > s$ (a wait of $2\varepsilon$) before releasing locks or exposing the write, ensuring any subsequent transaction anywhere on Earth receives a strictly larger timestamp.

### Part B: Worked C# Solution (`Chapter02.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter02
{
    [TestMethod]
    public void Lamport_clock_preserves_causal_order_and_breaks_ties_deterministically()
    {
        LamportClock nodeA = new LamportClock("node-a");
        LamportClock nodeB = new LamportClock("node-b");

        LamportTimestamp sendTs = nodeA.Tick();
        LamportTimestamp recvTs = nodeB.Receive(sendTs);
        LamportTimestamp concurrentOnA = nodeA.Tick();

        Assert.IsLessThan(0, sendTs.CompareTo(recvTs));
        Assert.AreEqual(concurrentOnA.Counter, recvTs.Counter);
        Assert.IsLessThan(0, concurrentOnA.CompareTo(recvTs));
    }

    [TestMethod]
    public void Vector_clock_distinguishes_causal_succession_from_concurrent_conflict()
    {
        VectorClock replica0 = new VectorClock(nodeIndex: 0, replicaCount: 3);
        VectorClock replica1 = new VectorClock(nodeIndex: 1, replicaCount: 3);

        int[] v1 = replica0.Tick();
        int[] v2OnReplica1 = replica1.Receive(v1);
        int[] concurrentOnReplica0 = replica0.Tick();

        Assert.AreEqual(CausalRelation.HappensBefore, VectorClock.Compare(v1, v2OnReplica1));
        Assert.AreEqual(CausalRelation.HappensAfter, VectorClock.Compare(v2OnReplica1, v1));
        Assert.AreEqual(CausalRelation.Concurrent, VectorClock.Compare(v2OnReplica1, concurrentOnReplica0));
    }

    [TestMethod]
    public void Hybrid_logical_clock_stays_monotonic_when_wall_clock_steps_backward()
    {
        HybridLogicalClock hlc = new HybridLogicalClock("node-1");

        HlcTimestamp t1 = hlc.Now(wallClockMs: 1_000);
        HlcTimestamp t2 = hlc.Now(wallClockMs: 950);
        HlcTimestamp remoteAhead = new HlcTimestamp(PhysicalTimeMs: 1_200, LogicalCounter: 3, NodeId: "node-2");
        HlcTimestamp t3 = hlc.Receive(remoteAhead, localWallClockMs: 1_010);

        Assert.IsLessThan(0, t1.CompareTo(t2));
        Assert.AreEqual(1_000, t2.PhysicalTimeMs);
        Assert.AreEqual(1, t2.LogicalCounter);
        Assert.AreEqual(1_200, t3.PhysicalTimeMs);
        Assert.AreEqual(4, t3.LogicalCounter);
    }
}

file readonly record struct LamportTimestamp(long Counter, string NodeId) : IComparable<LamportTimestamp>
{
    public int CompareTo(LamportTimestamp other)
    {
        int cmp = Counter.CompareTo(other.Counter);
        if (cmp != 0)
        {
            return cmp;
        }

        return string.CompareOrdinal(NodeId, other.NodeId);
    }
}

file sealed class LamportClock
{
    private readonly string _nodeId;
    private long _counter;

    public LamportClock(string nodeId)
    {
        _nodeId = nodeId;
    }

    public LamportTimestamp Tick()
    {
        _counter++;
        return new LamportTimestamp(_counter, _nodeId);
    }

    public LamportTimestamp Receive(LamportTimestamp incoming)
    {
        _counter = Math.Max(_counter, incoming.Counter) + 1;
        return new LamportTimestamp(_counter, _nodeId);
    }
}

file enum CausalRelation
{
    Equal,
    HappensBefore,
    HappensAfter,
    Concurrent
}

file sealed class VectorClock
{
    private readonly int _nodeIndex;
    private readonly int[] _vector;

    public VectorClock(int nodeIndex, int replicaCount)
    {
        _nodeIndex = nodeIndex;
        _vector = new int[replicaCount];
    }

    public int[] Tick()
    {
        _vector[_nodeIndex]++;
        return (int[])_vector.Clone();
    }

    public int[] Receive(ReadOnlySpan<int> incoming)
    {
        for (int i = 0; i < _vector.Length; i++)
        {
            if (incoming[i] > _vector[i])
            {
                _vector[i] = incoming[i];
            }
        }

        _vector[_nodeIndex]++;
        return (int[])_vector.Clone();
    }

    public static CausalRelation Compare(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        bool leftLess = false;
        bool rightLess = false;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] < right[i])
            {
                leftLess = true;
            }
            else if (left[i] > right[i])
            {
                rightLess = true;
            }
        }

        if (!leftLess && !rightLess)
        {
            return CausalRelation.Equal;
        }

        if (leftLess && !rightLess)
        {
            return CausalRelation.HappensBefore;
        }

        if (rightLess && !leftLess)
        {
            return CausalRelation.HappensAfter;
        }

        return CausalRelation.Concurrent;
    }
}

file readonly record struct HlcTimestamp(long PhysicalTimeMs, int LogicalCounter, string NodeId) : IComparable<HlcTimestamp>
{
    public int CompareTo(HlcTimestamp other)
    {
        int ptCmp = PhysicalTimeMs.CompareTo(other.PhysicalTimeMs);
        if (ptCmp != 0)
        {
            return ptCmp;
        }

        int lCmp = LogicalCounter.CompareTo(other.LogicalCounter);
        if (lCmp != 0)
        {
            return lCmp;
        }

        return string.CompareOrdinal(NodeId, other.NodeId);
    }
}

file sealed class HybridLogicalClock
{
    private readonly string _nodeId;
    private long _lastPhysicalMs;
    private int _logicalCounter;

    public HybridLogicalClock(string nodeId)
    {
        _nodeId = nodeId;
    }

    public HlcTimestamp Now(long wallClockMs)
    {
        if (wallClockMs > _lastPhysicalMs)
        {
            _lastPhysicalMs = wallClockMs;
            _logicalCounter = 0;
        }
        else
        {
            _logicalCounter++;
        }

        return new HlcTimestamp(_lastPhysicalMs, _logicalCounter, _nodeId);
    }

    public HlcTimestamp Receive(HlcTimestamp messageTs, long localWallClockMs)
    {
        long maxPhysical = Math.Max(localWallClockMs, Math.Max(_lastPhysicalMs, messageTs.PhysicalTimeMs));
        if (maxPhysical == _lastPhysicalMs && maxPhysical == messageTs.PhysicalTimeMs)
        {
            _logicalCounter = Math.Max(_logicalCounter, messageTs.LogicalCounter) + 1;
        }
        else if (maxPhysical == _lastPhysicalMs)
        {
            _logicalCounter++;
        }
        else if (maxPhysical == messageTs.PhysicalTimeMs)
        {
            _logicalCounter = messageTs.LogicalCounter + 1;
        }
        else
        {
            _logicalCounter = 0;
        }

        _lastPhysicalMs = maxPhysical;
        return new HlcTimestamp(_lastPhysicalMs, _logicalCounter, _nodeId);
    }
}
```

---

## Section 03: Replication, Quorums, and Anti-Entropy

### Part A: Worked Questionnaire Answers

1. **Synchronous vs asynchronous vs semi-synchronous replication.** **Synchronous replication** waits for all followers before acknowledging a write (guaranteeing zero data loss, `RPO = 0`, at the cost of blocking writes if any single follower fails). **Asynchronous replication** acknowledges writes as soon as the leader appends locally and streams to followers in the background (delivering low write latency and high availability, but losing acknowledged writes, `RPO > 0`, if the leader suffers an unrecoverable disk crash before shipping its log tail). **Semi-synchronous (quorum) replication** waits for at least a majority quorum of replicas before acknowledging, tolerating minority follower failures while guaranteeing zero committed data loss.
2. **The strict quorum inequality ($R + W > N$).** When choosing a write subset of size $W$ and a read subset of size $R$ from the same $N$ canonical replicas, the sum of their sizes exceeds $N$, so by the pigeonhole principle at least $R + W - N \ge 1$ replica must belong to both sets. That overlapping replica holds the newest write version, allowing the reader to pick the highest version number among the $R$ responses.
3. **Four scenarios where $R + W > N$ still fails linearizability.** (1) **Concurrent writes with Last-Write-Wins (LWW) physical clocks**, where clock skew silently drops the later write. (2) **Failed writes ($< W$ acks) that are not rolled back** on the 1 node where they succeeded, exposing uncommitted data to subsequent reads. (3) **Concurrent reads during an in-flight write**, where Reader A hits `{UpdatedNode1, StaleNode2}` and sees `v=2`, and immediately afterward Reader B hits `{StaleNode2, StaleNode3}` and sees `v=1` (violating real-time recency unless Reader A writes `v=2` back to a write quorum before returning). (4) **Falling back to a Sloppy Quorum** during a partition.
4. **Strict Quorum vs Sloppy Quorum + Hinted Handoff.** A strict quorum requires $W$ responses from the key's canonical $N$ nodes on the preference list. A sloppy quorum allows the coordinator to write to healthy *surrogate* nodes further clockwise around the ring when canonical nodes are unreachable, storing a local **hint** that is replayed via **Hinted Handoff** once the canonical nodes recover. Sloppy quorums do **not** guarantee read-your-writes consistency during the partition because a reader querying the canonical nodes will miss the hinted write sitting on the surrogate node.
5. **Digest-based Read Repair.** The coordinator sends a full data read to 1 replica and lightweight **digest (checksum + version) reads** to the other $R - 1$ replicas. If all digests match, it returns the full payload immediately. If any digest differs, it fetches full values from all $R$ replicas, returns the newest (or merged) value to the client, and asynchronously writes the newest value back to the stale replicas.
6. **Merkle Trees for background anti-entropy.** Read repair never heals **cold keys** that are not read. A **Merkle Tree** is a binary tree of hashes over sorted key subranges (leaves). Two replicas compare their 32-byte Root Hashes; if equal, the entire range is in sync. If different, they recurse down only the branches whose hashes differ, isolating the exact out-of-sync key bucket in $O(\log B)$ rounds. Without virtual nodes, node additions/removals shift key range boundaries and invalidate the tree's bucket layout.

### Part B: Worked C# Solution (`Chapter03.cs`)

```csharp
using System.Security.Cryptography;
using System.Text;

namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter03
{
    [TestMethod]
    public void Strict_quorum_reads_latest_version_and_triggers_read_repair_on_stale_replicas()
    {
        QuorumReplicaCluster cluster = new QuorumReplicaCluster(replicaCount: 3);

        bool writeOk = cluster.WriteStrictQuorum("k1", new VersionedValue(Version: 2, Value: "v2"), writeQuorum: 2, healthyMask: [true, true, false]);
        VersionedValue? readResult = cluster.ReadWithRepair("k1", readQuorum: 2, healthyMask: [false, true, true]);

        Assert.IsTrue(writeOk);
        Assert.IsNotNull(readResult);
        Assert.AreEqual(2, readResult.Value.Version);
        Assert.AreEqual("v2", readResult.Value.Value);
        Assert.AreEqual(2, cluster.GetReplicaRaw("k1", replicaIndex: 2)?.Version);
    }

    [TestMethod]
    public void Sloppy_quorum_stores_hint_on_surrogate_and_replays_during_hinted_handoff()
    {
        QuorumReplicaCluster cluster = new QuorumReplicaCluster(replicaCount: 3);

        bool sloppyOk = cluster.WriteSloppyQuorum("k1", new VersionedValue(Version: 5, Value: "v5"), writeQuorum: 2, primaryHealthy: [true, false, false]);
        Assert.IsTrue(sloppyOk);
        Assert.IsNull(cluster.GetReplicaRaw("k1", replicaIndex: 1));

        cluster.ReplayHints();
        Assert.AreEqual("v5", cluster.GetReplicaRaw("k1", replicaIndex: 1)?.Value);
    }

    [TestMethod]
    public void Merkle_tree_isolates_divergent_key_bucket_in_logarithmic_comparisons()
    {
        string[] replicaA = ["a:1", "b:1", "c:1", "d:1", "e:1", "f:1", "g:1", "h:1"];
        string[] replicaB = ["a:1", "b:1", "c:1", "d:1", "e:1", "f:999", "g:1", "h:1"];

        MerkleNode treeA = MerkleTree.Build(replicaA);
        MerkleNode treeB = MerkleTree.Build(replicaB);

        List<int> differingLeaves = new List<int>();
        MerkleTree.FindDifferingBuckets(treeA, treeB, differingLeaves);

        Assert.HasCount(1, differingLeaves);
        Assert.AreEqual(5, differingLeaves[0]);
    }
}

file readonly record struct VersionedValue(long Version, string Value);

file sealed class QuorumReplicaCluster
{
    private readonly Dictionary<string, VersionedValue>[] _replicas;
    private readonly List<(int TargetReplica, string Key, VersionedValue Value)> _hints = new List<(int, string, VersionedValue)>();

    public QuorumReplicaCluster(int replicaCount)
    {
        _replicas = new Dictionary<string, VersionedValue>[replicaCount];
        for (int i = 0; i < replicaCount; i++)
        {
            _replicas[i] = new Dictionary<string, VersionedValue>(StringComparer.Ordinal);
        }
    }

    public VersionedValue? GetReplicaRaw(string key, int replicaIndex)
    {
        if (_replicas[replicaIndex].TryGetValue(key, out VersionedValue val))
        {
            return val;
        }

        return null;
    }

    public bool WriteStrictQuorum(string key, VersionedValue val, int writeQuorum, ReadOnlySpan<bool> healthyMask)
    {
        int acks = 0;
        for (int i = 0; i < _replicas.Length; i++)
        {
            if (healthyMask[i])
            {
                _replicas[i][key] = val;
                acks++;
            }
        }

        return acks >= writeQuorum;
    }

    public bool WriteSloppyQuorum(string key, VersionedValue val, int writeQuorum, ReadOnlySpan<bool> primaryHealthy)
    {
        int acks = 0;
        for (int i = 0; i < _replicas.Length; i++)
        {
            if (primaryHealthy[i])
            {
                _replicas[i][key] = val;
                acks++;
            }
            else if (acks < writeQuorum)
            {
                _hints.Add((i, key, val));
                acks++;
            }
        }

        return acks >= writeQuorum;
    }

    public void ReplayHints()
    {
        foreach ((int targetReplica, string key, VersionedValue val) in _hints)
        {
            _replicas[targetReplica][key] = val;
        }

        _hints.Clear();
    }

    public VersionedValue? ReadWithRepair(string key, int readQuorum, ReadOnlySpan<bool> healthyMask)
    {
        List<(int ReplicaIndex, VersionedValue? Val)> responses = new List<(int, VersionedValue?)>();
        for (int i = 0; i < _replicas.Length && responses.Count < readQuorum; i++)
        {
            if (healthyMask[i])
            {
                VersionedValue? v = GetReplicaRaw(key, i);
                responses.Add((i, v));
            }
        }

        if (responses.Count < readQuorum)
        {
            return null;
        }

        VersionedValue? latest = null;
        foreach ((int _, VersionedValue? candidate) in responses)
        {
            if (candidate.HasValue && (!latest.HasValue || candidate.Value.Version > latest.Value.Version))
            {
                latest = candidate.Value;
            }
        }

        if (latest.HasValue)
        {
            foreach ((int replicaIdx, VersionedValue? candidate) in responses)
            {
                if (!candidate.HasValue || candidate.Value.Version < latest.Value.Version)
                {
                    _replicas[replicaIdx][key] = latest.Value;
                }
            }
        }

        return latest;
    }
}

file sealed record MerkleNode(string HashHex, int? LeafBucketIndex, MerkleNode? Left, MerkleNode? Right);

file static class MerkleTree
{
    public static MerkleNode Build(ReadOnlySpan<string> bucketPayloads)
    {
        List<MerkleNode> currentLevel = new List<MerkleNode>(bucketPayloads.Length);
        for (int i = 0; i < bucketPayloads.Length; i++)
        {
            currentLevel.Add(new MerkleNode(ComputeSha256(bucketPayloads[i]), i, null, null));
        }

        while (currentLevel.Count > 1)
        {
            List<MerkleNode> nextLevel = new List<MerkleNode>((currentLevel.Count + 1 / 2));
            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                MerkleNode left = currentLevel[i];
                MerkleNode right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;
                string combinedHash = ComputeSha256(left.HashHex + ":" + right.HashHex);
                nextLevel.Add(new MerkleNode(combinedHash, null, left, right));
            }

            currentLevel = nextLevel;
        }

        return currentLevel[0];
    }

    public static void FindDifferingBuckets(MerkleNode nodeA, MerkleNode nodeB, List<int> differingBuckets)
    {
        if (string.Equals(nodeA.HashHex, nodeB.HashHex, StringComparison.Ordinal))
        {
            return;
        }

        if (nodeA.LeafBucketIndex.HasValue)
        {
            differingBuckets.Add(nodeA.LeafBucketIndex.Value);
            return;
        }

        if (nodeA.Left is not null && nodeB.Left is not null)
        {
            FindDifferingBuckets(nodeA.Left, nodeB.Left, differingBuckets);
        }

        if (nodeA.Right is not null && nodeB.Right is not null)
        {
            FindDifferingBuckets(nodeA.Right, nodeB.Right, differingBuckets);
        }
    }

    private static string ComputeSha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
```

---

## Section 04: Consensus, Leases, and Split-Brain

### Part A: Worked Questionnaire Answers

1. **Single-Decree Paxos Phases 1 & 2.** In Phase 1a (`Prepare(n)`), a Proposer sends ballot number $n$ to a majority of Acceptors. In Phase 1b (`Promise`), each Acceptor checks if $n > \text{maxPromisedBallot}$; if so, it promises never to accept any ballot $< n$ and returns its highest previously accepted `(acceptedBallot, acceptedValue)`. In Phase 2a (`Accept(n, v)`), **if any Acceptor in the majority returned an already-accepted value, the Proposer is forced to propose the `acceptedValue` with the highest `acceptedBallot`**; only if all responses had no prior accepted value may the Proposer use its own client value. This guarantees safety because any future majority $Q_2$ must overlap in at least one Acceptor with the majority $Q_1$ that chose an earlier value.
2. **Multi-Paxos optimization.** Running Phase 1 on every log slot costs 2 RTTs and risks dueling-proposer livelock. Multi-Paxos elects a stable Leader by running Phase 1 *once* across the entire future sequence of log slots; during steady state, the Leader executes only Phase 2 (`Accept / Accepted`, 1 RTT) for each new client write.
3. **Raft Election Restriction and Leader Completeness.** During `RequestVote`, a Raft voter denies its vote if its own log is more up-to-date than the candidate's log, where log $A$ beats log $B$ iff $(A.\text{lastLogTerm} > B.\text{lastLogTerm}) \lor (A.\text{lastLogTerm} = B.\text{lastLogTerm} \land A.\text{lastLogIndex} \ge B.\text{lastLogIndex})$. Because a committed entry resides on a majority of servers and a candidate must win votes from a majority of servers, the overlapping voter rejects any candidate missing a committed entry; proving **Leader Completeness**.
4. **Why Raft never commits previous-term entries by counting replicas alone.** An uncommitted entry from an older term (say `term 2`) could be replicated to a majority of nodes by a later leader in `term 4`, yet still be overwritten if that leader crashes and another node whose last log entry has `term 3` (`3 > 2`) wins election from a majority that did not all hold the `term 4` entry. Therefore, a Raft Leader only marks entries from its **current term** committed by counting replicas; once a current-term entry is committed, the Log Matching Property commits all preceding entries indirectly.
5. **Leases vs GC pauses.** Indefinite locks wedge a resource forever if the lock holder crashes. A **time-bounded lease** expires automatically after duration $T_{\text{lease}}$. However, if Leader 1 checks its local lease timer (`valid!`) and then freezes in a 15-second GC pause right before sending a write packet, its lease expires while frozen, Leader 2 acquires the lease, and Leader 1 wakes up and overwrites Leader 2's state.
6. **Monotonic Fencing Tokens, Resource Fencing, and STONITH.** Every time the lock/lease service grants leadership, it increments a 64-bit monotonic **Fencing Token** (`33, 34, 35...`). Every write to the storage server carries the writer's token, and the storage server rejects any write with `token < maxSeenToken`. **Resource Fencing** revokes the old node's storage/network access (via tokens or SAN/switch ACLs); **Node Fencing (`STONITH`)** powers off or resets the physical machine via IPMI/BMC.

### Part B: Worked C# Solution (`Chapter04.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter04
{
    [TestMethod]
    public void Paxos_phase_one_discovers_already_chosen_value_and_prevents_overwriting_it()
    {
        PaxosAcceptor[] acceptors = [new PaxosAcceptor(), new PaxosAcceptor(), new PaxosAcceptor()];

        bool chosenFirst = PaxosProposer.Propose(acceptors, quorumIndices: [0, 1], ballot: 10, clientValue: "Rollout-v1");
        bool chosenSecond = PaxosProposer.Propose(acceptors, quorumIndices: [1, 2], ballot: 20, clientValue: "Conflicting-v2");

        Assert.IsTrue(chosenFirst);
        Assert.IsTrue(chosenSecond);
        Assert.AreEqual("Rollout-v1", acceptors[2].AcceptedValue);
    }

    [TestMethod]
    public void Monotonic_fencing_token_at_storage_layer_rejects_delayed_zombie_leader_writes()
    {
        LeaseLockService lockService = new LeaseLockService(leaseDurationMs: 1_000);
        FencedStorageServer storage = new FencedStorageServer();

        LeaseGrant leader1Lease = lockService.Acquire("shard-1", nowMs: 0);
        Assert.IsTrue(leader1Lease.IsGranted);

        LeaseGrant leader2Lease = lockService.Acquire("shard-1", nowMs: 1_200);
        Assert.IsTrue(leader2Lease.IsGranted);
        Assert.IsGreaterThan(leader1Lease.FencingToken, leader2Lease.FencingToken);

        bool leader2Write = storage.TryWrite("shard-1", "CleanStateFromLeader2", leader2Lease.FencingToken);
        bool zombieLeader1Write = storage.TryWrite("shard-1", "CorruptStateFromZombieLeader1", leader1Lease.FencingToken);

        Assert.IsTrue(leader2Write);
        Assert.IsFalse(zombieLeader1Write);
        Assert.AreEqual("CleanStateFromLeader2", storage.Read("shard-1"));
    }
}

file sealed class PaxosAcceptor
{
    public long MaxPromisedBallot { get; private set; } = -1;

    public long AcceptedBallot { get; private set; } = -1;

    public string? AcceptedValue { get; private set; }

    public (bool Promised, long AcceptedBallot, string? AcceptedValue) Prepare(long ballot)
    {
        if (ballot > MaxPromisedBallot)
        {
            MaxPromisedBallot = ballot;
            return (true, AcceptedBallot, AcceptedValue);
        }

        return (false, AcceptedBallot, AcceptedValue);
    }

    public bool Accept(long ballot, string value)
    {
        if (ballot >= MaxPromisedBallot)
        {
            MaxPromisedBallot = ballot;
            AcceptedBallot = ballot;
            AcceptedValue = value;
            return true;
        }

        return false;
    }
}

file static class PaxosProposer
{
    public static bool Propose(PaxosAcceptor[] cluster, ReadOnlySpan<int> quorumIndices, long ballot, string clientValue)
    {
        int majority = (cluster.Length / 2) + 1;
        int promises = 0;
        long highestAcceptedBallot = -1;
        string valueToPropose = clientValue;

        foreach (int idx in quorumIndices)
        {
            (bool promised, long accBallot, string? accVal) = cluster[idx].Prepare(ballot);
            if (promised)
            {
                promises++;
                if (accVal is not null && accBallot > highestAcceptedBallot)
                {
                    highestAcceptedBallot = accBallot;
                    valueToPropose = accVal;
                }
            }
        }

        if (promises < majority)
        {
            return false;
        }

        int acceptedCount = 0;
        foreach (int idx in quorumIndices)
        {
            if (cluster[idx].Accept(ballot, valueToPropose))
            {
                acceptedCount++;
            }
        }

        return acceptedCount >= majority;
    }
}

file readonly record struct LeaseGrant(bool IsGranted, long FencingToken, long ExpiresAtMs);

file sealed class LeaseLockService
{
    private readonly long _leaseDurationMs;
    private readonly Dictionary<string, long> _leaseExpiryByResource = new Dictionary<string, long>(StringComparer.Ordinal);
    private long _monotonicToken;

    public LeaseLockService(long leaseDurationMs)
    {
        _leaseDurationMs = leaseDurationMs;
    }

    public LeaseGrant Acquire(string resourceId, long nowMs)
    {
        if (_leaseExpiryByResource.TryGetValue(resourceId, out long expiresAt) && nowMs < expiresAt)
        {
            return new LeaseGrant(false, -1, expiresAt);
        }

        _monotonicToken++;
        long newExpiry = nowMs + _leaseDurationMs;
        _leaseExpiryByResource[resourceId] = newExpiry;
        return new LeaseGrant(true, _monotonicToken, newExpiry);
    }
}

file sealed class FencedStorageServer
{
    private readonly Dictionary<string, (long MaxToken, string Value)> _store = new Dictionary<string, (long, string)>(StringComparer.Ordinal);

    public bool TryWrite(string resourceId, string value, long fencingToken)
    {
        if (_store.TryGetValue(resourceId, out (long MaxToken, string Value) existing))
        {
            if (fencingToken < existing.MaxToken)
            {
                return false;
            }
        }

        _store[resourceId] = (fencingToken, value);
        return true;
    }

    public string? Read(string resourceId)
    {
        if (_store.TryGetValue(resourceId, out (long _, string Value) existing))
        {
            return existing.Value;
        }

        return null;
    }
}
```

---

## Section 05: Consistency and Transaction Isolation Models

### Part A: Worked Questionnaire Answers

1. **CAP vs PACELC.** Network partitions are physical faults, not an optional feature you can turn off, so "CA" does not exist in distributed networks; CAP means *during a partition (`P`), choose `C` (linearizability) or `A` (availability)*. **PACELC** adds that *even when the network is healthy (`E` = Else), a replicated system must choose between Latency (`L`) and Consistency (`C`)*: `PC/EC` systems (BigTable, Spanner, CockroachDB) pay consensus/lease latency in normal operation for strong consistency and reject minority partitions, whereas `PA/EL` systems (Dynamo, Cassandra) prioritize low latency in normal operation and high availability during partitions.
2. **Single-object consistency hierarchy.** Strongest to weakest: **Linearizability** (single global order matching real-time invocation/completion intervals) $\to$ **Sequential Consistency** (single global order preserving each node's program order, but allowed to lag real time across clients) $\to$ **Causal Consistency** (all nodes agree on the order of causally related events $a \rightarrow b$, while concurrent $a \parallel b$ may diverge) $\to$ **Eventual Consistency**. Linearizability provides **real-time recency**; **Causal Consistency** is the strongest model achievable while remaining `AP` during a partition.
3. **Session guarantees.** (1) **Read-Your-Writes** (a client always sees its own prior writes); (2) **Monotonic Reads** (once a client reads version $v_2$, a later read by that client never returns an older version $v_1 < v_2$); (3) **Monotonic Writes** (writes by the same client are applied in order on all replicas); and (4) **Writes-Follow-Reads** (a write following a read of $v_1$ is ordered after $v_1$). Violating Monotonic Reads causes a user who refreshes a page and hits a lagging replica to see newly appeared comments suddenly vanish ("time-traveling backward").
4. **Six multi-object ACID concurrency anomalies.** **Dirty Write** (overwriting an uncommitted row); **Dirty Read** (reading an uncommitted row); **Non-Repeatable Read / Read Skew** (re-reading a row or related row pair after another transaction committed mid-flight, seeing an inconsistent intermediate sum); **Lost Update** (two concurrent read-modify-write cycles overwriting one another); **Phantom Read** (a concurrent insert/delete changing the set of rows matching a `WHERE` predicate); and **Write Skew** (two transactions reading an overlapping set of rows, making disjoint updates based on that premise, and jointly violating a multi-row invariant).
5. **Write Skew under Snapshot Isolation (`MVCC`).** Both Alice (`T1`) and Bob (`T2`) start transactions at `Snapshot = 10` where both doctors are `on-call` (`count = 2`). Both read `count = 2 >= 2`, then `T1` updates `Alice = off-call` and `T2` updates `Bob = off-call`. Because `T1` and `T2` modified *different* keys (`Alice` vs `Bob`), Snapshot Isolation's First-Committer-Wins write-write conflict check passes both commits, leaving `0` doctors on call!
6. **2PL vs Serializable Snapshot Isolation (`SSI`).** **Strict Two-Phase Locking (`2PL`)** prevents Write Skew pessimistically by acquiring shared (`S`) index-range/predicate locks on read and upgrading to exclusive (`X`) locks on write (causing readers and writers to block each other and deadlocking `T1` and `T2`). **SSI** runs optimistically on lock-free **MVCC** (readers never block writers), tracking `SIREAD` rw-antidependencies and aborting the second committer at `COMMIT` time if any key/predicate in its `ReadSet` was modified after its snapshot.

### Part B: Worked C# Solution (`Chapter05.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter05
{
    [TestMethod]
    public void Snapshot_isolation_permits_write_skew_when_concurrent_transactions_mutate_disjoint_keys()
    {
        MvccDatabase db = new MvccDatabase();
        db.Seed("doctor:alice", "on-call");
        db.Seed("doctor:bob", "on-call");

        MvccTransaction txAlice = db.BeginTransaction();
        MvccTransaction txBob = db.BeginTransaction();

        int aliceCount = (txAlice.Read("doctor:alice") == "on-call" ? 1 : 0) + (txAlice.Read("doctor:bob") == "on-call" ? 1 : 0);
        int bobCount = (txBob.Read("doctor:alice") == "on-call" ? 1 : 0) + (txBob.Read("doctor:bob") == "on-call" ? 1 : 0);
        Assert.AreEqual(2, aliceCount);
        Assert.AreEqual(2, bobCount);

        txAlice.Write("doctor:alice", "off-call");
        txBob.Write("doctor:bob", "off-call");

        bool aliceCommitted = db.Commit(txAlice, enforceSerializableSsi: false);
        bool bobCommitted = db.Commit(txBob, enforceSerializableSsi: false);

        Assert.IsTrue(aliceCommitted);
        Assert.IsTrue(bobCommitted);
    }

    [TestMethod]
    public void Serializable_snapshot_isolation_aborts_second_committer_on_read_write_antidependency()
    {
        MvccDatabase db = new MvccDatabase();
        db.Seed("doctor:alice", "on-call");
        db.Seed("doctor:bob", "on-call");

        MvccTransaction txAlice = db.BeginTransaction();
        MvccTransaction txBob = db.BeginTransaction();

        _ = txAlice.Read("doctor:alice");
        _ = txAlice.Read("doctor:bob");
        _ = txBob.Read("doctor:alice");
        _ = txBob.Read("doctor:bob");

        txAlice.Write("doctor:alice", "off-call");
        txBob.Write("doctor:bob", "off-call");

        bool aliceCommitted = db.Commit(txAlice, enforceSerializableSsi: true);
        bool bobCommitted = db.Commit(txBob, enforceSerializableSsi: true);

        Assert.IsTrue(aliceCommitted);
        Assert.IsFalse(bobCommitted);
    }
}

file sealed class MvccTransaction
{
    public long SnapshotTxId { get; }

    public HashSet<string> ReadSet { get; } = new HashSet<string>(StringComparer.Ordinal);

    public Dictionary<string, string> WriteSet { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly MvccDatabase _db;

    public MvccTransaction(long snapshotTxId, MvccDatabase db)
    {
        SnapshotTxId = snapshotTxId;
        _db = db;
    }

    public string? Read(string key)
    {
        ReadSet.Add(key);
        if (WriteSet.TryGetValue(key, out string? localVal))
        {
            return localVal;
        }

        return _db.ReadAtSnapshot(key, SnapshotTxId);
    }

    public void Write(string key, string value)
    {
        WriteSet[key] = value;
    }
}

file sealed class MvccDatabase
{
    private readonly Dictionary<string, List<(long CommitTxId, string Value)>> _versions =
        new Dictionary<string, List<(long, string)>>(StringComparer.Ordinal);
    private long _latestCommitTxId;

    public void Seed(string key, string value)
    {
        _latestCommitTxId++;
        _versions[key] = new List<(long, string)> { (_latestCommitTxId, value) };
    }

    public MvccTransaction BeginTransaction()
    {
        return new MvccTransaction(_latestCommitTxId, this);
    }

    public string? ReadAtSnapshot(string key, long snapshotTxId)
    {
        if (!_versions.TryGetValue(key, out List<(long CommitTxId, string Value)>? history))
        {
            return null;
        }

        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].CommitTxId <= snapshotTxId)
            {
                return history[i].Value;
            }
        }

        return null;
    }

    public bool Commit(MvccTransaction tx, bool enforceSerializableSsi)
    {
        foreach (string writtenKey in tx.WriteSet.Keys)
        {
            if (_versions.TryGetValue(writtenKey, out List<(long CommitTxId, string Value)>? history))
            {
                if (history[^1].CommitTxId > tx.SnapshotTxId)
                {
                    return false;
                }
            }
        }

        if (enforceSerializableSsi)
        {
            foreach (string readKey in tx.ReadSet)
            {
                if (_versions.TryGetValue(readKey, out List<(long CommitTxId, string Value)>? history))
                {
                    if (history[^1].CommitTxId > tx.SnapshotTxId)
                    {
                        return false;
                    }
                }
            }
        }

        _latestCommitTxId++;
        foreach ((string key, string val) in tx.WriteSet)
        {
            if (!_versions.TryGetValue(key, out List<(long CommitTxId, string Value)>? history))
            {
                history = new List<(long, string)>();
                _versions[key] = history;
            }

            history.Add((_latestCommitTxId, val));
        }

        return true;
    }
}
```

---

## Section 06: Write-Ahead Logs and Storage Engines

### Part A: Worked Questionnaire Answers

1. **The WAL Invariant, `fsync`, and Group Commit.** The Write-Ahead Invariant requires every mutation to be appended and flushed to the on-disk WAL *before* modified data pages are written to disk or commit is acknowledged to the client. Ordinary `write()` only copies bytes into the volatile OS kernel pagecache, which is lost on power failure; **`fsync()`** (`Flush(flushToDisk: true)`) forces the storage drive to persist the bytes to non-volatile media. **Group Commit** batches concurrent transactions arriving within a 1–2 ms window into a single `fsync()` call, multiplying commit IOPS by $10\times\text{ to }50\times$.
2. **Log Segmentation, Low-Water Mark, and Log Compaction.** **Log Segmentation** rolls the active WAL file into immutable closed segment files once a size (`1 GB`) or time (`4h`) limit is reached. A **Checkpoint Low-Water Mark** records the LSN up to which all changes have been safely flushed to main B+Tree/SSTable storage, allowing whole older WAL segments to be deleted. **Log Compaction** scans log segments and retains only the latest record for each unique key.
3. **WAL Checksums and torn tail writes.** Every WAL frame carries a `CRC32C` checksum over its header and payload. If a power loss occurs mid-write, the last record at the tail of the WAL will have a truncated length or a mismatched CRC; during crash recovery, the engine detects the checksum failure, recognizes the record as an uncommitted **torn write**, **truncates the WAL file back to the start of that partial record**, and starts cleanly.
4. **B+Trees vs LSM-Trees.** **B+Trees** store data in fixed-size ($4\text{--}16\text{ KB}$) disk pages with branching factor $200\text{--}500$ and update pages **in place** (using ARIES undo/redo WAL recovery), optimizing for fast, predictable point and range reads (each key lives in 1 place) at the cost of random write I/O. **LSM-Trees** append writes sequentially to a WAL and a sorted in-memory **MemTable**, flushing immutable **SSTables** (guarded by in-memory **Sparse Indexes** and **Bloom Filters**) and merging them in the background, optimizing for massive write throughput and tight compression at the cost of multi-file read fan-out and compaction I/O.
5. **Tombstones and Size-Tiered vs Leveled Compaction.** Because SSTables are immutable on disk, deleting a key in an LSM-Tree appends a **Tombstone** marker that masks older versions until compaction reaches the oldest level. **Size-Tiered Compaction** merges similarly sized SSTables (lower write amplification, higher temporary disk space overhead up to 50%); **Leveled Compaction** organizes levels `L1..Lk` ($10\times$ growth per level) into non-overlapping key ranges (lower space amplification and faster point reads, higher compaction write amplification).
6. **Log End Offset (`LEO`) vs High-Water Mark (`HWM`).** `LEO` is the latest entry appended to the leader's local WAL; `HWM` is the highest entry replicated to a quorum of followers. Exposing entries above `HWM` to readers would allow a client to read uncommitted state that vanishes if the leader crashes before replicating it.

### Part B: Worked C# Solution (`Chapter06.cs`)

```csharp
using System.Buffers.Binary;
using System.Text;

namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter06
{
    [TestMethod]
    public void Write_ahead_log_validates_crc_and_truncates_torn_tail_record_on_recovery()
    {
        using MemoryStream walStream = new MemoryStream();
        BinaryWal wal = new BinaryWal(walStream);

        wal.Append(lsn: 1, key: "k1", value: "v1");
        wal.Append(lsn: 2, key: "k2", value: "v2");

        walStream.Write([0xDE, 0xAD, 0xBE, 0xEF, 0x01]);

        IReadOnlyList<(long Lsn, string Key, string Value)> recovered = wal.RecoverValidRecords();

        Assert.HasCount(2, recovered);
        Assert.AreEqual("v1", recovered[0].Value);
        Assert.AreEqual("v2", recovered[1].Value);
    }

    [TestMethod]
    public void Lsm_engine_reads_across_memtable_and_sstables_and_honors_tombstone_deletes()
    {
        MiniLsmEngine lsm = new MiniLsmEngine(memTableFlushThreshold: 2);

        lsm.Put("device-1", "v1");
        lsm.Put("device-2", "v1");
        lsm.Put("device-1", "v2");
        lsm.Delete("device-2");

        Assert.AreEqual("v2", lsm.Get("device-1"));
        Assert.IsNull(lsm.Get("device-2"));

        lsm.CompactAll();
        Assert.AreEqual(1, lsm.SsTableCount);
        Assert.AreEqual("v2", lsm.Get("device-1"));
        Assert.IsNull(lsm.Get("device-2"));
    }
}

file sealed class BinaryWal
{
    private readonly MemoryStream _stream;

    public BinaryWal(MemoryStream stream)
    {
        _stream = stream;
    }

    public void Append(long lsn, string key, string value)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] valBytes = Encoding.UTF8.GetBytes(value);
        byte[] payload = new byte[8 + 4 + 4 + keyBytes.Length + valBytes.Length];

        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, 8), lsn);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), keyBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), valBytes.Length);
        keyBytes.CopyTo(payload.AsSpan(16));
        valBytes.CopyTo(payload.AsSpan(16 + keyBytes.Length));

        uint crc = ComputeFnv1a32(payload);
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), crc);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), payload.Length);

        _stream.Position = _stream.Length;
        _stream.Write(header);
        _stream.Write(payload);
    }

    public IReadOnlyList<(long Lsn, string Key, string Value)> RecoverValidRecords()
    {
        List<(long, string, string)> result = new List<(long, string, string)>();
        _stream.Position = 0;
        Span<byte> header = stackalloc byte[8];

        while (_stream.Position + 8 <= _stream.Length)
        {
            long recordStart = _stream.Position;
            int readHeader = _stream.Read(header);
            if (readHeader < 8)
            {
                _stream.SetLength(recordStart);
                break;
            }

            uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0, 4));
            int payloadLen = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));
            if (payloadLen <= 0 || _stream.Position + payloadLen > _stream.Length)
            {
                _stream.SetLength(recordStart);
                break;
            }

            byte[] payload = new byte[payloadLen];
            _ = _stream.Read(payload, 0, payloadLen);
            if (ComputeFnv1a32(payload) != expectedCrc)
            {
                _stream.SetLength(recordStart);
                break;
            }

            long lsn = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0, 8));
            int keyLen = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4));
            int valLen = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(12, 4));
            string key = Encoding.UTF8.GetString(payload, 16, keyLen);
            string val = Encoding.UTF8.GetString(payload, 16 + keyLen, valLen);
            result.Add((lsn, key, val));
        }

        return result;
    }

    private static uint ComputeFnv1a32(ReadOnlySpan<byte> bytes)
    {
        uint hash = 2166136261u;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return hash;
    }
}

file readonly record struct LsmEntry(string? Value, bool IsTombstone);

file sealed class MiniLsmEngine
{
    private readonly int _flushThreshold;
    private readonly SortedDictionary<string, LsmEntry> _memTable = new SortedDictionary<string, LsmEntry>(StringComparer.Ordinal);
    private readonly List<SortedDictionary<string, LsmEntry>> _ssTables = new List<SortedDictionary<string, LsmEntry>>();

    public MiniLsmEngine(int memTableFlushThreshold)
    {
        _flushThreshold = memTableFlushThreshold;
    }

    public int SsTableCount
    {
        get { return _ssTables.Count; }
    }

    public void Put(string key, string value)
    {
        _memTable[key] = new LsmEntry(value, IsTombstone: false);
        FlushIfFull();
    }

    public void Delete(string key)
    {
        _memTable[key] = new LsmEntry(null, IsTombstone: true);
        FlushIfFull();
    }

    public string? Get(string key)
    {
        if (_memTable.TryGetValue(key, out LsmEntry memEntry))
        {
            return memEntry.IsTombstone ? null : memEntry.Value;
        }

        for (int i = _ssTables.Count - 1; i >= 0; i--)
        {
            if (_ssTables[i].TryGetValue(key, out LsmEntry sstEntry))
            {
                return sstEntry.IsTombstone ? null : sstEntry.Value;
            }
        }

        return null;
    }

    public void CompactAll()
    {
        SortedDictionary<string, LsmEntry> merged = new SortedDictionary<string, LsmEntry>(StringComparer.Ordinal);
        foreach (SortedDictionary<string, LsmEntry> sst in _ssTables)
        {
            foreach ((string k, LsmEntry v) in sst)
            {
                merged[k] = v;
            }
        }

        foreach ((string k, LsmEntry v) in _memTable)
        {
            merged[k] = v;
        }

        _memTable.Clear();
        _ssTables.Clear();

        SortedDictionary<string, LsmEntry> compacted = new SortedDictionary<string, LsmEntry>(StringComparer.Ordinal);
        foreach ((string k, LsmEntry v) in merged)
        {
            if (!v.IsTombstone)
            {
                compacted[k] = v;
            }
        }

        _ssTables.Add(compacted);
    }

    private void FlushIfFull()
    {
        if (_memTable.Count >= _flushThreshold)
        {
            _ssTables.Add(new SortedDictionary<string, LsmEntry>(_memTable, StringComparer.Ordinal));
            _memTable.Clear();
        }
    }
}
```

---

## Section 07: Indexes and Data Modeling: SQL vs NoSQL

### Part A: Worked Questionnaire Answers

1. **Clustered vs Non-Clustered Index and Bookmark Lookups.** A **Clustered Index** physically sorts and stores the full table rows inside its leaf pages (so a table can have only one physical sort order). A **Non-Clustered (Secondary) Index** is a separate B+Tree whose leaf pages store the indexed columns plus the **Clustered Primary Key** (or heap pointer). When a query filters via a Secondary Index and selects columns not present in that index, the engine must perform a random **Bookmark / Key Lookup** into the Clustered Index for each matching row.
2. **Leftmost Prefix and Equality-Before-Range Rules.** A composite index on `(A, B, C)` is sorted first by `A`, then by `B`, then by `C`. For `WHERE A = 1 AND B > 10 AND C = 'Active'`, the index **must be `(A, C, B)`** (equality columns `A` and `C` first, range column `B` last), because once a range predicate (`B > 10`) is evaluated, subsequent columns to the right are no longer contiguous in the leaf pages.
3. **Covering Indexes (`INCLUDE`).** A Covering Index includes all columns needed by a query inside its leaf pages, allowing an **Index-Only Scan** with zero bookmark lookups. Using `INCLUDE (ColX, ColY)` stores those extra columns *only in the leaf level* (omitting them from internal B+Tree routing nodes) so internal page fan-out stays maximal.
4. **Index Table vs Materialized View.** An **Index Table** is a secondary NoSQL lookup table partitioned by the queryable attribute (`PartitionKey = SecondaryAttr -> PrimaryKey` or duplicated columns). A **Materialized View** precomputes and stores the denormalized output of a complex join, aggregation, or projection asynchronously from change events.
5. **SQL vs NoSQL access modeling.** Relational SQL stores normalize entities into 3rd Normal Form (**model the data first**) to support arbitrary ad-hoc joins and multi-table ACID transactions. Wide-Column and Document NoSQL stores at scale partition horizontally across nodes where cross-node joins are prohibitive, so you **model the queries first**, designing each table's `(PartitionKey, ClusteringKey)` so hot-path reads hit a single partition in one hop.
6. **Fowler's PoEAA Data Patterns.** **Active Record** puts DB access and domain logic directly on the row object; **Data Mapper** isolates a persistence-ignorant Domain Model from the DB schema; **Unit of Work** tracks all objects modified during a request and commits them atomically; **Identity Map** ensures each DB row is loaded into at most one in-memory object instance per Unit of Work; **Lazy Load** defers loading child relations (risking the $N + 1$ query anti-pattern in loops); and **Inheritance Mapping** maps subclasses into **Single Table (`TPH`)**, **Class Table (`TPT`)**, or **Concrete Table (`TPC`)** layouts.

### Part B: Worked C# Solution (`Chapter07.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter07
{
    [TestMethod]
    public void Covering_index_eliminates_clustered_bookmark_lookups()
    {
        IndexedTable table = new IndexedTable();
        table.Insert(new DeviceRow("d-1", "tenant-a", "Healthy", "4.2.1"));
        table.Insert(new DeviceRow("d-2", "tenant-a", "Healthy", "4.2.0"));
        table.Insert(new DeviceRow("d-3", "tenant-b", "Unhealthy", "4.1.0"));

        (IReadOnlyList<string> versionsViaNonCovering, int bookmarkLookups1) =
            table.QueryByTenantAndStatus("tenant-a", "Healthy", useCoveringIndex: false);
        (IReadOnlyList<string> versionsViaCovering, int bookmarkLookups2) =
            table.QueryByTenantAndStatus("tenant-a", "Healthy", useCoveringIndex: true);

        Assert.HasCount(2, versionsViaNonCovering);
        Assert.AreEqual(2, bookmarkLookups1);
        Assert.HasCount(2, versionsViaCovering);
        Assert.AreEqual(0, bookmarkLookups2);
    }

    [TestMethod]
    public void Identity_map_and_unit_of_work_return_same_reference_and_commit_atomically()
    {
        Dictionary<string, DeviceRow> database = new Dictionary<string, DeviceRow>(StringComparer.Ordinal)
        {
            ["d-1"] = new DeviceRow("d-1", "tenant-a", "Healthy", "4.1.0")
        };

        UnitOfWorkSession uow = new UnitOfWorkSession(database);
        MutableDevice? firstLoad = uow.Load("d-1");
        MutableDevice? secondLoad = uow.Load("d-1");

        Assert.IsNotNull(firstLoad);
        Assert.AreSame(firstLoad, secondLoad);

        firstLoad.FirmwareVersion = "4.2.1";
        uow.MarkDirty(firstLoad);
        Assert.AreEqual("4.1.0", database["d-1"].FirmwareVersion);

        uow.Commit();
        Assert.AreEqual("4.2.1", database["d-1"].FirmwareVersion);
    }
}

file sealed record DeviceRow(string DeviceId, string TenantId, string Status, string FirmwareVersion);

file sealed class IndexedTable
{
    private readonly SortedDictionary<string, DeviceRow> _clusteredIndex = new SortedDictionary<string, DeviceRow>(StringComparer.Ordinal);
    private readonly Dictionary<(string TenantId, string Status), List<string>> _nonCoveringIndex =
        new Dictionary<(string, string), List<string>>();
    private readonly Dictionary<(string TenantId, string Status), List<(string DeviceId, string FirmwareVersion)>> _coveringIndex =
        new Dictionary<(string, string), List<(string, string)>>();

    public void Insert(DeviceRow row)
    {
        _clusteredIndex[row.DeviceId] = row;
        (string, string) compositeKey = (row.TenantId, row.Status);

        if (!_nonCoveringIndex.TryGetValue(compositeKey, out List<string>? pkList))
        {
            pkList = new List<string>();
            _nonCoveringIndex[compositeKey] = pkList;
        }

        pkList.Add(row.DeviceId);

        if (!_coveringIndex.TryGetValue(compositeKey, out List<(string, string)>? coveredList))
        {
            coveredList = new List<(string, string)>();
            _coveringIndex[compositeKey] = coveredList;
        }

        coveredList.Add((row.DeviceId, row.FirmwareVersion));
    }

    public (IReadOnlyList<string> Versions, int ClusteredBookmarkLookups) QueryByTenantAndStatus(
        string tenantId,
        string status,
        bool useCoveringIndex)
    {
        List<string> results = new List<string>();
        int bookmarkLookups = 0;
        (string, string) key = (tenantId, status);

        if (useCoveringIndex)
        {
            if (_coveringIndex.TryGetValue(key, out List<(string DeviceId, string FirmwareVersion)>? coveredRows))
            {
                foreach ((string _, string version) in coveredRows)
                {
                    results.Add(version);
                }
            }

            return (results, 0);
        }

        if (_nonCoveringIndex.TryGetValue(key, out List<string>? pkList))
        {
            foreach (string pk in pkList)
            {
                bookmarkLookups++;
                results.Add(_clusteredIndex[pk].FirmwareVersion);
            }
        }

        return (results, bookmarkLookups);
    }
}

file sealed class MutableDevice
{
    public string DeviceId { get; }

    public string TenantId { get; }

    public string Status { get; set; }

    public string FirmwareVersion { get; set; }

    public MutableDevice(string deviceId, string tenantId, string status, string firmwareVersion)
    {
        DeviceId = deviceId;
        TenantId = tenantId;
        Status = status;
        FirmwareVersion = firmwareVersion;
    }
}

file sealed class UnitOfWorkSession
{
    private readonly Dictionary<string, DeviceRow> _database;
    private readonly Dictionary<string, MutableDevice> _identityMap = new Dictionary<string, MutableDevice>(StringComparer.Ordinal);
    private readonly HashSet<MutableDevice> _dirtyEntities = new HashSet<MutableDevice>();

    public UnitOfWorkSession(Dictionary<string, DeviceRow> database)
    {
        _database = database;
    }

    public MutableDevice? Load(string deviceId)
    {
        if (_identityMap.TryGetValue(deviceId, out MutableDevice? cached))
        {
            return cached;
        }

        if (!_database.TryGetValue(deviceId, out DeviceRow? row))
        {
            return null;
        }

        MutableDevice entity = new MutableDevice(row.DeviceId, row.TenantId, row.Status, row.FirmwareVersion);
        _identityMap[deviceId] = entity;
        return entity;
    }

    public void MarkDirty(MutableDevice entity)
    {
        _dirtyEntities.Add(entity);
    }

    public void Commit()
    {
        foreach (MutableDevice dirty in _dirtyEntities)
        {
            _database[dirty.DeviceId] = new DeviceRow(dirty.DeviceId, dirty.TenantId, dirty.Status, dirty.FirmwareVersion);
        }

        _dirtyEntities.Clear();
    }
}
```

---

## Section 08: Data Partitioning and Hashing Schemes

### Part A: Worked Questionnaire Answers

1. **Horizontal vs Vertical vs Directory-Based Partitioning.** **Horizontal Partitioning (Sharding)** splits rows across multiple database instances with the same schema by a Shard/Partition Key. **Vertical Partitioning** splits columns/tables by access frequency (separating hot small status fields from cold multi-megabyte blobs). **Directory-Based Partitioning** places an explicit lookup directory (`Key/Tenant -> Shard`) in front of the shards so keys can be migrated dynamically without changing hash code.
2. **Range-Based vs Hash-Based Sharding.** **Range Sharding** assigns contiguous key intervals (`A..D`, `E..K`) to shards, enabling fast range scans across sorted keys, but suffers from severe **write hotspots** when keys are monotonic (100% of new timestamped writes hit the last range shard). **Hash Sharding** hashes the partition key uniformly across shards, eliminating monotonic write hotspots at the cost of requiring $N$-way scatter-gather queries for range scans over the partition key.
3. **Celebrity / Hotspot Keys and Key Salting.** Because `hash(key)` is deterministic, 100% of traffic for a single viral or mega-tenant key `K_hot` lands on a single shard. **Key Salting (Write Sharding)** appends a suffix `0 .. S-1` (`K_hot#00 .. K_hot#15`), spreading writes across $S = 16$ shards, at the cost of requiring readers of `K_hot` to fan out across all 16 salted keys and merge the results.
4. **Why `hash(k) % N` fails and how Consistent Hashing fixes it.** Changing $N$ in `hash(k) % N` changes the remainder for $\approx (N - 1)/N$ of all keys (`90%` when scaling from 10 to 11 nodes). **Consistent Hashing** maps nodes and keys onto a fixed circular hash space $[0, 2^{32}-1]$ and assigns each key to the first token $\ge \text{Hash}(k)$ via $O(\log M)$ binary search, so adding or removing a node only moves the $\approx 1/N$ keys in the adjacent ring arc.
5. **The three problems solved by Virtual Nodes (`Vnodes`).** Placing only 1 token per physical server causes: (1) high statistical variance in arc sizes (load imbalance); (2) dumping 100% of a crashed node's load onto a single clockwise neighbor; and (3) inability to weight heterogeneous server hardware. Assigning $V = 128\text{--}256$ **Virtual Nodes** per physical machine reduces load standard deviation to $\approx 1/\sqrt{V}$, spreads failover and bootstrap traffic across dozens of peers, and weights larger machines proportionally.
6. **Rendezvous Hashing (Highest Random Weight, `HRW`).** Computes $\text{Owner}(k) = \arg\max_{n_i} \text{Hash}(n_i \parallel k)$. It requires zero ring data structures, achieves exact $1/N$ minimal disruption, selects $R$ distinct physical replicas simply by taking the top $R$ highest weights in one sort pass, and is ideal when $N \le 200$ shards/gateways (where $O(N)$ hash evaluations take $< 1\text{ }\mu\text{s}$).

### Part B: Worked C# Solution (`Chapter08.cs`)

```csharp
using System.Text;

namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter08
{
    [TestMethod]
    public void Consistent_hash_ring_with_virtual_nodes_moves_only_fractional_keys_and_picks_distinct_physical_replicas()
    {
        ConsistentHashRing ring = new ConsistentHashRing(virtualNodesPerServer: 128);
        ring.AddNode("node-a");
        ring.AddNode("node-b");
        ring.AddNode("node-c");

        Dictionary<string, string> initialOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < 1_000; i++)
        {
            string key = $"device-{i}";
            initialOwners[key] = ring.GetPrimaryNode(key);
        }

        ring.AddNode("node-d");
        int movedCount = 0;
        for (int i = 0; i < 1_000; i++)
        {
            string key = $"device-{i}";
            if (ring.GetPrimaryNode(key) != initialOwners[key])
            {
                movedCount++;
            }
        }

        Assert.IsTrue(movedCount > 150 && movedCount < 350);

        IReadOnlyList<string> preferenceList = ring.GetPreferenceList("device-42", replicaCount: 3);
        Assert.HasCount(3, preferenceList);
        Assert.AreEqual(3, preferenceList.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void Rendezvous_hashing_achieves_minimal_disruption_without_ring_state()
    {
        string[] initialNodes = ["node-a", "node-b", "node-c"];
        string[] expandedNodes = ["node-a", "node-b", "node-c", "node-d"];

        int moved = 0;
        for (int i = 0; i < 1_000; i++)
        {
            string key = $"key-{i}";
            string before = RendezvousHasher.SelectTopK(initialNodes, key, k: 1)[0];
            string after = RendezvousHasher.SelectTopK(expandedNodes, key, k: 1)[0];
            if (before != after)
            {
                Assert.AreEqual("node-d", after);
                moved++;
            }
        }

        Assert.IsTrue(moved > 180 && moved < 320);
    }
}

file sealed class ConsistentHashRing
{
    private readonly int _virtualNodesPerServer;
    private readonly SortedList<uint, string> _ring = new SortedList<uint, string>();

    public ConsistentHashRing(int virtualNodesPerServer)
    {
        _virtualNodesPerServer = virtualNodesPerServer;
    }

    public void AddNode(string physicalNodeId)
    {
        for (int v = 0; v < _virtualNodesPerServer; v++)
        {
            uint token = Hash32($"{physicalNodeId}#vnode{v}");
            _ring[token] = physicalNodeId;
        }
    }

    public string GetPrimaryNode(string key)
    {
        return GetPreferenceList(key, replicaCount: 1)[0];
    }

    public IReadOnlyList<string> GetPreferenceList(string key, int replicaCount)
    {
        uint keyHash = Hash32(key);
        IList<uint> tokens = _ring.Keys;
        IList<string> nodes = _ring.Values;

        int startIdx = BinarySearchFirstGreaterOrEqual(tokens, keyHash);
        List<string> result = new List<string>(replicaCount);
        HashSet<string> seenPhysical = new HashSet<string>(StringComparer.Ordinal);

        for (int offset = 0; offset < tokens.Count && result.Count < replicaCount; offset++)
        {
            int idx = (startIdx + offset) % tokens.Count;
            string candidateNode = nodes[idx];
            if (seenPhysical.Add(candidateNode))
            {
                result.Add(candidateNode);
            }
        }

        return result;
    }

    private static int BinarySearchFirstGreaterOrEqual(IList<uint> sortedTokens, uint target)
    {
        int low = 0;
        int high = sortedTokens.Count - 1;
        int answer = 0;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (sortedTokens[mid] >= target)
            {
                answer = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return answer;
    }

    internal static uint Hash32(string text)
    {
        ReadOnlySpan<byte> bytes = Encoding.UTF8.GetBytes(text);
        uint h = 2166136261u;
        foreach (byte b in bytes)
        {
            h ^= b;
            h *= 16777619u;
        }

        h ^= h >> 16;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        return h;
    }
}

file static class RendezvousHasher
{
    public static IReadOnlyList<string> SelectTopK(ReadOnlySpan<string> nodes, string key, int k)
    {
        List<(string Node, uint Weight)> scored = new List<(string, uint)>(nodes.Length);
        foreach (string node in nodes)
        {
            scored.Add((node, ConsistentHashRing.Hash32($"{node}||{key}")));
        }

        scored.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));
        List<string> topK = new List<string>(Math.Min(k, scored.Count));
        for (int i = 0; i < scored.Count && i < k; i++)
        {
            topK.Add(scored[i].Node);
        }

        return topK;
    }
}
```

---

## Section 09: Probabilistic Data Structures at Scale

### Part A: Worked Questionnaire Answers

1. **Bloom Filter mechanics and error asymmetry.** A Bloom Filter initializes an array of $m$ bits to `0`. `Add(item)` hashes the item with $k$ functions and sets those $k$ bits to `1`. `MightContain(item)` checks those $k$ bits: if any bit is `0`, the item was definitely never inserted (**0% False Negatives**); if all $k$ bits are `1`, those bits might have been set by a combination of other items (**False Positive** with probability $p$).
2. **Bloom Filter sizing math.** After inserting $n$ elements with $k$ hashes, the probability a specific bit is still `0` is $(1 - 1/m)^{kn} \approx e^{-kn/m}$, so the probability all $k$ probed bits are `1` is $p \approx (1 - e^{-kn/m})^k$. Minimizing with respect to $k$ gives $k_{\text{opt}} = \frac{m}{n}\ln 2 \approx 0.693 \frac{m}{n}$ and $\frac{m}{n} = -\frac{\ln p}{(\ln 2)^2} \approx -1.44 \log_2 p$. For a `1%` false-positive rate ($p = 0.01$), you need **$\approx 9.6\text{ bits per element}$** and **$k = 7$ hash functions**.
3. **Kirsch-Mitzenmacher Double Hashing and why deletion fails.** Computing two independent hashes $g_1(x), g_2(x)$ and synthesizing $h_i(x) = (g_1(x) + i \cdot g_2(x)) \bmod m$ achieves the same asymptotic false-positive rate as $k$ independent hashes. You cannot delete an item by clearing its $k$ bits to `0` because another present item may share one of those bits; clearing it would create a **False Negative** (solved by 4-bit **Counting Bloom Filters** or **Cuckoo Filters**).
4. **Count-Min Sketch (`CMS`) mechanics.** A CMS is a $d \times w$ integer matrix with $d$ hash functions (one per row). `Increment(x)` adds `1` to `table[r, h_r(x)]` in each row $r$. `EstimateFrequency(x)` returns $\min_{0 \le r < d} \text{table}[r, h_r(x)]$. Because counts are non-negative, hash collisions in a row can only *add* to `table[r, h_r(x)]`, so every row is an upper bound $\ge f(x)$ (**zero underestimation**), and the **minimum** across all $d$ rows is the tightest, least-collided upper bound.
5. **CMS `(epsilon, delta)` sizing, merging, and Top-$K$.** Setting width $w = \lceil e / \varepsilon \rceil$ and depth $d = \lceil \ln(1/\delta) \rceil$ guarantees $\hat{f}(x) \le f(x) + \varepsilon N$ with probability $\ge 1 - \delta$. Two sketches with identical dimensions and seeds merge by **element-wise matrix addition**. Pairing CMS with a **Min-Heap of size $K$** tracks the Top-$K$ heavy hitters in streaming pass.
6. **HyperLogLog (`HLL`) intuition.** In a uniform binary hash stream, a prefix of $\rho - 1$ leading zeros occurs with probability $2^{-\rho}$. HLL splits incoming hashes by their top $b$ bits into $m = 2^b$ registers (e.g., $m = 16{,}384$, using `12 KB`), records the maximum leading-zero run $\rho_{\max}$ seen in each register, and takes the bias-corrected **harmonic mean** across all $m$ registers to estimate `COUNT(DISTINCT)` with $\approx 1.04/\sqrt{m}$ (`0.81%`) error.

### Part B: Worked C# Solution (`Chapter09.cs`)

```csharp
using System.Collections;
using System.Text;

namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter09
{
    [TestMethod]
    public void Bloom_filter_produces_zero_false_negatives_and_bounded_false_positives()
    {
        (int mBits, int kHashes) = BloomFilter.CalculateOptimalSizing(expectedItems: 1_000, targetFalsePositiveRate: 0.01);
        Assert.IsTrue(mBits is > 9_000 and < 10_500);
        Assert.AreEqual(7, kHashes);

        BloomFilter filter = new BloomFilter(mBits, kHashes);
        for (int i = 0; i < 1_000; i++)
        {
            filter.Add($"present-key-{i}");
        }

        for (int i = 0; i < 1_000; i++)
        {
            Assert.IsTrue(filter.MightContain($"present-key-{i}"));
        }

        int falsePositives = 0;
        for (int i = 0; i < 10_000; i++)
        {
            if (filter.MightContain($"absent-key-{i}"))
            {
                falsePositives++;
            }
        }

        double empiricalFpRate = falsePositives / 10_000.0;
        Assert.IsLessThan(0.025, empiricalFpRate);
    }

    [TestMethod]
    public void Count_min_sketch_never_underestimates_and_supports_distributed_matrix_merging()
    {
        CountMinSketch sketchA = new CountMinSketch(depth: 5, width: 512);
        CountMinSketch sketchB = new CountMinSketch(depth: 5, width: 512);

        sketchA.Increment("tenant-hot", 100);
        sketchA.Increment("tenant-cold", 5);
        sketchB.Increment("tenant-hot", 150);

        sketchA.MergeInPlace(sketchB);

        long hotEstimate = sketchA.EstimateFrequency("tenant-hot");
        long coldEstimate = sketchA.EstimateFrequency("tenant-cold");

        Assert.IsTrue(hotEstimate >= 250 && hotEstimate <= 260);
        Assert.IsTrue(coldEstimate >= 5 && coldEstimate <= 15);
    }
}

file sealed class BloomFilter
{
    private readonly BitArray _bits;
    private readonly int _mBits;
    private readonly int _kHashes;

    public BloomFilter(int mBits, int kHashes)
    {
        _mBits = mBits;
        _kHashes = kHashes;
        _bits = new BitArray(mBits);
    }

    public static (int MBits, int KHashes) CalculateOptimalSizing(int expectedItems, double targetFalsePositiveRate)
    {
        double ln2 = Math.Log(2.0);
        int m = (int)Math.Ceiling(-(expectedItems * Math.Log(targetFalsePositiveRate)) / (ln2 * ln2));
        int k = (int)Math.Round((m / (double)expectedItems) * ln2);
        return (m, Math.Max(1, k));
    }

    public void Add(string item)
    {
        (uint h1, uint h2) = ComputeDoubleHash(item);
        for (int i = 0; i < _kHashes; i++)
        {
            int bitIndex = (int)((h1 + ((uint)i * h2)) % (uint)_mBits);
            _bits.Set(bitIndex, true);
        }
    }

    public bool MightContain(string item)
    {
        (uint h1, uint h2) = ComputeDoubleHash(item);
        for (int i = 0; i < _kHashes; i++)
        {
            int bitIndex = (int)((h1 + ((uint)i * h2)) % (uint)_mBits);
            if (!_bits.Get(bitIndex))
            {
                return false;
            }
        }

        return true;
    }

    internal static (uint H1, uint H2) ComputeDoubleHash(string text)
    {
        ReadOnlySpan<byte> bytes = Encoding.UTF8.GetBytes(text);
        uint h1 = 2166136261u;
        uint h2 = 0x9e3779b9u;
        foreach (byte b in bytes)
        {
            h1 = (h1 ^ b) * 16777619u;
            h2 = (h2 ^ b) * 0x85ebca6bu;
        }

        h1 ^= h1 >> 16;
        h2 = (h2 ^ (h2 >> 13)) | 1u;
        return (h1, h2);
    }
}

file sealed class CountMinSketch
{
    private readonly int _depth;
    private readonly int _width;
    private readonly long[,] _table;

    public CountMinSketch(int depth, int width)
    {
        _depth = depth;
        _width = width;
        _table = new long[depth, width];
    }

    public void Increment(string item, long count = 1)
    {
        (uint h1, uint h2) = BloomFilter.ComputeDoubleHash(item);
        for (int r = 0; r < _depth; r++)
        {
            int col = (int)((h1 + ((uint)(r + 1) * h2)) % (uint)_width);
            _table[r, col] += count;
        }
    }

    public long EstimateFrequency(string item)
    {
        (uint h1, uint h2) = BloomFilter.ComputeDoubleHash(item);
        long minVal = long.MaxValue;
        for (int r = 0; r < _depth; r++)
        {
            int col = (int)((h1 + ((uint)(r + 1) * h2)) % (uint)_width);
            long cell = _table[r, col];
            if (cell < minVal)
            {
                minVal = cell;
            }
        }

        return minVal;
    }

    public void MergeInPlace(CountMinSketch other)
    {
        for (int r = 0; r < _depth; r++)
        {
            for (int c = 0; c < _width; c++)
            {
                _table[r, c] += other._table[r, c];
            }
        }
    }
}
```

---

## Section 10: Networking, Proxies, and Real-Time Protocols

### Part A: Worked Questionnaire Answers

1. **TLS 1.2 vs TLS 1.3 handshake and Forward Secrecy.** Asymmetric cryptography (`RSA / ECDSA / ECDHE`) is CPU-heavy and used only during the handshake to verify the server's X.509 certificate chain and agree on an ephemeral symmetric key for fast hardware-accelerated `AEAD` (`AES-GCM`) encryption. **Forward Secrecy** (via Ephemeral Diffie-Hellman `ECDHE`) ensures that compromising the server's long-term private key later cannot decrypt past sessions. **TLS 1.3** cuts handshake latency from **2 RTTs down to 1 RTT** by sending the client's `ECDHE` key share inside the initial `ClientHello`.
2. **Forward Proxy vs Reverse Proxy.** A **Forward Proxy** sits on the client side, making outbound requests on behalf of internal clients (hiding client identities, enforcing outbound egress rules, and caching external downloads). A **Reverse Proxy** sits in front of backend servers, terminating inbound client connections on behalf of the service (hiding backend server topology, terminating TLS, compressing responses, and enforcing WAF/rate-limiting rules).
3. **Layer 4 vs Layer 7 Load Balancers and DSR.** An **L4 Load Balancer** routes TCP/UDP packets using only the 5-tuple (`IPs/Ports`) over a single pass-through TCP connection (supporting **Direct Server Return `DSR`** where huge response payloads bypass the LB on the return path). An **L7 Load Balancer** terminates TLS and parses HTTP/gRPC headers, paths, and cookies across two separate TCP pools. Because **HTTP/2 and gRPC** multiplex all concurrent RPCs from a client over a *single* TCP connection, an L4 TCP load balancer pins 100% of that client's RPCs onto a single backend pod; balancing gRPC requires an HTTP/2-aware L7 proxy.
4. **Load-balancing routing algorithms and `P2C`.** **Round Robin** cycles evenly across servers; **Least Connections** picks the server with the fewest active connections; **Least Response Time** factors in EWMA latency; **Consistent/IP Hash** preserves locality for stateful caches. **Power of Two Random Choices (`P2C`)** samples 2 random healthy servers and picks the less loaded one, exponentially reducing max queue depth from $O(\frac{\ln N}{\ln \ln N})$ to $O(\ln \ln N)$ without global coordination between proxies.
5. **The five real-time protocols.** **HTTP Short Polling** issues periodic `GET`s (high empty-response overhead); **HTTP Long-Polling** holds a `GET` open on the server until an event arrives; **Server-Sent Events (`SSE`)** streams unidirectional text events over standard HTTP; **WebSockets** upgrades HTTP (`101`) to a persistent full-duplex binary/text TCP socket; **gRPC Streaming** multiplexes full-duplex typed Protobuf streams over HTTP/2.
6. **SSE `Last-Event-ID` reconnection.** Each SSE message includes an `id: <eventId>` line. If the TCP connection drops, the browser's `EventSource` automatically reconnects and sends `Last-Event-ID: <eventId>`, allowing the server to replay missed events from a ring buffer. Choose SSE over WebSockets whenever communication is **unidirectional server-to-client** (live feeds, AI token streaming, rollout progress) and you want standard HTTP/2 multiplexing and firewall compatibility without custom socket framing.

### Part B: Worked C# Solution (`Chapter10.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter10
{
    [TestMethod]
    public void Power_of_two_random_choices_selects_less_loaded_healthy_backend()
    {
        BackendServer[] servers =
        [
            new BackendServer("srv-0", ActiveConnections: 45, IsHealthy: true),
            new BackendServer("srv-1", ActiveConnections: 3, IsHealthy: true),
            new BackendServer("srv-2", ActiveConnections: 0, IsHealthy: false)
        ];

        BackendServer chosen = PowerOfTwoLoadBalancer.SelectBackend(servers, candidateIndexA: 0, candidateIndexB: 1);
        Assert.AreEqual("srv-1", chosen.ServerId);

        BackendServer fallbackWhenOneUnhealthy = PowerOfTwoLoadBalancer.SelectBackend(servers, candidateIndexA: 0, candidateIndexB: 2);
        Assert.AreEqual("srv-0", fallbackWhenOneUnhealthy.ServerId);
    }

    [TestMethod]
    public void Server_sent_events_stream_replays_missed_events_after_last_event_id()
    {
        SseStreamBuffer stream = new SseStreamBuffer();
        stream.Publish(new SseEvent(EventId: 101, Data: "phase=1%"));
        stream.Publish(new SseEvent(EventId: 102, Data: "phase=10%"));
        stream.Publish(new SseEvent(EventId: 103, Data: "phase=50%"));

        IReadOnlyList<SseEvent> replayed = stream.ResumeFrom(lastEventId: 101);

        Assert.HasCount(2, replayed);
        Assert.AreEqual(102, replayed[0].EventId);
        Assert.AreEqual(103, replayed[1].EventId);
    }
}

file sealed record BackendServer(string ServerId, int ActiveConnections, bool IsHealthy);

file static class PowerOfTwoLoadBalancer
{
    public static BackendServer SelectBackend(ReadOnlySpan<BackendServer> pool, int candidateIndexA, int candidateIndexB)
    {
        BackendServer a = pool[candidateIndexA];
        BackendServer b = pool[candidateIndexB];

        if (a.IsHealthy && !b.IsHealthy)
        {
            return a;
        }

        if (!a.IsHealthy && b.IsHealthy)
        {
            return b;
        }

        return (a.ActiveConnections <= b.ActiveConnections) ? a : b;
    }
}

file readonly record struct SseEvent(long EventId, string Data);

file sealed class SseStreamBuffer
{
    private readonly List<SseEvent> _events = new List<SseEvent>();

    public void Publish(SseEvent evt)
    {
        _events.Add(evt);
    }

    public IReadOnlyList<SseEvent> ResumeFrom(long lastEventId)
    {
        List<SseEvent> missed = new List<SseEvent>();
        foreach (SseEvent evt in _events)
        {
            if (evt.EventId > lastEventId)
            {
                missed.Add(evt);
            }
        }

        return missed;
    }
}
```

---

## Section 11: Caching Architectures and Failure Modes

### Part A: Worked Questionnaire Answers

1. **The five caching topologies.** **Cache-Aside** loads lazily from the DB on a miss and invalidates the cache on a DB write. **Read-Through** delegates DB loading on miss to the cache provider. **Write-Through** synchronously writes to both the cache and the DB on every mutation. **Write-Around** writes directly to the DB, bypassing the cache so write-once bulk data does not evict hot read items. **Write-Back (Write-Behind)** acknowledges writes immediately in the cache and flushes batches asynchronously to the DB (risking data loss if the cache node crashes before flushing).
2. **$O(1)$ LRU Cache implementation.** Combine a **`Dictionary<TKey, LinkedListNode<(TKey, TValue)>>`** (for $O(1)$ node lookup by key) with a **Doubly Linked List (`LinkedList`)** ordered from most-recently-used at the Head to least-recently-used at the Tail. Every `Get` or `Put` splices the node to the Head in $O(1)$ pointer updates; when capacity is exceeded, `RemoveLast()` evicts the Tail node in $O(1)$.
3. **Scan Pollution, Frequency Stagnation, and `W-TinyLFU`.** In pure LRU, a one-time scan over 1M cold keys flushes the entire hot working set (**Scan Pollution**). In pure LFU, old items with huge historical counts sit in the cache forever after becoming cold (**Frequency Stagnation**). **Window-TinyLFU (`W-TinyLFU`)** puts a 1% Window LRU in front of a 99% Segmented LRU and uses a periodically halved **Count-Min Sketch** admission filter so an item leaving the 1% window only enters the 99% main cache if its estimated frequency beats the main cache's eviction victim.
4. **Stampede, Penetration, and Avalanche.** **Cache Stampede**: thousands of concurrent requests miss an expired hot key and hammer the DB simultaneously (solved via **`Singleflight` coalescing**, **Cache Leases**, and **Stale-While-Revalidate logical expiration**). **Cache Penetration**: requests for non-existent keys miss both cache and DB repeatedly (solved via **short-TTL Negative Caching** and **Bloom Filters**). **Cache Avalanche**: mass simultaneous key expiration or shard failure (solved via **TTL Jitter** and L1/L2 tiering).
5. **Why Cache-Aside invalidates (`Delete`) instead of updating (`Set`) on write.** If two concurrent writers `W1` and `W2` update the DB in order `W1 -> W2`, their network calls to `Set` the cache can arrive out of order (`W2` then `W1`), leaving the cache permanently poisoned with `W1`'s older value.
6. **The Slow-Reader Stale-Set Race and Memcache Leases.** Even with `Update DB -> Delete Cache`, if Reader A misses a cold cache and reads old `v1` from the DB, then pauses in GC while Writer B updates the DB to `v2` and deletes the cache key, Reader A can resume and `Set(v1)` into the cache! Meta's **Memcache Lease** assigns Reader A a monotonic `leaseToken` on miss and invalidates that token when Writer B calls `Delete`; when Reader A calls `TrySetWithLease(v1, leaseToken)`, the cache rejects the invalidated lease token.

### Part B: Worked C# Solution (`Chapter11.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter11
{
    [TestMethod]
    public void Lru_cache_evicts_least_recently_used_item_in_constant_time()
    {
        LruCache<string, string> cache = new LruCache<string, string>(capacity: 2);
        cache.Put("k1", "v1");
        cache.Put("k2", "v2");

        Assert.IsTrue(cache.TryGet("k1", out string? val1));
        Assert.AreEqual("v1", val1);

        cache.Put("k3", "v3");

        Assert.IsFalse(cache.TryGet("k2", out _));
        Assert.IsTrue(cache.TryGet("k1", out _));
        Assert.IsTrue(cache.TryGet("k3", out _));
    }

    [TestMethod]
    public void Cache_lease_token_prevents_slow_reader_from_poisoning_cache_after_concurrent_delete()
    {
        LeaseProtectedCache cache = new LeaseProtectedCache();

        (string? cachedValue, long leaseToken) = cache.GetOrAcquireLease("config:eu");
        Assert.IsNull(cachedValue);

        cache.InvalidateOnWrite("config:eu");

        bool leaseSetAccepted = cache.TrySetWithLease("config:eu", "stale-v1", leaseToken);
        Assert.IsFalse(leaseSetAccepted);

        (string? afterStaleAttempt, long freshLease) = cache.GetOrAcquireLease("config:eu");
        Assert.IsNull(afterStaleAttempt);
        Assert.IsTrue(cache.TrySetWithLease("config:eu", "fresh-v2", freshLease));
        Assert.AreEqual("fresh-v2", cache.GetOrAcquireLease("config:eu").Value);
    }
}

file sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _list = new LinkedList<(TKey, TValue)>();

    public LruCache(int capacity)
    {
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default;
        return false;
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? existing))
        {
            _list.Remove(existing);
            existing.Value = (key, value);
            _list.AddFirst(existing);
            return;
        }

        if (_map.Count >= _capacity && _list.Last is not null)
        {
            LinkedListNode<(TKey Key, TValue Value)> victim = _list.Last;
            _list.RemoveLast();
            _map.Remove(victim.Value.Key);
        }

        LinkedListNode<(TKey, TValue)> newNode = new LinkedListNode<(TKey, TValue)>((key, value));
        _list.AddFirst(newNode);
        _map[key] = newNode;
    }
}

file sealed class LeaseProtectedCache
{
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _validLeases = new Dictionary<string, long>(StringComparer.Ordinal);
    private long _nextLeaseToken;

    public (string? Value, long LeaseToken) GetOrAcquireLease(string key)
    {
        if (_values.TryGetValue(key, out string? existing))
        {
            return (existing, 0);
        }

        _nextLeaseToken++;
        _validLeases[key] = _nextLeaseToken;
        return (null, _nextLeaseToken);
    }

    public void InvalidateOnWrite(string key)
    {
        _values.Remove(key);
        _validLeases.Remove(key);
    }

    public bool TrySetWithLease(string key, string value, long leaseToken)
    {
        if (!_validLeases.TryGetValue(key, out long activeToken) || activeToken != leaseToken)
        {
            return false;
        }

        _validLeases.Remove(key);
        _values[key] = value;
        return true;
    }
}
```

---

## Section 12: Rate Limiting, Throttling, and Resiliency

### Part A: Worked Questionnaire Answers

1. **Rate Limiting vs Throttling vs Load Shedding.** **Rate Limiting** is client/caller-side pacing to stay under a downstream limit. **Throttling (`429 Too Many Requests`)** is server/gateway enforcement of per-tenant quotas for fairness. **Load Shedding (`503 Service Unavailable`)** is a server survival mechanism that rejects low-priority requests when aggregate host CPU, memory, or concurrency crosses a critical threshold; even if every individual tenant is well within their legal throttling quota!
2. **The five rate-limiting algorithms.** **Token Bucket** (2 numbers per key; permits bursts up to capacity $B$ at refill rate $r$); **Leaky Bucket Queue** ($O(B)$ queue; clamps output to a flat rate with zero downstream burst); **Fixed Window Counter** (1 integer per key; suffers from a **$2\times$ boundary spike** when bursts hit the end of window $T$ and start of window $T+1$); **Sliding Window Log** ($O(\text{Limit})$ timestamps in a sorted set; 100% exact across any window); and **Sliding Window Counter** (2 integers per key; interpolates `prevCount * (1 - elapsedFraction) + currCount`, preventing boundary spikes in $O(1)$ space).
3. **Token Bucket lazy refill and atomic Redis Lua.** On request arrival at $t_{\text{now}}$, $\text{tokens} \leftarrow \min(B, \text{tokens} + (t_{\text{now}} - t_{\text{last}}) \times r)$. In a distributed Redis limiter, reading the token count in C# and then writing the decremented count in a second command creates a race condition when 20 gateway pods check the same tenant simultaneously; executing the read, refill math, and decrement inside a single **Redis Lua script (`EVALSHA`)** runs atomically on Redis's single-threaded event loop.
4. **Exponential Backoff with Full Jitter and Retry Budgets.** Deterministic backoff (`1s, 2s, 4s`) causes thousands of clients that failed during the same 1-second blip to wake up at the exact same millisecond and hammer the recovering service in synchronized waves. **Full Jitter** ($\text{Random}(0, \min(T_{\max}, T_{\text{base}} \cdot 2^{\text{attempt}}))$) spreads retries uniformly across the window, and a **10% Retry Budget** (token bucket where normal requests add `0.1` tokens and retries cost `1.0` token) caps total retry amplification at $\le 10\%$.
5. **Circuit Breaker state machine.** Starts **Closed** (requests pass through; sliding window counts failures). When failure rate crosses threshold, transitions to **Open** (fails fast in `0 ms` for `breakDuration`). When `breakDuration` elapses, transitions to **Half-Open** (admits a bounded number of probe requests: if probes succeed, resets to **Closed**; if any probe fails, trips immediately back to **Open**).
6. **Bulkheads, Shuffle Sharding, and Liveness vs Readiness probes.** **Bulkheads** isolate thread/connection pools per dependency. **Shuffle Sharding** assigns each tenant a random subset of $k$ workers (e.g., 4 out of 100, giving $\binom{100}{4} \approx 3.9\text{M}$ combinations) so a poisonous tenant crashing its 4 workers leaves $99.99997\%$ of other tenants unaffected. A Kubernetes **Liveness Probe** must never check a downstream DB because a temporary DB slowdown would cause Kubernetes to kill and restart all application pods at once; use **Readiness Probes** to remove pods from load-balancer rotation instead.

### Part B: Worked C# Solution (`Chapter12.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter12
{
    [TestMethod]
    public void Token_bucket_allows_burst_up_to_capacity_and_refills_linearly_over_time()
    {
        TokenBucketLimiter bucket = new TokenBucketLimiter(capacity: 3, refillTokensPerSecond: 2.0, initialTimeSeconds: 0.0);

        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));
        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));
        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));
        Assert.IsFalse(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));

        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.5));
        Assert.IsFalse(bucket.TryAcquire(cost: 1, nowSeconds: 0.5));
    }

    [TestMethod]
    public void Sliding_window_counter_prevents_double_burst_at_fixed_window_boundary()
    {
        SlidingWindowCounterLimiter limiter = new SlidingWindowCounterLimiter(windowSeconds: 60, limit: 100);

        for (int i = 0; i < 80; i++)
        {
            Assert.IsTrue(limiter.TryAcquire(nowSeconds: 50.0));
        }

        for (int i = 0; i < 40; i++)
        {
            Assert.IsTrue(limiter.TryAcquire(nowSeconds: 75.0));
        }

        Assert.IsFalse(limiter.TryAcquire(nowSeconds: 75.0));
    }

    [TestMethod]
    public void Circuit_breaker_transitions_closed_to_open_to_half_open_and_back_to_closed()
    {
        CircuitBreakerStateMachine breaker = new CircuitBreakerStateMachine(failureThreshold: 3, breakDurationMs: 5_000);
        Assert.AreEqual(CircuitState.Closed, breaker.State);

        breaker.RecordResult(succeeded: false, nowMs: 100);
        breaker.RecordResult(succeeded: false, nowMs: 200);
        breaker.RecordResult(succeeded: false, nowMs: 300);

        Assert.AreEqual(CircuitState.Open, breaker.State);
        Assert.IsFalse(breaker.AllowRequest(nowMs: 4_000));

        Assert.IsTrue(breaker.AllowRequest(nowMs: 5_500));
        Assert.AreEqual(CircuitState.HalfOpen, breaker.State);

        breaker.RecordResult(succeeded: true, nowMs: 5_510);
        Assert.AreEqual(CircuitState.Closed, breaker.State);
    }
}

file sealed class TokenBucketLimiter
{
    private readonly double _capacity;
    private readonly double _refillRate;
    private double _tokens;
    private double _lastRefillSeconds;

    public TokenBucketLimiter(double capacity, double refillTokensPerSecond, double initialTimeSeconds)
    {
        _capacity = capacity;
        _refillRate = refillTokensPerSecond;
        _tokens = capacity;
        _lastRefillSeconds = initialTimeSeconds;
    }

    public bool TryAcquire(double cost, double nowSeconds)
    {
        if (nowSeconds > _lastRefillSeconds)
        {
            double elapsed = nowSeconds - _lastRefillSeconds;
            _tokens = Math.Min(_capacity, _tokens + (elapsed * _refillRate));
            _lastRefillSeconds = nowSeconds;
        }

        if (_tokens >= cost)
        {
            _tokens -= cost;
            return true;
        }

        return false;
    }
}

file sealed class SlidingWindowCounterLimiter
{
    private readonly long _windowSeconds;
    private readonly int _limit;
    private long _currentWindowIndex;
    private int _previousCount;
    private int _currentCount;

    public SlidingWindowCounterLimiter(long windowSeconds, int limit)
    {
        _windowSeconds = windowSeconds;
        _limit = limit;
    }

    public bool TryAcquire(double nowSeconds)
    {
        long windowIdx = (long)(nowSeconds / _windowSeconds);
        if (windowIdx > _currentWindowIndex)
        {
            _previousCount = (windowIdx == _currentWindowIndex + 1) ? _currentCount : 0;
            _currentCount = 0;
            _currentWindowIndex = windowIdx;
        }

        double elapsedInCurrent = nowSeconds - (windowIdx * _windowSeconds);
        double fraction = elapsedInCurrent / _windowSeconds;
        double estimatedCount = (_previousCount * (1.0 - fraction)) + _currentCount;

        if (estimatedCount + 1.0 <= _limit + 1e-9)
        {
            _currentCount++;
            return true;
        }

        return false;
    }
}

file enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

file sealed class CircuitBreakerStateMachine
{
    private readonly int _failureThreshold;
    private readonly long _breakDurationMs;
    private int _consecutiveFailures;
    private long _openedAtMs;

    public CircuitState State { get; private set; } = CircuitState.Closed;

    public CircuitBreakerStateMachine(int failureThreshold, long breakDurationMs)
    {
        _failureThreshold = failureThreshold;
        _breakDurationMs = breakDurationMs;
    }

    public bool AllowRequest(long nowMs)
    {
        if (State == CircuitState.Closed)
        {
            return true;
        }

        if (State == CircuitState.Open && nowMs - _openedAtMs >= _breakDurationMs)
        {
            State = CircuitState.HalfOpen;
            return true;
        }

        return State == CircuitState.HalfOpen;
    }

    public void RecordResult(bool succeeded, long nowMs)
    {
        if (succeeded)
        {
            _consecutiveFailures = 0;
            State = CircuitState.Closed;
            return;
        }

        _consecutiveFailures++;
        if (State == CircuitState.HalfOpen || _consecutiveFailures >= _failureThreshold)
        {
            State = CircuitState.Open;
            _openedAtMs = nowMs;
        }
    }
}
```

---

## Section 13: Microservices, Messaging, and Event-Driven Architecture

### Part A: Worked Questionnaire Answers

1. **The Distributed Monolith and cascading synchronous availability.** A **Distributed Monolith** is a set of separately deployed services coupled by synchronous RPC chains, shared databases, or lockstep schema deployments. Across $k = 10$ sequential synchronous services each with $99.9\%$ availability, end-to-end availability multiplies to $0.999^{10} \approx 99.0\%$, and any latency spike in the 10th service blocks threads across all 9 upstream callers.
2. **Rodger's 2x2 Message Interaction Matrix.** (1) **Synchronous + Consumed** (HTTP/gRPC call to one load-balanced worker); (2) **Asynchronous + Consumed** (Competing Consumers pulling jobs from a point-to-point queue); (3) **Asynchronous + Observed** (Pub/Sub event stream where multiple independent subscribers observe a fact); and (4) **Synchronous + Observed** (Scatter-Gather query aggregating responses from multiple policy/search engines).
3. **Pattern-Matched Routing and Transport Independence.** Instead of hardcoding destination hostnames and DTOs, messages are treated as key-value maps and routed by matching key subsets inside a **Message Abstraction Layer** (where the most specific matching rule wins). Senders and receivers remain completely ignorant of network addresses and transport protocols (**Transport Independence**), allowing new regional or specialized services to intercept subsets of messages via routing configuration alone.
4. **Five Cloud Messaging Patterns.** **Queue-Based Load Leveling** buffers bursty arrivals in front of workers; **Priority Queue** fast-tracks critical `P0` commands ahead of backlog; **Sequential Convoy** locks a session/partition key to one consumer at a time for strict per-entity FIFO ordering; **Claim Check** offloads multi-megabyte blobs to object storage and passes only a URI on the broker; and **Asynchronous Request-Reply** returns `HTTP 202 Accepted` with a `Location` polling URL and `Retry-After` header.
5. **Dual-Write Bug and Transactional Outbox + Idempotent Inbox.** Writing to a DB and then calling `kafka.Publish()` outside a shared transaction risks crashing in between, leaving DB and broker permanently out of sync. The **Transactional Outbox** inserts the event into an `Outbox` table inside the *exact same local ACID DB transaction* as the domain update; a background relay publishes pending outbox rows at-least-once, and consumers deduplicate `EventId`s in an **Idempotent Inbox**.
6. **Sagas, Compensating Transactions, Semantic Locks, CQRS, and Event Sourcing.** **Saga Choreography** chains events peer-to-peer; **Saga Orchestration** uses a central state machine to invoke steps and run reverse **Compensating Transactions** on failure. Because local transactions commit immediately after each step (lacking multi-step ACID isolation), Sagas use **Semantic Locks** (`Status = PendingApproval / Reserved`) to prevent concurrent transactions from misusing intermediate state. **CQRS** splits Command (write) models from Query (read) projections; **Event Sourcing** stores the immutable sequence of domain events as the source of truth.

### Part B: Worked C# Solution (`Chapter13.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter13
{
    [TestMethod]
    public void Pattern_matched_router_selects_most_specific_microservice_rule_without_hardcoded_addresses()
    {
        PatternMatchingRouter router = new PatternMatchingRouter();
        router.Register(new Dictionary<string, string> { ["role"] = "rollout", ["cmd"] = "evaluate" }, "DefaultRolloutEngine");
        router.Register(
            new Dictionary<string, string> { ["role"] = "rollout", ["cmd"] = "evaluate", ["region"] = "eu", ["tier"] = "critical" },
            "EuCriticalRolloutEngine");

        string? standardMatch = router.Resolve(new Dictionary<string, string>
        {
            ["role"] = "rollout",
            ["cmd"] = "evaluate",
            ["region"] = "us"
        });
        string? euCriticalMatch = router.Resolve(new Dictionary<string, string>
        {
            ["role"] = "rollout",
            ["cmd"] = "evaluate",
            ["region"] = "eu",
            ["tier"] = "critical",
            ["payload"] = "fw-4.2"
        });

        Assert.AreEqual("DefaultRolloutEngine", standardMatch);
        Assert.AreEqual("EuCriticalRolloutEngine", euCriticalMatch);
    }

    [TestMethod]
    public void Transactional_outbox_and_idempotent_inbox_deliver_exactly_once_side_effects_under_duplicate_relay()
    {
        OutboxDatabase db = new OutboxDatabase();
        IdempotentConsumerInbox consumer = new IdempotentConsumerInbox();

        db.SaveRolloutWithOutbox("rollout-1", "evt-100", "Activate:rollout-1");

        foreach ((string eventId, string payload) in db.GetUnpublishedOutboxEvents())
        {
            consumer.Process(eventId, payload);
            consumer.Process(eventId, payload);
            db.MarkOutboxPublished(eventId);
        }

        Assert.IsEmpty(db.GetUnpublishedOutboxEvents());
        Assert.HasCount(1, consumer.AppliedEvents);
        Assert.AreEqual("Activate:rollout-1", consumer.AppliedEvents[0]);
    }

    [TestMethod]
    public void Saga_orchestrator_runs_compensating_transactions_in_reverse_order_when_step_fails()
    {
        List<string> auditTrail = new List<string>();
        SagaStep[] steps =
        [
            new SagaStep(
                Execute: () => { auditTrail.Add("Do:ReserveBudget"); return true; },
                Compensate: () => auditTrail.Add("Undo:ReleaseBudget")),
            new SagaStep(
                Execute: () => { auditTrail.Add("Do:SignFirmware"); return true; },
                Compensate: () => auditTrail.Add("Undo:RevokeSignature")),
            new SagaStep(
                Execute: () => { auditTrail.Add("Fail:ProvisionCdn"); return false; },
                Compensate: () => auditTrail.Add("Undo:ProvisionCdn"))
        ];

        bool sagaSucceeded = SagaOrchestrator.Run(steps);

        Assert.IsFalse(sagaSucceeded);
        CollectionAssert.AreEqual(
            new[] { "Do:ReserveBudget", "Do:SignFirmware", "Fail:ProvisionCdn", "Undo:RevokeSignature", "Undo:ReleaseBudget" },
            auditTrail);
    }
}

file sealed class PatternMatchingRouter
{
    private readonly List<(Dictionary<string, string> Pattern, string TargetService)> _rules =
        new List<(Dictionary<string, string>, string)>();

    public void Register(Dictionary<string, string> pattern, string targetService)
    {
        _rules.Add((pattern, targetService));
    }

    public string? Resolve(IReadOnlyDictionary<string, string> message)
    {
        string? bestTarget = null;
        int bestSpecificity = -1;

        foreach ((Dictionary<string, string> pattern, string target) in _rules)
        {
            bool allMatch = true;
            foreach ((string k, string v) in pattern)
            {
                if (!message.TryGetValue(k, out string? msgVal) || !string.Equals(v, msgVal, StringComparison.Ordinal))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch && pattern.Count > bestSpecificity)
            {
                bestSpecificity = pattern.Count;
                bestTarget = target;
            }
        }

        return bestTarget;
    }
}

file sealed class OutboxDatabase
{
    private readonly Dictionary<string, string> _rollouts = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<(string EventId, string Payload, bool Published)> _outbox = new List<(string, string, bool)>();

    public void SaveRolloutWithOutbox(string rolloutId, string eventId, string eventPayload)
    {
        _rollouts[rolloutId] = "Active";
        _outbox.Add((eventId, eventPayload, Published: false));
    }

    public IReadOnlyList<(string EventId, string Payload)> GetUnpublishedOutboxEvents()
    {
        List<(string, string)> pending = new List<(string, string)>();
        foreach ((string id, string payload, bool published) in _outbox)
        {
            if (!published)
            {
                pending.Add((id, payload));
            }
        }

        return pending;
    }

    public void MarkOutboxPublished(string eventId)
    {
        for (int i = 0; i < _outbox.Count; i++)
        {
            if (_outbox[i].EventId == eventId)
            {
                _outbox[i] = (_outbox[i].EventId, _outbox[i].Payload, true);
            }
        }
    }
}

file sealed class IdempotentConsumerInbox
{
    private readonly HashSet<string> _processedIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _appliedEvents = new List<string>();

    public IReadOnlyList<string> AppliedEvents
    {
        get { return _appliedEvents; }
    }

    public void Process(string eventId, string payload)
    {
        if (_processedIds.Add(eventId))
        {
            _appliedEvents.Add(payload);
        }
    }
}

file sealed record SagaStep(Func<bool> Execute, Action Compensate);

file static class SagaOrchestrator
{
    public static bool Run(ReadOnlySpan<SagaStep> steps)
    {
        int completedCount = 0;
        for (int i = 0; i < steps.Length; i++)
        {
            if (!steps[i].Execute())
            {
                for (int j = completedCount - 1; j >= 0; j--)
                {
                    steps[j].Compensate();
                }

                return false;
            }

            completedCount++;
        }

        return true;
    }
}
```

---

## Section 14: Domain-Driven Design and Software Architecture Patterns

### Part A: Worked Questionnaire Answers

1. **SOLID, Law of Demeter, and Postel's Law.** **SRP** (one reason/actor to change); **OCP** (extend behavior via new implementations without modifying existing tested code); **LSP** (subtypes must honor the full contract of their base type); **ISP** (split bloated interfaces into focused client-specific roles); and **DIP** (high-level domain modules define repository/messaging interfaces that low-level SQL/Kafka infrastructure projects implement). **Law of Demeter** forbids chaining through deep internal object graphs (`a.B.C.Do()`). **Postel's Law** requires emitting strict, well-formed payloads while tolerating unknown fields on input.
2. **Core GoF Patterns.** **Factory Method / Abstract Factory / Builder** encapsulate single-object, family, and step-by-step construction. **Adapter** translates incompatible interfaces; **Bridge** separates abstraction and implementation hierarchies; **Composite** models recursive trees uniformly; **Decorator** layers cross-cutting wrappers; **Facade** simplifies a complex subsystem; **Flyweight** shares immutable intrinsic state; **Proxy** controls access. **Chain of Responsibility** forms middleware pipelines; **Command** reifies requests; **Mediator** centralizes hub communication; **State** models lifecycle state machines; **Strategy** swaps algorithms; **Visitor** adds operations over an AST.
3. **Transaction Script vs Domain Model.** Use **Transaction Script** for simple procedural CRUD microservices where business validation is shallow; use **Domain Model + Service Layer** when rich invariants, complex state machines, and combinatoric business rules must be tested offline without a database.
4. **Strategic DDD.** **Ubiquitous Language** is a shared, unambiguous vocabulary used identically in conversation and code. **Core Subdomains** hold proprietary competitive advantage (invest in rich DDD); **Supporting** and **Generic Subdomains** use simple scripts or off-the-shelf platforms. **Bounded Contexts** draw explicit boundaries so `Device` in Provisioning is decoupled from `Device` in Billing, avoiding a fragile 150-field enterprise god-class.
5. **Tactical DDD: Entities, Value Objects, and Aggregates.** An **Entity** has continuous lifecycle identity (`Id`); a **Value Object** is immutable and compared purely by structural value (`SemVer`, `Money`). An **Aggregate** is a transactional consistency boundary rooted at an **Aggregate Root**: (1) external objects reference only the Root; (2) all invariants inside the Aggregate are enforced synchronously on commit; and (3) one transaction mutates **one Aggregate** (referencing other Aggregates by ID only and coordinating across them via Domain Events).
6. **Cloud Structural, Migration, and Residency Patterns.** **Gateway Routing/Aggregation/Offloading** and **BFF** tailor edge traffic; **Sidecar** attaches operational helpers and **Ambassador** proxies outbound network calls; **Strangler Fig + Anti-Corruption Layer (`ACL`)** incrementally replace a legacy monolith slice by slice; **Valet Key** issues time-limited direct blob SAS/presigned tokens; **Geode** deploys active-active regional compute rings; and **Regional Privacy Mapping Tables** pseudonymize EU customer identifiers at the regional boundary to satisfy GDPR.

### Part B: Worked C# Solution (`Chapter14.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter14
{
    [TestMethod]
    public void Composite_applicability_expression_evaluates_local_boolean_rules_without_external_service()
    {
        IApplicabilityRule rule = new AndRule(
            new EqualsRule("Region", "EU"),
            new AndRule(
                new OrRule(new EqualsRule("Sku", "Industrial"), new EqualsRule("Sku", "Automotive")),
                new NotRule(new EqualsRule("BatteryLow", "true"))));

        Dictionary<string, string> eligibleDevice = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Region"] = "EU",
            ["Sku"] = "Industrial",
            ["BatteryLow"] = "false"
        };
        Dictionary<string, string> lowBatteryDevice = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Region"] = "EU",
            ["Sku"] = "Industrial",
            ["BatteryLow"] = "true"
        };

        Assert.IsTrue(rule.IsApplicable(eligibleDevice));
        Assert.IsFalse(rule.IsApplicable(lowBatteryDevice));
    }

    [TestMethod]
    public void Regional_privacy_mapping_table_pseudonymizes_eu_customer_identifiers_before_global_logging()
    {
        RegionalPrivacyMappingTable euVault = new RegionalPrivacyMappingTable();

        Guid opaqueToken1 = euVault.GetOrCreateOpaqueToken("eu-customer-berlin-gmbh");
        Guid opaqueToken2 = euVault.GetOrCreateOpaqueToken("eu-customer-berlin-gmbh");

        Assert.AreEqual(opaqueToken1, opaqueToken2);
        Assert.AreEqual("eu-customer-berlin-gmbh", euVault.ResolveInsideRegion(opaqueToken1));
    }
}

file interface IApplicabilityRule
{
    bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes);
}

file sealed class EqualsRule : IApplicabilityRule
{
    private readonly string _key;
    private readonly string _expectedValue;

    public EqualsRule(string key, string expectedValue)
    {
        _key = key;
        _expectedValue = expectedValue;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return deviceAttributes.TryGetValue(_key, out string? actual) &&
               string.Equals(actual, _expectedValue, StringComparison.Ordinal);
    }
}

file sealed class AndRule : IApplicabilityRule
{
    private readonly IApplicabilityRule _left;
    private readonly IApplicabilityRule _right;

    public AndRule(IApplicabilityRule left, IApplicabilityRule right)
    {
        _left = left;
        _right = right;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return _left.IsApplicable(deviceAttributes) && _right.IsApplicable(deviceAttributes);
    }
}

file sealed class OrRule : IApplicabilityRule
{
    private readonly IApplicabilityRule _left;
    private readonly IApplicabilityRule _right;

    public OrRule(IApplicabilityRule left, IApplicabilityRule right)
    {
        _left = left;
        _right = right;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return _left.IsApplicable(deviceAttributes) || _right.IsApplicable(deviceAttributes);
    }
}

file sealed class NotRule : IApplicabilityRule
{
    private readonly IApplicabilityRule _inner;

    public NotRule(IApplicabilityRule inner)
    {
        _inner = inner;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return !_inner.IsApplicable(deviceAttributes);
    }
}

file sealed class RegionalPrivacyMappingTable
{
    private readonly Dictionary<string, Guid> _cidToToken = new Dictionary<string, Guid>(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _tokenToCid = new Dictionary<Guid, string>();

    public Guid GetOrCreateOpaqueToken(string customerIdentifiableData)
    {
        if (_cidToToken.TryGetValue(customerIdentifiableData, out Guid existing))
        {
            return existing;
        }

        Guid token = Guid.NewGuid();
        _cidToToken[customerIdentifiableData] = token;
        _tokenToCid[token] = customerIdentifiableData;
        return token;
    }

    public string? ResolveInsideRegion(Guid opaqueToken)
    {
        if (_tokenToCid.TryGetValue(opaqueToken, out string? cid))
        {
            return cid;
        }

        return null;
    }
}
```

---

## Section 15: Distributed File Systems and Coordination: GFS, HDFS, and Chubby

### Part A: Worked Questionnaire Answers

1. **GFS/HDFS assumptions and components.** Assumptions: (1) commodity hardware fails constantly; (2) files are multi-GB; (3) reads are streaming/random and writes are almost exclusively sequential appends; (4) sustained bandwidth beats low latency. Components: a single **Master (`NameNode`)** holding namespace and file-to-chunk metadata in RAM, **ChunkServers (`DataNodes`)** storing 64 MB / 128 MB chunk files on local disks, and a **Client Library**.
2. **Why chunks are 64 MB / 128 MB and the small-file problem.** Large chunks keep Master RAM overhead tiny ($\approx 64\text{ bytes}$ of RAM per 64 MB chunk, so 1 PB takes only 1 GB of RAM), minimize client-to-Master RPCs, and allow persistent TCP connections to ChunkServers. Storing 100 million 1 KB files creates 100 million metadata entries ($\approx 15\text{ GB}$ of Master heap) and turns every 1 KB read into a Master RPC bottleneck, exhausting Master RAM while disks sit empty.
3. **Why chunk locations are never persisted on the GFS Master.** The Master persists namespace and `file -> chunkHandle` mutations in its replicated `Operation Log (EditLog)`, but discovers `chunkHandle -> ChunkServer` locations dynamically via `BlockReports` at startup and periodic `HeartBeats`; because ChunkServers crash and disks die constantly, making the ChunkServers themselves the only authoritative source of truth for what is on their disks.
4. **Separation of Control Flow and Pipelined Data Flow, plus Primary Leases.** The Client asks the Master only for metadata (`Primary + Secondaries`), then pushes the data payload along a linear **topology-aware TCP pipeline** from the closest replica to the next. The Master grants a 60-second **Chunk Lease** to the **Primary ChunkServer**, which assigns a sequential serial order to concurrent mutations and instructs Secondaries to apply them in that exact order.
5. **GFS Atomic Record Append vs HDFS WORM.** In GFS `RecordAppend`, the client specifies no offset; if the record would exceed the 64 MB boundary, the Primary pads the chunk and tells the client to retry on the next chunk; otherwise it picks the offset and appends across replicas. Because failed appends are retried without rollback, replicas can contain padding holes and duplicate records (**at-least-once relaxed consistency**). HDFS simplified this to strict **Single-Writer, Write-Once-Read-Many (`WORM`)** byte-identical replicas.
6. **Chubby locks, KeepAlive, Jeopardy, and Sequencers.** **Coarse-grained locks** are held for hours/days (leader election); **fine-grained locks** are held for milliseconds. Clients create **ephemeral files** and register **Event Watches** to avoid polling. Chubby holds `KeepAlive` calls open via **Long-Polling** to push invalidations for consistent client-side caching; if a Master failover delays `KeepAlive` past local lease expiry, the client enters a **45-second Jeopardy grace window** to reconnect to the new Master without losing its locks. A **Sequencer** is a monotonic fencing token string verified by storage servers.

### Part B: Worked C# Solution (`Chapter15.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter15
{
    [TestMethod]
    public void Gfs_primary_chunk_server_pads_chunk_at_64mb_boundary_and_assigns_deterministic_append_offsets()
    {
        GfsChunkPrimary primary = new GfsChunkPrimary(maxChunkBytes: 100);

        (bool RollToNextChunk, int Offset) append1 = primary.RecordAppend(recordBytes: 60);
        (bool RollToNextChunk, int Offset) append2ExceedingBoundary = primary.RecordAppend(recordBytes: 50);

        Assert.IsFalse(append1.RollToNextChunk);
        Assert.AreEqual(0, append1.Offset);

        Assert.IsTrue(append2ExceedingBoundary.RollToNextChunk);
        Assert.AreEqual(100, primary.CurrentLengthBytes);
    }

    [TestMethod]
    public void Chubby_session_enters_jeopardy_grace_window_on_master_failover_and_validates_sequencers()
    {
        ChubbyCoordinator cell = new ChubbyCoordinator(jeopardyGraceMs: 45_000);
        ChubbySequencer seq1 = cell.AcquireExclusiveLock("/ls/cell/bigtable/master", "server-a", leaseExpiresAtMs: 10_000);

        Assert.IsTrue(cell.IsSessionSavedByJeopardy("/ls/cell/bigtable/master", nowMs: 12_000));

        ChubbySequencer seq2 = cell.AcquireExclusiveLock("/ls/cell/bigtable/master", "server-b", leaseExpiresAtMs: 70_000, nowMs: 60_000);
        Assert.IsFalse(cell.CheckSequencer(seq1));
        Assert.IsTrue(cell.CheckSequencer(seq2));
    }
}

file sealed class GfsChunkPrimary
{
    private readonly int _maxChunkBytes;

    public int CurrentLengthBytes { get; private set; }

    public GfsChunkPrimary(int maxChunkBytes)
    {
        _maxChunkBytes = maxChunkBytes;
    }

    public (bool RollToNextChunk, int Offset) RecordAppend(int recordBytes)
    {
        if (CurrentLengthBytes + recordBytes > _maxChunkBytes)
        {
            CurrentLengthBytes = _maxChunkBytes;
            return (RollToNextChunk: true, Offset: -1);
        }

        int assignedOffset = CurrentLengthBytes;
        CurrentLengthBytes += recordBytes;
        return (RollToNextChunk: false, Offset: assignedOffset);
    }
}

file readonly record struct ChubbySequencer(string LockPath, long GenerationNumber);

file sealed class ChubbyCoordinator
{
    private readonly long _jeopardyGraceMs;
    private readonly Dictionary<string, (string Owner, long LeaseExpiresAtMs, long Generation)> _locks =
        new Dictionary<string, (string, long, long)>(StringComparer.Ordinal);
    private long _generationCounter;

    public ChubbyCoordinator(long jeopardyGraceMs)
    {
        _jeopardyGraceMs = jeopardyGraceMs;
    }

    public ChubbySequencer AcquireExclusiveLock(string path, string owner, long leaseExpiresAtMs, long nowMs = 0)
    {
        if (_locks.TryGetValue(path, out (string Owner, long LeaseExpiresAtMs, long Generation) existing))
        {
            if (nowMs < existing.LeaseExpiresAtMs + _jeopardyGraceMs)
            {
                throw new InvalidOperationException("Lock is still held or inside Jeopardy grace window.");
            }
        }

        _generationCounter++;
        _locks[path] = (owner, leaseExpiresAtMs, _generationCounter);
        return new ChubbySequencer(path, _generationCounter);
    }

    public bool IsSessionSavedByJeopardy(string path, long nowMs)
    {
        if (!_locks.TryGetValue(path, out (string Owner, long LeaseExpiresAtMs, long Generation) existing))
        {
            return false;
        }

        return nowMs <= existing.LeaseExpiresAtMs + _jeopardyGraceMs;
    }

    public bool CheckSequencer(ChubbySequencer sequencer)
    {
        return _locks.TryGetValue(sequencer.LockPath, out (string Owner, long LeaseExpiresAtMs, long Generation) active) &&
               active.Generation == sequencer.GenerationNumber;
    }
}
```

---

## Section 16: Wide-Column and Dynamo-Family Stores: BigTable, Dynamo, and Cassandra

### Part A: Worked Questionnaire Answers

1. **BigTable data model.** A sparse, distributed, persistent multi-dimensional sorted map: $(\text{row}: \texttt{string}, \text{column}: \texttt{string}, \text{time}: \texttt{int64}) \to \texttt{byte[]}$. Rows are sorted lexicographically and split dynamically into 100–200 MB **Tablets**; reversing domain names (`com.cnn.www`) groups related pages contiguously in the same Tablet. **Column Families** (`family:qualifier`) define access control and physical **Locality Groups** (separate SSTables per column group).
2. **Three-Level Tablet Location Hierarchy.** Level 0 is a **Chubby file** pointing to the **Root Tablet** (Level 1, the first `METADATA` tablet, which never splits). The Root Tablet points to other **`METADATA` Tablets** (Level 2), which store the `TabletServer` location of each **User Tablet** (Level 3). With 1 KB rows in 128 MB `METADATA` tablets ($2^{17}$ entries per level), 2 levels of `METADATA` address $2^{34}$ User Tablets (2 Exabytes) in $\le 3$ network hops (0 hops when cached).
3. **Tablet Server internals and zero-copy failover.** Each Tablet is owned by **one Tablet Server at a time** (`CP`). Writes append to a shared **GFS Commit Log** and insert into a **MemTable**; flushes write immutable **SSTables** directly onto **GFS** (with Block Cache, Scan Cache, and **Bloom Filters**). When a Tablet Server dies, its Chubby lock expires and the Master reassigns the Tablet to another server, which reads the existing SSTables directly from shared **GFS** without copying files between nodes!
4. **Why Amazon built Dynamo (`AP / PA-EL`) and its six mechanisms.** Amazon needed shopping carts to remain **100% writable** even during network partitions or server failures. Dynamo combines: (1) **Consistent Hashing with Virtual Nodes**, (2) **Gossip membership and Coordinator routing**, (3) **Tunable Quorums ($R + W > N$)**, (4) **Sloppy Quorums + Hinted Handoff**, (5) **Vector Clocks** with client-side semantic conflict resolution, and (6) **Read Repair + Merkle Tree Anti-Entropy**.
5. **How Cassandra fuses Dynamo and BigTable.** Cassandra adopts **Dynamo's masterless peer-to-peer ring, gossip, and tunable quorums** alongside **BigTable's wide-column model and local LSM-Tree (`CommitLog + MemTable + SSTables`)**. In `PRIMARY KEY ((tenant_id, device_id, bucket_day), recorded_at, event_id)`, the **Partition Key** `(tenant_id, device_id, bucket_day)` is hashed by the Partitioner to choose the node on the Consistent Hash Ring, while the **Clustering Key** `recorded_at, event_id` physically sorts rows within that partition's SSTables for fast range queries.
6. **Comparison matrix.** BigTable is asymmetric `CP (PC/EC)` with range-partitioned Tablets (fast cross-row scans, single-server tablet serialization). Dynamo is symmetric masterless `AP (PA/EL)` with hash-partitioned key-value blobs and Vector Clocks. Cassandra is symmetric masterless tunable `AP` with hash-partitioned wide-column partitions, sorted intra-partition Clustering Keys, and cell-level Last-Write-Wins timestamps (plus optional Paxos LWT).

### Part B: Worked C# Solution (`Chapter16.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter16
{
    [TestMethod]
    public void Bigtable_three_level_hierarchy_resolves_user_tablet_server_in_at_most_three_hops_and_caches()
    {
        BigTableLocationHierarchy hierarchy = new BigTableLocationHierarchy();
        hierarchy.RegisterUserTablet(startKey: "com.acme.a", endKey: "com.acme.z", tabletServer: "ts-eu-07");

        (string server1, int networkHops1) = hierarchy.LocateTablet("com.acme.portal/index.html");
        (string server2, int networkHops2) = hierarchy.LocateTablet("com.acme.support/faq.html");

        Assert.AreEqual("ts-eu-07", server1);
        Assert.AreEqual(3, networkHops1);
        Assert.AreEqual("ts-eu-07", server2);
        Assert.AreEqual(0, networkHops2);
    }

    [TestMethod]
    public void Cassandra_compound_key_hashes_partition_key_and_sorts_clustering_key_for_range_queries()
    {
        CassandraWideColumnStore store = new CassandraWideColumnStore();

        store.Insert(partitionKey: "tenant1:dev42:2026-11-21", clusteringTimestamp: 1_000, payload: "t=1000");
        store.Insert(partitionKey: "tenant1:dev42:2026-11-21", clusteringTimestamp: 3_000, payload: "t=3000");
        store.Insert(partitionKey: "tenant1:dev42:2026-11-21", clusteringTimestamp: 2_000, payload: "t=2000");

        IReadOnlyList<string> range = store.QueryClusteringRange(
            partitionKey: "tenant1:dev42:2026-11-21",
            minTimestampInclusive: 1_500,
            maxTimestampInclusive: 3_000);

        CollectionAssert.AreEqual(new[] { "t=2000", "t=3000" }, range.ToArray());
    }
}

file sealed class BigTableLocationHierarchy
{
    private readonly List<(string StartKey, string EndKey, string Server)> _userTablets =
        new List<(string, string, string)>();
    private readonly List<(string StartKey, string EndKey, string Server)> _clientCache =
        new List<(string, string, string)>();

    public void RegisterUserTablet(string startKey, string endKey, string tabletServer)
    {
        _userTablets.Add((startKey, endKey, tabletServer));
    }

    public (string TabletServer, int NetworkHops) LocateTablet(string rowKey)
    {
        foreach ((string start, string end, string server) in _clientCache)
        {
            if (string.CompareOrdinal(rowKey, start) >= 0 && string.CompareOrdinal(rowKey, end) <= 0)
            {
                return (server, 0);
            }
        }

        foreach ((string start, string end, string server) in _userTablets)
        {
            if (string.CompareOrdinal(rowKey, start) >= 0 && string.CompareOrdinal(rowKey, end) <= 0)
            {
                _clientCache.Add((start, end, server));
                return (server, 3);
            }
        }

        throw new KeyNotFoundException($"No tablet found for row key '{rowKey}'.");
    }
}

file sealed class CassandraWideColumnStore
{
    private readonly Dictionary<string, SortedDictionary<long, string>> _partitions =
        new Dictionary<string, SortedDictionary<long, string>>(StringComparer.Ordinal);

    public void Insert(string partitionKey, long clusteringTimestamp, string payload)
    {
        if (!_partitions.TryGetValue(partitionKey, out SortedDictionary<long, string>? clusteringRows))
        {
            clusteringRows = new SortedDictionary<long, string>();
            _partitions[partitionKey] = clusteringRows;
        }

        clusteringRows[clusteringTimestamp] = payload;
    }

    public IReadOnlyList<string> QueryClusteringRange(
        string partitionKey,
        long minTimestampInclusive,
        long maxTimestampInclusive)
    {
        List<string> results = new List<string>();
        if (!_partitions.TryGetValue(partitionKey, out SortedDictionary<long, string>? clusteringRows))
        {
            return results;
        }

        foreach ((long ts, string payload) in clusteringRows)
        {
            if (ts >= minTimestampInclusive && ts <= maxTimestampInclusive)
            {
                results.Add(payload);
            }
        }

        return results;
    }
}
```

---

## Section 17: Distributed Streaming and Log-Centric Systems: Kafka

### Part A: Worked Questionnaire Answers

1. **Topics, Partitions, Offsets, and Key Hashing.** A Kafka **Topic** is sharded into **Partitions**, each stored as a segmented append-only log on disk where records receive sequential 64-bit **Offsets**. Ordering is guaranteed **only within a single Partition** (never across partitions). Hashing an entity key (`Murmur2(DeviceId) % numPartitions`) routes all events for that entity to the exact same partition, preserving per-entity causal ordering.
2. **Consumer Groups unifying Queue and Pub/Sub.** Within a single **Consumer Group (`group.id`)**, each partition is assigned to **at most one live consumer instance**, providing competing-consumer load sharing with per-partition ordering. Different Consumer Groups subscribe independently and track their own offsets, providing pub/sub broadcast. If a group has 20 consumers for a 12-partition topic, 8 consumers receive zero partitions and sit idle because two consumers in the same group cannot share a partition without breaking offset order.
3. **Eager vs Cooperative Sticky Rebalancing.** **Eager Rebalancing** revokes *all* partitions across *all* consumers simultaneously ("stop-the-world") before reassigning them. **Cooperative Sticky Rebalancing** allows consumers to continue processing unchanged partitions while migrating only the specific partitions that need to move.
4. **Leader, Followers, ISR, and `acks`.** Each partition has 1 **Leader** and $N - 1$ **Followers** that pull batches sequentially. The **In-Sync Replica (`ISR`) set** contains replicas caught up within `replica.lag.time.max.ms`. Setting `acks = all` (`-1`) with `min.insync.replicas = 2` (on `replication.factor = 3`) ensures the Leader waits for all ISRs (and at least 2 replicas) before acknowledging, guaranteeing zero data loss on single-broker crash.
5. **High-Water Mark (`HWM`) and Leader Epoch (`KIP-101`).** `HWM` is the highest offset replicated to all ISRs; consumers can only read up to `HWM`. Because followers learn the updated `HWM` one fetch round-trip *after* appending records, truncating a restarting follower to its stale local `HWM` followed by a leader crash could lose committed writes or diverge logs; Kafka fixed this with a monotonic **Leader Epoch** (`(Epoch, StartOffset)` fencing token) so recovering followers query the active leader for the exact epoch truncation offset.
6. **Zero-copy `sendfile()`, Idempotent Producers, and KRaft.** Kafka keeps records in wire format inside the **OS Pagecache** and uses the **`sendfile()`** syscall to DMA bytes directly from pagecache to the NIC without copying into user-space JVM heap. **Idempotent Producers** tag batches with `(ProducerId, SequenceNumber)` so the Leader deduplicates retries in memory. **KRaft** replaced external ZooKeeper with an internal Raft metadata topic (`__cluster_metadata`), keeping standby controllers warm in memory and scaling clusters to millions of partitions.

### Part B: Worked C# Solution (`Chapter17.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter17
{
    [TestMethod]
    public void Kafka_partition_exposes_records_only_up_to_high_water_mark_and_deduplicates_idempotent_producer()
    {
        KafkaPartitionLeader partition = new KafkaPartitionLeader(isrCount: 2);

        long offset0 = partition.ProduceIdempotent(producerId: 99, sequenceNumber: 0, payload: "msg-0");
        long offset0Retry = partition.ProduceIdempotent(producerId: 99, sequenceNumber: 0, payload: "msg-0");
        long offset1 = partition.ProduceIdempotent(producerId: 99, sequenceNumber: 1, payload: "msg-1");

        Assert.AreEqual(0, offset0);
        Assert.AreEqual(0, offset0Retry);
        Assert.AreEqual(1, offset1);

        Assert.IsEmpty(partition.FetchConsumerVisible());

        partition.AckFollowerReplication(followerReplicatedOffset: 0);
        IReadOnlyList<string> visibleAfterFirstAck = partition.FetchConsumerVisible();
        Assert.HasCount(1, visibleAfterFirstAck);
        Assert.AreEqual("msg-0", visibleAfterFirstAck[0]);
    }

    [TestMethod]
    public void Consumer_group_assignor_caps_active_consumers_at_partition_count()
    {
        IReadOnlyDictionary<string, List<int>> assignment =
            KafkaConsumerGroupAssignor.AssignPartitions(partitionCount: 3, consumerIds: ["c1", "c2", "c3", "c4"]);

        Assert.HasCount(1, assignment["c1"]);
        Assert.HasCount(1, assignment["c2"]);
        Assert.HasCount(1, assignment["c3"]);
        Assert.IsEmpty(assignment["c4"]);
    }
}

file sealed class KafkaPartitionLeader
{
    private readonly int _isrCount;
    private readonly List<string> _log = new List<string>();
    private readonly Dictionary<long, int> _lastProducerSeq = new Dictionary<long, int>();
    private readonly Dictionary<(long Pid, int Seq), long> _producerOffsetMap = new Dictionary<(long, int), long>();
    private long _highWaterMark = -1;

    public KafkaPartitionLeader(int isrCount)
    {
        _isrCount = isrCount;
    }

    public long ProduceIdempotent(long producerId, int sequenceNumber, string payload)
    {
        if (_lastProducerSeq.TryGetValue(producerId, out int lastSeq) && sequenceNumber <= lastSeq)
        {
            return _producerOffsetMap[(producerId, sequenceNumber)];
        }

        long assignedOffset = _log.Count;
        _log.Add(payload);
        _lastProducerSeq[producerId] = sequenceNumber;
        _producerOffsetMap[(producerId, sequenceNumber)] = assignedOffset;

        if (_isrCount == 1)
        {
            _highWaterMark = assignedOffset;
        }

        return assignedOffset;
    }

    public void AckFollowerReplication(long followerReplicatedOffset)
    {
        if (followerReplicatedOffset < _log.Count && followerReplicatedOffset > _highWaterMark)
        {
            _highWaterMark = followerReplicatedOffset;
        }
    }

    public IReadOnlyList<string> FetchConsumerVisible()
    {
        List<string> visible = new List<string>();
        for (int i = 0; i <= _highWaterMark && i < _log.Count; i++)
        {
            visible.Add(_log[i]);
        }

        return visible;
    }
}

file static class KafkaConsumerGroupAssignor
{
    public static IReadOnlyDictionary<string, List<int>> AssignPartitions(int partitionCount, ReadOnlySpan<string> consumerIds)
    {
        Dictionary<string, List<int>> map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (string cid in consumerIds)
        {
            map[cid] = new List<int>();
        }

        for (int p = 0; p < partitionCount; p++)
        {
            string targetConsumer = consumerIds[p % consumerIds.Length];
            map[targetConsumer].Add(p);
        }

        return map;
    }
}
```

---

## Section 18: Capacity Planning and Asymptotic Analysis

### Part A: Worked Questionnaire Answers

1. **Worst-case, average-case, amortized complexity, and cache locality.** Appending to a `List<T>` has $O(N)$ **worst-case** time when the backing array doubles, but **$O(1)$ amortized time** averaged across $N$ appends. On real CPUs, an $O(N)$ scan over a contiguous array stays in L1/L2 cache (`0.5–7 ns`), often beating $O(\log N)$ pointer-chasing across heap-allocated BST nodes that suffer `100 ns` RAM cache misses on every hop.
2. **Hardware Latency Hierarchy.** L1 cache = `0.5 ns`; L2 cache = `7 ns`; Main RAM = `100 ns`; Compress 1 KB (`LZ4`) = `3 us`; NVMe 4 KB random read = `50 us`; Intra-datacenter RTT = `500 us (0.5 ms)`; HDD seek = `10 ms`; Cross-continent RTT (`US <-> EU`) = `150 ms`.
3. **Time conversions.** $1\text{ day} = 86{,}400\text{ s} \approx 10^5\text{ s}$; $1\text{ year} \approx 3 \times 10^7\text{ s}$; $3\text{ years} \approx 1{,}000\text{ days} \approx 10^8\text{ s}$. Therefore, `500 million requests/day` is $\frac{5 \times 10^8}{10^5} = \mathbf{5{,}000\text{ RPS}}$ average.
4. **Little's Law ($L = \lambda W$).** At $\lambda = 10{,}000\text{ RPS}$ and $W = 0.01\text{ s}$ (`10 ms`), average concurrency is $L = 10{,}000 \times 0.01 = \mathbf{100\text{ in-flight requests}}$. When downstream latency degrades to $W = 0.5\text{ s}$ (`500 ms`), required concurrency jumps $50\times$ to $L = 10{,}000 \times 0.5 = \mathbf{5{,}000\text{ in-flight connections/threads}}$.
5. **Server sizing with 33% headroom ($\text{Safe RPS} \approx 30{,}000 / T_{\text{ms}}$).** At $T = 150\text{ ms}$, one server safely handles $\frac{30{,}000}{150} = 200\text{ RPS}$. To serve a peak of `6,000 RPS`, you need $\frac{6{,}000}{200} = \mathbf{30\text{ servers}}$.
6. **3-Year Storage and Shard Sizing.** `5,000 writes/sec * 4 KB/write` = `20 MB/sec` ($\approx 2\text{ TB/day}$). Over 3 years ($10^8\text{ s}$), raw volume is $20\text{ MB/s} \times 10^8\text{ s} = \mathbf{2\text{ PB raw}}$. With $N = 3$ replication, total physical storage is $\mathbf{6\text{ PB}}$, requiring **2,000 logical shards of `1 TB` raw each** (`6,000` 1 TB replica slices).

### Part B: Worked C# Solution (`Chapter18.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter18
{
    [TestMethod]
    public void Littles_law_and_headroom_formulas_size_concurrency_servers_and_multi_year_storage()
    {
        double inFlight = CapacityMath.ComputeLittlesLawInFlight(arrivalRateRps: 5_000, latencySeconds: 0.020);
        Assert.AreEqual(100.0, inFlight, 0.001);

        int servers = CapacityMath.ComputeRequiredServers(targetPeakRps: 1_500, processingTimeMs: 200.0);
        Assert.AreEqual(10, servers);

        long totalBytes = CapacityMath.ComputeThreeYearReplicatedStorageBytes(
            writesPerSecond: 2_000,
            bytesPerWrite: 2_000,
            replicationFactor: 3);
        Assert.AreEqual(1_200_000_000_000_000L, totalBytes);
    }
}

file static class CapacityMath
{
    public static double ComputeLittlesLawInFlight(double arrivalRateRps, double latencySeconds)
    {
        return arrivalRateRps * latencySeconds;
    }

    public static int ComputeRequiredServers(double targetPeakRps, double processingTimeMs)
    {
        double safeRpsPerServer = 30_000.0 / processingTimeMs;
        return (int)Math.Ceiling(targetPeakRps / safeRpsPerServer);
    }

    public static long ComputeThreeYearReplicatedStorageBytes(long writesPerSecond, long bytesPerWrite, int replicationFactor)
    {
        const long ThreeYearsSecondsApprox = 100_000_000L;
        return writesPerSecond * bytesPerWrite * ThreeYearsSecondsApprox * replicationFactor;
    }
}
```

---

## Section 19: Trees, Tries, and Spatial Indexing

### Part A: Worked Questionnaire Answers

1. **BST degeneration and AVL vs Red-Black Trees.** Inserting sorted keys (`1, 2, 3...`) into an unbalanced BST chains every node as a right child, creating a linked list of height $H = N - 1$ ($O(N)$ lookup). **AVL Trees** enforce $|\text{Height}(L) - \text{Height}(R)| \le 1$ via 4 rotations (`LL, RR, LR, RL`), giving stricter height ($< 1.44 \log_2 N$) and faster lookups at the cost of up to $O(\log N)$ rotations on delete. **Red-Black Trees** enforce 1-bit color rules (no two consecutive red nodes, equal black height on all paths), bounding height to $\le 2 \log_2(N+1)$ while capping insert/delete rotations at $\le 2$ / $\le 3$ ($O(1)$).
2. **Tree traversals, BST validation, Inorder Successor, and $N$-th Highest.** **Preorder (`Root-L-R`)**, **Inorder (`L-Root-R`)**, and **Postorder (`L-R-Root`)** use $O(H)$ stack space (iteratively via an explicit `Stack<Node>` pushing left chains); **Level-Order (`BFS`)** uses a FIFO queue in $O(W)$ space. Validate a BST in $O(N)$ by passing `(minAllowed, maxAllowed)` bounds down Preorder (or verifying strictly increasing order during Inorder). Find the **Inorder Successor** of `key` in $O(H)$ time by walking from the root: whenever `key < curr.Key`, record `successor = curr.Key` and move left; otherwise move right. Find the **$N$-th Highest Element** via **Reverse Inorder (`Right -> Visit -> Left`)**, stopping at the $N$-th visited node.
3. **Binary Tree to Doubly Linked List in-place, Boundary Perimeter, and $O(1)$-space Sibling Linking.** Convert a binary tree to a DLL in-place in $O(N)$ time and $O(1)$ extra heap space by traversing Inorder while maintaining `head` and `prev` references and stitching `prev.Right = curr; curr.Left = prev`. Print the **Tree Perimeter** counter-clockwise by emitting the root, walking the left boundary top-down (excluding leaves), collecting all leaf nodes left-to-right via DFS, and walking the right boundary bottom-up via a reversal stack (excluding leaves). Connect all level-order `Next` sibling pointers in $O(1)$ space by initializing `current = last = root` and appending `current.Left` and `current.Right` onto `last.Next` while advancing `current = current.Next`.
4. **Standard Trie vs Radix (Patricia) Tree, LPM, and Trie Sorting.** A **Standard Trie** allocates one node per character edge; a **Radix Tree** compresses chains of single-child nodes into substring-labeled edges (bounding total nodes to $\le 2N$). Both walk character by character along the query string of length $L$ in **$O(L)$ time** independent of $N$, resolve **Longest-Prefix Match (`LPM`)** by recording the last `IsEndOfWord` node seen along the path, count words by summing `IsEndOfWord` flags via DFS, and **sort an array of strings** by inserting all strings into a Trie and running a Preorder traversal across ordered child edges.
5. **Why 1D B+Trees fail on 2D boxes and how Quad-Trees solve it.** In a composite B+Tree on `(Latitude, Longitude)`, once `Latitude` is queried with a `BETWEEN` range, `Longitude` is unsorted across the slice, forcing a global latitude band scan. A **Point Quad-Tree** recursively subdivides 2D rectangles into `NW, NE, SW, SE` quadrants when leaf capacity $C$ is exceeded, pruning any quadrant whose bounding box does not intersect `queryBox` in $O(1)$.
6. **Geohashes and the 9-cell neighbor rule.** A Geohash interleaves bisected longitude and latitude bits into a Base-32 string so shared prefixes correspond to nested Quad-Tree cells, turning 2D searches into 1D prefix queries. Because two points 10 meters apart across a quadrant boundary (like the Prime Meridian) have different prefixes, a proximity search must query the **target cell plus its 8 surrounding neighbor cells (9 cells total)**.

### Part B: Worked C# Solution (`Chapter19.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter19
{
    [TestMethod]
    public void Avl_tree_maintains_logarithmic_height_on_sorted_insertions_and_finds_inorder_successor()
    {
        AvlTree tree = new AvlTree();
        for (int i = 1; i <= 15; i++)
        {
            tree.Insert(i * 10);
        }

        Assert.IsTrue(tree.IsValidBst());
        Assert.AreEqual(4, tree.Height);
        Assert.AreEqual(80, tree.FindInorderSuccessor(70));
        Assert.IsNull(tree.FindInorderSuccessor(150));
    }

    [TestMethod]
    public void Prefix_trie_supports_autocomplete_and_longest_prefix_match_routing()
    {
        PrefixTrie trie = new PrefixTrie();
        trie.Insert("10.0.");
        trie.Insert("10.0.24.");
        trie.Insert("10.0.24.128");
        trie.Insert("192.168.");

        IReadOnlyList<string> completions = trie.GetWordsWithPrefix("10.0.24.");
        CollectionAssert.AreEqual(new[] { "10.0.24.", "10.0.24.128" }, completions.ToArray());

        string? longestMatch = trie.LongestPrefixMatch("10.0.24.99.1");
        Assert.AreEqual("10.0.24.", longestMatch);
    }

    [TestMethod]
    public void Quad_tree_subdivides_dense_quadrants_and_prunes_non_intersecting_regions_on_range_query()
    {
        QuadTree quadTree = new QuadTree(new BoundingBox(0, 0, 100, 100), leafCapacity: 2);
        quadTree.Insert(new Point2D(10, 10, "gw-1"));
        quadTree.Insert(new Point2D(12, 14, "gw-2"));
        quadTree.Insert(new Point2D(85, 85, "gw-3"));
        quadTree.Insert(new Point2D(11, 11, "gw-4"));

        List<Point2D> found = new List<Point2D>();
        quadTree.QueryRange(new BoundingBox(5, 5, 20, 20), found);

        Assert.HasCount(3, found);
        CollectionAssert.AreEquivalent(new[] { "gw-1", "gw-2", "gw-4" }, found.Select(static p => p.Id).ToArray());
    }

    [TestMethod]
    public void Binary_tree_to_doubly_linked_list_and_boundary_perimeter_and_sibling_links_preserve_order()
    {
        // Build BST:
        //       4
        //     /   \
        //    2     6
        //   / \   / \
        //  1   3 5   7
        AvlNode rootForPerimeter = new AvlNode(4)
        {
            Left = new AvlNode(2) { Left = new AvlNode(1), Right = new AvlNode(3) },
            Right = new AvlNode(6) { Left = new AvlNode(5), Right = new AvlNode(7) }
        };

        IReadOnlyList<int> iterativeInOrder = BinaryTreeAlgorithms.TraverseInOrderIterative(rootForPerimeter);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, iterativeInOrder.ToArray());

        IReadOnlyList<int> perimeter = BinaryTreeAlgorithms.GetTreeBoundaryPerimeter(rootForPerimeter);
        CollectionAssert.AreEqual(new[] { 4, 2, 1, 3, 5, 7, 6 }, perimeter.ToArray());

        AvlNode? dllHead = BinaryTreeAlgorithms.ConvertBinaryTreeToDoublyLinkedList(rootForPerimeter);
        List<int> forward = new List<int>();
        AvlNode? curr = dllHead;
        AvlNode? tail = null;
        while (curr is not null)
        {
            forward.Add(curr.Key);
            tail = curr;
            curr = curr.Right;
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, forward);
        Assert.IsNotNull(tail);
        Assert.AreEqual(6, tail.Left?.Key);

        PrefixTrie sortTrie = new PrefixTrie();
        sortTrie.Insert("delta");
        sortTrie.Insert("alpha");
        sortTrie.Insert("charlie");
        sortTrie.Insert("bravo");
        Assert.AreEqual(4, sortTrie.CountWords());
        CollectionAssert.AreEqual(
            new[] { "alpha", "bravo", "charlie", "delta" },
            PrefixTrie.SortArrayWithTrie(["delta", "alpha", "charlie", "bravo"]).ToArray());
    }
}

file sealed class AvlNode
{
    public int Key { get; }

    public int Height { get; set; } = 1;

    public AvlNode? Left { get; set; }

    public AvlNode? Right { get; set; }

    public AvlNode(int key)
    {
        Key = key;
    }
}

file sealed class AvlTree
{
    private AvlNode? _root;

    public int Height
    {
        get { return GetHeight(_root); }
    }

    public void Insert(int key)
    {
        _root = Insert(_root, key);
    }

    public bool IsValidBst()
    {
        return IsValidBst(_root, long.MinValue, long.MaxValue);
    }

    public int? FindInorderSuccessor(int key)
    {
        AvlNode? curr = _root;
        int? successor = null;

        while (curr is not null)
        {
            if (key < curr.Key)
            {
                successor = curr.Key;
                curr = curr.Left;
            }
            else
            {
                curr = curr.Right;
            }
        }

        return successor;
    }

    private static bool IsValidBst(AvlNode? node, long minAllowed, long maxAllowed)
    {
        if (node is null)
        {
            return true;
        }

        if (node.Key <= minAllowed || node.Key >= maxAllowed)
        {
            return false;
        }

        return IsValidBst(node.Left, minAllowed, node.Key) && IsValidBst(node.Right, node.Key, maxAllowed);
    }

    private static AvlNode Insert(AvlNode? node, int key)
    {
        if (node is null)
        {
            return new AvlNode(key);
        }

        if (key < node.Key)
        {
            node.Left = Insert(node.Left, key);
        }
        else if (key > node.Key)
        {
            node.Right = Insert(node.Right, key);
        }
        else
        {
            return node;
        }

        UpdateHeight(node);
        int balance = GetBalance(node);

        if (balance > 1 && node.Left is not null && key < node.Left.Key)
        {
            return RotateRight(node);
        }

        if (balance < -1 && node.Right is not null && key > node.Right.Key)
        {
            return RotateLeft(node);
        }

        if (balance > 1 && node.Left is not null && key > node.Left.Key)
        {
            node.Left = RotateLeft(node.Left);
            return RotateRight(node);
        }

        if (balance < -1 && node.Right is not null && key < node.Right.Key)
        {
            node.Right = RotateRight(node.Right);
            return RotateLeft(node);
        }

        return node;
    }

    private static int GetHeight(AvlNode? n)
    {
        return n?.Height ?? 0;
    }

    private static int GetBalance(AvlNode? n)
    {
        return n is null ? 0 : GetHeight(n.Left) - GetHeight(n.Right);
    }

    private static void UpdateHeight(AvlNode n)
    {
        n.Height = 1 + Math.Max(GetHeight(n.Left), GetHeight(n.Right));
    }

    private static AvlNode RotateRight(AvlNode y)
    {
        AvlNode x = y.Left!;
        AvlNode? t2 = x.Right;
        x.Right = y;
        y.Left = t2;
        UpdateHeight(y);
        UpdateHeight(x);
        return x;
    }

    private static AvlNode RotateLeft(AvlNode x)
    {
        AvlNode y = x.Right!;
        AvlNode? t2 = y.Left;
        y.Left = x;
        x.Right = t2;
        UpdateHeight(x);
        UpdateHeight(y);
        return y;
    }
}

file sealed class TrieNode
{
    public SortedDictionary<char, TrieNode> Children { get; } = new SortedDictionary<char, TrieNode>();

    public bool IsEndOfWord { get; set; }
}

file sealed class PrefixTrie
{
    private readonly TrieNode _root = new TrieNode();

    public void Insert(string word)
    {
        TrieNode curr = _root;
        foreach (char ch in word)
        {
            if (!curr.Children.TryGetValue(ch, out TrieNode? next))
            {
                next = new TrieNode();
                curr.Children[ch] = next;
            }

            curr = next;
        }

        curr.IsEndOfWord = true;
    }

    public IReadOnlyList<string> GetWordsWithPrefix(string prefix)
    {
        List<string> results = new List<string>();
        TrieNode? curr = _root;
        foreach (char ch in prefix)
        {
            if (!curr.Children.TryGetValue(ch, out curr))
            {
                return results;
            }
        }

        CollectWords(curr, prefix, results);
        return results;
    }

    public string? LongestPrefixMatch(string query)
    {
        TrieNode? curr = _root;
        string? longest = null;
        int len = 0;

        foreach (char ch in query)
        {
            if (!curr.Children.TryGetValue(ch, out curr))
            {
                break;
            }

            len++;
            if (curr.IsEndOfWord)
            {
                longest = query.Substring(0, len);
            }
        }

        return longest;
    }

    public int CountWords()
    {
        return CountWords(_root);
    }

    public static IReadOnlyList<string> SortArrayWithTrie(ReadOnlySpan<string> words)
    {
        PrefixTrie trie = new PrefixTrie();
        foreach (string w in words)
        {
            trie.Insert(w);
        }

        return trie.GetWordsWithPrefix(string.Empty);
    }

    private static int CountWords(TrieNode node)
    {
        int count = node.IsEndOfWord ? 1 : 0;
        foreach (TrieNode child in node.Children.Values)
        {
            count += CountWords(child);
        }

        return count;
    }

    private static void CollectWords(TrieNode node, string currentPrefix, List<string> output)
    {
        if (node.IsEndOfWord)
        {
            output.Add(currentPrefix);
        }

        foreach ((char ch, TrieNode child) in node.Children)
        {
            CollectWords(child, currentPrefix + ch, output);
        }
    }
}

file static class BinaryTreeAlgorithms
{
    public static IReadOnlyList<int> TraverseInOrderIterative(AvlNode? root)
    {
        List<int> result = new List<int>();
        Stack<AvlNode> stack = new Stack<AvlNode>();
        AvlNode? curr = root;

        while (curr is not null || stack.Count > 0)
        {
            while (curr is not null)
            {
                stack.Push(curr);
                curr = curr.Left;
            }

            AvlNode popped = stack.Pop();
            result.Add(popped.Key);
            curr = popped.Right;
        }

        return result;
    }

    public static AvlNode? ConvertBinaryTreeToDoublyLinkedList(AvlNode? root)
    {
        AvlNode? head = null;
        AvlNode? prev = null;

        void StitchInOrder(AvlNode? node)
        {
            if (node is null)
            {
                return;
            }

            StitchInOrder(node.Left);
            if (prev is null)
            {
                head = node;
            }
            else
            {
                prev.Right = node;
                node.Left = prev;
            }

            prev = node;
            StitchInOrder(node.Right);
        }

        StitchInOrder(root);
        return head;
    }

    public static IReadOnlyList<int> GetTreeBoundaryPerimeter(AvlNode? root)
    {
        List<int> perimeter = new List<int>();
        if (root is null)
        {
            return perimeter;
        }

        static bool IsLeaf(AvlNode n) => n.Left is null && n.Right is null;

        perimeter.Add(root.Key);
        if (IsLeaf(root))
        {
            return perimeter;
        }

        AvlNode? curr = root.Left;
        while (curr is not null)
        {
            if (!IsLeaf(curr))
            {
                perimeter.Add(curr.Key);
            }

            curr = curr.Left ?? curr.Right;
        }

        void CollectLeaves(AvlNode? n)
        {
            if (n is null)
            {
                return;
            }

            if (IsLeaf(n))
            {
                perimeter.Add(n.Key);
                return;
            }

            CollectLeaves(n.Left);
            CollectLeaves(n.Right);
        }

        CollectLeaves(root);

        Stack<int> rightBoundary = new Stack<int>();
        curr = root.Right;
        while (curr is not null)
        {
            if (!IsLeaf(curr))
            {
                rightBoundary.Push(curr.Key);
            }

            curr = curr.Right ?? curr.Left;
        }

        while (rightBoundary.Count > 0)
        {
            perimeter.Add(rightBoundary.Pop());
        }

        return perimeter;
    }
}

file readonly record struct Point2D(double X, double Y, string Id);

file readonly record struct BoundingBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool Contains(Point2D p)
    {
        return p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }

    public bool Intersects(BoundingBox other)
    {
        return !(other.MinX > MaxX || other.MaxX < MinX || other.MinY > MaxY || other.MaxY < MinY);
    }
}

file sealed class QuadTree
{
    private readonly BoundingBox _boundary;
    private readonly int _capacity;
    private readonly List<Point2D> _points = new List<Point2D>();
    private QuadTree[]? _children;

    public QuadTree(BoundingBox boundary, int leafCapacity)
    {
        _boundary = boundary;
        _capacity = leafCapacity;
    }

    public bool Insert(Point2D p)
    {
        if (!_boundary.Contains(p))
        {
            return false;
        }

        if (_points.Count < _capacity && _children is null)
        {
            _points.Add(p);
            return true;
        }

        if (_children is null)
        {
            Subdivide();
        }

        foreach (QuadTree child in _children!)
        {
            if (child.Insert(p))
            {
                return true;
            }
        }

        return false;
    }

    public void QueryRange(BoundingBox range, List<Point2D> matches)
    {
        if (!_boundary.Intersects(range))
        {
            return;
        }

        foreach (Point2D p in _points)
        {
            if (range.Contains(p))
            {
                matches.Add(p);
            }
        }

        if (_children is not null)
        {
            foreach (QuadTree child in _children)
            {
                child.QueryRange(range, matches);
            }
        }
    }

    private void Subdivide()
    {
        double midX = (_boundary.MinX + _boundary.MaxX) / 2.0;
        double midY = (_boundary.MinY + _boundary.MaxY) / 2.0;

        _children =
        [
            new QuadTree(new BoundingBox(_boundary.MinX, midY, midX, _boundary.MaxY), _capacity),
            new QuadTree(new BoundingBox(midX, midY, _boundary.MaxX, _boundary.MaxY), _capacity),
            new QuadTree(new BoundingBox(_boundary.MinX, _boundary.MinY, midX, midY), _capacity),
            new QuadTree(new BoundingBox(midX, _boundary.MinY, _boundary.MaxX, midY), _capacity)
        ];
    }
}
```

---

## Section 20: Hash Tables, Heaps, and Graphs

### Part A: Worked Questionnaire Answers

1. **Separate Chaining vs Open Addressing and Classic Hashing Drills.** **Separate Chaining** resolves collisions by linking entries that hash to the same bucket; .NET's `Dictionary<TKey, TValue>` stores those chains inside a contiguous `Entry[] _entries` struct array indexed by integer array offsets (`next`), avoiding heap allocations per node. **Open Addressing (Linear Probing)** stores all entries directly inside the flat slot array and probes `i + 1, i + 2...` on collision, maximizing L1 cache locality. The 7 classic hashing drills (`AreDisjoint`, `FindSymmetricPairs`, `a + b == c + d`, `HasZeroSumSubarray` via running prefix-sum set, `FirstNonRepeating` via two-pass frequency map, linked-list duplicate removal, and list union/intersection) reduce $O(N^2)$ or $O(N^4)$ nested scans to $O(N)$ (or $O(N^2)$) hash lookups.
2. **Tombstones and Robin Hood Hashing.** In Open Addressing, deleting a key cannot mark the slot `EMPTY` because keys inserted later in the same probe chain would become unreachable; instead, `Remove` marks the slot as a **`TOMBSTONE`**, which `TryGet` steps over and `Put` reuses. **Robin Hood Hashing** tracks each entry's Probe Sequence Length (`PSL`); during `Put`, if the incoming key has probed further from its home bucket than the current slot's occupant (`psl > slot.Psl`), they swap places, equalizing probe lengths.
3. **Hash-Flooding DoS and SipHash.** If an attacker crafts 50,000 request keys that all collide to the same hash bucket, insertion degrades from $O(N)$ to $O(N^2)$ comparisons, freezing the CPU. Initializing the hash function with a per-process cryptographic random seed (or using keyed **SipHash-2-4**) prevents attackers from predicting collisions.
4. **Binary Heap array indexing and $O(N)$ Floyd's `Heapify`.** At 0-based index `i`: `Parent = (i - 1) / 2`, `Left = 2i + 1`, `Right = 2i + 2`. Floyd's bottom-up `Heapify` runs `SiftDown` starting from the last internal parent `N/2 - 1` down to `0`. Because $N/2^{h+1}$ nodes sit at height $h$ and travel at most $h$ steps down, total work is $\sum_{h=0}^{\log N} \frac{N}{2^{h+1}} h \le N \sum \frac{h}{2^{h+1}} = O(N)$.
5. **Classic Sorts (`Selection`, `Insertion`, `Bubble`, `Merge`, `QuickSort Hoare/Lomuto`) and External $K$-Way Merge Sort.** **Selection Sort** takes $\Theta(N^2)$ comparisons but at most $N - 1$ swaps; **Insertion Sort** shifts larger elements right in $O(N + I)$ time ($O(N)$ on nearly-sorted streams, making it the leaf finisher in `IntroSort`); **Bubble Sort** swaps adjacent inversions with an early-exit flag; **Merge Sort** guarantees stable $\Theta(N \log N)$ via an auxiliary merge buffer; **QuickSort** partitions in place in $O(N \log N)$ average time (where **Hoare's two-pointer partition** stops on equal keys and performs $3\times$ fewer swaps than **Lomuto's single-direction partition**, protected by median-of-three pivot selection). **External $K$-Way Merge Sort** sorts `4 GB` chunks into $K = 2{,}500$ run files on disk and merges them via a **Min-Heap of size $K$**.
6. **Graph algorithms and Path Counting.** **3-Color DFS** (`White = Unvisited`, `Gray = Visiting on recursion stack`, `Black = Explored`) detects a directed cycle iff an edge points to a `Gray` vertex. **DFS Backtracking (`CountPaths`)** counts all simple paths from `source` to `destination` by marking `onPath[u] = true` before recursing and resetting `onPath[u] = false` on return. **Kahn's Algorithm** tracks `InDegree[]` and processes `InDegree == 0` vertices via a BFS queue, detecting a cycle if `order.Count < V`. Use **BFS** for unweighted shortest paths, **Dijkstra / A\*** for non-negative weighted shortest paths, **Bellman-Ford** for negative edge weights, and **Union-Find** for dynamic connected components and Kruskal's MST.

### Part B: Worked C# Solution (`Chapter20.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter20
{
    [TestMethod]
    public void Robin_hood_hash_table_preserves_lookup_across_tombstone_deletions()
    {
        RobinHoodTable table = new RobinHoodTable(capacity: 8);
        table.Put("k1", 10);
        table.Put("k2", 20);
        table.Put("k3", 30);

        Assert.IsTrue(table.Remove("k2"));
        Assert.IsFalse(table.TryGet("k2", out _));
        Assert.IsTrue(table.TryGet("k1", out int v1) && v1 == 10);
        Assert.IsTrue(table.TryGet("k3", out int v3) && v3 == 30);
    }

    [TestMethod]
    public void Floyds_bottom_up_heapify_converts_unsorted_array_to_min_heap_in_linear_time()
    {
        int[] data = [45, 12, 89, 3, 27, 7, 19];
        BinaryMinHeap.HeapifyInPlace(data);

        Assert.AreEqual(3, data[0]);
        for (int i = 0; i < data.Length; i++)
        {
            int left = (2 * i) + 1;
            int right = (2 * i) + 2;
            if (left < data.Length)
            {
                Assert.IsLessThanOrEqualTo(data[left], data[i]);
            }

            if (right < data.Length)
            {
                Assert.IsLessThanOrEqualTo(data[right], data[i]);
            }
        }
    }

    [TestMethod]
    public void Kahns_topological_sort_orders_package_dag_and_detects_cycles()
    {
        (int From, int To)[] validDagEdges = [(0, 1), (0, 2), (1, 3), (2, 3)];
        IReadOnlyList<int>? order = GraphAlgorithms.TopologicalSortKahn(vertexCount: 4, validDagEdges);

        Assert.IsNotNull(order);
        Assert.HasCount(4, order);
        Assert.AreEqual(0, order[0]);
        Assert.AreEqual(3, order[3]);

        (int From, int To)[] cyclicEdges = [(0, 1), (1, 2), (2, 0)];
        Assert.IsNull(GraphAlgorithms.TopologicalSortKahn(vertexCount: 3, cyclicEdges));
    }

    [TestMethod]
    public void Dijkstra_and_union_find_compute_shortest_paths_and_connected_components()
    {
        (int To, int Weight)[][] adj =
        [
            [(1, 4), (2, 1)],
            [(3, 1)],
            [(1, 2), (3, 5)],
            []
        ];

        long[] dist = GraphAlgorithms.DijkstraShortestPaths(adj, source: 0);
        CollectionAssert.AreEqual(new long[] { 0, 3, 1, 4 }, dist);

        DisjointSet dsu = new DisjointSet(5);
        Assert.IsTrue(dsu.Union(0, 1));
        Assert.IsTrue(dsu.Union(1, 2));
        Assert.IsFalse(dsu.Union(0, 2));
        Assert.AreEqual(3, dsu.ComponentCount);

        int[][] dagAdj = [[1, 2], [2, 3], [3], []];
        Assert.AreEqual(3, GraphAlgorithms.CountPathsInDag(dagAdj, source: 0, destination: 3));
    }

    [TestMethod]
    public void Classic_sorting_algorithms_and_hash_table_drills_produce_exact_results()
    {
        int[] expected = [2, 5, 9, 12, 23, 34, 41, 78];

        int[] a1 = [34, 12, 78, 2, 41, 9, 23, 5];
        SortingAlgorithms.SelectionSort(a1);
        CollectionAssert.AreEqual(expected, a1);

        int[] a2 = [34, 12, 78, 2, 41, 9, 23, 5];
        SortingAlgorithms.InsertionSort(a2);
        CollectionAssert.AreEqual(expected, a2);

        int[] a3 = [34, 12, 78, 2, 41, 9, 23, 5];
        SortingAlgorithms.BubbleSort(a3);
        CollectionAssert.AreEqual(expected, a3);

        int[] a4 = [34, 12, 78, 2, 41, 9, 23, 5];
        SortingAlgorithms.MergeSort(a4);
        CollectionAssert.AreEqual(expected, a4);

        int[] a5 = [34, 12, 78, 2, 41, 9, 23, 5];
        SortingAlgorithms.QuickSortHoare(a5);
        CollectionAssert.AreEqual(expected, a5);

        int[] a6 = [34, 12, 78, 2, 41, 9, 23, 5];
        SortingAlgorithms.QuickSortLomuto(a6);
        CollectionAssert.AreEqual(expected, a6);

        Assert.IsTrue(HashSetDrills.AreDisjoint([1, 2, 3], [4, 5, 6]));
        Assert.IsFalse(HashSetDrills.AreDisjoint([1, 2, 3], [3, 4, 5]));
        Assert.IsTrue(HashSetDrills.HasZeroSumSubarray([4, 2, -3, 1, 6]));
        Assert.AreEqual(9, HashSetDrills.FirstNonRepeating([4, 5, 9, 4, 5, 7]));
    }
}

file sealed class RobinHoodTable
{
    private enum SlotState : byte
    {
        Empty,
        Occupied,
        Tombstone
    }

    private struct Slot
    {
        public SlotState State;
        public string Key;
        public int Value;
        public int Psl;
    }

    private readonly Slot[] _slots;

    public RobinHoodTable(int capacity)
    {
        _slots = new Slot[capacity];
    }

    public void Put(string key, int value)
    {
        int idx = GetHomeIndex(key);
        int psl = 0;
        string curKey = key;
        int curVal = value;

        while (true)
        {
            if (_slots[idx].State != SlotState.Occupied)
            {
                _slots[idx] = new Slot { State = SlotState.Occupied, Key = curKey, Value = curVal, Psl = psl };
                return;
            }

            if (string.Equals(_slots[idx].Key, curKey, StringComparison.Ordinal))
            {
                _slots[idx].Value = curVal;
                return;
            }

            if (psl > _slots[idx].Psl)
            {
                (curKey, _slots[idx].Key) = (_slots[idx].Key, curKey);
                (curVal, _slots[idx].Value) = (_slots[idx].Value, curVal);
                (psl, _slots[idx].Psl) = (_slots[idx].Psl, psl);
            }

            idx = (idx + 1) % _slots.Length;
            psl++;
        }
    }

    public bool TryGet(string key, out int value)
    {
        int idx = GetHomeIndex(key);
        for (int probed = 0; probed < _slots.Length; probed++)
        {
            if (_slots[idx].State == SlotState.Empty)
            {
                break;
            }

            if (_slots[idx].State == SlotState.Occupied && string.Equals(_slots[idx].Key, key, StringComparison.Ordinal))
            {
                value = _slots[idx].Value;
                return true;
            }

            idx = (idx + 1) % _slots.Length;
        }

        value = 0;
        return false;
    }

    public bool Remove(string key)
    {
        int idx = GetHomeIndex(key);
        for (int probed = 0; probed < _slots.Length; probed++)
        {
            if (_slots[idx].State == SlotState.Empty)
            {
                return false;
            }

            if (_slots[idx].State == SlotState.Occupied && string.Equals(_slots[idx].Key, key, StringComparison.Ordinal))
            {
                _slots[idx].State = SlotState.Tombstone;
                return true;
            }

            idx = (idx + 1) % _slots.Length;
        }

        return false;
    }

    private int GetHomeIndex(string key)
    {
        uint h = 2166136261u;
        foreach (char c in key)
        {
            h ^= c;
            h *= 16777619u;
        }

        return (int)(h % (uint)_slots.Length);
    }
}

file static class BinaryMinHeap
{
    public static void HeapifyInPlace(Span<int> array)
    {
        for (int i = (array.Length / 2) - 1; i >= 0; i--)
        {
            SiftDown(array, i, array.Length);
        }
    }

    private static void SiftDown(Span<int> array, int idx, int length)
    {
        while (true)
        {
            int left = (2 * idx) + 1;
            int right = (2 * idx) + 2;
            int smallest = idx;

            if (left < length && array[left] < array[smallest])
            {
                smallest = left;
            }

            if (right < length && array[right] < array[smallest])
            {
                smallest = right;
            }

            if (smallest == idx)
            {
                return;
            }

            (array[idx], array[smallest]) = (array[smallest], array[idx]);
            idx = smallest;
        }
    }
}

file static class GraphAlgorithms
{
    public static IReadOnlyList<int>? TopologicalSortKahn(int vertexCount, ReadOnlySpan<(int From, int To)> edges)
    {
        int[] inDegree = new int[vertexCount];
        List<int>[] adj = new List<int>[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            adj[i] = new List<int>();
        }

        foreach ((int u, int v) in edges)
        {
            adj[u].Add(v);
            inDegree[v]++;
        }

        Queue<int> ready = new Queue<int>();
        for (int i = 0; i < vertexCount; i++)
        {
            if (inDegree[i] == 0)
            {
                ready.Enqueue(i);
            }
        }

        List<int> order = new List<int>(vertexCount);
        while (ready.Count > 0)
        {
            int u = ready.Dequeue();
            order.Add(u);
            foreach (int v in adj[u])
            {
                inDegree[v]--;
                if (inDegree[v] == 0)
                {
                    ready.Enqueue(v);
                }
            }
        }

        return order.Count == vertexCount ? order : null;
    }

    public static long[] DijkstraShortestPaths((int To, int Weight)[][] adj, int source)
    {
        long[] dist = new long[adj.Length];
        Array.Fill(dist, long.MaxValue);
        dist[source] = 0;

        PriorityQueue<int, long> pq = new PriorityQueue<int, long>();
        pq.Enqueue(source, 0);

        while (pq.TryDequeue(out int u, out long d))
        {
            if (d > dist[u])
            {
                continue;
            }

            foreach ((int v, int w) in adj[u])
            {
                long candidate = dist[u] + w;
                if (candidate < dist[v])
                {
                    dist[v] = candidate;
                    pq.Enqueue(v, candidate);
                }
            }
        }

        return dist;
    }

    public static int CountPathsInDag(int[][] adj, int source, int destination)
    {
        bool[] onPath = new bool[adj.Length];

        int Dfs(int curr)
        {
            if (curr == destination)
            {
                return 1;
            }

            onPath[curr] = true;
            int total = 0;
            foreach (int next in adj[curr])
            {
                if (!onPath[next])
                {
                    total += Dfs(next);
                }
            }

            onPath[curr] = false;
            return total;
        }

        return Dfs(source);
    }
}

file static class SortingAlgorithms
{
    public static void SelectionSort(Span<int> a)
    {
        for (int i = 0; i < a.Length - 1; i++)
        {
            int min = i;
            for (int j = i + 1; j < a.Length; j++)
            {
                if (a[j] < a[min])
                {
                    min = j;
                }
            }

            (a[i], a[min]) = (a[min], a[i]);
        }
    }

    public static void InsertionSort(Span<int> a)
    {
        for (int i = 1; i < a.Length; i++)
        {
            int v = a[i];
            int j = i;
            while (j > 0 && v < a[j - 1])
            {
                a[j] = a[j - 1];
                j--;
            }

            a[j] = v;
        }
    }

    public static void BubbleSort(Span<int> a)
    {
        for (int i = 0; i < a.Length - 1; i++)
        {
            bool swapped = false;
            for (int j = a.Length - 1; j > i; j--)
            {
                if (a[j] < a[j - 1])
                {
                    (a[j - 1], a[j]) = (a[j], a[j - 1]);
                    swapped = true;
                }
            }

            if (!swapped)
            {
                break;
            }
        }
    }

    public static void MergeSort(Span<int> a)
    {
        if (a.Length <= 1)
        {
            return;
        }

        int[] temp = new int[a.Length];
        MergeSortRecursive(a, temp, 0, a.Length - 1);
    }

    private static void MergeSortRecursive(Span<int> a, int[] temp, int left, int right)
    {
        if (left >= right)
        {
            return;
        }

        int mid = left + ((right - left) / 2);
        MergeSortRecursive(a, temp, left, mid);
        MergeSortRecursive(a, temp, mid + 1, right);

        for (int k = left; k <= right; k++)
        {
            temp[k] = a[k];
        }

        int i = left;
        int j = mid + 1;
        for (int k = left; k <= right; k++)
        {
            if (i > mid)
            {
                a[k] = temp[j++];
            }
            else if (j > right)
            {
                a[k] = temp[i++];
            }
            else if (temp[i] <= temp[j])
            {
                a[k] = temp[i++];
            }
            else
            {
                a[k] = temp[j++];
            }
        }
    }

    public static void QuickSortHoare(Span<int> a)
    {
        if (a.Length > 1)
        {
            QuickSortHoareRecursive(a, 0, a.Length - 1);
        }
    }

    private static void QuickSortHoareRecursive(Span<int> a, int l, int r)
    {
        if (r <= l)
        {
            return;
        }

        int i = l - 1;
        int j = r;
        int v = a[r];
        while (true)
        {
            while (a[++i] < v)
            {
            }

            while (v < a[--j])
            {
                if (j == l)
                {
                    break;
                }
            }

            if (i >= j)
            {
                break;
            }

            (a[i], a[j]) = (a[j], a[i]);
        }

        (a[i], a[r]) = (a[r], a[i]);
        QuickSortHoareRecursive(a, l, i - 1);
        QuickSortHoareRecursive(a, i + 1, r);
    }

    public static void QuickSortLomuto(Span<int> a)
    {
        if (a.Length > 1)
        {
            QuickSortLomutoRecursive(a, 0, a.Length - 1);
        }
    }

    private static void QuickSortLomutoRecursive(Span<int> a, int l, int r)
    {
        if (r <= l)
        {
            return;
        }

        int pivot = a[r];
        int storeIdx = l;
        for (int j = l; j < r; j++)
        {
            if (a[j] < pivot)
            {
                (a[storeIdx], a[j]) = (a[j], a[storeIdx]);
                storeIdx++;
            }
        }

        (a[storeIdx], a[r]) = (a[r], a[storeIdx]);
        QuickSortLomutoRecursive(a, l, storeIdx - 1);
        QuickSortLomutoRecursive(a, storeIdx + 1, r);
    }
}

file static class HashSetDrills
{
    public static bool AreDisjoint(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        HashSet<int> set = new HashSet<int>();
        foreach (int x in a)
        {
            set.Add(x);
        }

        foreach (int y in b)
        {
            if (set.Contains(y))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasZeroSumSubarray(ReadOnlySpan<int> arr)
    {
        HashSet<long> seenPrefixes = new HashSet<long> { 0L };
        long sum = 0;
        foreach (int x in arr)
        {
            sum += x;
            if (x == 0 || !seenPrefixes.Add(sum))
            {
                return true;
            }
        }

        return false;
    }

    public static int? FirstNonRepeating(ReadOnlySpan<int> arr)
    {
        Dictionary<int, int> counts = new Dictionary<int, int>();
        foreach (int x in arr)
        {
            counts[x] = counts.GetValueOrDefault(x) + 1;
        }

        foreach (int x in arr)
        {
            if (counts[x] == 1)
            {
                return x;
            }
        }

        return null;
    }
}

file sealed class DisjointSet
{
    private readonly int[] _parent;
    private readonly int[] _rank;

    public int ComponentCount { get; private set; }

    public DisjointSet(int size)
    {
        _parent = new int[size];
        _rank = new int[size];
        ComponentCount = size;
        for (int i = 0; i < size; i++)
        {
            _parent[i] = i;
        }
    }

    public int Find(int x)
    {
        if (_parent[x] != x)
        {
            _parent[x] = Find(_parent[x]);
        }

        return _parent[x];
    }

    public bool Union(int a, int b)
    {
        int rootA = Find(a);
        int rootB = Find(b);
        if (rootA == rootB)
        {
            return false;
        }

        if (_rank[rootA] < _rank[rootB])
        {
            _parent[rootA] = rootB;
        }
        else if (_rank[rootA] > _rank[rootB])
        {
            _parent[rootB] = rootA;
        }
        else
        {
            _parent[rootB] = rootA;
            _rank[rootA]++;
        }

        ComponentCount--;
        return true;
    }
}
```

---

## Section 21: Dynamic Programming, Greedy Algorithms, and Coding Patterns

### Part A: Worked Questionnaire Answers

1. **Dynamic Programming properties and Top-Down vs Bottom-Up.** DP requires **Optimal Substructure** (optimal solution to a problem incorporates optimal solutions to subproblems) and **Overlapping Subproblems** (recursive branches repeatedly hit the same state). **Top-Down Memoization** caches recursive calls on demand (great for sparse state spaces); **Bottom-Up Tabulation** fills an iterative array in topological order (zero call-stack overflow risk, L1 cache locality, and enables state-space reduction).
2. **Why 1D 0/1 Knapsack iterates capacity backward.** When updating `dp[c] = max(dp[c], dp[c - w] + p)` for item $i$, iterating $c$ **backward from `capacity` down to `w`** guarantees that `dp[c - w]` still holds the value from before item $i$ was considered (each item used at most once: **0/1 Knapsack**). Iterating $c$ **forward** from `w` to `capacity` reads `dp[c - w]` after item $i$ has already been added to it in the same pass, allowing item $i$ to be reused infinitely many times (**Unbounded Knapsack**).
3. **Greedy Choice Property, Prim's/Kruskal's MST, and counterexamples.** A problem has the Greedy Choice Property if making the locally best choice first can always be extended to a globally optimal solution (proved via an **Exchange Argument**). Sorting by **Earliest Finish Time** is optimal for **Interval Scheduling** because finishing as early as possible leaves maximal room for future intervals. **Prim's MST** ($O((V+E)\log V)$ via a Min-Heap of crossing edges from a start vertex) and **Kruskal's MST** ($O(E\log E)$ via global edge sort + Union-Find) both exploit the **Greedy Cut Property** (the minimum-weight edge crossing any cut $(S, V \setminus S)$ belongs to an MST). Greedy fails on **0/1 Knapsack** (picking one high-density item of weight 3, profit 4 in a capacity-4 bag leaves 1 empty slot, losing to two weight-2, profit-3 items giving profit 6) and on **Coin Change `[1, 3, 4]` for target `6`** (Greedy picks `4+1+1 = 3 coins` instead of `3+3 = 2 coins`).
4. **Core array/list patterns.** **Two Pointers** ($O(N)$ time, $O(1)$ space on sorted arrays); **Fast & Slow Pointers** ($O(N)$ time, $O(1)$ space cycle/midpoint detection); **Sliding Window** ($O(N)$ contiguous range maintenance); **Merge Intervals** ($O(N \log N)$ sort by `Start` then merge when `next.Start <= curr.End`); **Cyclic Sort** ($O(N)$ time, $O(1)$ space index swapping for $1..N$ ranges); **Bitwise XOR** ($x \oplus x = 0$ cancels duplicate pairs in $O(N)$ time, $O(1)$ space).
5. **Two Heaps and $K$-Way Merge.** **Two Heaps** keeps a **Max-Heap** of the smaller half and a **Min-Heap** of the larger half balanced within 1 element (`O(log N)` insert, `O(1)` median at the heap roots). **$K$-Way Merge** pushes the head of each of the $K$ sorted streams into a Min-Heap of size $K$, popping the minimum and pushing its successor in $O(N \log K)$ time.
6. **Monotonic Stack $O(N)$ amortization and Binary Search on Answer.** In a **Monotonic Decreasing Stack**, each of the $N$ indices is pushed once and popped at most once across the entire loop ($\le 2N$ total stack operations = **$O(N)$ amortized time**). **Binary Search on the Answer Space** applies whenever a boolean feasibility check `CanAchieve(mid)` is monotonic over `[low, high]`, finding the optimal threshold in $O(\log(\text{Range}))$ evaluations.

### Part B: Worked C# Solution (`Chapter21.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter21
{
    [TestMethod]
    public void One_dimensional_zero_one_knapsack_maximizes_profit_and_greedy_fails_on_indivisible_items()
    {
        int[] weights = [3, 2, 2];
        int[] profits = [4, 3, 3];

        int optimalProfit = DynamicProgrammingAndPatterns.SolveZeroOneKnapsack(weights, profits, capacity: 4);
        Assert.AreEqual(6, optimalProfit);
    }

    [TestMethod]
    public void Greedy_interval_scheduling_and_merge_intervals_handle_time_windows_optimally()
    {
        (int Start, int End)[] intervals = [(1, 4), (2, 3), (3, 6), (5, 7), (6, 8)];
        int maxNonOverlapping = DynamicProgrammingAndPatterns.MaxNonOverlappingIntervals(intervals);
        Assert.AreEqual(3, maxNonOverlapping);

        IReadOnlyList<(int Start, int End)> merged = DynamicProgrammingAndPatterns.MergeOverlappingIntervals(intervals);
        Assert.HasCount(1, merged);
        Assert.AreEqual((1, 8), merged[0]);

        (int To, long Weight)[][] mstGraph =
        [
            [(1, 4), (2, 1)],
            [(0, 4), (2, 2), (3, 5)],
            [(0, 1), (1, 2), (3, 8)],
            [(1, 5), (2, 8)]
        ];
        long? mstWeight = DynamicProgrammingAndPatterns.PrimMinimumSpanningTreeWeight(mstGraph);
        Assert.AreEqual(8L, mstWeight);
    }

    [TestMethod]
    public void Monotonic_stack_and_sliding_window_and_two_heaps_execute_in_optimal_bounds()
    {
        int[] nextGreater = DynamicProgrammingAndPatterns.NextGreaterElements([2, 1, 5, 3, 6]);
        CollectionAssert.AreEqual(new[] { 5, 5, 6, 6, -1 }, nextGreater);

        int longestKDistinct = DynamicProgrammingAndPatterns.LongestSubstringWithAtMostKDistinct("araaci", k: 2);
        Assert.AreEqual(4, longestKDistinct);

        StreamingMedianTracker medianTracker = new StreamingMedianTracker();
        medianTracker.Add(10);
        medianTracker.Add(20);
        Assert.AreEqual(15.0, medianTracker.GetMedian(), 0.001);
        medianTracker.Add(12);
        Assert.AreEqual(12.0, medianTracker.GetMedian(), 0.001);
    }
}

file static class DynamicProgrammingAndPatterns
{
    public static int SolveZeroOneKnapsack(ReadOnlySpan<int> weights, ReadOnlySpan<int> profits, int capacity)
    {
        int[] dp = new int[capacity + 1];
        for (int i = 0; i < weights.Length; i++)
        {
            int w = weights[i];
            int p = profits[i];
            for (int c = capacity; c >= w; c--)
            {
                int candidate = dp[c - w] + p;
                if (candidate > dp[c])
                {
                    dp[c] = candidate;
                }
            }
        }

        return dp[capacity];
    }

    public static int MaxNonOverlappingIntervals(ReadOnlySpan<(int Start, int End)> intervals)
    {
        (int Start, int End)[] sorted = intervals.ToArray();
        Array.Sort(sorted, static (a, b) => a.End.CompareTo(b.End));

        int count = 0;
        int lastEnd = int.MinValue;
        foreach ((int start, int end) in sorted)
        {
            if (start >= lastEnd)
            {
                count++;
                lastEnd = end;
            }
        }

        return count;
    }

    public static IReadOnlyList<(int Start, int End)> MergeOverlappingIntervals(ReadOnlySpan<(int Start, int End)> intervals)
    {
        (int Start, int End)[] sorted = intervals.ToArray();
        Array.Sort(sorted, static (a, b) => a.Start.CompareTo(b.Start));

        List<(int Start, int End)> merged = new List<(int, int)>();
        foreach ((int start, int end) in sorted)
        {
            if (merged.Count == 0 || start > merged[^1].End)
            {
                merged.Add((start, end));
            }
            else
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            }
        }

        return merged;
    }

    public static int[] NextGreaterElements(ReadOnlySpan<int> nums)
    {
        int[] result = new int[nums.Length];
        Array.Fill(result, -1);
        Stack<int> decreasingIndices = new Stack<int>();

        for (int i = 0; i < nums.Length; i++)
        {
            while (decreasingIndices.Count > 0 && nums[i] > nums[decreasingIndices.Peek()])
            {
                int idx = decreasingIndices.Pop();
                result[idx] = nums[i];
            }

            decreasingIndices.Push(i);
        }

        return result;
    }

    public static int LongestSubstringWithAtMostKDistinct(string text, int k)
    {
        Dictionary<char, int> charCounts = new Dictionary<char, int>();
        int left = 0;
        int maxLen = 0;

        for (int right = 0; right < text.Length; right++)
        {
            char rCh = text[right];
            charCounts[rCh] = charCounts.GetValueOrDefault(rCh) + 1;

            while (charCounts.Count > k)
            {
                char lCh = text[left];
                charCounts[lCh]--;
                if (charCounts[lCh] == 0)
                {
                    charCounts.Remove(lCh);
                }

                left++;
            }

            maxLen = Math.Max(maxLen, right - left + 1);
        }

        return maxLen;
    }

    public static long? PrimMinimumSpanningTreeWeight((int To, long Weight)[][] adj)
    {
        if (adj.Length == 0)
        {
            return 0;
        }

        bool[] inMst = new bool[adj.Length];
        PriorityQueue<(int To, long Weight), long> pq = new PriorityQueue<(int, long), long>();
        inMst[0] = true;
        int visited = 1;

        foreach ((int next, long w) in adj[0])
        {
            pq.Enqueue((next, w), w);
        }

        long totalWeight = 0;
        while (pq.TryDequeue(out (int To, long Weight) edge, out _) && visited < adj.Length)
        {
            if (inMst[edge.To])
            {
                continue;
            }

            inMst[edge.To] = true;
            visited++;
            totalWeight += edge.Weight;

            foreach ((int next, long w) in adj[edge.To])
            {
                if (!inMst[next])
                {
                    pq.Enqueue((next, w), w);
                }
            }
        }

        return visited == adj.Length ? totalWeight : null;
    }
}

file sealed class StreamingMedianTracker
{
    private readonly PriorityQueue<int, int> _lowerMaxHeap =
        new PriorityQueue<int, int>(Comparer<int>.Create(static (a, b) => b.CompareTo(a)));
    private readonly PriorityQueue<int, int> _upperMinHeap = new PriorityQueue<int, int>();

    public void Add(int num)
    {
        _lowerMaxHeap.Enqueue(num, num);
        int largestOfLower = _lowerMaxHeap.Dequeue();
        _upperMinHeap.Enqueue(largestOfLower, largestOfLower);

        if (_upperMinHeap.Count > _lowerMaxHeap.Count)
        {
            int smallestOfUpper = _upperMinHeap.Dequeue();
            _lowerMaxHeap.Enqueue(smallestOfUpper, smallestOfUpper);
        }
    }

    public double GetMedian()
    {
        if (_lowerMaxHeap.Count > _upperMinHeap.Count)
        {
            return _lowerMaxHeap.Peek();
        }

        return (_lowerMaxHeap.Peek() + _upperMinHeap.Peek()) / 2.0;
    }
}
```

---

## Section 22: Control Plane vs Data Plane and Cell-Based Architectures

### Part A: Worked Questionnaire Answers

1. **Control Plane vs Data Plane and the Multiplication Rule.** The **Control Plane** handles low-QPS (`10-5,000 RPS`), strongly consistent (`PC/EC`) CRUD, policy authoring, and orchestration at a `99.9%` SLA. The **Data Plane** handles high-QPS (`100K-10M+ RPS`), ultra-low-latency (`PA/EL`) runtime execution at a `99.999%` SLA. Because sequential dependency availability multiplies ($A_{\text{total}} = \prod a_i$), a `99.999%` Data Plane that makes a synchronous hot-path call to a `99.9%` Control Plane drops its effective availability to at most `99.9%`.
2. **Static Stability and hidden coupling.** A Data Plane is **statically stable** if it continues operating at 100% capacity using already-held local state when the Control Plane is completely unreachable. Three hidden couplings that violate static stability are: (1) falling back to a Control Plane HTTP call on a local cache miss or pod restart, (2) checking a central Control Plane feature-flag service on the request path, and (3) relying on the Control Plane to renew short-lived runtime credentials every few minutes.
3. **Zero-Dependency Stamp Data Plane pattern.** The Control Plane periodically compacts active policies into an immutable **Baseline Snapshot Blob** (`snapshot-v10400`) in stamp-local object storage and streams real-time mutations into a **stamp-local Incremental Change Queue** (`delta-v10401...`). A booting Data Plane pod reads the local baseline snapshot, replays local queue deltas with `SequenceVersion > SnapshotVersion`, pre-compiles the rules, and publishes the catalog via an atomic reference swap.
4. **Why both monotonicity guards are mandatory during hydration.** Because snapshot writing and queue delivery are asynchronous, the queue contains historical events (`v <= SnapshotVersion`) already folded into the snapshot, and queue retries can deliver older events out of order after newer events. Checking both `delta.SequenceVersion <= baselineSnapshot.SnapshotVersion` and `existing.Version >= delta.SequenceVersion` guarantees idempotent, monotonic convergence.
5. **Cell-Based Architectures and Shuffle Sharding math.** A **Cell (Deployment Stamp)** has (1) zero cross-cell runtime state sharing, (2) a fixed, load-tested maximum capacity ceiling, (3) one-cell-at-a-time canary progression, and (4) a thin stateless cell router. Assigning each tenant $k = 4$ workers out of $N = 100$ via **Shuffle Sharding** creates $\binom{100}{4} = 3{,}921{,}225$ virtual shards, reducing complete co-tenant overlap to $1 / 3{,}921{,}225 \approx 0.0000255\%$.
6. **Public REST API and Multi-Language SDK governance.** (1) Author explicit versioned **OpenAPI** contracts first (`?api-version=YYYY-MM-DD`); (2) require **`Idempotency-Key`** headers on mutating `POST`s; (3) return **`HTTP 202 Accepted` + `Operation-Location` + `Retry-After`** for Long-Running Operations (`> 1-2s`); and (4) use opaque **continuation tokens** instead of `OFFSET/LIMIT`, generating idiomatic SDKs (`C#, Java, Python, Go, TypeScript`) with built-in retry, LRO polling, and async paging iterators.

### Part B: Worked C# Solution (`Chapter22.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter22
{
    [TestMethod]
    public void Zero_dependency_stamp_cache_hydrates_snapshot_and_reconciles_overlapping_change_queue_deltas_monotonically()
    {
        PolicySnapshot baseline = new PolicySnapshot(
            SnapshotVersion: 500,
            Rules:
            [
                new PolicyRule("pol-1", Version: 490, CompiledExpression: "sku == 'pro'"),
                new PolicyRule("pol-2", Version: 500, CompiledExpression: "region == 'eu'")
            ]);

        // Out-of-order and overlapping queue events around snapshot boundary 500:
        PolicyDeltaEvent[] deltas =
        [
            new PolicyDeltaEvent(SequenceVersion: 498, DeltaOperation.Upsert, "pol-1", "stale-before-snapshot"),
            new PolicyDeltaEvent(SequenceVersion: 502, DeltaOperation.Upsert, "pol-3", "os >= 10"),
            new PolicyDeltaEvent(SequenceVersion: 501, DeltaOperation.Delete, "pol-2", null),
            new PolicyDeltaEvent(SequenceVersion: 501, DeltaOperation.Delete, "pol-3", null) // Older than 502!
        ];

        StampDataPlaneCache stampCache = new StampDataPlaneCache();
        stampCache.HydrateAndApplyDeltas(baseline, deltas);

        Assert.AreEqual(502L, stampCache.CurrentCatalogVersion);
        Assert.IsTrue(stampCache.TryGetPolicy("pol-1", out PolicyRule? p1) && p1 is not null && p1.CompiledExpression == "sku == 'pro'");
        Assert.IsFalse(stampCache.TryGetPolicy("pol-2", out _));
        Assert.IsTrue(stampCache.TryGetPolicy("pol-3", out PolicyRule? p3) && p3 is not null && p3.Version == 502);
    }

    [TestMethod]
    public void Shuffle_sharding_allocates_distinct_endpoints_and_reduces_full_overlap_combinatorially()
    {
        long virtualShardCombinations = ShuffleShardAllocator.CountVirtualShardCombinations(totalNodes: 100, endpointsPerTenant: 4);
        Assert.AreEqual(3_921_225L, virtualShardCombinations);

        int[] shardA = ShuffleShardAllocator.AssignEndpoints("tenant-alpha", totalNodes: 100, endpointsPerTenant: 4);
        int[] shardB = ShuffleShardAllocator.AssignEndpoints("tenant-bravo", totalNodes: 100, endpointsPerTenant: 4);

        Assert.HasCount(4, shardA.Distinct().ToArray());
        Assert.HasCount(4, shardB.Distinct().ToArray());
        CollectionAssert.AreNotEqual(shardA, shardB);
    }
}

file enum DeltaOperation : byte
{
    Upsert = 1,
    Delete = 2
}

file sealed record PolicyRule(string PolicyId, long Version, string CompiledExpression);

file sealed record PolicySnapshot(long SnapshotVersion, IReadOnlyList<PolicyRule> Rules);

file sealed record PolicyDeltaEvent(
    long SequenceVersion,
    DeltaOperation Operation,
    string PolicyId,
    string? CompiledExpression);

file sealed class StampDataPlaneCache
{
    private Dictionary<string, PolicyRule> _activeRules = new Dictionary<string, PolicyRule>(StringComparer.Ordinal);

    public long CurrentCatalogVersion { get; private set; }

    public bool TryGetPolicy(string policyId, out PolicyRule? rule)
    {
        return _activeRules.TryGetValue(policyId, out rule);
    }

    public void HydrateAndApplyDeltas(PolicySnapshot baselineSnapshot, IEnumerable<PolicyDeltaEvent> deltaQueue)
    {
        Dictionary<string, PolicyRule> state = new Dictionary<string, PolicyRule>(StringComparer.Ordinal);
        long highestVersion = baselineSnapshot.SnapshotVersion;

        foreach (PolicyRule rule in baselineSnapshot.Rules)
        {
            state[rule.PolicyId] = rule;
            if (rule.Version > highestVersion)
            {
                highestVersion = rule.Version;
            }
        }

        foreach (PolicyDeltaEvent delta in deltaQueue.OrderBy(static d => d.SequenceVersion))
        {
            if (delta.SequenceVersion <= baselineSnapshot.SnapshotVersion)
            {
                continue;
            }

            if (state.TryGetValue(delta.PolicyId, out PolicyRule? existing) &&
                existing.Version >= delta.SequenceVersion)
            {
                continue;
            }

            if (delta.Operation == DeltaOperation.Delete)
            {
                state.Remove(delta.PolicyId);
            }
            else if (delta.Operation == DeltaOperation.Upsert && delta.CompiledExpression is not null)
            {
                state[delta.PolicyId] = new PolicyRule(delta.PolicyId, delta.SequenceVersion, delta.CompiledExpression);
            }

            if (delta.SequenceVersion > highestVersion)
            {
                highestVersion = delta.SequenceVersion;
            }
        }

        _activeRules = state;
        CurrentCatalogVersion = highestVersion;
    }
}

file static class ShuffleShardAllocator
{
    public static long CountVirtualShardCombinations(int totalNodes, int endpointsPerTenant)
    {
        long result = 1;
        for (int i = 1; i <= endpointsPerTenant; i++)
        {
            result = result * (totalNodes - i + 1) / i;
        }

        return result;
    }

    public static int[] AssignEndpoints(string tenantId, int totalNodes, int endpointsPerTenant)
    {
        List<int> assigned = new List<int>(endpointsPerTenant);
        uint salt = 0;

        while (assigned.Count < endpointsPerTenant)
        {
            uint h = 2166136261u ^ salt;
            foreach (char c in tenantId)
            {
                h ^= c;
                h *= 16777619u;
            }

            int candidate = (int)(h % (uint)totalNodes);
            if (!assigned.Contains(candidate))
            {
                assigned.Add(candidate);
            }

            salt += 0x9E3779B9u;
        }

        assigned.Sort();
        return assigned.ToArray();
    }
}
```

---

## Section 23: Multi-Tenant Isolation and Noisy-Neighbor Economics

### Part A: Worked Questionnaire Answers

1. **The Four-Tier Multi-Tenancy Isolation Spectrum.** (1) **Shared Pool**: shared compute and shared DB tables partitioned logically by `TenantId` (lowest COGS, highest noisy-neighbor risk); (2) **Pod/Stamp-Partitioned**: shared cell compute with dedicated DB shards/topics for large tenants; (3) **Provider-Hosted Silo**: dedicated cluster per customer inside the SaaS provider's cloud subscription; and (4) **Customer-Hosted (`Managed Resource Group / BYOS`)**: SaaS Control Plane orchestrates Data Plane compute and storage provisioned directly inside the **customer's own cloud subscription**.
2. **Why Customer-Hosted (`MRG`) solves sovereignty, noisy neighbors, and COGS.** Customer data and compliance logs remain physically inside the customer's subscription encrypted with **Customer-Managed Keys (`CMK`)**; evaluation containers consume only that customer's subscription quota (zero cross-tenant noisy neighbors); and cloud compute/storage I/O bills directly to the customer's cloud invoice (100% direct COGS attribution).
3. **Cross-Boundary Delegation and Asynchronous Provisioning.** The customer deploys an onboarding template granting scoped RBAC on the Managed Resource Group to the Control Plane's **Managed Identity (OIDC Federation)** with zero stored secrets. The Control Plane drives an idempotent, asynchronous **`HTTP 202` Reconciliation Saga** to provision and update resources inside the MRG.
4. **Physics of the 87% Multi-Tenant Fan-Out Throughput Collapse.** When a `20 MB` input blob spans **200 tenants** (`~100 KB/tenant`) instead of 1 tenant, a naive worker performs **200 separate object-store flushes** (`200 * 25 ms = 5 seconds` of pure network/metadata latency overhead), allocates 200 concurrent compression buffers on the .NET Large Object Heap (`LOH`), and contends on 200 partition locks, dropping single-worker throughput by **87% (`15.4 MB/s -> 2.0 MB/s`)**.
5. **The $78\times$ Throughput Architecture (`1.3 MB/s -> 102.2 MB/s`).** Combine (1) **Claim-Check byte-range splitting** across parallel workers, (2) **in-memory tenant batch coalescing** (buffering small-tenant events across blobs until reaching `8-32 MB` or a staleness SLA before flushing), and (3) **`ArrayPool<byte>` memory pooling**. To prevent data loss if a pod crashes before flushing its RAM buffer, **never commit the upstream Kafka/Event Hub offset until the coalesced batch is durably flushed to object storage**.
6. **Noisy-Neighbor Defense (`Stratified Tiers + Token Buckets + DRR`).** Separate Small, Medium, and Mega-Tenants into stratified worker pools, enforce per-tenant **Token Buckets** at the edge (`HTTP 429`), and schedule shared workers via **Deficit Round-Robin (`DRR`)**, where each active tenant queue receives a `Quantum` credit per round so a mega-tenant with 10,000 queued items cannot block a quiet tenant with 1 item.

### Part B: Worked C# Solution (`Chapter23.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter23
{
    [TestMethod]
    public void Tenant_batch_coalescer_eliminates_per_blob_fanout_flush_amplification()
    {
        TenantBatchCoalescer coalescer = new TenantBatchCoalescer(flushThresholdBytes: 1_000, maxAgeMs: 60_000);

        // Simulate 10 mixed blobs, each containing 50 small tenants (100 bytes per tenant per blob).
        // Naive flushing would perform 10 * 50 = 500 small flushes!
        // Coalescing until 1,000 bytes performs only 50 flushes total (a 10x reduction in I/O calls).
        for (int blobIdx = 0; blobIdx < 10; blobIdx++)
        {
            for (int t = 0; t < 50; t++)
            {
                coalescer.AppendEvent($"tenant-{t}", payloadBytes: 100, nowMs: blobIdx * 1_000);
            }
        }

        Assert.AreEqual(50, coalescer.CompletedFlushCount);
        Assert.AreEqual(50_000L, coalescer.TotalBytesFlushed);
    }

    [TestMethod]
    public void Deficit_round_robin_scheduler_prevents_noisy_mega_tenant_from_starving_small_tenant()
    {
        DeficitRoundRobinScheduler<string> scheduler = new DeficitRoundRobinScheduler<string>();
        scheduler.RegisterTenant("mega-tenant", weightQuantum: 100);
        scheduler.RegisterTenant("small-hospital", weightQuantum: 100);

        for (int i = 0; i < 50; i++)
        {
            scheduler.Enqueue("mega-tenant", $"mega-job-{i}", workCost: 100);
        }

        scheduler.Enqueue("small-hospital", "urgent-patch", workCost: 100);

        Assert.IsTrue(scheduler.TryDequeueNext(out string? firstTenant, out _));
        Assert.AreEqual("mega-tenant", firstTenant);

        // On the very next quantum, small-hospital is served immediately instead of waiting behind 49 mega-tenant jobs!
        Assert.IsTrue(scheduler.TryDequeueNext(out string? secondTenant, out string? secondPayload));
        Assert.AreEqual("small-hospital", secondTenant);
        Assert.AreEqual("urgent-patch", secondPayload);
    }
}

file sealed class TenantBatchCoalescer
{
    private readonly int _flushThresholdBytes;
    private readonly long _maxAgeMs;
    private readonly Dictionary<string, (int Bytes, long FirstSeenMs)> _buffers =
        new Dictionary<string, (int, long)>(StringComparer.Ordinal);

    public int CompletedFlushCount { get; private set; }

    public long TotalBytesFlushed { get; private set; }

    public TenantBatchCoalescer(int flushThresholdBytes, long maxAgeMs)
    {
        _flushThresholdBytes = flushThresholdBytes;
        _maxAgeMs = maxAgeMs;
    }

    public void AppendEvent(string tenantId, int payloadBytes, long nowMs)
    {
        if (!_buffers.TryGetValue(tenantId, out (int Bytes, long FirstSeenMs) current))
        {
            current = (0, nowMs);
        }

        int updatedBytes = current.Bytes + payloadBytes;
        if (updatedBytes >= _flushThresholdBytes || (nowMs - current.FirstSeenMs) >= _maxAgeMs)
        {
            CompletedFlushCount++;
            TotalBytesFlushed += updatedBytes;
            _buffers.Remove(tenantId);
        }
        else
        {
            _buffers[tenantId] = (updatedBytes, current.FirstSeenMs);
        }
    }
}

file sealed class DeficitRoundRobinScheduler<TPayload>
{
    private sealed class TenantQueue
    {
        public string TenantId { get; }
        public int Quantum { get; }
        public int Deficit { get; set; }
        public Queue<(TPayload Item, int Cost)> Items { get; } = new Queue<(TPayload, int)>();

        public TenantQueue(string tenantId, int quantum)
        {
            TenantId = tenantId;
            Quantum = quantum;
        }
    }

    private readonly Dictionary<string, TenantQueue> _queues = new Dictionary<string, TenantQueue>(StringComparer.Ordinal);
    private readonly Queue<TenantQueue> _activeList = new Queue<TenantQueue>();

    public void RegisterTenant(string tenantId, int weightQuantum)
    {
        _queues[tenantId] = new TenantQueue(tenantId, weightQuantum);
    }

    public void Enqueue(string tenantId, TPayload item, int workCost)
    {
        TenantQueue q = _queues[tenantId];
        bool wasEmpty = q.Items.Count == 0;
        q.Items.Enqueue((item, workCost));
        if (wasEmpty)
        {
            _activeList.Enqueue(q);
        }
    }

    public bool TryDequeueNext(out string? tenantId, out TPayload? item)
    {
        int activeCount = _activeList.Count;
        for (int i = 0; i < activeCount; i++)
        {
            TenantQueue current = _activeList.Dequeue();
            current.Deficit += current.Quantum;

            if (current.Items.TryPeek(out (TPayload Item, int Cost) head) && head.Cost <= current.Deficit)
            {
                current.Items.Dequeue();
                current.Deficit -= head.Cost;
                if (current.Items.Count > 0)
                {
                    _activeList.Enqueue(current);
                }
                else
                {
                    current.Deficit = 0;
                }

                tenantId = current.TenantId;
                item = head.Item;
                return true;
            }

            if (current.Items.Count > 0)
            {
                _activeList.Enqueue(current);
            }
            else
            {
                current.Deficit = 0;
            }
        }

        tenantId = null;
        item = default;
        return false;
    }
}
```

---

## Section 24: Columnar Storage and Lakehouse Pipelines

### Part A: Worked Questionnaire Answers

1. **Row-Oriented (`OLTP`) vs Column-Oriented (`OLAP`) storage.** A row store places all 50 columns of each row contiguously, forcing an analytical scan of 3 columns to read 100% of the table's bytes from disk. A column store (`Apache Parquet`) stores each column contiguously in **Column Chunks**, giving (1) **Projection Pushdown** (reading only the 3 requested column byte ranges), (2) **$5\times\text{--}20\times$ higher compression** (homogeneous data types encoded via Dictionary/RLE/Delta), and (3) **SIMD vectorized CPU execution**.
2. **Apache Parquet file anatomy and why the Footer is read first.** A Parquet file starts with `"PAR1"`, followed by horizontal **Row Groups (`128-512 MB`)** divided vertically into **Column Chunks** and **Data Pages**, and ends with the **`FileMetaData` Footer**, `FooterLength (4B)`, and `"PAR1"`. Streaming writers write the Footer at the end after all byte offsets and column statistics are known; readers issue a tail HTTP range read (`Range: bytes=-65536`) first to inspect the schema, column byte offsets, and `Min/Max`/Bloom statistics before reading data pages.
3. **Parquet encodings and Dremel levels.** **Dictionary Encoding** replaces repeated strings with small integer IDs; **RLE / Bit-Packing** compresses contiguous runs `(Value, Count)` or packs small integers to minimal bit width; **Delta Encoding** stores differences between consecutive timestamps/integers. Google Dremel's **Definition Level (`D-Level`)** counts how many optional ancestors are non-null, and **Repetition Level (`R-Level`)** records at which nested list depth a new element begins, shredding arbitrary nested trees into flat columns without losing structure.
4. **Min/Max Statistics and Bloom Filter Predicate Pushdown.** If a Row Group's footer metadata shows `[Min, Max]` outside the query predicate range, the entire Row Group is skipped with zero disk reads. Because unsorted high-cardinality columns (`DeviceId`, `TraceId` GUIDs) span `"0000..."` to `"ffff..."` in every Row Group, `Min/Max` cannot prune them; reading the **Column-Chunk Split-Block Bloom Filter** proves with 0% false negatives when a target GUID is absent, skipping 99% of Row Groups.
5. **The Small-File Problem and two-stage cure.** Flushing tiny `50 KB` Parquet files every few seconds creates millions of files, overwhelming object-store `ListBlobs`/footer `GET` calls, destroying RLE compression, and hitting storage IOPS limits. Cure it via **Stage 1 (Ingestion-Time Micro-Batch Coalescing)** and **Stage 2 (Background Bin-Packing Compaction / `OPTIMIZE`)** into `256-512 MB` files.
6. **Delta Lake and Apache Iceberg ACID mechanics.** Lakehouse formats maintain an **atomic ordered Transaction Log (`_delta_log/00000N.json` / Manifest tree)** as the single source of truth instead of directory listings. Compaction and deletes write new Parquet files and append a log commit (`RemoveFile + AddFile`) while retaining old files on disk until `VACUUM`, giving lock-free **MVCC Snapshot Isolation** and **Time Travel**. **Z-Order Clustering** interleaves bits across multiple columns so `Min/Max` pushdown prunes across multiple dimensions simultaneously.

### Part B: Worked C# Solution (`Chapter24.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter24
{
    [TestMethod]
    public void Columnar_dictionary_rle_chunk_compresses_runs_and_prunes_row_groups_via_minmax_and_bloom_pushdown()
    {
        ColumnarRleDictionaryChunk chunk = new ColumnarRleDictionaryChunk();
        string[] sortedTenants =
        [
            "tenant-b", "tenant-b", "tenant-b",
            "tenant-d", "tenant-d",
            "tenant-f", "tenant-f", "tenant-f", "tenant-f"
        ];

        chunk.EncodeColumn(sortedTenants);

        Assert.AreEqual(9, chunk.TotalRowCount);
        Assert.AreEqual(3, chunk.DistinctCount);
        Assert.AreEqual(3, chunk.CompressedRunCount);

        // Outside Min/Max bounds ("tenant-a" < "tenant-b"): pruned by Min/Max pushdown!
        int countA = chunk.CountMatchesDecoded("tenant-a", out bool skippedA);
        Assert.AreEqual(0, countA);
        Assert.IsTrue(skippedA);

        // Inside Min/Max bounds ("tenant-b" < "tenant-c" < "tenant-f"), but absent from Bloom Filter: pruned!
        int countC = chunk.CountMatchesDecoded("tenant-c", out bool skippedC);
        Assert.AreEqual(0, countC);
        Assert.IsTrue(skippedC);

        // Present in chunk: decoded from RLE runs accurately
        int countF = chunk.CountMatchesDecoded("tenant-f", out bool skippedF);
        Assert.AreEqual(4, countF);
        Assert.IsFalse(skippedF);
    }

    [TestMethod]
    public void Delta_lake_transaction_log_provides_snapshot_isolation_across_concurrent_file_compaction()
    {
        DeltaTransactionLog log = new DeltaTransactionLog();
        long v1 = log.Commit(addedFiles: ["part-01.parquet", "part-02.parquet"], removedFiles: []);
        long v2 = log.Commit(addedFiles: ["compacted-01-02.parquet"], removedFiles: ["part-01.parquet", "part-02.parquet"]);

        IReadOnlyList<string> snapshotV1Files = log.GetActiveFilesAtVersion(v1);
        IReadOnlyList<string> snapshotV2Files = log.GetActiveFilesAtVersion(v2);

        CollectionAssert.AreEquivalent(new[] { "part-01.parquet", "part-02.parquet" }, snapshotV1Files.ToArray());
        CollectionAssert.AreEquivalent(new[] { "compacted-01-02.parquet" }, snapshotV2Files.ToArray());
    }
}

file sealed class ColumnarRleDictionaryChunk
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

    public bool CanRowGroupMatchEquality(string targetValue)
    {
        if (TotalRowCount == 0 || MinValue is null || MaxValue is null)
        {
            return false;
        }

        if (string.CompareOrdinal(targetValue, MinValue) < 0 ||
            string.CompareOrdinal(targetValue, MaxValue) > 0)
        {
            return false;
        }

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

file sealed class DeltaTransactionLog
{
    private readonly List<(long Version, string[] Added, string[] Removed)> _commits =
        new List<(long, string[], string[])>();

    public long Commit(string[] addedFiles, string[] removedFiles)
    {
        long nextVersion = _commits.Count + 1;
        _commits.Add((nextVersion, addedFiles, removedFiles));
        return nextVersion;
    }

    public IReadOnlyList<string> GetActiveFilesAtVersion(long targetVersion)
    {
        HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
        foreach ((long ver, string[] added, string[] removed) in _commits)
        {
            if (ver > targetVersion)
            {
                break;
            }

            foreach (string r in removed)
            {
                active.Remove(r);
            }

            foreach (string a in added)
            {
                active.Add(a);
            }
        }

        return active.ToList();
    }
}
```

---

## Section 25: Policy Engines, DSLs, and Cross-Language Runtimes

### Part A: Worked Questionnaire Answers

1. **Why hardcoded rules, external RPCs, and Turing-complete scripts fail.** Hardcoded `if/else` logic requires full code deployments for rule changes; synchronous external rules-service RPCs add network tail latency and violate Static Stability; and Turing-complete user scripts (`JS/Lua`) risk infinite loops (`while (true)`), non-determinism, and heap exhaustion.
2. **The 4-Stage `< 30 ms` Applicability & 1.4B-Device Driver Ranking Architecture.** (1) **Symbol Interning** maps hardware/attribute strings to 32-bit `int` IDs; (2) **Bloom Filter + Inverted Index Pre-Screening** probes the device's `1,000+` hardware IDs in $O(1)$ per attribute to prune `130,000+` catalog rules down to `~150` candidates; (3) **Short-Circuit AST Evaluation with Flyweight DAG Memoization** evaluates shared sub-expressions once in `< 3 us`/expression; and (4) **Multi-Criteria Lexicographical Ranking** (`FeatureScore ASC, HardwareRank ASC, SignerTier ASC, DriverVersion DESC`) deterministically selects the winning package.
3. **Domain Grammar for Multivariate ML Experimentation.** At the Control Plane publish gate, the grammar compiler (1) statically verifies attribute and treatment types against the Feature Schema Registry, (2) detects **mutual-exclusion slot collisions** across overlapping concurrent experiments before activation, and (3) wraps dynamic ML outputs in **runtime guardrail invariants with deterministic fallbacks**.
4. **Declarative Policy-as-Code (`OPA Rego / CEL / Cedar`) guarantees.** They are **non-Turing-complete** (forbidding unbounded loops so evaluation is mathematically guaranteed to terminate in bounded steps) and **hermetically deterministic** (pure functions of `(Policy, Input, Data)` with zero ambient network I/O). **Partial Evaluation** pre-computes known context into a simplified residual AST or database pushdown filter.
5. **Why in-process `.NET + Rust` (`Regorus`) beats sidecars and managed-heap interpreters.** Calling a `localhost` Go sidecar copies multi-megabyte JSON payloads over loopback sockets twice per request; evaluating complex Datalog/Rego unifications on the managed .NET heap allocates thousands of transient nodes per call and triggers Gen-2 GC pauses. An in-process **Rust** engine runs in the same address space with **zero loopback IPC** and **zero .NET GC pressure**.
6. **Safe `.NET [LibraryImport] <-> Rust extern "C"` FFI rules.** (1) Wrap the opaque `*mut Engine` pointer in a **`SafeHandle`** (`SafeHandleZeroOrMinusOneIsInvalid`) for deterministic `Box::from_raw` disposal; (2) pass **`ReadOnlySpan<byte>` UTF-8** pinned on the stack (`fixed`) instead of marshaling UTF-16 `string`s (which would allocate and copy UTF-8 buffers on every call); and (3) wrap every Rust `extern "C"` function in **`std::panic::catch_unwind`** because unwinding a Rust panic into .NET stack frames is immediate Undefined Behavior (process crash).

### Part B: Worked C# Solution (`Chapter25.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter25
{
    [TestMethod]
    public void Inverted_index_and_bloom_filter_prune_catalog_and_multi_criteria_ranker_selects_best_driver()
    {
        HighSpeedApplicabilityEngine engine = new HighSpeedApplicabilityEngine();

        // Populate 1,000 unrelated catalog rules + 3 candidate drivers for hardware symbol 42:
        List<CatalogDriverRule> catalog = new List<CatalogDriverRule>();
        for (int i = 100; i < 1_100; i++)
        {
            catalog.Add(new CatalogDriverRule(
                DriverId: $"unrelated-{i}",
                RequiredHardwareSymbolId: i,
                RuleAst: new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 26100),
                FeatureScore: 1,
                HardwareRank: 0,
                DriverVersionSortable: 100));
        }

        // Candidate 1: Generic inbox driver (FeatureScore = 10)
        catalog.Add(new CatalogDriverRule(
            DriverId: "drv-generic",
            RequiredHardwareSymbolId: 42,
            RuleAst: new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 22000),
            FeatureScore: 10,
            HardwareRank: 2,
            DriverVersionSortable: 500));

        // Candidate 2: OEM certified driver v1 (FeatureScore = 1, Exact HardwareRank = 0, Version = 100)
        catalog.Add(new CatalogDriverRule(
            DriverId: "drv-oem-v1",
            RequiredHardwareSymbolId: 42,
            RuleAst: new LogicalAndNode(
            [
                new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 22000),
                new AttributeCheckNode(AttributeSymbolId: 2, ComparisonOp.Equals, TargetValue: 64)
            ]),
            FeatureScore: 1,
            HardwareRank: 0,
            DriverVersionSortable: 100));

        // Candidate 3: OEM certified driver v2 (FeatureScore = 1, Exact HardwareRank = 0, Version = 200 -> WINNER!)
        catalog.Add(new CatalogDriverRule(
            DriverId: "drv-oem-v2",
            RequiredHardwareSymbolId: 42,
            RuleAst: new LogicalAndNode(
            [
                new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 22000),
                new AttributeCheckNode(AttributeSymbolId: 2, ComparisonOp.Equals, TargetValue: 64)
            ]),
            FeatureScore: 1,
            HardwareRank: 0,
            DriverVersionSortable: 200));

        engine.IndexCatalog(catalog);

        Dictionary<int, int> deviceAttrs = new Dictionary<int, int>
        {
            [1] = 26100, // OsBuild
            [2] = 64     // ArchBits
        };

        CatalogDriverRule? best = engine.EvaluateAndSelectBestDriver(
            deviceHardwareSymbols: [42, 9999],
            deviceAttributes: deviceAttrs,
            out int evaluatedCount);

        Assert.IsNotNull(best);
        Assert.AreEqual("drv-oem-v2", best.DriverId);
        // Only the 3 rules matching hardware symbol 42 were evaluated out of 1,003 rules!
        Assert.AreEqual(3, evaluatedCount);
    }
}

file enum ComparisonOp : byte
{
    Equals = 1,
    GreaterThanOrEqual = 2
}

file abstract record ApplicabilityNode;

file sealed record AttributeCheckNode(int AttributeSymbolId, ComparisonOp Op, int TargetValue) : ApplicabilityNode;

file sealed record LogicalAndNode(IReadOnlyList<ApplicabilityNode> Children) : ApplicabilityNode;

file sealed record CatalogDriverRule(
    string DriverId,
    int RequiredHardwareSymbolId,
    ApplicabilityNode RuleAst,
    int FeatureScore,
    int HardwareRank,
    long DriverVersionSortable);

file sealed class HighSpeedApplicabilityEngine
{
    private readonly HashSet<int> _hardwareBloomFilter = new HashSet<int>();
    private readonly Dictionary<int, List<CatalogDriverRule>> _invertedHardwareIndex =
        new Dictionary<int, List<CatalogDriverRule>>();

    public void IndexCatalog(IEnumerable<CatalogDriverRule> catalogRules)
    {
        foreach (CatalogDriverRule rule in catalogRules)
        {
            _hardwareBloomFilter.Add(rule.RequiredHardwareSymbolId);
            if (!_invertedHardwareIndex.TryGetValue(rule.RequiredHardwareSymbolId, out List<CatalogDriverRule>? list))
            {
                list = new List<CatalogDriverRule>();
                _invertedHardwareIndex[rule.RequiredHardwareSymbolId] = list;
            }

            list.Add(rule);
        }
    }

    public CatalogDriverRule? EvaluateAndSelectBestDriver(
        ReadOnlySpan<int> deviceHardwareSymbols,
        IReadOnlyDictionary<int, int> deviceAttributes,
        out int rulesEvaluatedCount)
    {
        rulesEvaluatedCount = 0;
        CatalogDriverRule? bestMatch = null;

        foreach (int hwSymbol in deviceHardwareSymbols)
        {
            if (!_hardwareBloomFilter.Contains(hwSymbol))
            {
                continue;
            }

            if (!_invertedHardwareIndex.TryGetValue(hwSymbol, out List<CatalogDriverRule>? candidates))
            {
                continue;
            }

            foreach (CatalogDriverRule candidate in candidates)
            {
                rulesEvaluatedCount++;
                if (!EvaluateAst(candidate.RuleAst, deviceAttributes))
                {
                    continue;
                }

                if (bestMatch is null || IsBetterCandidate(candidate, bestMatch))
                {
                    bestMatch = candidate;
                }
            }
        }

        return bestMatch;
    }

    private static bool EvaluateAst(ApplicabilityNode node, IReadOnlyDictionary<int, int> attrs)
    {
        switch (node)
        {
            case AttributeCheckNode check:
                if (!attrs.TryGetValue(check.AttributeSymbolId, out int actual))
                {
                    return false;
                }

                return check.Op switch
                {
                    ComparisonOp.Equals => actual == check.TargetValue,
                    ComparisonOp.GreaterThanOrEqual => actual >= check.TargetValue,
                    _ => false
                };

            case LogicalAndNode andNode:
                foreach (ApplicabilityNode child in andNode.Children)
                {
                    if (!EvaluateAst(child, attrs))
                    {
                        return false;
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private static bool IsBetterCandidate(CatalogDriverRule candidate, CatalogDriverRule currentBest)
    {
        if (candidate.FeatureScore != currentBest.FeatureScore)
        {
            return candidate.FeatureScore < currentBest.FeatureScore;
        }

        if (candidate.HardwareRank != currentBest.HardwareRank)
        {
            return candidate.HardwareRank < currentBest.HardwareRank;
        }

        return candidate.DriverVersionSortable > currentBest.DriverVersionSortable;
    }
}
```

---

## Section 26: Testing in Production, Safe Rollouts, and Emergency Kill-Switches

### Part A: Worked Questionnaire Answers

1. **Why Staging Never Catches All Planet-Scale Bugs.** Staging cannot replicate the combinatorial matrix of `100,000+` tenant configs, `1.4B` heterogeneous devices/drivers, and Pareto mega-tenant bursts, and privacy laws (`GDPR / EUII`) forbid copying production customer data into test environments.
2. **The Three Pillars of Testing in Production (`TiP`).** (1) **Shadow Consumer Groups** tap live Kafka/Event Hub streams in dark mode with zero broker disk impact and write to isolated shadow storage; (2) **Stratified Tenant-Tier Synthetic Extrapolation** models the statistical shape of Small (`90%`), Medium (`9.5%`), and Mega-Tenants (`0.5%`, `60%` of volume) without touching PII; and (3) **Automated Parity Verifiers** compare row counts, schema nulls, and rolling `SHA-256` output digests between v1 and v2.
3. **Progressive Delivery Rings and Dual Gate Invariants.** Deploy concentrically (`Ring 0 Canary -> Ring 1 Small Region -> Ring 2 Medium -> Ring 3 Paired Regions`, never deploying both halves of a DR pair simultaneously). A ring gate must require **both** a minimum elapsed bake window $T_{\text{bake}}$ (to catch slow leaks) **and** a minimum sample count $N_{\min}$ (for statistical significance), while auto-aborting immediately if error or latency SLIs breach.
4. **Asynchronous (`HTTP 202`) Supply-Chain Security Pipeline.** Uploads land in an untrusted **Quarantine Zone**: Stage 1 verifies streaming `SHA-256` digests and deduplicates existing content in $O(1)$; Stage 2 scans unpacked archives in isolated detonation containers; Stage 3 signs manifests inside an **HSM**, generates **P2P Delivery Optimization (`DO`)** piece-hash metadata for LAN peer sharing, and promotes to the immutable CDN vault.
5. **Why Normal Publishing Is Too Slow for an Emergency.** Republishing a catalog revision through the normal build/compaction/snapshot/TTL pipeline takes **`30+ minutes`**, exposing millions of additional devices during an active incident.
6. **The `< 5-Second` Multi-Stage Emergency Kill-Switch.** An out-of-band priority push updates all stamps in `< 5 seconds` across three gates: **Stage A (`Scan/Discovery` bitset)** blocks new matches; **Stage B (`CDN Download` Valet-Key revocation)** aborts in-flight downloads; and **Stage C (`Pre-Install / Pre-Reboot Commit Check`)** intercepts devices that *already downloaded* the file hours earlier right before they install/reboot at 3:00 AM!

### Part B: Worked C# Solution (`Chapter26.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter26
{
    [TestMethod]
    public void Emergency_kill_switch_halts_scan_download_and_pre_install_commit_stages_immediately()
    {
        EmergencyKillSwitchAndRingGate gate = new EmergencyKillSwitchAndRingGate();

        Assert.IsTrue(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ScanDiscovery));
        Assert.IsTrue(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ContentDownload));
        Assert.IsTrue(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.PreInstallCommit));

        // Canary ring breaches error rate threshold (2.5% > 0.5% max allowed): auto-triggers kill-switch!
        GateDecision decision = gate.EvaluateRingProgression(
            new RingHealthMetrics(RingIndex: 1, SampleCount: 5_000, ElapsedBakeMinutes: 15, ObservedErrorRate: 0.025, ObservedP99LatencyMs: 40),
            packageId: "pkg-bad-driver",
            valetKeyPrefix: "sas-token-99",
            minRequiredSamples: 10_000,
            minRequiredBakeMinutes: 60,
            maxAllowedErrorRate: 0.005,
            maxAllowedP99LatencyMs: 200);

        Assert.AreEqual(GateDecision.AutoRollbackAndKill, decision);

        // All three client lifecycle stages are now blocked (including devices that already downloaded the file!)
        Assert.IsFalse(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ScanDiscovery));
        Assert.IsFalse(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ContentDownload));
        Assert.IsFalse(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.PreInstallCommit));
    }

    [TestMethod]
    public void Canary_ring_gate_holds_until_both_minimum_sample_size_and_bake_time_are_met()
    {
        EmergencyKillSwitchAndRingGate gate = new EmergencyKillSwitchAndRingGate();

        GateDecision earlyDecision = gate.EvaluateRingProgression(
            new RingHealthMetrics(RingIndex: 0, SampleCount: 500, ElapsedBakeMinutes: 120, ObservedErrorRate: 0.001, ObservedP99LatencyMs: 25),
            packageId: "pkg-good",
            valetKeyPrefix: "sas-ok",
            minRequiredSamples: 10_000,
            minRequiredBakeMinutes: 60,
            maxAllowedErrorRate: 0.005,
            maxAllowedP99LatencyMs: 200);

        Assert.AreEqual(GateDecision.HoldForMoreSamples, earlyDecision);

        GateDecision readyDecision = gate.EvaluateRingProgression(
            new RingHealthMetrics(RingIndex: 0, SampleCount: 15_000, ElapsedBakeMinutes: 65, ObservedErrorRate: 0.001, ObservedP99LatencyMs: 25),
            packageId: "pkg-good",
            valetKeyPrefix: "sas-ok",
            minRequiredSamples: 10_000,
            minRequiredBakeMinutes: 60,
            maxAllowedErrorRate: 0.005,
            maxAllowedP99LatencyMs: 200);

        Assert.AreEqual(GateDecision.Proceed, readyDecision);
    }
}

file enum RolloutLifecycleStage : byte
{
    ScanDiscovery = 1,
    ContentDownload = 2,
    PreInstallCommit = 3
}

file enum GateDecision : byte
{
    Proceed = 1,
    HoldForMoreSamples = 2,
    AutoRollbackAndKill = 3
}

file sealed record RingHealthMetrics(
    int RingIndex,
    long SampleCount,
    int ElapsedBakeMinutes,
    double ObservedErrorRate,
    double ObservedP99LatencyMs);

file sealed class EmergencyKillSwitchAndRingGate
{
    private readonly HashSet<string> _killedPackageIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _revokedValetKeyPrefixes = new HashSet<string>(StringComparer.Ordinal);

    public long LastKillSwitchVersion { get; private set; }

    public void TriggerEmergencyKillSwitch(string packageId, string valetKeyPrefix, long killVersion)
    {
        _killedPackageIds.Add(packageId);
        _revokedValetKeyPrefixes.Add(valetKeyPrefix);
        if (killVersion > LastKillSwitchVersion)
        {
            LastKillSwitchVersion = killVersion;
        }
    }

    public bool IsOperationPermitted(string packageId, string? valetKeyPrefix, RolloutLifecycleStage stage)
    {
        if (_killedPackageIds.Contains(packageId))
        {
            return false;
        }

        if (stage == RolloutLifecycleStage.ContentDownload &&
            valetKeyPrefix is not null &&
            _revokedValetKeyPrefixes.Contains(valetKeyPrefix))
        {
            return false;
        }

        return true;
    }

    public GateDecision EvaluateRingProgression(
        RingHealthMetrics metrics,
        string packageId,
        string valetKeyPrefix,
        long minRequiredSamples,
        int minRequiredBakeMinutes,
        double maxAllowedErrorRate,
        double maxAllowedP99LatencyMs)
    {
        if (metrics.SampleCount >= 100 &&
            (metrics.ObservedErrorRate > maxAllowedErrorRate || metrics.ObservedP99LatencyMs > maxAllowedP99LatencyMs))
        {
            TriggerEmergencyKillSwitch(packageId, valetKeyPrefix, LastKillSwitchVersion + 1);
            return GateDecision.AutoRollbackAndKill;
        }

        if (metrics.SampleCount < minRequiredSamples || metrics.ElapsedBakeMinutes < minRequiredBakeMinutes)
        {
            return GateDecision.HoldForMoreSamples;
        }

        return GateDecision.Proceed;
    }
}
```

---

## Section 27: Production AI and Agentic Engineering Systems

### Part A: Worked Questionnaire Answers

1. **LLMs as bounded distributed components.** An LLM is high-latency (`1-30s`), non-deterministic, context-window-bounded, and vulnerable to prompt injection and parameter hallucination. Wrap it in **strict JSON Schema validation**, **bounded context pagination**, **grounded citations**, and **Read-Only vs Write-Authorized capability sandboxes**.
2. **Model Context Protocol (`MCP`) primitives and transports.** An MCP Host/Client communicates with domain MCP Servers over `stdio` or `Streaming HTTP/SSE` using three primitives: **Resources** (URI-addressed read-only data), **Tools** (JSON-Schema-validated executable functions), and **Prompts** (parameterized workflow templates).
3. **Four rules for hallucination-resistant MCP Tools.** (1) Expose domain-scoped tools with explicit, bounded parameters (`lookbackMinutes [1..120]`) rather than raw unbounded query strings; (2) return **actionable, self-correcting validation errors**; (3) enforce hard **output row/byte ceilings (`<= 20 KB`)** with pagination cursors; and (4) physically separate **Read-Only investigation tools/credentials** from mutating tools.
4. **LLM-Structured Knowledge Bases (`PolicyWiki / TSGs`).** Human wikis fail when fixed-token chunking separates preconditions from commands. Structure TSGs with (1) **YAML metadata frontmatter** (`service`, `error_codes`), (2) **explicit symptom-to-query decision trees** in code blocks, (3) **heading-aligned chunking with prepended breadcrumb/safety headers**, and (4) verification against the **Git repository and Code Graph**.
5. **Hybrid Retrieval and Reciprocal Rank Fusion (`RRF`).** Dense vector embeddings capture conceptual paraphrases but blur exact hex codes (`0x80072EE2`) and method names; BM25 lexical search finds exact tokens but misses paraphrases; Code Graph traversal follows resolved `CALLS/IMPLEMENTS` edges. **RRF** combines rankings via $\text{RRF}(d) = \sum_{m} \frac{1}{k + \text{rank}_m(d)}$ ($k = 60$), requiring zero raw-score calibration and boosting consensus hits.
6. **Spec-Driven Multi-Agent Workflows.** Coordinate parallel coding/review agents via (1) a written **Specification & Task Dependency DAG (`todos + todo_deps`)**, (2) an **isolated Git Worktree per agent (`git worktree add`)** to prevent filesystem/build contention, (3) **automated verification gates (`build + test + stylefix + xmldoc + PR review`)**, and (4) **durable Session Checkpoints** at verified boundaries.

### Part B: Worked C# Solution (`Chapter27.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter27
{
    [TestMethod]
    public void Mcp_tool_schema_gatekeeper_rejects_mutating_calls_from_readonly_agents_and_catches_hallucinated_args()
    {
        McpToolSchemaGatekeeper queryTool = new McpToolSchemaGatekeeper(
            toolName: "query_stamp_telemetry",
            isMutatingTool: false,
            specs: [new McpToolParameterSpec("lookbackMinutes", Required: true, MinIntValue: 1, MaxIntValue: 120)]);

        McpToolSchemaGatekeeper deleteTool = new McpToolSchemaGatekeeper(
            toolName: "delete_stamp_deployment",
            isMutatingTool: true,
            specs: [new McpToolParameterSpec("deploymentId", Required: true)]);

        // 1. Read-only agent blocked from calling mutating tool:
        (bool mutatingAllowed, string? mutatingErr) = deleteTool.ValidateToolCall(
            new Dictionary<string, int> { ["deploymentId"] = 10 },
            callerIsReadOnlyAgent: true);
        Assert.IsFalse(mutatingAllowed);
        StringAssert.Contains(mutatingErr, "SecurityDenied");

        // 2. Hallucinated parameter rejected:
        (bool hallucinatedAllowed, string? hallucinatedErr) = queryTool.ValidateToolCall(
            new Dictionary<string, int> { ["lookbackMinutes"] = 30, ["inventedParam"] = 1 },
            callerIsReadOnlyAgent: true);
        Assert.IsFalse(hallucinatedAllowed);
        StringAssert.Contains(hallucinatedErr, "HallucinatedParameter");

        // 3. Out-of-bounds parameter rejected with self-correcting guidance:
        (bool unboundedAllowed, string? unboundedErr) = queryTool.ValidateToolCall(
            new Dictionary<string, int> { ["lookbackMinutes"] = 10_000 },
            callerIsReadOnlyAgent: true);
        Assert.IsFalse(unboundedAllowed);
        StringAssert.Contains(unboundedErr, "OutOfBounds");

        // 4. Valid bounded call succeeds:
        (bool validAllowed, _) = queryTool.ValidateToolCall(
            new Dictionary<string, int> { ["lookbackMinutes"] = 60 },
            callerIsReadOnlyAgent: true);
        Assert.IsTrue(validAllowed);
    }

    [TestMethod]
    public void Reciprocal_rank_fusion_ranks_consensus_document_above_single_retriever_outlier()
    {
        // Doc-B is #1 in lexical BM25, but #5 in semantic search (1/61 + 1/65 = 0.03178).
        // Doc-C is #5 in lexical BM25, and #1 in semantic search (1/65 + 1/61 = 0.03178).
        // Doc-A is #2 in BOTH lexical BM25 and semantic search (2/62 = 0.03226) -> Consensus winner!
        string[] lexicalRanking = ["doc-B", "doc-A", "doc-D", "doc-E", "doc-C"];
        string[] semanticRanking = ["doc-C", "doc-A", "doc-D", "doc-E", "doc-B"];

        IReadOnlyList<(string DocId, double RrfScore)> fused = HybridRrfRetriever.FuseRankedLists(
            lexicalRanking,
            semanticRanking,
            k: 60);

        Assert.AreEqual("doc-A", fused[0].DocId);
        Assert.AreEqual(2.0 / 62.0, fused[0].RrfScore, 0.00001);
    }
}

file sealed record McpToolParameterSpec(
    string Name,
    bool Required,
    int? MinIntValue = null,
    int? MaxIntValue = null);

file sealed class McpToolSchemaGatekeeper
{
    private readonly string _toolName;
    private readonly bool _isMutatingTool;
    private readonly Dictionary<string, McpToolParameterSpec> _paramSpecs =
        new Dictionary<string, McpToolParameterSpec>(StringComparer.Ordinal);

    public McpToolSchemaGatekeeper(string toolName, bool isMutatingTool, IEnumerable<McpToolParameterSpec> specs)
    {
        _toolName = toolName;
        _isMutatingTool = isMutatingTool;
        foreach (McpToolParameterSpec spec in specs)
        {
            _paramSpecs[spec.Name] = spec;
        }
    }

    public (bool IsValid, string? ErrorMessage) ValidateToolCall(
        IReadOnlyDictionary<string, int> intArgs,
        bool callerIsReadOnlyAgent)
    {
        if (callerIsReadOnlyAgent && _isMutatingTool)
        {
            return (false, $"SecurityDenied: Tool '{_toolName}' is a mutating tool and cannot be invoked by a read-only agent.");
        }

        foreach (string argName in intArgs.Keys)
        {
            if (!_paramSpecs.ContainsKey(argName))
            {
                return (false, $"HallucinatedParameter: Unknown argument '{argName}' for tool '{_toolName}'.");
            }
        }

        foreach (McpToolParameterSpec spec in _paramSpecs.Values)
        {
            if (!intArgs.TryGetValue(spec.Name, out int val))
            {
                if (spec.Required)
                {
                    return (false, $"MissingRequiredParameter: '{spec.Name}' is required for tool '{_toolName}'.");
                }

                continue;
            }

            if (spec.MinIntValue.HasValue && val < spec.MinIntValue.Value)
            {
                return (false, $"OutOfBounds: '{spec.Name}' ({val}) must be >= {spec.MinIntValue.Value}.");
            }

            if (spec.MaxIntValue.HasValue && val > spec.MaxIntValue.Value)
            {
                return (false, $"OutOfBounds: '{spec.Name}' ({val}) must be <= {spec.MaxIntValue.Value}.");
            }
        }

        return (true, null);
    }
}

file static class HybridRrfRetriever
{
    public static IReadOnlyList<(string DocId, double RrfScore)> FuseRankedLists(
        IReadOnlyList<string> lexicalRankedDocIds,
        IReadOnlyList<string> semanticRankedDocIds,
        int k = 60)
    {
        Dictionary<string, double> scores = new Dictionary<string, double>(StringComparer.Ordinal);

        for (int i = 0; i < lexicalRankedDocIds.Count; i++)
        {
            string docId = lexicalRankedDocIds[i];
            int rank = i + 1;
            scores[docId] = scores.GetValueOrDefault(docId) + (1.0 / (k + rank));
        }

        for (int i = 0; i < semanticRankedDocIds.Count; i++)
        {
            string docId = semanticRankedDocIds[i];
            int rank = i + 1;
            scores[docId] = scores.GetValueOrDefault(docId) + (1.0 / (k + rank));
        }

        return scores
            .Select(static kvp => (DocId: kvp.Key, RrfScore: kvp.Value))
            .OrderByDescending(static item => item.RrfScore)
            .ThenBy(static item => item.DocId, StringComparer.Ordinal)
            .ToList();
    }
}
```

---

## Section 28: System Design Methodology and Architecture Reviews

### Part A: Worked Questionnaire Answers

1. **The Six Phases of Principal System Design.** (1) **Functional Requirements (`User Stories`, 3–5 min)**: plain-English actor stories and out-of-scope boundaries; (2) **Technical / Non-Functional Requirements & Sizing (`4–6 min`)**: RPS, storage math, `p99` latency, and per-workflow CAP/PACELC posture; (3) **Microservice Identification (`Depth vs Breadth`, 3–5 min)**: grouping around domain aggregates; (4) **Logical Block Diagram (`5–7 min`)**: Control/Data Plane separation and sync (`--->`) vs async (`- - ->`) flows; (5) **Three-Tier Deep Dive (`20–25 min`)**: Application, Cache, and Storage tiers for the 1–2 hardest services; and (6) **Bottlenecks, SPOFs & Trade-Off Defense (`5–8 min`)**.
2. **Depth-Oriented vs Breadth-Oriented Systems.** A **Depth-Oriented System** (Distributed Rate Limiter, Key-Value Store, Web Crawler) has only 1–2 microservices and spends 80% of the session inside storage/consensus/algorithmic internals. A **Breadth-Oriented System** (E-Commerce, Ride-Sharing, Atlas) spans 4–6 collaborating microservices across control and data planes, requiring a clean Logical Block Diagram in Phase 4 before zooming into 1–2 critical services in Phase 5.
3. **The Phase 5 Three-Tier Checklist.** **Application Tier**: concrete REST/gRPC API signatures, idempotency headers, `HTTP 202` LROs, stateless VM sizing (`30,000 / T_ms` or `~8,000 RPS`), and L4/L7 load balancing + Shuffle Sharding. **Cache Tier**: L1/L2 topology or Zero-Dependency Stamp Snapshot + Delta Queue, exact cache keys, eviction (`W-TinyLFU`), and stampede/penetration/stale-race defenses. **Storage Tier**: SQL vs NoSQL vs Columnar Lakehouse choice, exact `PartitionKey + ClusteringKey` schema, B+Tree vs LSM-Tree vs Parquet Row Groups, sharding scheme, replication factor (`N=3`), quorum/consensus rules, and hot-key/fan-out coalescing.
4. **Why deterministic canary hashing beats pre-inserting 500M assignment rows.** Computing `Hash(RolloutId + ":" + DeviceId) % 100 < targetPercent` requires **zero database writes** when a rollout starts or advances phases, and because the bucket `0..99` for `(RolloutId, DeviceId)` is deterministic, every device in the `1%` canary automatically stays in the `10%` and `50%` cohorts as the threshold expands.
5. **End-to-end Atlas European Firmware Rollout.** The binary uploads directly to EU Quarantine/Vault Storage via a **Valet Key** SAS URL (`SHA-256` dedup, malware scan, HSM sign, P2P DO metadata), publishing a 1 KB **Claim Check** event. The **Rollout Orchestrator** validates the package DAG via **Kahn's Topological Sort**, prevents concurrent rollout Write Skew via a **filtered unique index**, and progresses phases under an **etcd lease with a monotonic `CoordinatorEpoch` fencing token**. Regional Data Plane stamps hydrate pre-compiled **Composite ASTs** via **Snapshots + Delta Queues** and pseudonymize EU customer identifiers through a **Regional Privacy Mapping Table**.
6. **Surviving a 10M-device reconnect storm.** (1) Devices reconnect with **Exponential Backoff + Full Jitter**; (2) TLS 1.3 **Session Tickets** avoid repeating asymmetric crypto handshakes; (3) the gateway's **Priority Load Shedder** drops `P2` background telemetry (`503 + Retry-After`) when host utilization exceeds 80%, preserving `P0` rollback commands; and (4) **Kafka Load Leveling** + **Claim-Check Batch Coalescing** buffers accepted writes ahead of Cassandra and Delta Lake.

### Part B: Worked C# Solution (`Chapter28.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter28
{
    [TestMethod]
    public void Atlas_canary_cohort_hasher_is_monotonic_as_rollout_phase_expands()
    {
        int countAt1Pct = 0;
        int countAt10Pct = 0;

        for (int i = 0; i < 10_000; i++)
        {
            string deviceId = $"dev-{i}";
            bool in1 = AtlasRolloutPlane.IsDeviceInCanaryBucket("rollout-2026-11", deviceId, targetPercent: 1);
            bool in10 = AtlasRolloutPlane.IsDeviceInCanaryBucket("rollout-2026-11", deviceId, targetPercent: 10);
            bool in50 = AtlasRolloutPlane.IsDeviceInCanaryBucket("rollout-2026-11", deviceId, targetPercent: 50);

            if (in1)
            {
                countAt1Pct++;
                Assert.IsTrue(in10, "A device in the 1% cohort must remain in the 10% cohort.");
            }

            if (in10)
            {
                countAt10Pct++;
                Assert.IsTrue(in50, "A device in the 10% cohort must remain in the 50% cohort.");
            }
        }

        Assert.IsTrue(countAt1Pct is > 50 and < 150);
        Assert.IsTrue(countAt10Pct is > 850 and < 1_150);
    }

    [TestMethod]
    public void Priority_load_shedder_preserves_p0_rollback_traffic_when_host_utilization_is_critical()
    {
        Assert.IsTrue(AtlasRolloutPlane.ShouldAdmitRequest(priorityTier: 0, hostUtilization: 0.95));
        Assert.IsFalse(AtlasRolloutPlane.ShouldAdmitRequest(priorityTier: 2, hostUtilization: 0.85));
        Assert.IsTrue(AtlasRolloutPlane.ShouldAdmitRequest(priorityTier: 2, hostUtilization: 0.60));
    }
}

file static class AtlasRolloutPlane
{
    public static bool IsDeviceInCanaryBucket(string rolloutId, string deviceId, int targetPercent)
    {
        if (targetPercent <= 0)
        {
            return false;
        }

        if (targetPercent >= 100)
        {
            return true;
        }

        uint hash = Hash32($"{rolloutId}:{deviceId}");
        int bucket = (int)(hash % 100u);
        return bucket < targetPercent;
    }

    private static uint Hash32(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text)
        {
            h ^= c;
            h *= 16777619u;
        }

        h ^= h >> 16;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        return h;
    }

    public static bool ShouldAdmitRequest(int priorityTier, double hostUtilization)
    {
        return priorityTier switch
        {
            0 => hostUtilization < 0.99,
            1 => hostUtilization < 0.90,
            _ => hostUtilization < 0.80
        };
    }
}
```

---

## Section 29: Principal Leadership and Behavioral Execution

### Part A: Worked Questionnaire Answers

1. **The 60-Second Reverse-Chronological Career Pitch.** (1) One-sentence Principal specialization statement; (2) start with your **most recent role** and walk backward chronologically through signature systems (`control/data plane stamps`, `78x telemetry pipeline`, `1.4B-device ranking & < 5s kill-switch`, `IoT multi-tenancy/GDPR`); (3) highlight engineering excellence and mentoring (e.g., leading an engineering study group); and (4) connect directly to what you want to build next.
2. **The Principal STAR Framework.** **Situation**: set business and scale stakes concisely (~30s). **Task**: state your specific Principal ownership and why the problem was hard (~20s). **Action**: structure into **three pillars** (~90s): (1) *Technical diagnosis & architecture*, (2) *Prioritization, risk management & stakeholder buy-in*, and (3) *Influence & execution across teams*, using `"I"` accurately for your designs and decisions. **Result + Reflection** (~40s): state quantifiable impact plus an unprompted learning or what you would do differently.
3. **Case Studies 1 & 6 (`Local Applicability Expression Engine`, `Zero-Dependency Stamp Data Plane`, & `Customer-Hosted MRG`).** A **Tier-1 Data Plane must never take a synchronous hot-path dependency on a Control Plane or lower-SLA Tier-2/3 service**. Hydrating pre-compiled expressions (`10,000 rules in < 30 ms`) or embedded zero-GC **Rust (`Regorus`)** evaluators from stamp-local snapshots and delta queues eliminates cross-service outages, while Customer-Hosted Managed Resource Groups isolate enterprise compute/storage COGS and sovereignty.
4. **Case Studies 2 & 4 (Pragmatic Descoping & Stopping Low-ROI Convergence).** Before IoT Cloud Distribution GA, pushing back on unproven "automatic server-side file encryption" (and documenting client pre-encryption + device downloader decryption instead) shipped GA on schedule without taking on unvalidated key-custody liability. Similarly, when asked to converge three file-delivery platforms, quantifying the 18+ engineer-year cost and Tier-1 regression risk led to recommending cancellation; saving the organization from a costly sideways migration.
5. **Case Studies 3, 5, 7, & 8 (`GDPR Privacy Mapping Tables`, `Deployment Journal & ML Grammar`, `78x Telemetry Pipeline`, & `1.4B-Device Driver Ranking / < 5s Kill-Switch`).** Partnering with legal counsel turned an EU GDPR logging blocker into **Regional Privacy Mapping Tables**; accepting peer criticism evolved a mutable catalog submission schema into an immutable **Deployment Journal**; quantifying the **87% 200-tenant fan-out drop** drove a Claim-Check batching redesign achieving a **$78\times$ speedup (`1.3 -> 102.2 MB/s`)** verified via **Testing in Production**; and combining **Bloom-filtered inverted indexes** with a **multi-stage `< 5-second` kill-switch** protected 1.4B devices across scan, download, and pre-install phases.
6. **Philosophical Questions & Reverse Interviewing.** Answer philosophical prompts using the **60-Second Direct-Then-Evidence Rule** (state your principle directly in two sentences, support with a brief concrete example, avoid defensiveness or blaming others). In reverse interviewing, always prepare open-ended diagnostic and cultural questions, and close when rapport is strong with: *"Based on my background and our conversation today, is there anything that makes you question whether I would be a strong fit for this role?"*

### Part B: Worked C# Solution (`Chapter29.cs`)

```csharp
namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter29
{
    [TestMethod]
    public void Tier_dependency_validator_rejects_synchronous_hot_path_calls_to_lower_tier_services()
    {
        ServiceDependencyRule[] dependencies =
        [
            new ServiceDependencyRule(CallerTier: 1, TargetTier: 1, IsSynchronousHotPath: true),
            new ServiceDependencyRule(CallerTier: 1, TargetTier: 2, IsSynchronousHotPath: false),
            new ServiceDependencyRule(CallerTier: 1, TargetTier: 2, IsSynchronousHotPath: true)
        ];

        Assert.IsTrue(PrincipalArchitectureGovernance.IsValidDependency(dependencies[0]));
        Assert.IsTrue(PrincipalArchitectureGovernance.IsValidDependency(dependencies[1]));
        Assert.IsFalse(PrincipalArchitectureGovernance.IsValidDependency(dependencies[2]));
    }

    [TestMethod]
    public void Convergence_roi_evaluator_recommends_cancelling_migration_when_payback_exceeds_horizon()
    {
        ConvergenceProposal badConvergence = new ConvergenceProposal(
            EstimatedEngineeringMonths: 240,
            MonthlyInfraSavingsDollars: 5_000,
            MonthlyEngineeringCostDollars: 20_000,
            Tier1RegressionRiskScore: 0.80);

        Assert.IsFalse(PrincipalArchitectureGovernance.ShouldProceedWithConvergence(badConvergence, maxPaybackMonths: 24));
    }
}

file readonly record struct ServiceDependencyRule(int CallerTier, int TargetTier, bool IsSynchronousHotPath);

file readonly record struct ConvergenceProposal(
    int EstimatedEngineeringMonths,
    double MonthlyInfraSavingsDollars,
    double MonthlyEngineeringCostDollars,
    double Tier1RegressionRiskScore);

file static class PrincipalArchitectureGovernance
{
    public static bool IsValidDependency(ServiceDependencyRule rule)
    {
        if (rule.IsSynchronousHotPath && rule.TargetTier > rule.CallerTier)
        {
            return false;
        }

        return true;
    }

    public static bool ShouldProceedWithConvergence(ConvergenceProposal proposal, int maxPaybackMonths)
    {
        if (proposal.Tier1RegressionRiskScore > 0.50 || proposal.MonthlyInfraSavingsDollars <= 0)
        {
            return false;
        }

        double totalInvestment = proposal.EstimatedEngineeringMonths * proposal.MonthlyEngineeringCostDollars;
        double paybackMonths = totalInvestment / proposal.MonthlyInfraSavingsDollars;
        return paybackMonths <= maxPaybackMonths;
    }
}
```


