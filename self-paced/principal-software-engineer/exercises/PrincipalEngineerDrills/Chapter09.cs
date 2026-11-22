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
