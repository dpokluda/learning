# 21: Dynamic Programming, Greedy Algorithms, and Coding Patterns

Why do senior and Principal engineering loops still test algorithmic problem-solving alongside system design and leadership?
Not because anyone expects you to invert a binary tree on a whiteboard for sport, but because **production optimization bottlenecks; such as packing variable-sized update deltas into bandwidth budgets, computing line-by-line configuration diffs, scheduling non-overlapping maintenance windows, sliding time windows over telemetry streams, and merging sorted shard cursors; are direct instances of Dynamic Programming, Greedy Optimization, and the 20 Essential Coding Patterns**.
When you recognize the underlying structural pattern of a problem in thirty seconds, you do not fumble around with ad-hoc nested loops that blow up to \(O(N^2)\) or \(O(2^N)\) in production; you reach directly for the proven \(O(N)\) or \(O(N \log N)\) invariant.
This module demystifies **Dynamic Programming** (optimal substructure, overlapping subproblems, top-down memoization vs bottom-up tabulation, and state-space compression), establishes when **Greedy Algorithms** are provably optimal versus when they fail, and provides a complete, concrete field guide to the **20 Essential Coding Patterns**.

> **Prerequisite:** [Module 18: Capacity Planning and Asymptotic Analysis](./18-capacity-planning-and-asymptotic-analysis.md), [Module 19: Trees, Tries, and Spatial Indexing](./19-trees-tries-and-spatial-indexing.md), and [Module 20: Hash Tables, Heaps, and Graphs](./20-hash-tables-heaps-and-graphs.md).

---

## 1. Dynamic Programming: optimal substructure, overlapping subproblems, and state reduction

**Dynamic Programming (DP)** is a method for solving optimization, counting, and decision problems by decomposing them into simpler subproblems and **remembering each distinct subproblem's answer so it is computed at most once**[^skiena-adm].
Divide-and-Conquer algorithms (like MergeSort) also break a problem into smaller subproblems, but in MergeSort the left half `arr[0..mid]` and the right half `arr[mid+1..end]` are **completely disjoint**; they never share subproblems.
Dynamic Programming applies when two mathematical properties hold simultaneously[^skiena-adm]:

