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
            List<MerkleNode> nextLevel = new List<MerkleNode>((currentLevel.Count + 1) / 2);
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
