# 19: Trees, Tries, and Spatial Indexing

Hierarchical data structures are the workhorses of systems engineering.
In Module 06 we saw how disk-based **B+Trees** and in-memory **LSM-Tree MemTables** power storage engines, and in Module 14 we saw how **Composite ASTs** evaluate boolean applicability rules.
At the Principal level, you need both a command of **in-memory tree structures and their balancing invariants** (`Binary Search Trees`, `AVL Trees`, `Red-Black Trees`, `Depth-First` and `Breadth-First` traversals) and a command of the specialized trees that solve **prefix matching** (`Tries` and `Radix/Patricia Trees` for autocomplete and IP routing) and **multi-dimensional spatial queries** (`Quad-Trees`, `Geohashes`, and `R-Trees` for geospatial proximity and regional partitioning).
This module connects the data-structure mechanics to their algorithmic patterns and production system applications.

> **Prerequisite:** [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md) and [Module 18: Capacity Planning and Asymptotic Analysis](./18-capacity-planning-and-asymptotic-analysis.md).

---

## 1. Binary Search Trees (BSTs), AVL rotations, and Red-Black invariants

A **Tree** is a connected, acyclic undirected graph rooted at a single **Root** node (at `depth = 0`), where every other node has exactly one **Parent** and zero or more **Children** (nodes with zero children are **Leaf** nodes; nodes with at least one child are **Internal** nodes)[^skiena-adm].
The **Height** $H$ of a tree is the number of edges on the longest path from the Root to a Leaf.

In a **Binary Search Tree (BST)**, every node has at most two children (`Left` and `Right`) and satisfies the **BST Ordering Invariant**:
> For every node $X$, all keys in $X.\text{Left}$ are strictly less than $X.\text{Key}$ (or $\le$ by convention), and all keys in $X.\text{Right}$ are strictly greater than $X.\text{Key}$.

Because of the BST Ordering Invariant, searching, inserting, or deleting a key walks a single root-to-leaf path in **$O(H)$ time**.
If a BST of $N$ nodes is reasonably balanced, its height is $H \approx \log_2 N$, so all operations take $O(\log N)$ time.
However, if you insert keys into an ordinary unbalanced BST in **sorted or nearly sorted order** (such as monotonically increasing timestamps or sequential IDs `1, 2, 3, ..., N`), every new key lands as the right child of the previous node!
The tree degenerates into a singly linked list of height $H = N - 1$, and every lookup and insert degrades from $O(\log N)$ (`20 steps` for $10^6$ keys) to **$O(N)$ (`1,000,000 steps`)**.

To guarantee $H = O(\log N)$ in the worst case regardless of insertion order, production libraries use **Self-Balancing Binary Search Trees**:

### 1. AVL Trees (Adelson-Velsky and Landis, 1962)

An **AVL Tree** enforces a strict height-balance invariant on every node $X$[^skiena-adm][^sedgewick-alg]:

$$
\text{BalanceFactor}(X) = \text{Height}(X.\text{Left}) - \text{Height}(X.\text{Right}) \in \{-1, 0, +1\}
$$

Whenever an insertion or deletion causes any ancestor node's balance factor to become `+2` (left-heavy) or `-2` (right-heavy), the tree restores balance locally in $O(1)$ pointer reassignments using one of **four rotation cases**:

```
1. Left-Left (LL) Imbalance -> Single Right Rotation:     2. Left-Right (LR) Imbalance -> Left-then-Right Rotation:
        Z (+2)                  Y (0)                             Z (+2)               Z (+2)              X (0)
       /                       /     \                           /                    /                   /     \
      Y (+1)       ===>       X       Z                         Y (-1)     ===>      X (+1)    ===>      Y       Z
     /                                                           \                  /
    X                                                             X                Y
```

- **Left-Left (`LL`, `BF = +2` and left child `BF >= 0`)**: Fixed by a single **Right Rotation** around `Z`.
- **Right-Right (`RR`, `BF = -2` and right child `BF <= 0`)**: Fixed by a single **Left Rotation** around `Z`.
- **Left-Right (`LR`, `BF = +2` and left child `BF = -1`)**: First perform a **Left Rotation** on the left child `Y` (reducing it to the `LL` case), then perform a **Right Rotation** on `Z`.
- **Right-Left (`RL`, `BF = -2` and right child `BF = +1`)**: First perform a **Right Rotation** on the right child `Y`, then a **Left Rotation** on `Z`.

