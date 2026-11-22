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
