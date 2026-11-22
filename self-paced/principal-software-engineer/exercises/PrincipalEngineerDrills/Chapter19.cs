namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter19
{
    [TestMethod]
    public void Avl_tree_maintains_logarithmic_height_on_sorted_insertions_and_finds_inorder_successor()
    {
        AvlTree tree = new AvlTree();
        for (int i = 1; i <= 15; i++)
        {
            tree.Insert(i * 10);
        }

        Assert.IsTrue(tree.IsValidBst());
        Assert.AreEqual(4, tree.Height);
        Assert.AreEqual(80, tree.FindInorderSuccessor(70));
        Assert.IsNull(tree.FindInorderSuccessor(150));
    }

    [TestMethod]
    public void Prefix_trie_supports_autocomplete_and_longest_prefix_match_routing()
    {
        PrefixTrie trie = new PrefixTrie();
        trie.Insert("10.0.");
        trie.Insert("10.0.24.");
        trie.Insert("10.0.24.128");
        trie.Insert("192.168.");

        IReadOnlyList<string> completions = trie.GetWordsWithPrefix("10.0.24.");
        CollectionAssert.AreEqual(new[] { "10.0.24.", "10.0.24.128" }, completions.ToArray());

        string? longestMatch = trie.LongestPrefixMatch("10.0.24.99.1");
        Assert.AreEqual("10.0.24.", longestMatch);
    }

    [TestMethod]
    public void Quad_tree_subdivides_dense_quadrants_and_prunes_non_intersecting_regions_on_range_query()
    {
        QuadTree quadTree = new QuadTree(new BoundingBox(0, 0, 100, 100), leafCapacity: 2);
        quadTree.Insert(new Point2D(10, 10, "gw-1"));
        quadTree.Insert(new Point2D(12, 14, "gw-2"));
        quadTree.Insert(new Point2D(85, 85, "gw-3"));
        quadTree.Insert(new Point2D(11, 11, "gw-4"));

        List<Point2D> found = new List<Point2D>();
        quadTree.QueryRange(new BoundingBox(5, 5, 20, 20), found);

        Assert.HasCount(3, found);
        CollectionAssert.AreEquivalent(new[] { "gw-1", "gw-2", "gw-4" }, found.Select(static p => p.Id).ToArray());
    }

    [TestMethod]
    public void Binary_tree_to_doubly_linked_list_and_boundary_perimeter_and_sibling_links_preserve_order()
    {
        // Build BST:
        //       4
        //     /   \
        //    2     6
        //   / \   / \
        //  1   3 5   7
        AvlNode rootForPerimeter = new AvlNode(4)
        {
            Left = new AvlNode(2) { Left = new AvlNode(1), Right = new AvlNode(3) },
            Right = new AvlNode(6) { Left = new AvlNode(5), Right = new AvlNode(7) }
        };

        IReadOnlyList<int> iterativeInOrder = BinaryTreeAlgorithms.TraverseInOrderIterative(rootForPerimeter);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, iterativeInOrder.ToArray());

        IReadOnlyList<int> perimeter = BinaryTreeAlgorithms.GetTreeBoundaryPerimeter(rootForPerimeter);
        CollectionAssert.AreEqual(new[] { 4, 2, 1, 3, 5, 7, 6 }, perimeter.ToArray());

        AvlNode? dllHead = BinaryTreeAlgorithms.ConvertBinaryTreeToDoublyLinkedList(rootForPerimeter);
        List<int> forward = new List<int>();
        AvlNode? curr = dllHead;
        AvlNode? tail = null;
        while (curr is not null)
        {
            forward.Add(curr.Key);
            tail = curr;
            curr = curr.Right;
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, forward);
        Assert.IsNotNull(tail);
        Assert.AreEqual(6, tail.Left?.Key);

        PrefixTrie sortTrie = new PrefixTrie();
        sortTrie.Insert("delta");
        sortTrie.Insert("alpha");
        sortTrie.Insert("charlie");
        sortTrie.Insert("bravo");
        Assert.AreEqual(4, sortTrie.CountWords());
        CollectionAssert.AreEqual(
            new[] { "alpha", "bravo", "charlie", "delta" },
            PrefixTrie.SortArrayWithTrie(["delta", "alpha", "charlie", "bravo"]).ToArray());
    }
}

file sealed class AvlNode
{
    public int Key { get; }

    public int Height { get; set; } = 1;

    public AvlNode? Left { get; set; }

    public AvlNode? Right { get; set; }

    public AvlNode(int key)
    {
        Key = key;
    }
}

