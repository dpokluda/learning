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
