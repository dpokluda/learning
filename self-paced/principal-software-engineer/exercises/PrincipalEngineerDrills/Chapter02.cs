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