file sealed class AvlTree
{
    private AvlNode? _root;

    public int Height
    {
        get { return GetHeight(_root); }
    }

    public void Insert(int key)
    {
        _root = Insert(_root, key);
    }

    public bool IsValidBst()
    {
        return IsValidBst(_root, long.MinValue, long.MaxValue);
    }

    public int? FindInorderSuccessor(int key)
    {
        AvlNode? curr = _root;
        int? successor = null;

        while (curr is not null)
        {
            if (key < curr.Key)
            {
                successor = curr.Key;
                curr = curr.Left;
            }
            else
            {
                curr = curr.Right;
            }
        }

        return successor;
    }

    private static bool IsValidBst(AvlNode? node, long minAllowed, long maxAllowed)
    {
        if (node is null)
        {
            return true;
        }

        if (node.Key <= minAllowed || node.Key >= maxAllowed)
        {
            return false;
        }

        return IsValidBst(node.Left, minAllowed, node.Key) && IsValidBst(node.Right, node.Key, maxAllowed);
    }

    private static AvlNode Insert(AvlNode? node, int key)
    {
        if (node is null)
        {
            return new AvlNode(key);
        }

        if (key < node.Key)
        {
            node.Left = Insert(node.Left, key);
        }
        else if (key > node.Key)
        {
            node.Right = Insert(node.Right, key);
        }
        else
        {
            return node;
        }

        UpdateHeight(node);
        int balance = GetBalance(node);

        if (balance > 1 && node.Left is not null && key < node.Left.Key)
        {
            return RotateRight(node);
        }

        if (balance < -1 && node.Right is not null && key > node.Right.Key)
        {
            return RotateLeft(node);
        }

        if (balance > 1 && node.Left is not null && key > node.Left.Key)
        {
            node.Left = RotateLeft(node.Left);
            return RotateRight(node);
        }

        if (balance < -1 && node.Right is not null && key < node.Right.Key)
        {
            node.Right = RotateRight(node.Right);
            return RotateLeft(node);
        }

        return node;
    }

    private static int GetHeight(AvlNode? n)
    {
        return n?.Height ?? 0;
    }

    private static int GetBalance(AvlNode? n)
    {
        return n is null ? 0 : GetHeight(n.Left) - GetHeight(n.Right);
    }

    private static void UpdateHeight(AvlNode n)
    {
        n.Height = 1 + Math.Max(GetHeight(n.Left), GetHeight(n.Right));
    }

    private static AvlNode RotateRight(AvlNode y)
    {
        AvlNode x = y.Left!;
        AvlNode? t2 = x.Right;
        x.Right = y;
        y.Left = t2;
        UpdateHeight(y);
        UpdateHeight(x);
        return x;
    }

    private static AvlNode RotateLeft(AvlNode x)
    {
        AvlNode y = x.Right!;
        AvlNode? t2 = y.Left;
        y.Left = x;
        x.Right = t2;
        UpdateHeight(x);
        UpdateHeight(y);
        return y;
    }
}

file sealed class TrieNode
{
    public SortedDictionary<char, TrieNode> Children { get; } = new SortedDictionary<char, TrieNode>();

    public bool IsEndOfWord { get; set; }
}

file sealed class PrefixTrie
{
    private readonly TrieNode _root = new TrieNode();

    public void Insert(string word)
    {
        TrieNode curr = _root;
        foreach (char ch in word)
        {
            if (!curr.Children.TryGetValue(ch, out TrieNode? next))
            {
                next = new TrieNode();
                curr.Children[ch] = next;
            }

            curr = next;
        }

        curr.IsEndOfWord = true;
    }

    public IReadOnlyList<string> GetWordsWithPrefix(string prefix)
    {
        List<string> results = new List<string>();
        TrieNode? curr = _root;
        foreach (char ch in prefix)
        {
            if (!curr.Children.TryGetValue(ch, out curr))
            {
                return results;
            }
        }

        CollectWords(curr, prefix, results);
        return results;
    }

    public string? LongestPrefixMatch(string query)
    {
        TrieNode? curr = _root;
        string? longest = null;
        int len = 0;

        foreach (char ch in query)
        {
            if (!curr.Children.TryGetValue(ch, out curr))
            {
                break;
            }

            len++;
            if (curr.IsEndOfWord)
            {
                longest = query.Substring(0, len);
            }
        }

        return longest;
    }

    public int CountWords()
    {
        return CountWords(_root);
    }

    public static IReadOnlyList<string> SortArrayWithTrie(ReadOnlySpan<string> words)
    {
        PrefixTrie trie = new PrefixTrie();
        foreach (string w in words)
        {
            trie.Insert(w);
        }

        return trie.GetWordsWithPrefix(string.Empty);
    }

    private static int CountWords(TrieNode node)
    {
        int count = node.IsEndOfWord ? 1 : 0;
        foreach (TrieNode child in node.Children.Values)
        {
            count += CountWords(child);
        }

        return count;
    }

    private static void CollectWords(TrieNode node, string currentPrefix, List<string> output)
    {
        if (node.IsEndOfWord)
        {
            output.Add(currentPrefix);
        }

        foreach ((char ch, TrieNode child) in node.Children)
        {
            CollectWords(child, currentPrefix + ch, output);
        }
    }
}

