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

        // Hop 1: Chubby file -> Root Tablet; Hop 2: Root Tablet -> METADATA Tablet; Hop 3: User Tablet location
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