1. **Optimal Substructure**: An optimal solution to the overall problem of size \(n\) can be constructed deterministically from optimal solutions to its smaller subproblems (represented as a **Recurrence Relation**, such as Bellman's equations).
2. **Overlapping Subproblems**: A naive recursive exploration repeatedly hits the **exact same subproblem state** across exponentially many branches (turning a polynomial number of distinct states into \(O(2^n)\) redundant work!).

```
Naive Recursive Tree (Exponential O(2^N) due to redundant recomputation)
                      Solve(idx=0, remCap=10)
                       /                   \
          Solve(1, 10)                       Solve(1, 6)
           /        \                         /        \
   Solve(2, 10)   [Solve(2, 6)]       [Solve(2, 6)]   Solve(2, 2)
                        \               /
             (Exact same state (idx=2, remCap=6) computed twice!)
```

### Top-Down Memoization vs Bottom-Up Tabulation (and State-Space Compression)

Every DP problem can be implemented in two equivalent ways:
- **Top-Down with Memoization**: Write the natural recursive function `Solve(state)` and cache results in a `Dictionary<State, Result>` or array `memo[state]` right before returning.
  - *When Top-Down wins*: When the state space is sparse and only a small fraction of all possible `(i, j)` table entries are actually reachable from the starting state.
  - *When Top-Down hurts*: Deep recursion on \(N = 100{,}000\) overflows the thread call stack (`StackOverflowException`), and hash-table lookups carry higher constant overhead than contiguous array indexing.
- **Bottom-Up with Tabulation**: Allocate a contiguous array `dp[...]` and iterate through the subproblem states in **topological dependency order** (smallest subproblems to largest) so that whenever you compute `dp[i]`, all smaller states it depends on are already filled in.
  - *The Superpower of Bottom-Up Tabulation (**State-Space Reduction**)*: In 90% of 2D dynamic programming problems (`dp[i, w]` where row `i` only reads values from the immediately preceding row `i - 1`), you **do not need to allocate the full \(O(N \times W)\) 2D matrix**! You can collapse the memory footprint down to a **single 1D array of size \(O(W)\)** (or just two scalar variables for Fibonacci/Climbing Stairs, reducing space to **\(O(1)\)**)!

### Worked Production Derivation: The 0/1 Knapsack Problem in 1D Space

Suppose an Atlas edge gateway has a remaining cellular bandwidth budget of \(C\text{ megabytes}\) during a maintenance window, and there are \(N\) candidate diagnostic/firmware packages where package \(i\) has size `weights[i]` and business priority value `profits[i]`.
Each package is **0/1** (cannot be split fractionally: you either transmit the whole package or skip it).
We want to maximize total profit within capacity \(C\):

1. **State & Recurrence**: Let `dp[i, c]` be the maximum profit achievable using a subset of the first \(i\) packages within capacity \(c \in [0, C]\). For item \(i\) (with weight \(w_i\) and profit \(p_i\)), we have two choices:
   - **Skip item \(i\)**: Profit is `dp[i - 1, c]`.
   - **Take item \(i\)** (only valid if \(c \ge w_i\)): Profit is \(p_i + \text{dp}[i - 1, c - w_i]\).
   \[
   \text{dp}[i, c] = \max\!\Bigl(\text{dp}[i - 1, c], \;\; p_i + \text{dp}[i - 1, c - w_i]\Bigr)
   \]
2. **State-Space Reduction to a Single 1D Array `dp[0..C]`**:
   Notice that row `i` only ever reads `dp[i - 1, c]` and `dp[i - 1, c - w_i]` (an index to the *left* in the previous row).
   If we iterate capacity \(c\) **backward from \(C\) down to \(w_i\)** (`for (int c = C; c >= w_i; c--)`), then when we read `dp[c - w_i]`, it is guaranteed to still hold the value from the **previous iteration `i - 1`** (whereas iterating forward from `w_i` to `C` would read the *already-updated* row `i`, solving the **Unbounded Knapsack** problem where each item can be taken infinitely many times!):

```csharp
public static int SolveZeroOneKnapsack(ReadOnlySpan<int> weights, ReadOnlySpan<int> profits, int capacity)
{
    int[] dp = new int[capacity + 1];

    for (int i = 0; i < weights.Length; i++)
    {
        int weight = weights[i];
        int profit = profits[i];

        // Iterate BACKWARD so dp[c - weight] still holds state from before item i was used
        for (int c = capacity; c >= weight; c--)
        {
            int withItem = dp[c - weight] + profit;
            if (withItem > dp[c])
            {
                dp[c] = withItem;
            }
        }
    }

    return dp[capacity];
}
```

That 15-line method solves 0/1 Knapsack in **\(O(N \cdot C)\) time and \(O(C)\) space** with L1-cache-friendly sequential array access.
Replacing `int[] dp` with `bool[] dp` (`dp[0] = true; dp[s] |= dp[s - num]`) immediately solves **Subset Sum** and **Equal Subset Sum Partition** (`target = totalSum / 2`) using the exact same backward loop!

---

## 2. Greedy algorithms: when local choices yield the global optimum

A **Greedy Algorithm** is simpler and faster than Dynamic Programming: instead of exploring all subproblem branches and taking a `max` over future outcomes, a Greedy algorithm **makes the single choice that looks best locally at the current step, commits to it irrevocably, and never looks back**[^skiena-adm].
When a Greedy algorithm is valid, it typically runs in **\(O(N \log N)\) sorting time** or **\(O(N)\) linear time**.

The danger of Greedy algorithms is that **human intuition loves to invent plausible-sounding greedy rules that are mathematically wrong**!
How do you tell when Greedy works versus when you must use DP?
A problem admits an optimal Greedy algorithm if in addition to Optimal Substructure it satisfies the **Greedy Choice Property** (provable via an **Exchange Argument**: showing that any optimal solution that did *not* make the greedy choice first can have its first choice swapped with the greedy choice without making the solution worse)[^skiena-adm]:

| Problem | Does Greedy work? | The exact rule (or why Greedy fails) |
| --- | --- | --- |
| **Fractional Knapsack** (items are divisible fluids/streams) | **Yes ($O(N \log N)$)** | Sort items descending by **value-to-weight density ratio ($p_i / w_i$)** and take as much of the densest item as fits. |
| **0/1 Knapsack** (items are indivisible packages) | **NO! (Requires DP)** | *Counterexample*: Capacity $C = 5$, Item A = `(w=3, p=4, ratio=1.33)`, Item B = `(w=2.5, p=3)`, Item C = `(w=2.5, p=3)`. Taking highest-ratio Item A leaves `2` empty space (`profit = 4`), whereas taking `B + C` fills `5` (`profit = 6`)! |
| **Activity / Interval Scheduling** (max non-overlapping jobs) | **Yes ($O(N \log N)$)** | Sort all intervals by **Earliest Finish Time (`end` ascending)** (NOT earliest start time and NOT shortest duration!) and greedily pick every interval whose `start >= lastPicked.end`. *Exchange proof*: Finishing as early as possible leaves the maximum possible remaining timeline for all subsequent intervals. |
| **Huffman Lossless Prefix Coding** | **Yes ($O(N \log N)$)** | Put all symbol frequencies in a **Min-Heap**; repeatedly extract the **two least frequent** nodes, merge them under a new parent with `freq = f1 + f2`, and push back into the Min-Heap. |
| **Minimum Spanning Tree (`Prim's` & `Kruskal's`)** | **Yes ($O(E \log V)$)** | **Cut Property**: The minimum-weight edge crossing *any* cut $(S, V \setminus S)$ of a graph is guaranteed to belong to an MST. **Prim's** grows a single tree from a start vertex via a Min-Heap of crossing edges; **Kruskal's** sorts all edges globally and merges disjoint components via Union-Find. |
| **Coin Change (Minimum Coins for Target $T$)** | **NO for general coins! (Requires DP)** | Greedy ("take largest coin $\le$ remaining") works for US coins `[1, 5, 10, 25]`, but **fails on `[1, 3, 4]` for target `T = 6`**: Greedy picks `4 + 1 + 1` (**3 coins**), whereas the optimal DP answer is `3 + 3` (**2 coins**)! |

### Minimum Spanning Trees: Prim's Algorithm vs Kruskal's Algorithm

Suppose you are designing a low-cost fiber or peer-to-peer relay mesh connecting \(V\) edge gateways across a factory floor, where undirected edge \((u, v)\) carries link cost \(w(u, v) \ge 0\).
A **Minimum Spanning Tree (MST)** is a subset of \(V - 1\) acyclic edges that connects all \(V\) vertices with the **minimum possible total edge weight**[^skiena-adm].
Both classic MST algorithms exploit the **Greedy Cut Property** (if you partition the vertices \(V\) into two disjoint sets \(S\) and \(V \setminus S\), the lightest edge with one endpoint in \(S\) and one endpoint in \(V \setminus S\) is always safe to include in the MST), but they grow the tree in complementary ways:

- **Kruskal's Algorithm (`Edge-Centric + Union-Find`, $O(E \log E) = O(E \log V)$)**: Sorts all \(E\) edges in the entire graph by weight ascending, then scans them in order and adds edge \((u, v)\) to the MST if and only if `dsu.Union(u, v)` returns `true` (meaning \(u\) and \(v\) were in different connected components, so adding the edge does not create a cycle). Kruskal's excels on sparse edge lists where edges are already streamed or easily sorted globally.
- **Prim's Algorithm (`Vertex-Centric + Min-Heap`, $O((V + E) \log V)$)**: Starts from an arbitrary seed vertex (`start = 0`), marks it in the visited tree set \(S\), and pushes all of its outgoing edges into a **Min-Heap (`PriorityQueue<(int From, int To, long Weight), long>`)**. At each step, Prim's extracts the minimum-weight edge `(u, v, w)` from the heap: if `inMst[v]` is already `true`, it skips the edge; otherwise, it adds `v` (and edge `(u, v)`) to the MST and pushes all outgoing edges from `v` to unvisited neighbors into the Min-Heap, stopping once all \(V\) vertices are connected!

```csharp
public static (long TotalWeight, IReadOnlyList<(int From, int To, long Weight)> Edges)? ComputePrimMst(
    IReadOnlyList<(int To, long Weight)>[] adj)
{
    int vCount = adj.Length;
    if (vCount == 0)
    {
        return (0, Array.Empty<(int, int, long)>());
    }

    bool[] inMst = new bool[vCount];
    List<(int From, int To, long Weight)> mstEdges = new List<(int, int, long)>(vCount - 1);
    PriorityQueue<(int From, int To, long Weight), long> minHeap =
        new PriorityQueue<(int, int, long), long>();

    inMst[0] = true;
    int visitedCount = 1;
    foreach ((int neighbor, long weight) in adj[0])
    {
        minHeap.Enqueue((0, neighbor, weight), weight);
    }

    long totalWeight = 0;
    while (minHeap.TryDequeue(out (int From, int To, long Weight) edge, out _) && visitedCount < vCount)
    {
        if (inMst[edge.To])
        {
            continue;
        }

        inMst[edge.To] = true;
        visitedCount++;
        totalWeight += edge.Weight;
        mstEdges.Add(edge);

        foreach ((int next, long nextWeight) in adj[edge.To])
        {
            if (!inMst[next])
            {
                minHeap.Enqueue((edge.To, next, nextWeight), nextWeight);
            }
        }
    }

    return visitedCount == vCount ? (totalWeight, mstEdges) : null;
}
```

Notice the structural kinship between **Prim's MST Algorithm** and **Dijkstra's Shortest-Path Algorithm** (Module 20): both grow a visited set \(S\) from a starting vertex using a Min-Heap! The only difference is the priority key pushed into the Min-Heap: **Prim's** orders by the **individual crossing edge weight \(w(u, v)\)** (to minimize total tree weight), whereas **Dijkstra's** orders by the **cumulative path distance from the source \(\text{dist}[u] + w(u, v)\)** (to minimize root-to-node path latency).

---

## 3. Field guide to the 20 Essential Coding Patterns

When you encounter an algorithmic problem in a design review, performance optimization, or coding interview, map its input shape and constraint to this **20-Pattern Decision Matrix**[^coding-patterns]:

| # | Pattern name | Input signal / trigger | Core algorithmic invariant & complexity |
| --- | --- | --- | --- |
| **01** | **Two Pointers** | **Sorted array / string**; find pairs/triplets satisfying a sum constraint or deduplicate in place. | Place `left = 0` and `right = n - 1`. If `arr[left] + arr[right] < target`, `left++` (to increase sum); if `> target`, `right--` (to decrease sum). **$O(N)$ time, $O(1)$ space.** |
| **02** | **Fast & Slow Pointers (Floyd's Cycle)** | Singly linked list or functional sequence `f(x)` where extra memory is forbidden; detect cycle or find middle. | `slow = slow.Next`; `fast = fast.Next.Next`. If there is a cycle of length $C$, `fast` gains 1 step per tick and **must collide with `slow` inside the cycle**; if `fast == null`, `slow` sits at the exact midpoint! **$O(N)$ time, $O(1)$ space.** |
| **03** | **Sliding Window** | **Contiguous subarray or substring** (longest/shortest/max-sum with constraint like "$\le K$ distinct chars"). | Expand window right (`right++`) to accumulate state; whenever the window invariant is violated (or exceeds size $K$), shrink from the left (`left++`) until valid again. Each element enters and leaves once: **$O(N)$ time.** |
| **04** | **Merge Intervals** | List of `[start, end]` time windows, maintenance schedules, or byte ranges; merge overlaps or find free slots. | Sort intervals by `start` ascending. Two sorted intervals `A` and `B` ($A.\text{start} \le B.\text{start}$) overlap iff **`B.start <= A.end`**, merging into `[A.start, max(A.end, B.end)]`. **$O(N \log N)$ time.** |
| **05** | **Cyclic Sort** | Array of $N$ integers guaranteed to lie in the range **$[1, N]$ (or $[0, N]$)**; find missing or duplicate numbers in $O(1)$ space. | Value `v` belongs at index `v - 1`. While `arr[i] != arr[arr[i] - 1]`, swap `arr[i]` into its home slot `arr[i] - 1`; then scan once for any index `i` where `arr[i] != i + 1`. **$O(N)$ time, $O(1)$ space.** |
| **06** | **In-Place Linked List Reversal** | Reverse a singly linked list, a sub-segment `[p..q]`, or every $K$-element group with zero allocations. | Maintain three pointers `prev = null`, `curr = head`, `next = null`; in each step, save `next = curr.Next`, flip `curr.Next = prev`, advance `prev = curr; curr = next`. **$O(N)$ time, $O(1)$ space.** |
| **07** | **Island (Matrix Traversal)** | 2D grid/matrix `grid[R, C]` where adjacent cells (`up, down, left, right`) form connected clusters. | Iterate cells; on hitting an unvisited `'1'`, increment `islandCount` and run **BFS, DFS, or Union-Find** to mark all reachable `'1'` cells in that component visited. **$O(R \cdot C)$ time.** |
| **08** | **Tree BFS (Level Order)** | Binary/N-ary tree; level-by-level grouping, zigzag order, level averages, or minimum depth. | Enqueue root in FIFO `Queue`; at each outer iteration, capture `int count = queue.Count` and process exactly `count` nodes for the current depth level. **$O(N)$ time, $O(W)$ space.** |
| **09** | **Tree DFS (Path Sums)** | Root-to-leaf or any-node-to-any-node path constraints in a tree. | Pass running state (`remainingSum` or prefix-sum dictionary) down `Preorder` and aggregate child results up `Postorder`, backtracking state on return. **$O(N)$ time, $O(H)$ space.** |
| **10** | **Two Heaps (Streaming Median)** | Stream of numbers where you must query the **running median** dynamically after every insert. | Maintain a **Max-Heap `lowerHalf`** (for the smaller half of numbers) and a **Min-Heap `upperHalf`** (for the larger half), keeping `0 <= lowerHalf.Count - upperHalf.Count <= 1`. The median is `lowerHalf.Peek()` (or the average of both roots)! **$O(\log N)$ insert, $O(1)$ median.** |
| **11** | **Subsets (Combinatorial BFS / Bitmask)** | Generate all $2^N$ subsets (power set) or $N!$ permutations of a small collection. | Start with `[[]]`; for each element `x`, clone all existing subsets and append `x` to the clones (for duplicates, sort first and only extend subsets created in the previous step!). **$O(N \cdot 2^N)$ time.** |
| **12** | **Modified Binary Search** | Sorted or rotated-sorted array, or **Binary Search on the Answer Space** (monotonic boolean predicate `CanFit(capacity)`). | Whenever `Check(mid)` is monotonic (`false, false, ..., true, true`), binary-search `[low, high]` in $O(\log(\text{Range}))$ probes! In rotated sorted arrays, **at least one half (`[low..mid]` or `[mid..high]`) is guaranteed to be strictly sorted**; check which half is sorted and whether `target` lies inside it. |
| **13** | **Bitwise XOR** | Find the 1 or 2 elements appearing an odd number of times when all others appear twice, in $O(1)$ space. | Exploits associative/commutative identities **$x \oplus x = 0$** and **$x \oplus 0 = x$**. XORing all elements cancels every paired duplicate to `0`, leaving the unique number (or `a ^ b`, which can be split by its lowest set bit `diffBit = xor & -xor`). **$O(N)$ time, $O(1)$ space.** |
| **14** | **Top-$K$ Elements** | Find the $K$ largest, $K$ smallest, or $K$ most frequent items in a stream of size $N \gg K$. | Maintain a **Min-Heap of fixed size $K$**: push the first $K$ items; for each remaining item `x`, if `x > minHeap.Peek()`, `DequeueEnqueue(x)`. Never sort all $N$ items! **$O(N \log K)$ time, $O(K)$ space.** |
| **15** | **$K$-Way Merge** | Merge $K$ sorted lists, arrays, or SSTable iterators, or find the smallest range covering all $K$ lists. | Push the head `(value, listIndex, elementIndex)` of each of the $K$ lists into a **Min-Heap of size $K$**; repeatedly pop the minimum and push the next element from that same list. **$O(N \log K)$ time, $O(K)$ space.** |
| **16** | **Topological Sort** | Task scheduling, course prerequisites, or package build ordering with directed dependencies `(u -> v)`. | Compute `InDegree[]` and run **Kahn's BFS Queue** starting from `InDegree == 0` nodes (Module 20). **$O(V + E)$ time.** |
| **17** | **Trie (Prefix Tree)** | Autocomplete, dictionary word search on a grid, or longest-prefix matching over strings. | Walk character transitions from root in **$O(L)$ time**; combine with backtracking on 2D boards to prune dead character branches immediately. |
| **18** | **Backtracking** | Constraint satisfaction (N-Queens, Sudoku, valid parentheses, word break, IP segmentation). | `Choose -> Explore -> Unchoose`: mutate candidate state in place, recurse if pruning constraints pass, and undo the mutation immediately after the recursive call returns. |
| **19** | **Monotonic Stack / Deque** | Find the **Next Greater / Smaller Element** for every index, Largest Rectangle in Histogram, or **Sliding Window Maximum**. | Maintain a stack of indices whose values are strictly monotonic (`decreasing` to find Next Greater Element). When `arr[i] > arr[stack.Peek()]`, `arr[i]` is the Next Greater Element for `stack.Pop()`! Every index is pushed once and popped at most once: **$O(N)$ amortized time!** |
| **20** | **0/1 & Unbounded Knapsack DP** | Resource allocation under capacity/cost constraints, coin change, rod cutting, or string edit distance. | Formulate state recurrence `dp[i, c]` and tabulate bottom-up (iterating capacity backward for 0/1 Knapsack and forward for Unbounded Knapsack). **$O(N \cdot C)$ time, $O(C)$ space.** |

---

## 4. Deeper look at two high-leverage patterns: Monotonic Stack and Binary Search on Answer

Two patterns from the table above turn seemingly quadratic \(O(N^2)\) system telemetry problems into blazing-fast \(O(N)\) or \(O(N \log R)\) single passes:

### 1. The Monotonic Stack / Monotonic Deque Pattern

Suppose Atlas monitors CPU spikes across a time series `cpu[0..N-1]` and needs to compute, for every minute `i`, **how many minutes elapsed until the next higher CPU reading occurred (`NextGreaterIndex[i]`)**.
A naive nested loop checks `j = i + 1, i + 2, ...` for every `i`, taking \(O(N^2)\) time.
Instead, maintain a **Monotonic Decreasing Stack** of unresolved indices:

```csharp
public static int[] FindNextGreaterIndices(ReadOnlySpan<int> values)
{
    int[] result = new int[values.Length];
    Array.Fill(result, -1);
    Stack<int> decreasingIndices = new Stack<int>();

    for (int i = 0; i < values.Length; i++)
    {
        // Any prior index on the stack whose value is strictly smaller than values[i]
        // has just found its FIRST greater element to the right: index i!
        while (decreasingIndices.Count > 0 && values[i] > values[decreasingIndices.Peek()])
        {
            int resolvedIdx = decreasingIndices.Pop();
            result[resolvedIdx] = i;
        }

        decreasingIndices.Push(i);
    }

    return result;
}
```

Why is that nested `while` loop inside a `for` loop strictly **\(O(N)\) time** overall?
By **amortized accounting**: each of the \(N\) indices `0 .. N - 1` is pushed onto `decreasingIndices` **at most once**, and therefore can be popped by the inner `while` loop **at most once** across the entire execution (`<= 2N` stack operations total)!

### 2. Modified Binary Search on the Answer Space

We normally think of Binary Search as finding an element inside a sorted array.
A far more powerful generalization in systems engineering is **Binary Search on the Answer Space**:
Suppose you need to split \(N\) ordered rollout batches `batches[0..N-1]` across \(K\) maintenance windows so as to **minimize the maximum bandwidth `M` required in any single window**.
Instead of trying to construct `M` directly with complex DP, ask the inverse boolean question: *"If I pick a candidate bandwidth cap `M`, can I greedily pack all $N$ batches into $\le K$ windows without exceeding `M`?"*
That `CanPack(batches, K, candidateM)` check is a trivial \(O(N)\) linear scan, and its output is **monotonic** in `candidateM` (`false, false, ..., false, true, true, ...`)!
Therefore, we simply binary-search `candidateM` between `low = max(batches)` and `high = sum(batches)` in **\(O(N \log(\sum \text{batches}))\)** time!

---

## 5. DP, greedy, and coding patterns in the Atlas platform

In **Atlas**, these algorithmic patterns run in production across the rollout and telemetry engines:
- **0/1 Knapsack DP for Edge Bandwidth Packing**: Satellite-connected maritime and remote industrial gateways have a strict monthly/daily satellite data cap \(C\). Atlas's Edge Sync Scheduler runs the **1D 0/1 Knapsack DP** algorithm to select the highest-priority subset of pending telemetry batches, config updates, and security patches that fit within the gateway's exact byte quota.
- **Merge Intervals + Activity Scheduling for Maintenance Windows**: Enterprise tenants define recurring regional blackout and maintenance windows across timezones; the Rollout Scheduler uses **Merge Intervals** to coalesce overlapping windows and **Greedy Earliest-Finish Scheduling** to pack non-conflicting tenant rollouts onto shared delta-generation workers.
- **Sliding Window + Two Heaps in Stream Health Monitors**: Regional rollout health monitors evaluate **Sliding Window** error rates and maintain **Two Heaps** (and `t-digest` sketches) to track real-time median and tail installation durations, automatically halting a rollout if the window threshold trips.

---

## Before you move on

**Dynamic Programming** solves problems with **Optimal Substructure** and **Overlapping Subproblems** by caching subproblem states via **Top-Down Memoization** or **Bottom-Up Tabulation** (where iterating capacity backward in 0/1 Knapsack collapses a 2D \(O(N \cdot C)\) table into a single 1D \(O(C)\) array).
**Greedy Algorithms** make irrevocable locally optimal choices (such as sorting by **Earliest Finish Time** in Interval Scheduling, **Value-to-Weight Ratio** in Fractional Knapsack, **Lowest Frequencies** in Huffman Coding, or **Minimum Crossing Edges** in **Prim's and Kruskal's Minimum Spanning Tree** algorithms) and succeed only when the **Greedy Choice Property** holds.
And the **20 Essential Coding Patterns** (`Two Pointers`, `Fast & Slow Pointers`, `Sliding Window`, `Merge Intervals`, `Cyclic Sort`, `In-Place List Reversal`, `Matrix BFS/DFS`, `Tree BFS/DFS`, `Two Heaps`, `Subsets`, `Modified Binary Search`, `Bitwise XOR`, `Top-K`, `K-Way Merge`, `Topological Sort`, `Trie`, `Backtracking`, `Monotonic Stack`, and `Knapsack DP`) map recurring structural problems directly to optimal time and space bounds.

To test your understanding before moving to Part VI (Principal Platform Architecture, Data Lakes, Policy Engines, and Production AI), consider this changed case: in the **0/1 Knapsack 1D array algorithm** in Section 1, what happens if you accidentally change the inner loop from `for (int c = capacity; c >= weight; c--)` (backward) to **`for (int c = weight; c <= capacity; c++)` (forward)**?
Trace the forward loop on a single item of `weight = 2, profit = 10` with `capacity = 6` (`dp[2]`, then `dp[4]`, then `dp[6]`).
Why does the forward loop allow the same single item to be picked **3 times** (`dp[6] = 30`), transforming the algorithm from **0/1 Knapsack** into **Unbounded Knapsack**?
If that direction-of-iteration invariant is not crystal clear, review **Section 1** before continuing.

---

### Sources

[^skiena-adm]: **Tier 2: Steven S. Skiena, *The Algorithm Design Manual* (Springer, 3rd ed., 2020), Chapters 4, 9, & 10**: Backtracking, pruning, Dynamic Programming (memoization vs tabulation, edit distance, knapsack, state-space reduction), and Greedy algorithms vs DP counterexamples.
[^coding-patterns]: **Tier 4: Arslan Ahmad / Design Gurus, *Grokking the Coding Interview: Patterns for Coding Questions* & Thomas H. Cormen et al. (CLRS), *Introduction to Algorithms* (MIT Press, 4th ed., 2022)**: Taxonomy and formal complexity proofs of sliding window, two pointers, Floyd's cycle detection, merge intervals, monotonic stacks, two heaps, k-way merge, and knapsack dynamic programming.