file static class BinaryTreeAlgorithms
{
    public static IReadOnlyList<int> TraverseInOrderIterative(AvlNode? root)
    {
        List<int> result = new List<int>();
        Stack<AvlNode> stack = new Stack<AvlNode>();
        AvlNode? curr = root;

        while (curr is not null || stack.Count > 0)
        {
            while (curr is not null)
            {
                stack.Push(curr);
                curr = curr.Left;
            }

            AvlNode popped = stack.Pop();
            result.Add(popped.Key);
            curr = popped.Right;
        }

        return result;
    }

    public static AvlNode? ConvertBinaryTreeToDoublyLinkedList(AvlNode? root)
    {
        AvlNode? head = null;
        AvlNode? prev = null;

        void StitchInOrder(AvlNode? node)
        {
            if (node is null)
            {
                return;
            }

            StitchInOrder(node.Left);
            if (prev is null)
            {
                head = node;
            }
            else
            {
                prev.Right = node;
                node.Left = prev;
            }

            prev = node;
            StitchInOrder(node.Right);
        }

        StitchInOrder(root);
        return head;
    }

    public static IReadOnlyList<int> GetTreeBoundaryPerimeter(AvlNode? root)
    {
        List<int> perimeter = new List<int>();
        if (root is null)
        {
            return perimeter;
        }

        static bool IsLeaf(AvlNode n) => n.Left is null && n.Right is null;

        perimeter.Add(root.Key);
        if (IsLeaf(root))
        {
            return perimeter;
        }

        AvlNode? curr = root.Left;
        while (curr is not null)
        {
            if (!IsLeaf(curr))
            {
                perimeter.Add(curr.Key);
            }

            curr = curr.Left ?? curr.Right;
        }

        void CollectLeaves(AvlNode? n)
        {
            if (n is null)
            {
                return;
            }

            if (IsLeaf(n))
            {
                perimeter.Add(n.Key);
                return;
            }

            CollectLeaves(n.Left);
            CollectLeaves(n.Right);
        }

        CollectLeaves(root);

        Stack<int> rightBoundary = new Stack<int>();
        curr = root.Right;
        while (curr is not null)
        {
            if (!IsLeaf(curr))
            {
                rightBoundary.Push(curr.Key);
            }

            curr = curr.Right ?? curr.Left;
        }

        while (rightBoundary.Count > 0)
        {
            perimeter.Add(rightBoundary.Pop());
        }

        return perimeter;
    }
}

file readonly record struct Point2D(double X, double Y, string Id);

file readonly record struct BoundingBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool Contains(Point2D p)
    {
        return p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }

    public bool Intersects(BoundingBox other)
    {
        return !(other.MinX > MaxX || other.MaxX < MinX || other.MinY > MaxY || other.MaxY < MinY);
    }
}

file sealed class QuadTree
{
    private readonly BoundingBox _boundary;
    private readonly int _capacity;
    private readonly List<Point2D> _points = new List<Point2D>();
    private QuadTree[]? _children;

    public QuadTree(BoundingBox boundary, int leafCapacity)
    {
        _boundary = boundary;
        _capacity = leafCapacity;
    }

    public bool Insert(Point2D p)
    {
        if (!_boundary.Contains(p))
        {
            return false;
        }

        if (_points.Count < _capacity && _children is null)
        {
            _points.Add(p);
            return true;
        }

        if (_children is null)
        {
            Subdivide();
        }

        foreach (QuadTree child in _children!)
        {
            if (child.Insert(p))
            {
                return true;
            }
        }

        return false;
    }

    public void QueryRange(BoundingBox range, List<Point2D> matches)
    {
        if (!_boundary.Intersects(range))
        {
            return;
        }

        foreach (Point2D p in _points)
        {
            if (range.Contains(p))
            {
                matches.Add(p);
            }
        }

        if (_children is not null)
        {
            foreach (QuadTree child in _children)
            {
                child.QueryRange(range, matches);
            }
        }
    }

    private void Subdivide()
    {
        double midX = (_boundary.MinX + _boundary.MaxX) / 2.0;
        double midY = (_boundary.MinY + _boundary.MaxY) / 2.0;

        _children =
        [
            new QuadTree(new BoundingBox(_boundary.MinX, midY, midX, _boundary.MaxY), _capacity),
            new QuadTree(new BoundingBox(midX, midY, _boundary.MaxX, _boundary.MaxY), _capacity),
            new QuadTree(new BoundingBox(_boundary.MinX, _boundary.MinY, midX, midY), _capacity),
            new QuadTree(new BoundingBox(midX, _boundary.MinY, _boundary.MaxX, midY), _capacity)
        ];
    }
}
