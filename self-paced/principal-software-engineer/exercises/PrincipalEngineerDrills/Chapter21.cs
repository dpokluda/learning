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
