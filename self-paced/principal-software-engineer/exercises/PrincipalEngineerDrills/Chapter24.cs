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
