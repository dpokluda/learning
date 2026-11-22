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