Because AVL trees enforce the strictest height bound ($H < 1.44 \log_2(N + 2)$), they deliver the **fastest worst-case lookup latency**, at the cost of performing up to $O(\log N)$ rotations when deleting a node.

### 2. Red-Black Trees (Guibas and Sedgewick, 1978)

Used in .NET's `SortedSet<T>` and `SortedDictionary<TKey, TValue>`, C++'s `std::map`, Java's `TreeMap`, and the Linux kernel's Completely Fair Scheduler (CFS), a **Red-Black Tree** relaxes the balance invariant slightly (storing just 1 bit of color per node: `Red` or `Black`) to minimize rotation overhead on mutations[^sedgewick-alg]:
1. Every node is either **Red** or **Black**.
2. The **Root** and all **NIL leaves** are **Black**.
3. **Red-Node Rule**: No Red node may have a Red child (two consecutive Red nodes on a path are forbidden).
4. **Black-Height Rule**: Every path from any node down to any descendant NIL leaf contains the **exact same number of Black nodes** (the node's *Black Height*).

Look at what Rules 3 and 4 together prove mathematically:
Because the shortest possible root-to-leaf path contains *only* Black nodes (length $B$), and the longest possible root-to-leaf path alternates `Black -> Red -> Black -> Red` (length at most $2B$), **the longest path in a Red-Black Tree is never more than twice the length of the shortest path ($H \le 2 \log_2(N + 1)$)**!
Even better, fixing a Red-Black violation after an **insertion requires at most 2 tree rotations**, and after a **deletion requires at most 3 tree rotations** (`O(1)` structural pointer mutations, plus color flips); making Red-Black Trees faster than AVL Trees for write-heavy workloads.

| Tree structure | Max height $H$ | Worst-case rotations on Insert / Delete | Where it is used in practice |
| --- | --- | --- | --- |
| **Unbalanced BST** | $N$ (degenerates on sorted input!) | $0$ | Never use on untrusted/monotonic production inputs. |
| **AVL Tree** | $\approx 1.44 \log_2 N$ | $\le 2$ on insert; $O(\log N)$ on delete | Read-heavy in-memory dictionaries where lookup speed dominates. |
| **Red-Black Tree** | $\le 2 \log_2(N + 1)$ | $\le 2$ on insert; $\le 3$ on delete ($O(1)$!) | Standard library sorted maps (`.NET SortedDictionary`, `std::map`, Linux kernel). |
| **B+Tree (Module 06)** | $\log_b N$ ($b \approx 200\text{--}500$) | Page split / merge across $4\text{--}16\text{ KB}$ blocks | Disk & SSD storage engines (`PostgreSQL`, `InnoDB`, `SQL Server`, `NTFS`). |

---

## 2. Essential tree traversals and algorithmic invariants

Every recursive or iterative algorithm on a binary tree reduces to one of four canonical traversals, classified by whether they explore **depth-first** (using recursion or an explicit LIFO `Stack<Node>`, costing **$O(H)$ auxiliary space**) or **breadth-first** (using a FIFO `Queue<Node>`, costing **$O(W)$ auxiliary space**, where $W \le N/2$ is the maximum width of any level):

```
         1
       /   \          * Preorder  (Root, Left, Right):  1 -> 2 -> 4 -> 5 -> 3  (Cloning / Serialization)
      2     3         * Inorder   (Left, Root, Right):  4 -> 2 -> 5 -> 1 -> 3  (Sorted order in a BST!)
     / \              * Postorder (Left, Right, Root):  4 -> 5 -> 2 -> 3 -> 1  (Bottom-up aggregation / deletion)
    4   5             * BFS / Level-Order (by depth):   [1] -> [2, 3] -> [4, 5] (Minimum depth / level averages)
```

Let us connect these four traversals to the complete family of classic binary tree algorithms and structural transformations from `Tree.md`:

### 1. Iterative In-Order Traversal with an Explicit Stack ($O(N)$ Time, $O(H)$ Space)

Recursive in-order traversal is concise, but on a skewed or deep tree ($H > 10{,}000$) it risks overflowing the thread call stack (`StackOverflowException`).
Replacing recursion with an explicit heap-allocated `Stack<TreeNode>` gives you complete control over memory while preserving the exact `Left -> Root -> Right` visitation order.
The invariant uses two state variables, `TreeNode? current = root` and `Stack<TreeNode> stack`: in each outer iteration, push `current` onto the stack and walk left (`current = current.Left`) until `current` hits `null`.
At that point, the node on top of the stack has no unvisited left descendants, so pop it, record its value, and pivot into its right subtree (`current = popped.Right`).
The loop terminates as soon as both `current is null` and `stack.Count == 0`.

```csharp
public static IReadOnlyList<int> TraverseInOrderIterative(TreeNode? root)
{
    List<int> result = new List<int>();
    Stack<TreeNode> stack = new Stack<TreeNode>();
    TreeNode? current = root;

    while (current is not null || stack.Count > 0)
    {
        while (current is not null)
        {
            stack.Push(current);
            current = current.Left;
        }

        TreeNode popped = stack.Pop();
        result.Add(popped.Val);
        current = popped.Right;
    }

    return result;
}
```

Every node is pushed onto `stack` once and popped once, yielding strict **$O(N)$ time** and **$O(H)$ stack space**.

### 2. Validating a BST, Finding the In-Order Successor, and the $N$-th Highest Element

Once you master in-order orderings, three core BST search problems follow directly:

First, **Validating a BST (`O(N)` time, `O(H)` space)** requires checking global subtree bounds, not just immediate children. A common bug is checking only `node.Left.Val < node.Val && node.Right.Val > node.Val` locally at each node, which fails whenever a left-right grandchild carries a key larger than the root. You can enforce the global BST invariant either top-down during **Preorder** by passing `(long minAllowed, long maxAllowed)` down the recursion (`minAllowed < node.Val < maxAllowed`, recursing left with `(minAllowed, node.Val)` and right with `(node.Val, maxAllowed)`), or bottom-up during **Inorder (`L-Root-R`)** by tracking `long prevValue = long.MinValue` and verifying that every visited node is strictly greater than `prevValue`.

Second, **Finding the In-Order Successor in a BST (`O(H)` time, `O(1)` space)** asks for the smallest key in the tree strictly greater than `d`. If node `d` has a non-null `Right` child, its in-order successor is the **leftmost node in `d.Right`** (found by walking `.Left` until null). If `d` has no `Right` child, its successor is the **lowest ancestor of `d` whose left subtree contains `d`**. Starting from `curr = root` with `candidate = null`, whenever `d < curr.Val` you record `candidate = curr` and step left (`curr = curr.Left`); whenever `d >= curr.Val` you step right (`curr = curr.Right`). If `d` is the maximum key in the BST, `candidate` remains `null`.

Third, finding the **$N$-th Highest (Largest) Number in a BST (`O(H + N)` time)** exploits symmetry: because standard Inorder (`Left -> Visit -> Right`) visits keys in ascending order, **Reverse Inorder (`Right -> Visit -> Left`)** visits keys in strictly **descending order**. Walking `Right -> Visit -> Left` while incrementing a counter `visitedCount` returns the exact $N$-th highest node the moment `visitedCount == n`, without visiting the rest of the tree.

### 3. Converting a Binary Tree to a Doubly Linked List In-Place ($O(N)$ Time, $O(1)$ Heap Allocations)

Suppose you must flatten a binary tree into a sorted Doubly Linked List (DLL) without allocating any new list nodes, repurposing each tree node's `Left` pointer as `Prev` and `Right` pointer as `Next`[^skiena-adm].
There are two equivalent ways to think about this in-place transformation:
1. **Divide-and-Conquer Subtree List Fusion**: Recursively convert `root.Left` into a DLL and `root.Right` into a DLL; then concatenate the tail of the left list to `root` (`leftTail.Right = root; root.Left = leftTail`) and concatenate `root` to the head of the right list (`root.Right = rightHead; rightHead.Left = root`).
2. **In-Order Pointer Stitching (`head` and `prev` tracking)**: Walk the tree in **Inorder (`Left -> Root -> Right`)** while maintaining a mutable reference to the previously visited node `TreeNode? prev` and the first visited node `TreeNode? head`. When visiting `curr`, if `prev is null` then `curr` is the leftmost minimum node (`head = curr`); otherwise stitch `prev.Right = curr` and `curr.Left = prev`. Advance `prev = curr` and recurse into `curr.Right`!

```csharp
public static TreeNode? ConvertToDoublyLinkedListInPlace(TreeNode? root)
{
    TreeNode? head = null;
    TreeNode? prev = null;

    void StitchInOrder(TreeNode? curr)
    {
        if (curr is null)
        {
            return;
        }

        StitchInOrder(curr.Left);

        if (prev is null)
        {
            head = curr;
        }
        else
        {
            prev.Right = curr;
            curr.Left = prev;
        }

        prev = curr;
        StitchInOrder(curr.Right);
    }

    StitchInOrder(root);
    return head;
}
```

Notice why `StitchInOrder(curr.Right)` remains safe after mutating `curr.Left` and `prev.Right`: by the time we visit `curr`, its entire left subtree has already been processed, `prev` sits inside `curr.Left` (or is an ancestor whose left child was not `curr`), and `curr.Right` has not yet been touched!

### 4. Printing the Tree Perimeter (Boundary Traversal in Counter-Clockwise Order)

Given a binary tree, **Print Tree Perimeter (Boundary Traversal)** outputs the outer perimeter of the tree counter-clockwise starting from the root: first the **left boundary** top-down, then all **leaf nodes** left-to-right, and finally the **right boundary** bottom-up (in reverse order), without duplicating the root or the corner leaves where boundaries meet:

```
            1                Perimeter order: [1, 2, 4, 7, 8, 9, 6, 3]
          /   \              * Root:           [1]
         2     3             * Left boundary:  [2]       (4 is a leaf, handled in leaves pass)
        / \     \            * All leaves:     [4, 7, 8, 9]
       4   5     6           * Right boundary: [6, 3]    (bottom-up, excluding leaf 9 and root 1)
          / \     \
         7   8     9
```

The secret to writing a bug-free boundary traversal is **explicitly excluding leaf nodes from the left-boundary and right-boundary passes** so the corner leaves are emitted once during the leaf pass:

```csharp
public static IReadOnlyList<int> GetTreePerimeter(TreeNode? root)
{
    List<int> perimeter = new List<int>();
    if (root is null)
    {
        return perimeter;
    }

    static bool IsLeaf(TreeNode n) => n.Left is null && n.Right is null;

    perimeter.Add(root.Val);
    if (IsLeaf(root))
    {
        return perimeter;
    }

    // 1. Left boundary top-down (excluding root and excluding leaves)
    TreeNode? curr = root.Left;
    while (curr is not null)
    {
        if (!IsLeaf(curr))
        {
            perimeter.Add(curr.Val);
        }

        curr = curr.Left ?? curr.Right;
    }

    // 2. All leaf nodes left-to-right via Preorder/Inorder DFS
    void CollectLeaves(TreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (IsLeaf(node))
        {
            perimeter.Add(node.Val);
            return;
        }

        CollectLeaves(node.Left);
        CollectLeaves(node.Right);
    }

    CollectLeaves(root);

    // 3. Right boundary bottom-up (collected on a stack to reverse, excluding root and leaves)
    Stack<int> rightBoundary = new Stack<int>();
    curr = root.Right;
    while (curr is not null)
    {
        if (!IsLeaf(curr))
        {
            rightBoundary.Push(curr.Val);
        }

        curr = curr.Right ?? curr.Left;
    }

    while (rightBoundary.Count > 0)
    {
        perimeter.Add(rightBoundary.Pop());
    }

    return perimeter;
}
```

### 5. Level-Order Traversal, Minimum Depth, and Connecting All Siblings in $O(1)$ Space

In standard **Level-Order Traversal (BFS)**, you can delimit levels either by swapping two queues (`current_queue` and `next_queue`), by enqueuing a `null` end-of-level sentinel marker whenever the previous level finishes, or by reading `int levelSize = queue.Count` at the top of each outer `while` iteration.
Because BFS explores the tree strictly by distance from the root, it finds the **Minimum Depth of a Binary Tree** as soon as it dequeues the *very first leaf node* (`node.Left is null && node.Right is null`), avoiding the $O(N)$ penalty of DFS on trees where one branch is 2 nodes deep and the other is 50,000 nodes deep.

What if each node has a `Next` sibling pointer and we want to **Connect All Siblings of a Binary Tree** across the entire tree into one continuous level-order chain without allocating a `Queue<Node>`?
We can use the already-linked `Next` pointers as our queue in **$O(1)$ extra space** by maintaining two pointers, `current` (the node whose children we are currently discovering) and `last` (the tail of the linked level-order chain):

```csharp
public static void ConnectAllSiblings(SiblingTreeNode? root)
{
    if (root is null)
    {
        return;
    }

    SiblingTreeNode? current = root;
    SiblingTreeNode last = root;

    while (current is not null)
    {
        if (current.Left is not null)
        {
            last.Next = current.Left;
            last = current.Left;
        }

        if (current.Right is not null)
        {
            last.Next = current.Right;
            last = current.Right;
        }

        current = current.Next;
    }
}
```

Trace how `current` and `last` cooperate: initially both point to `root`. In the first iteration, `root.Left` is appended after `last` (`root.Next = root.Left; last = root.Left`), and `root.Right` is appended after that (`root.Left.Next = root.Right; last = root.Right`). Then `current` advances to `current.Next` (which is now `root.Left`!) and threads the depth-2 children onto `last`, traversing all $N$ nodes in **$O(N)$ time and $O(1)$ auxiliary memory**.

### 6. Postorder Bottom-Up Pruning and Preorder Serialization

Rounding out the binary tree toolkit are two structural patterns:
- **Postorder Bottom-Up Pruning (`Delete Zero-Sum Subtrees`)**: Whenever a parent's fate depends on the computed aggregate of its children, use **Postorder (`Left -> Right -> Root`)**. To delete every subtree whose node values sum to zero, recursively compute `int leftSum = DeleteZeroSum(node.Left)` and `int rightSum = DeleteZeroSum(node.Right)`. If `leftSum == 0`, sever `node.Left = null`; if `rightSum == 0`, sever `node.Right = null`; then return `node.Val + leftSum + rightSum` (and if the root itself returns `0`, clear the entire tree).
- **Preorder Serialization and Deserialization**: An in-order sequence alone cannot reconstruct a tree's topology. Emitting a **Preorder (`Root -> Left -> Right`)** stream that writes an explicit null sentinel (`"#"`) for every `null` child reference encodes both values and structure in $O(N)$ tokens and deserializes recursively in a single $O(N)$ pass.

---

## 3. Prefix Tries and Radix (Patricia) Trees

Suppose you need to index 5 million domain names, URL routes, command-line flags, or firmware version prefixes (`"4.2.1-eu-canary"`), and support three operations: exact lookup, **prefix search (`StartsWith(prefix)`)**, and **longest-prefix match (`LongestPrefixOf(query)`)**.
A Hash Table supports exact lookup in $O(L)$ time (where $L$ is the string length, since hashing the string reads all $L$ characters), but **a Hash Table cannot do prefix searches or longest-prefix matches at all** without scanning every key!
A balanced BST supports prefix ranges in $O(L \cdot \log N)$ character comparisons.
Can we eliminate the $\log N$ factor completely so lookups depend *only* on the string length $L$, regardless of whether the dictionary holds 100 strings or 100 million strings?

Yes: the **Trie (Prefix Tree / Digital Search Tree)**[^skiena-adm][^sedgewick-alg]:

```
Standard Prefix Trie (Keys: "app", "apple", "apply", "apt")      Compressed Radix / Patricia Tree
===========================================================      ================================
                ( root )                                                    ( root )
                   | 'a'                                                       | "ap"
                 ( a )                                                       ( ap )
                   | 'p'                                                    /      \
                 ( p )                                                   "p"        "t"*
                /     \                                                 (app)*
             'p'       't'                                              /    \
           ( p )*     ( t )*                                         "le"*  "ly"*
            | 'l'
          ( l )
         /     \
      'e'       'y'
    ( e )*     ( y )*
```

### Standard Trie vs Compressed Radix (Patricia) Tree

In a **Standard Prefix Trie**, each node contains an ordered child array (for example, `TrieNode?[26]` for lowercase ASCII `'a'..'z'` or `SortedDictionary<char, TrieNode>`) plus a boolean `IsEndOfWord` marker.
Inserting a word, searching for an exact word, or checking whether a prefix exists walks character by character from the root in **$O(L)$ time**, independent of the number of words $N$ stored in the trie.
To delete a word without breaking longer words that share its prefix (such as deleting `"be"` while keeping `"beer"`), recursion walks to the terminal node of `"be"`, clears `IsEndOfWord = false`, and unlinks child pointers on the way back up **only if a node has zero remaining children and `IsEndOfWord == false`**.

What is the downside of a naive Standard Trie in production? **Space overhead on long unbranched strings!**
If you insert `"microservice-configuration-v1"` into a character-by-character trie, it allocates 29 separate heap nodes connected by single-child pointers.
A **Radix Tree (Compact Prefix Tree / Patricia Trie)** fixes this by **merging every chain of single-child nodes into a single edge labeled with a substring slice (`"ap"`, `"le"`, `"ly"`)**[^sedgewick-alg].
Every internal node in a Radix Tree is guaranteed to have at least two children (or be an end-of-key marker), bounding the total number of nodes in the tree to at most $2N$ regardless of how long the strings are!

**Where Radix Tries Run the Internet (IP Routing Longest-Prefix Match):**
Every core internet router and cloud virtual network switch maintains a forwarding table of CIDR prefixes (`10.0.0.0/8`, `10.24.0.0/16`, `10.24.128.0/20`).
When a packet arrives for destination IP `10.24.130.5`, it matches *all three* prefixes, and the router must forward it according to the **most specific (longest) matching prefix (`10.24.128.0/20`)** in nanoseconds.
Storing CIDR prefixes as bit strings in a binary **Radix / Multibit Trie** lets the router walk the bits of the destination IP and track the last non-empty rule encountered along the path, resolving **Longest-Prefix Match (LPM)** in a single bounded pass!

### Core Trie Algorithms: Counting Words, Enumerating All Words, and Lexicographical Trie Sort

Because the children of each trie node are indexed in alphabetical order (`'a'` at index `0` through `'z'` at index `25`), **a Preorder DFS traversal of a Trie visits every stored word in strict lexicographical (alphabetical) order automatically**!
That structural property makes the three classic challenges from `Trie.md` clean and unified:

1. **Total Number of Words in a Trie (`CountWords`)**: Starting at `_root`, traverse all non-null child branches recursively and increment the count by `1` at every node where `node.IsEndOfWord == true`.
2. **Find All Words in a Trie in Alphabetical Order (`GetAllWordsSorted`)**: Maintain a character buffer (or `StringBuilder`) indexed by recursion `level`: at depth `level`, write the edge character `ch` into `buffer[level]`, emit `new string(buffer, 0, level + 1)` whenever `child.IsEndOfWord` is true, and recurse to `level + 1`.
3. **Sort an Array of Strings Using a Trie (`SortArrayWithTrie`)**: Insert all strings from the unsorted input array into a fresh Trie in $O(\sum L_i)$ total character steps, and then run `GetAllWordsSorted()` to read them out in alphabetical order without a single string-to-string `CompareTo` call! (If duplicate strings in the input array must be preserved, store an integer `WordCount` instead of a boolean `IsEndOfWord` at each terminal node.)

```csharp
public sealed class AlphabetTrie
{
    private sealed class Node
    {
        public Node?[] Children { get; } = new Node?[26];
        public int WordCount { get; set; }
        public bool IsEndOfWord => WordCount > 0;
    }

    private readonly Node _root = new Node();

    public void Insert(string word)
    {
        Node curr = _root;
        foreach (char ch in word)
        {
            int idx = ch - 'a';
            curr.Children[idx] ??= new Node();
            curr = curr.Children[idx]!;
        }

        curr.WordCount++;
    }

    public int CountUniqueWords() => CountUniqueWords(_root);

    private static int CountUniqueWords(Node? node)
    {
        if (node is null)
        {
            return 0;
        }

        int count = node.IsEndOfWord ? 1 : 0;
        for (int i = 0; i < 26; i++)
        {
            if (node.Children[i] is not null)
            {
                count += CountUniqueWords(node.Children[i]);
            }
        }

        return count;
    }

    public IReadOnlyList<string> GetAllWordsAlphabetical(bool includeDuplicates = false)
    {
        List<string> result = new List<string>();
        char[] buffer = new char[128];
        CollectWords(_root, buffer, level: 0, includeDuplicates, result);
        return result;
    }

    private static void CollectWords(Node? node, char[] buffer, int level, bool includeDuplicates, List<string> result)
    {
        if (node is null)
        {
            return;
        }

        if (node.IsEndOfWord)
        {
            string word = new string(buffer, 0, level);
            int copies = includeDuplicates ? node.WordCount : 1;
            for (int c = 0; c < copies; c++)
            {
                result.Add(word);
            }
        }

        for (int i = 0; i < 26; i++)
        {
            if (node.Children[i] is not null)
            {
                buffer[level] = (char)('a' + i);
                CollectWords(node.Children[i], buffer, level + 1, includeDuplicates, result);
            }
        }
    }

    public static IReadOnlyList<string> SortArrayWithTrie(ReadOnlySpan<string> unsortedWords)
    {
        AlphabetTrie trie = new AlphabetTrie();
        foreach (string word in unsortedWords)
        {
            trie.Insert(word);
        }

        return trie.GetAllWordsAlphabetical(includeDuplicates: true);
    }
}
```

---

## 4. Spatial indexing: Quad-Trees, Geohashes, and R-Trees

Now suppose your query is two-dimensional: *"Find all connected delivery vehicles or healthy edge gateways within a 5 km bounding box around `(Latitude = 47.6062, Longitude = -122.3321)`."*
Why does a standard B+Tree index on `(Latitude, Longitude)` perform poorly on 2D bounding-box queries (`WHERE Lat BETWEEN 47.55 AND 47.65 AND Lon BETWEEN -122.40 AND -122.25`)?
Remember the **Equality-Before-Range rule** of composite B+Trees from Module 07: once the first column (`Latitude`) is queried with a `BETWEEN` range, the second column (`Longitude`) is no longer globally sorted across that slice! The database must scan *every single point around the entire globe* that sits between latitudes `47.55` and `47.65` (from Seattle to Montana to Paris!) and filter `Longitude` row by row.

To index 2D (or $k$-D) space so that points close together in physical space are stored together in the index, we use **Spatial Indexing**:

### 1. The Quad-Tree (Recursive 2D Quadrant Subdivision)

A **Quad-Tree** partitions a 2D rectangular bounding box (`Boundary = [MinX, MaxX] x [MinY, MaxY]`) by recursively subdividing any leaf region that exceeds its capacity threshold $C$ (say $C = 4$ or $C = 64$ points) into **four equal child quadrants**: **NorthWest (`NW`)**, **NorthEast (`NE`)**, **SouthWest (`SW`)**, and **SouthEast (`SE`)**[^samet-spatial]:

```
2D Spatial Map with Adaptive Quad-Tree Subdivision (Capacity C = 2 points per leaf)
===================================================================================
+-----------------------+-----------------------+
|                       |           |  * p3     |
|         NW            |   NE-NW   |   NE-NE   |
|        * p1           |-----------+-----------|
|                       | * p4 * p5 |           |
|                       |   NE-SW   |   NE-SE   |
+-----------------------+-----------+-----------+
|                       |                       |
|         SW            |          SE           |
|                       |         * p2          |
|                       |                       |
+-----------------------+-----------------------+
```

Look at why a Quad-Tree adapts so cleanly to skewed real-world geography:
- Oceans and deserts (with almost zero points) stay as **single large undivided leaf nodes** at depth 1 or 2, consuming almost zero memory.
- Dense urban centers (like downtown Tokyo or London) subdivide recursively to depth 12 or 16, isolating small city blocks into fast leaf buckets!
- **2D Bounding-Box Range Query (`QueryRange(Rect queryBox)`)**: Start at the Root. If `!node.Boundary.Intersects(queryBox)`, **prune the entire subtree immediately in $O(1)$ time**! Otherwise, check the points in the node and recurse only into the child quadrants whose boundaries intersect `queryBox`.

### 2. Geohashes and Space-Filling Curves (Mapping 2D into a 1D Prefix Trie/B+Tree)

What if you want to run 2D spatial queries inside an existing 1D storage engine like Cassandra, DynamoDB, Redis, or PostgreSQL without building a custom Quad-Tree server?
Use a **Geohash** (or **Hilbert / Z-Order Space-Filling Curve**)!
A Geohash encodes a `(Latitude, Longitude)` pair by recursively bisecting latitude (`[-90, +90]`) and longitude (`[-180, +180]`) and **interleaving their bits (`LonBit0, LatBit0, LonBit1, LatBit1, ...`)**, then encoding every 5 bits into a Base-32 string (for example, Seattle is `"c23nb62w"`):
- Look at the connection between **Quad-Trees**, **Tries**, and **Geohashes**: **every additional character in a Geohash string represents zooming one level deeper into a Quad-Tree quadrant!**
- Two points that share a longer Geohash prefix (`"c23nb6..."`) are guaranteed to lie inside the same small bounding box on Earth.
- Therefore, indexing the 1D `Geohash` string in a standard **B+Tree, LSM-Tree, or Prefix Trie** turns a 2D spatial proximity search into a fast **1D prefix range query** (`WHERE Geohash LIKE 'c23nb6%'`, checking the target cell plus its 8 surrounding neighbor cells to handle boundary edges)!

### 3. R-Trees (Balanced Bounding-Box Trees on Disk)

While Quad-Trees are great in memory for point data, they are not height-balanced on disk (like a trie) and struggle with indexing irregular 2D polygons (such as city boundaries, road segments, or geofences).
Relational spatial extensions like **PostGIS (`GiST`)** and **SQLite R\*Tree** use **R-Trees** (Guttman, 1984): the 2D analog of a B+Tree, where every page is balanced at the same leaf depth and stores the **Minimum Bounding Rectangle (MBR)** enclosing all geometric shapes in its child subtree.

---

## 5. Trees, tries, and spatial indexes in the Atlas platform

In **Atlas**, all three tree families solve concrete subsystem bottlenecks:
- **Radix Tree for Device Firmware Routing and Topic Matching**: When a device reports its current firmware string (`"atlas-os/4.2.104-industrial-eu"`), the Rollout Engine matches it against hierarchical target prefix rules stored in an in-memory **Radix Tree**, resolving the most specific matching rollout policy (Longest-Prefix Match) in `< 200 nanoseconds`.
- **Quad-Tree + Geohash for Regional Anomaly Detection**: Cellular IoT gateways report GPS coordinates and packet-loss metrics. To distinguish a device-specific hardware failure from a **localized cell-tower outage**, Atlas indexes active gateway statuses in an in-memory **Point Quad-Tree** (and persists their 7-character **Geohash** in Cassandra). Whenever a gateway drops offline, the anomaly detector runs a `QueryRange(5km box)` against the Quad-Tree in microseconds to check whether neighboring gateways across different tenants in that exact geographic cell dropped simultaneously!

---

## Before you move on

Trees organize keys hierarchically to accelerate search, ordering, and spatial partitioning.
In memory, **AVL Trees** maintain strict height balance (`BF in {-1, 0, +1}`) via single and double rotations (`LL, RR, LR, RL`) for optimal lookup speed, while **Red-Black Trees** enforce color and black-height invariants (`H <= 2 log_2(N+1)`) to cap insert/delete rotations at $O(1)$.
Algorithmic problems over binary trees map cleanly onto **Preorder** (top-down bounds, serialization), **Inorder** (sorted BST sequences, validation, successor), **Postorder** (bottom-up subtree aggregation and pruning), and **BFS Level-Order** (minimum depth, level width).
For string keys, **Prefix Tries** and path-compressed **Radix Trees** deliver $O(L)$ prefix searches, lexicographical autocomplete, and **Longest-Prefix Match (LPM)** routing.
For 2D geospatial coordinates, **Quad-Trees** recursively subdivide crowded quadrants into `NW, NE, SW, SE` leaves, **Geohashes** interleave coordinate bits to turn 2D cells into 1D prefix strings, and **R-Trees** balance minimum bounding rectangles on disk.

To test your understanding before moving to Module 20, trace this changed case: suppose two delivery vans are parked just **10 meters apart** on opposite sides of the **Prime Meridian (`Longitude = -0.00005` and `Longitude = +0.00005`)** in London.
Why do their **Geohash strings** differ in the **very first character** (having zero shared prefix at all!) despite being only 10 meters apart physically, and why must every production Geohash proximity search compute and query **all 9 cells (the center Geohash cell plus its 8 adjacent neighbor cells)** rather than querying only the center Geohash prefix?
If that prime-meridian / cell-boundary edge case is not crystal clear, review **Section 4** before continuing.

---

### Sources

[^skiena-adm]: **Tier 2: Steven S. Skiena, *The Algorithm Design Manual* (Springer, 3rd ed., 2020), Chapters 3 & 12**: Binary search trees, balanced tree invariants, DFS/BFS traversals, tries, and kd-trees / quadtrees.
[^sedgewick-alg]: **Tier 2: Robert Sedgewick and Kevin Wayne, *Algorithms* (Addison-Wesley, 4th ed., 2011), Chapters 3 & 5**: AVL and Red-Black BST derivations, rotation invariants, and R-way / Patricia tries.
[^samet-spatial]: **Tier 2: Hanan Samet, *Foundations of Multidimensional and Metric Data Structures* (Morgan Kaufmann, 2006)**: Comprehensive reference on Point and Region Quad-Trees, R-Trees, and space-filling curves (Z-order / Geohash and Hilbert curves).
