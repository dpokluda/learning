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

        // Leader 1 pauses for 1,500ms (GC pause); Leader 2 acquires lease at t = 1,200ms
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
