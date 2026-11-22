# 20: Hash Tables, Heaps, and Graphs

If trees (Module 19) are the masters of ordered ranges and hierarchies, **Hash Tables**, **Heaps**, and **Graphs** are the masters of constant-time lookup, priority scheduling, and arbitrary network relationships.
Every day in production, your C# services rely on `Dictionary<TKey, TValue>`, `PriorityQueue<TElement, TPriority>`, sorting pipelines, and dependency graphs.
Yet under adversarial or high-scale workloads, naive assumptions about these three structures break down: a poorly seeded hash table degrades from \(O(1)\) to \(O(N)\) per insert during a hash-flooding denial-of-service attack; sorting a 5 TB dataset in memory crashes with `OutOfMemoryException` unless you switch to external \(K\)-way merge sort; and a subtle circular dependency in a microservice deployment manifest or package graph hangs the orchestrator forever unless caught by a three-color cycle detector or Kahn's topological sort.
This module examines the internal memory layouts, invariants, and scale trade-offs of **Hash Tables** (chaining vs open addressing, Robin Hood probing, SipHash), **Heaps** (binary and \(d\)-ary array representation, \(O(N)\) `Heapify`, Top-\(K\) streaming), **Sorting** (in-memory vs external merge sort), and **Graphs** (`BFS`, `DFS`, `Dijkstra`, `A*`, `Topological Sort`, `Cycle Detection`, and `Union-Find`).

> **Prerequisite:** [Module 18: Capacity Planning and Asymptotic Analysis](./18-capacity-planning-and-asymptotic-analysis.md) and [Module 19: Trees, Tries, and Spatial Indexing](./19-trees-tries-and-spatial-indexing.md).

---

## 1. Hash table internals: chaining vs open addressing, Robin Hood hashing, and SipHash

A **Hash Table (Hash Map)** maps keys to values in \(O(1)\) average time by applying a deterministic **Hash Function** `h(key)` to project a large key universe onto an array of \(m\) bucket slots (`index = (h(key) & 0x7FFFFFFF) % m`)[^skiena-adm][^sedgewick-alg].
Because the key universe is vastly larger than \(m\), two distinct keys will inevitably map to the same slot (**a hash collision**).
Every hash table implementation resolves collisions using one of two memory architectures, governed by the **Load Factor** \(\alpha = \frac{n}{m}\) (the ratio of stored entries \(n\) to array slots \(m\)):

```
1. Separate Chaining (Linked Lists / Flat Entry Array):    2. Open Addressing (Linear Probing + Tombstones):
=======================================================    =================================================
Buckets[]        Entries[] (Contiguous in .NET Dictionary)  Slots[] (All keys/values inline in one flat array!)
+-------+        +-----+-------+-------+------+             +-------+-------+-------+-------+-------+-------+
|   0   | -----> |Idx 0| Key A | Val 1 |Next=2|             |Idx 0  |Idx 1  |Idx 2  |Idx 3  |Idx 4  |Idx 5  |
+-------+        +-----+-------+-------+------+             |EMPTY  |Key A  |[TOMB] |Key B  |Key C  |EMPTY  |
|   1   | (nil)  |Idx 1| Key D | Val 4 |Next=-1             |       |(h=1)  |(del!) |(h=1!) |(h=3)  |       |
+-------+        +-----+-------+-------+------+             +-------+-------+-------+-------+-------+-------+
|   2   | -----> |Idx 2| Key B | Val 2 |Next=-1                      \____________________/
+-------+        +-----+-------+-------+------+                 Lookup(Key B): h(Key B)=1 -> probes 1, skips
                                                                Tombstone at 2, finds Key B at 3!
```

### 1. Separate Chaining (and How .NET's `Dictionary<TKey, TValue>` Avoids Pointer Chasing)

In textbook **Separate Chaining**, each bucket in the main array points to a heap-allocated singly linked list of colliding `(Key, Value)` nodes.
Textbook linked-list chaining tolerates high load factors (\(\alpha > 1\)), but allocating a separate heap object per dictionary entry creates massive GC overhead and L1/L2 CPU cache misses on every collision hop!

Look at how **.NET's `Dictionary<TKey, TValue>`** fixes that brilliantly in production:
Instead of allocating `LinkedListNode` objects on the heap, `.NET Dictionary` allocates **two contiguous flat arrays**: an `int[] _buckets` array and an `Entry[] _entries` struct array!
Each `Entry` struct holds `(uint hashCode, int next, TKey key, TValue value)`, where `next` is simply an **integer array index** (`0, 1, 2...`) into `_entries[]` rather than a heap pointer, and deleted slots are threaded onto a `freeList` index chain!
That layout gives you the collision isolation of Separate Chaining with the contiguous array allocation and zero-GC-node overhead of Open Addressing.

### 2. Open Addressing: Linear Probing, Tombstones, and Robin Hood Hashing

In **Open Addressing** (used by Python's `dict`, Rust's `HashMap / SwissTable`, C++'s `absl::flat_hash_map`), **all entries are stored directly inside the single flat slot array itself**:
- **Linear Probing**: If slot `i = h(key) % m` is occupied by a different key, probe `i + 1`, `i + 2`, `i + 3` `(mod m)` sequentially until you find the key or an `EMPTY` slot. Because `i, i+1, i+2` sit inside the **exact same 64-byte L1 CPU cache line**, short linear probes are practically free on modern hardware!
- **Why Deletion in Open Addressing Requires Tombstones**: Look at the Open Addressing diagram above. Both `Key A` and `Key B` hashed to `slot 1`. `Key A` took `slot 1`, and `Key B` probed forward to `slot 3` (because `slot 2` was also occupied at the time). Now suppose we delete whatever was at `slot 2`. **If we marked `slot 2` as `EMPTY`, a subsequent `Lookup(Key B)` starting at `slot 1` would hit `EMPTY` at `slot 2` and falsely stop, claiming `Key B` does not exist!**
  Instead, deleting a key in basic Open Addressing must write a **`TOMBSTONE` (Deleted Sentinel)**: `Lookup` treats a Tombstone as occupied and keeps probing past it, whereas `Insert` can reuse the Tombstone slot for a new key! (When Tombstones accumulate and lengthen probe chains, the table rehashes to clear them.)
- **Robin Hood Hashing**: Standard linear probing suffers from primary clustering (long runs of occupied slots). **Robin Hood Hashing** tracks each entry's **Probe Sequence Length (`PSL`)**; how many slots away it sits from its ideal home bucket. When inserting a new key `X`, if `X` has probed further from its home (`PSL(X) = 4`) than the existing occupant `Y` at the current slot (`PSL(Y) = 1`), **`X` steals the slot from the "rich" key `Y` and continues inserting `Y` further down the array ("stealing from the rich and giving to the poor")**! This dramatically compresses probe-length variance so lookups remain fast even at load factors up to \(\alpha = 0.85\).

### 3. Hash-Flooding DoS Attacks and Keyed SipHash

What happens if an external attacker sends an HTTP POST containing a JSON object with `50,000 keys` that have all been carefully crafted to produce the **exact same 32-bit hash code** in your deterministic non-cryptographic hash function?
Every single one of the 50,000 keys collides into the **exact same bucket**, turning 50,000 \(O(1)\) insertions into \(\sum_{i=1}^{50000} i \approx 1.25 \times 10^9\) string comparisons (**\(O(N^2)\) time**); freezing a CPU core for 30 seconds on a single 500 KB HTTP request (**a Hash-Flooding Denial-of-Service Attack**)!
Production runtimes defend against hash flooding in two ways:
1. **Per-Process Randomized Hash Seeds / Keyed `SipHash`**: Runtimes initialize string hashing with a cryptographic random 128-bit secret key per process startup (or use **SipHash-2-4**) so an external attacker cannot precompute colliding keys offline.
2. **Collision Threshold Treeification / Re-seeding**: If any single bucket chain in .NET `Dictionary` or Java `HashMap` exceeds a collision threshold (e.g., 100 collisions in .NET or 8 in Java), the runtime dynamically switches to a randomized cryptographic hash or converts the bucket chain into a balanced \(O(\log N)\) Red-Black Tree!

### 4. Classic Hash Table Problem Patterns (All Seven `Hash-Table.md` Drills)

Beyond hash table internals, seven recurring data-processing drills from `Hash-Table.md` demonstrate how \(O(1)\) set/dictionary lookups collapse quadratic \(O(N^2)\) or \(O(N^4)\) loops into linear \(O(N)\) (or \(O(N^2)\)) single passes:

1. **Check if Two Arrays Are Disjoint (`O(N + M)` time, `O(min(N, M))` space)**: Two arrays are disjoint if they share zero elements in common (`A intersect B == empty`). Load the smaller array into a `HashSet<T> set` in \(O(\min(N, M))\) time and space, then iterate through the larger array and return `false` the instant `set.Contains(elem)` returns `true`. (This same `HashSet<T>` complement pattern solves **Two-Sum (`a + b == n`)**: for each `x`, check if `seen.Contains(n - x)` before adding `x` to `seen`.)
2. **Find Symmetric Pairs `(a, b)` and `(b, a)` in an Array (`O(N)` time)**: Given an array of pairs `(first, second)`, store seen pairs in a `Dictionary<int, int> map`. For each pair `(a, b)`, check if `map.TryGetValue(b, out int existingTarget) && existingTarget == a`; if so, `(b, a)` and `(a, b)` are a symmetric pair! Otherwise set `map[a] = b`.
3. **Find Two Pairs `(a, b)` and `(c, d)` Such That `a + b == c + d` (`O(N^2)` time instead of `O(N^4)`)**: Iterate through all index pairs `(i, j)` with `i < j`, compute `long sum = arr[i] + arr[j]`, and probe a `Dictionary<long, (int I, int J)> sumToPair`. If `sumToPair` already holds a pair `(p, q)` with `p != i && p != j && q != i && q != j`, return both pairs immediately; otherwise record `sumToPair[sum] = (i, j)`.
4. **Find if a Subarray with Sum Equal to `0` (or `K`) Exists (`O(N)` Prefix-Sum Map)**: A contiguous subarray `arr[i..j]` sums to `0` if and only if the cumulative prefix sum at index `j` equals the cumulative prefix sum at index `i - 1` (\(P_j - P_{i-1} = 0 \iff P_j = P_{i-1}\)), or \(P_j = 0\) directly. Walk the array once while maintaining `long runningSum = 0` and a `HashSet<long> seenPrefixes = [0]`: if `arr[j] == 0` or `!seenPrefixes.Add(runningSum + arr[j])`, a zero-sum subarray exists! Generalizing to a `Dictionary<long, int> prefixCounts = new() { [0] = 1 }` counts how many subarrays sum to any target `K` by summing `prefixCounts.GetValueOrDefault(runningSum - K)`.
5. **First Non-Repeating Integer in an Array (`O(N)` two-pass frequency map)**: Pass 1 iterates through `arr` and populates `Dictionary<int, int> counts` (`counts[x] = counts.GetValueOrDefault(x) + 1`). Pass 2 iterates through `arr` in its **original array index order** and returns the first `x` where `counts[x] == 1`. Why iterate `arr` in Pass 2 instead of iterating `counts`? Because iterating `arr` guarantees returning the *first* non-repeating element in input order regardless of hash bucket ordering!
6. **Remove Duplicates from an Unsorted Linked List Using Hashing (`O(N)` time)**: Maintain a `HashSet<T> visited`, a `Node? curr = head`, and a `Node? prev = null`. As you walk `curr`, if `visited.Add(curr.Value)` returns `true` (first time seen), advance `prev = curr`; if `visited.Add(curr.Value)` returns `false` (duplicate!), unlink `curr` in \(O(1)\) by setting `prev!.Next = curr.Next`.
7. **Union and Intersection of Two Lists Using Hashing (`O(N + M)` time)**:
   - **Union (`A union B`)**: Traverse `list1` and `list2` sequentially with a single `HashSet<T> visited`; append each element `x` to the result list only when `visited.Add(x)` returns `true`.
   - **Intersection (`A intersect B`)**: Traverse `list1` and insert every element into `HashSet<T> inFirst`; then traverse `list2` and append `x` to the result list whenever `inFirst.Remove(x)` returns `true` (using `.Remove(x)` so duplicate copies of `x` in `list2` are not emitted twice!).

```csharp
public static class HashTableDrills
{
    public static bool AreDisjoint<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : notnull
    {
        if (a.Length > b.Length)
        {
            return AreDisjoint(b, a);
        }

        HashSet<T> set = new HashSet<T>(a.Length);
        foreach (T item in a)
        {
            set.Add(item);
        }

        foreach (T item in b)
        {
            if (set.Contains(item))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<(int A, int B)> FindSymmetricPairs(ReadOnlySpan<(int First, int Second)> pairs)
    {
        List<(int, int)> result = new List<(int, int)>();
        Dictionary<int, int> seen = new Dictionary<int, int>();

        foreach ((int a, int b) in pairs)
        {
            if (seen.TryGetValue(b, out int partner) && partner == a)
            {
                result.Add((b, a));
            }
            else
            {
                seen[a] = b;
            }
        }

        return result;
    }

    public static ((int A, int B), (int C, int D))? FindEqualSumPairs(ReadOnlySpan<int> arr)
    {
        Dictionary<long, (int I, int J)> sumToIndices = new Dictionary<long, (int, int)>();

        for (int i = 0; i < arr.Length; i++)
        {
            for (int j = i + 1; j < arr.Length; j++)
            {
                long sum = (long)arr[i] + arr[j];
                if (sumToIndices.TryGetValue(sum, out (int I, int J) prev))
                {
                    if (prev.I != i && prev.I != j && prev.J != i && prev.J != j)
                    {
                        return ((arr[prev.I], arr[prev.J]), (arr[i], arr[j]));
                    }
                }
                else
                {
                    sumToIndices[sum] = (i, j);
                }
            }
        }

        return null;
    }

    public static bool HasZeroSumSubarray(ReadOnlySpan<int> arr)
    {
        HashSet<long> prefixSums = new HashSet<long> { 0L };
        long runningSum = 0;

        foreach (int x in arr)
        {
            runningSum += x;
            if (x == 0 || !prefixSums.Add(runningSum))
            {
                return true;
            }
        }

        return false;
    }

    public static int? FirstNonRepeating(ReadOnlySpan<int> arr)
    {
        Dictionary<int, int> counts = new Dictionary<int, int>(arr.Length);
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
```

---

## 2. Binary and $d$-ary Heaps (Priority Queues)

Whenever a system needs to repeatedly schedule the **earliest-expiring timer**, process the **highest-priority request**, merge **$K$ sorted SSTable streams**, or track the **Top-$K$ heavy hitters**, it uses a **Heap (Priority Queue)**[^skiena-adm][^sedgewick-alg].

### Complete Binary Tree in a Flat Array (Zero Pointer Overhead)

A **Binary Heap** is a **Complete Binary Tree** (every level is completely filled from left to right) that satisfies the **Heap Order Property**:
- In a **Min-Heap**, every parent key is \(\le\) its children's keys (so the **global minimum** is always at the root `index 0` in \(O(1)\) time).
- In a **Max-Heap**, every parent key is \(\ge\) its children's keys (so the **global maximum** is always at `index 0`).

Because the tree is complete with zero gaps, a Binary Heap **never allocates tree node pointers**; it packs the entire tree into a single flat array `T[] heap`:

```
Flat Array Representation of a Min-Heap:
Index:    0    1    2    3    4    5    6
        +----+----+----+----+----+----+----+
Array:  |  2 |  5 |  4 |  9 |  7 |  8 |  6 |
        +----+----+----+----+----+----+----+
For any node at 0-based index i:
  * Parent(i)     = (i - 1) / 2
  * LeftChild(i)  = 2 * i + 1
  * RightChild(i) = 2 * i + 2
```

- **`Insert(item)` (`O(log N)`)**: Append `item` at the end of the array (`index = Count`) and **`SiftUp` (Bubble Up)**: repeatedly swap it with its parent at `(i - 1) / 2` while it is smaller than its parent.
- **`ExtractMin()` (`O(log N)`)**: Save `heap[0]` to return, move the last element `heap[Count - 1]` into `heap[0]`, shrink `Count` by 1, and **`SiftDown` (Bubble Down)**: repeatedly swap it with the *smaller* of its two children (`2i + 1` and `2i + 2`) until the heap property holds.
- **Why `Heapify` (Building a Heap from an Unsorted Array) Is \(O(N)\), NOT \(O(N \log N)\)**:
  If you insert \(N\) items one by one using `SiftUp`, the half of the nodes at the bottom leaf level travel up \(\log_2 N\) levels, taking \(O(N \log N)\).
  Instead, **Floyd's Bottom-Up `Heapify`** starts at the **last internal parent node (`index = N/2 - 1`)** and runs `SiftDown` backward down to `index = 0`!
  Because \(N/2\) nodes are leaves (requiring `0` swaps), \(N/4\) nodes are at height 1 (requiring at most `1` swap), and \(N/2^{h+1}\) nodes are at height \(h\), the total work is bounded by the convergent series:
  \[
  \sum_{h=0}^{\lfloor \log_2 N \rfloor} \frac{N}{2^{h+1}} \cdot h \le N \sum_{h=0}^{\infty} \frac{h}{2^{h+1}} = \mathbf{O(N)}
  \]
  This same \(O(N)\) bottom-up `SiftDown` loop is how you **convert a Max-Heap array into a Min-Heap array in place in \(O(N)\) time**!

*(Production note: .NET's `PriorityQueue<TElement, TPriority>` uses an **Arity-4 (`4-ary`) Heap** (`children = 4i + 1 .. 4i + 4`, `parent = (i - 1) / 4`), because 4 contiguous children fit inside a single CPU cache line and reduce tree height by \(2\times\) during `SiftUp`!)*

---

## 3. Sorting algorithms, partition schemes, and External $K$-Way Merge Sort

Every Principal engineer should know not just the asymptotic bounds of `SelectionSort`, `InsertionSort`, `BubbleSort`, `MergeSort`, and `QuickSort`, but **their loop invariants, why `InsertionSort` and `QuickSort` combine inside `.NET Array.Sort`, how Hoare's and Lomuto's partition schemes differ, and how to sort multi-terabyte files that do not fit in RAM**[^skiena-adm][^sedgewick-alg]:

| Algorithm | Time: Best / Avg / Worst | Extra Space | Stable? | When to use it in production |
| --- | --- | --- | --- | --- |
| **Selection Sort** | $O(N^2)$ / $O(N^2)$ / $O(N^2)$ | $O(1)$ | No | Almost never for general sorting, though it performs **at most $N - 1$ swaps ($O(N)$ writes)** when memory writes are orders of magnitude costlier than reads. |
| **Bubble Sort** | $O(N)$ / $O(N^2)$ / $O(N^2)$ | $O(1)$ | Yes | Pedagogical baseline; with an early-exit `swapped` flag it verifies an already-sorted array in one $O(N)$ pass, but is strictly dominated by Insertion Sort. |
| **Insertion Sort** | $O(N)$ / $O(N^2)$ / $O(N^2)$ | $O(1)$ | Yes | **Tiny arrays ($N \le 16\text{--}32$)** and **nearly sorted streams** (used as the base-case leaf finisher inside `.NET Array.Sort` and `TimSort`!). |
| **QuickSort / IntroSort** | $O(N \log N)$ / $O(N \log N)$ / $O(N^2)^*$ | $O(\log N)$ stack | No | **Fastest general-purpose in-memory sort** for value types due to L1 cache locality. (*.NET `Array.Sort` uses **IntroSort**: starts with QuickSort, switches to **HeapSort** if recursion depth exceeds $2 \log_2 N$ to guarantee $O(N \log N)$ worst-case, and finishes small partitions with **Insertion Sort**!) |
| **MergeSort** | $O(N \log N)$ across all cases | $O(N)$ buffer | **Yes** | When **stability** (preserving relative order of equal keys) is required (e.g., `Enumerable.OrderBy` / TimSort) or when merging linked lists / external sequential streams. |
| **Counting / Radix Sort** | $O(N \cdot (k / d))$ linear | $O(N + 2^d)$ | Yes | Fixed-width integers (`uint64`, IP addresses, timestamps) when $N$ is large and beating the $\Omega(N \log N)$ comparison bound matters. |

### 1. Elementary Quadratic Sorts: Selection, Insertion, and Bubble Sort (`Sort.md`)

Why study the three elementary \(O(N^2)\) sorts if \(O(N \log N)\) algorithms exist? Because their loop invariants explain how data movement and hardware cache lines behave on small arrays:

- **Selection Sort**: Divides the array into a sorted prefix `a[0..i-1]` and an unsorted suffix `a[i..N-1]`. In iteration `i`, it scans `j = i + 1 .. N - 1` to find the index `minIdx` of the smallest element in the unsorted suffix and swaps `a[i]` with `a[minIdx]`. Even if the input is already sorted, Selection Sort blindly performs \(\frac{N(N-1)}{2}\) comparisons (**\(\Theta(N^2)\) in all cases**), and its long-range swap can reorder equal elements (**unstable**). Its single virtue is that it executes **at most \(N - 1\) swaps total (\(O(N)\) memory writes)**.
- **Insertion Sort**: Also maintains a sorted prefix `a[0..i-1]`, but instead of scanning the entire unsorted suffix for the global minimum, it takes the very next element `v = a[i]` and shifts larger elements in `a[0..i-1]` one slot to the right (`a[j] = a[j - 1]`) until `v` drops into its exact sorted slot.
  Look at why **Insertion Sort is the undisputed champion for small arrays (\(N \le 24\)) and nearly sorted streams**:
  1. Using a single temp variable `v = a[i]` and shifting `a[j] = a[j - 1]` cuts memory writes in half compared to swapping `exchange(a[j-1], a[j])` on every step.
  2. If each element in a stream is at most \(k\) positions away from its sorted position (an array with \(I\) inversions), Insertion Sort runs in **\(O(N + I)\) linear time**; just \(N - 1\) comparisons when the input is already sorted!
  3. It has zero recursion overhead and walks sequentially within a single 64-byte CPU cache line, which is why `.NET Array.Sort` stops QuickSort recursion at partition size \(\le 16\) and finishes with Insertion Sort.
- **Bubble Sort**: Repeatedly steps through the array comparing adjacent elements `(a[j - 1], a[j])` and swapping them if `a[j] < a[j - 1]`, so the next smallest (or largest) element "bubbles" into position at index `i`. Adding a boolean `bool swapped` flag per pass allows Bubble Sort to exit in \(O(N)\) if no swaps occur, but whenever elements are out of order it performs \(3\times\) more memory writes than Insertion Sort's shift loop.

```csharp
public static class ElementarySorts
{
    public static void SelectionSort(Span<int> a)
    {
        for (int i = 0; i < a.Length - 1; i++)
        {
            int minIdx = i;
            for (int j = i + 1; j < a.Length; j++)
            {
                if (a[j] < a[minIdx])
                {
                    minIdx = j;
                }
            }

            if (minIdx != i)
            {
                (a[i], a[minIdx]) = (a[minIdx], a[i]);
            }
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
}
```

### 2. Merge Sort ($O(N \log N)$ Worst-Case, Stable Divide-and-Conquer)

Invented by John von Neumann in 1945, **Merge Sort** recursively splits `a[left..right]` at `mid = left + (right - left) / 2` until subarrays reach length 1, and then merges the two sorted halves `a[left..mid]` and `a[mid+1..right]` using an auxiliary buffer `temp[]`.
Because splitting always divides the array in exact halves regardless of key values, Merge Sort obeys the recurrence \(T(N) = 2T(N/2) + \Theta(N)\), guaranteeing **\(\Theta(N \log N)\) time even in the worst case**.
Furthermore, writing `if (temp[i] <= temp[j])` (taking from the left half when keys are equal) makes Merge Sort **strictly stable**, preserving the relative order of records that share the same sort key.
In production C#, allocate the auxiliary buffer `int[] temp = new int[a.Length]` **once** up front and pass it down the recursion rather than allocating a new array on every merge call!

```csharp
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

    // Optimization: if the largest item of the left half <= smallest of the right half, already sorted!
    if (a[mid] <= a[mid + 1])
    {
        return;
    }

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
        else if (temp[i] <= temp[j]) // '<=' guarantees stability!
        {
            a[k] = temp[i++];
        }
        else
        {
            a[k] = temp[j++];
        }
    }
}
```

### 3. QuickSort: Hoare's Two-Pointer Partition vs Lomuto's Partition and Pivot Selection

Developed by Tony Hoare in 1960, **QuickSort** sorts in place without an \(O(N)\) auxiliary buffer by choosing a **pivot** element `v`, **partitioning** the subarray so all elements \(\le v\) sit to the left of the pivot's final index `p` and all elements \(\ge v\) sit to the right, and recursively sorting `a[left..p-1]` and `a[p+1..right]`.

You should know both classic partition schemes and why Hoare's scheme from `Sort.md` is faster in practice:
1. **Hoare / Sedgewick Two-Pointer Partition (`Sort.md`)**: Places pivot `v = a[right]` at the right end, starts pointer `i = left - 1` scanning right (`while (a[++i] < v)`) and pointer `j = right` scanning left (`while (v < a[--j]) if (j == left) break;`). Whenever `i < j`, it swaps the out-of-place pair `(a[i], a[j])`; when the pointers cross (`i >= j`), it swaps the pivot `a[right]` into `a[i]` and returns `i`.
   *Why stopping on equal keys (`< v` instead of `<= v`) matters*: If an array contains 1,000,000 identical keys (`[5, 5, 5, ...]`), stopping both pointers on keys equal to the pivot causes `i` and `j` to swap equal keys and meet at the **exact midpoint**, keeping the recursion tree balanced at \(O(N \log N)\) instead of degenerating to \(O(N^2)\)!
2. **Lomuto Single-Direction Partition**: Maintains a single boundary index `storeIdx = left` and scans `j = left .. right - 1`: whenever `a[j] < pivot`, it swaps `a[storeIdx]` with `a[j]` and increments `storeIdx`, finally swapping the pivot into `a[storeIdx]`. Lomuto is simpler to write on a whiteboard, but performs \(3\times\) more swaps than Hoare's two-pointer scan on average and degrades to \(O(N^2)\) when all elements are equal.
3. **Preventing the $O(N^2)$ Worst Case on Sorted Inputs (Median-of-Three Pivot Selection)**: If you naively pick `a[right]` as the pivot on an array that is *already sorted* (`[1, 2, 3, ..., N]`), every partition splits into `N - 1` and `0` elements, causing \(O(N^2)\) comparisons and \(O(N)\) stack depth! Selecting the **Median of Three** (`left`, `mid`, `right`) as the pivot guarantees an ideal midpoint split on sorted and reverse-sorted arrays.

```csharp
public static class QuickSortAlgorithms
{
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

        // Median-of-three pivot selection placed at a[r] to avoid O(N^2) on sorted input
        int mid = l + ((r - l) / 2);
        if (a[mid] < a[l])
        {
            (a[l], a[mid]) = (a[mid], a[l]);
        }

        if (a[r] < a[l])
        {
            (a[l], a[r]) = (a[r], a[l]);
        }

        if (a[mid] < a[r])
        {
            (a[mid], a[r]) = (a[r], a[mid]);
        }

        int p = PartitionHoare(a, l, r);
        QuickSortHoareRecursive(a, l, p - 1);
        QuickSortHoareRecursive(a, p + 1, r);
    }

    private static int PartitionHoare(Span<int> a, int l, int r)
    {
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
        return i;
    }

    public static void QuickSortLomuto(Span<int> a, int l, int r)
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
        QuickSortLomuto(a, l, storeIdx - 1);
        QuickSortLomuto(a, storeIdx + 1, r);
    }
}
```

### 4. External $K$-Way Merge Sort (Sorting 10 TB with 4 GB of RAM)

Suppose you need to sort a **10 TB log file** on disk using a worker with only **4 GB of RAM** (or compact 10 SSTables in an LSM-Tree, Module 06).
You use **External $K$-Way Merge Sort**:
1. **Run Generation Phase**: Read `4 GB` chunks of the 10 TB file into RAM one at a time, sort each 4 GB chunk in memory using `IntroSort`, and flush it out to disk as a sorted temporary file (**a Sorted Run**), producing \(K = \frac{10\text{ TB}}{4\text{ GB}} = 2{,}500\) sorted run files on disk.
2. **$K$-Way Heap Merge Phase**: Open a buffered sequential file stream reader (say `1 MB` buffer each, using `2.5 GB` of RAM total) on the head of all \(K = 2{,}500\) sorted run files, and insert the first record from each stream into a **Min-Heap of size $K = 2{,}500$**:
   - Repeatedly `ExtractMin()` from the Min-Heap (in \(\log_2(2500) \approx 12\) comparisons!), append that globally smallest record to the final sorted output file stream, and advance the file stream that owned that record to push its next record into the Min-Heap!

---

## 4. Graphs: representations, traversals, topological sort, and shortest paths

A **Graph** \(G = (V, E)\) models entities (**Vertices** \(V\)) and their pairwise relationships (**Edges** \(E\))[^skiena-adm].
In memory, you represent a graph in one of two ways:
- **Adjacency List (`List<Edge>[]` or `Dictionary<Vertex, List<Edge>>`)**: Takes **\(O(V + E)\) space** and iterates over a vertex's actual neighbors in \(O(\text{degree}(u))\) time. Because almost all real-world graphs (road networks, microservice call graphs, package dependency DAGs, social graphs) are **sparse** (\(E \ll V^2\)), **Adjacency Lists are the default choice 99% of the time**.
- **Adjacency Matrix (`bool[V, V]` or `Weight[V, V]`)**: Takes **\(O(V^2)\) space** regardless of how few edges exist. Useful only for small, dense graphs (\(V \le 2{,}000\)) where \(O(1)\) edge-existence matrix multiplication is needed.

### Graph Traversals, Path Counting, Cycle Detection, and Topological Sort

Every Principal engineer must be able to write and reason about the core graph traversal and ordering algorithms from `Graph.md`:

1. **Breadth-First Search (`BFS`, $O(V + E)$)**: Uses a FIFO `Queue<int>` and a `visited[]` array (or `distance[]` array initialized to `-1`) to explore vertices strictly in layers of edge distance from the source, wrapped in an outer loop over `0..V-1` if the graph is disconnected. **BFS guarantees finding the shortest path (minimum number of hops) in any unweighted graph.**
2. **Depth-First Search (`DFS`, $O(V + E)$)**: Uses recursion or an explicit LIFO `Stack<int>` to plunge to the end of a branch before backtracking. Used to enumerate **Connected Components** (incrementing a component counter each time the outer loop over `0..V-1` launches a traversal from an unvisited vertex) and test **Bipartiteness via 2-Coloring** (alternating `Color 0` and `Color 1` across neighbors; if any edge connects two vertices of the same color, the graph contains an odd-length cycle and is *not* bipartite!).
3. **Counting All Simple Paths Between Two Nodes (`CountPaths`, DFS with Backtracking or DAG Memoization)**: Suppose we want to count how many distinct simple paths exist from `source` to `destination` in a directed graph (`Graph.md`). Our base case occurs when `current == destination`, which returns `1` path found! Otherwise, mark `onCurrentPath[current] = true`, recursively sum `CountPaths(neighbor, destination)` across all neighbors not currently on the active path, and **backtrack (`onCurrentPath[current] = false`)** before returning so other branches can still traverse through `current`:
   ```csharp
   public static int CountAllSimplePaths(IReadOnlyList<int>[] adj, int source, int destination)
   {
       bool[] onCurrentPath = new bool[adj.Length];

       int DfsCount(int current)
       {
           if (current == destination)
           {
               return 1;
           }

           onCurrentPath[current] = true;
           int totalPaths = 0;
           foreach (int neighbor in adj[current])
           {
               if (!onCurrentPath[neighbor])
               {
                   totalPaths += DfsCount(neighbor);
               }
           }

           onCurrentPath[current] = false; // Backtrack so sibling paths can visit 'current'
           return totalPaths;
       }

       return DfsCount(source);
   }
   ```
   *(Note: if the graph is guaranteed to be a **DAG**, paths cannot cycle and we can memoize `int[] memo` at each node to count all paths in strict \(O(V + E)\) time instead of exponential path enumeration!)*
4. **Directed Cycle Detection (3-Color DFS State)**: In a **Directed Graph**, simply seeing an already-`visited` node during DFS does *not* mean there is a cycle (two diamond paths `A -> B -> D` and `A -> C -> D` both visit `D` without any cycle!). To detect a directed cycle, each vertex maintains **3 states** (or two boolean arrays `visited[]` and `stackFlag[]` as in `Graph.md`):
   - `0 = Unvisited (White)`
   - `1 = Visiting / Currently on Recursion Stack (Gray / stackFlag = true)`
   - `2 = Completely Explored (Black / visited = true, stackFlag = false)`
   **A directed graph contains a cycle if and only if DFS encounters an edge pointing to a vertex currently in state `1 (Visiting / On Stack)` (a back-edge)!**
5. **Topological Sort and Task Scheduling (`DAG`, $O(V + E)$)**: Given \(N\) tasks labeled `0 .. N-1` with prerequisite pairs `(U, V)` where directed edge \(U \rightarrow V\) means *"$U$ must complete before $V$ can be scheduled,"* a **Topological Ordering** is a linear sequence of all vertices where every prerequisite appears before its dependents.
   While you can compute a topological sort on an acyclic graph by running DFS and pushing each vertex onto a stack **only after all of its descendants have finished (`post-order reversal`)**, **Kahn's In-Degree Algorithm** is far superior in production because it is iterative (never overflows the call stack on deep DAGs), naturally exposes **parallel execution waves/stages**, and **solves Task Scheduling feasibility (`TopologicalOrder.Count == N`) by detecting cycles automatically**:
   ```text
   KAHN'S TOPOLOGICAL SORT (Iterative Queue + Task Scheduling Cycle Detection):
   1. Compute InDegree[v] (number of incoming prerequisite edges) for every vertex v in V.
   2. Enqueue all vertices with InDegree[v] == 0 into Queue Q (these have zero unmet prerequisites!).
   3. While Q is not empty:
        Dequeue u from Q and append u to TopologicalOrder.
        For each neighbor v of u (edge u -> v):
          Decrement InDegree[v] by 1.
          If InDegree[v] == 0: Enqueue v into Q.
   4. If TopologicalOrder.Count < V.Count:
        Task scheduling is IMPOSSIBLE! (The remaining vertices with InDegree > 0 contain a cycle!)
   ```

### Shortest Paths (`Dijkstra`, `A*`, `Bellman-Ford`) and Union-Find (`Disjoint Set`)

When edges carry numerical weights (such as network link latency in ms, routing cost, or distance):
- **Dijkstra's Algorithm ($O((V + E) \log V)$)**: Finds the shortest path from a source vertex to all other vertices when **all edge weights are non-negative ($w(u, v) \ge 0$)**. Maintains a `dist[]` array (initialized to $\infty$, `dist[src] = 0`) and a **Min-Heap (`PriorityQueue<(int Vertex, long Dist), long>`)**. Repeatedly extracts the vertex $u$ with the smallest tentative distance; if `d > dist[u]`, skips it as a stale heap entry; otherwise relaxes each outgoing edge $(u, v)$: if $\text{dist}[u] + w(u, v) < \text{dist}[v]$, updates $\text{dist}[v]$ and pushes $(v, \text{dist}[v])$ into the Min-Heap.
- **A\* Search**: Guides Dijkstra toward a specific target vertex $t$ by ordering the Min-Heap by $f(u) = g(u) + h(u)$, where $g(u) = \text{dist}[u]$ is the exact cost from the start and $h(u)$ is an **admissible heuristic** (such as straight-line Euclidean/Haversine distance to $t$ that never overestimates the true remaining cost).
- **Bellman-Ford ($O(V \cdot E)$)**: Relaxes all $E$ edges $V - 1$ times; required only when edges can have **negative weights** (such as currency arbitrage or negative cost credits) and detects negative-weight cycles on the $V$-th pass.
- **Union-Find (Disjoint Set) and Minimum Spanning Trees (`Kruskal's`)**: To track dynamic connected components across $N$ nodes (or build a Minimum Spanning Tree by sorting edges by weight in **Kruskal's Algorithm** and adding edges that connect disjoint components), **Union-Find** maintains an array `parent[i]` and `rank[i]`. Combining **Path Compression** (`parent[x] = Find(parent[x])`) with **Union by Rank** executes every `Find(x)` and `Union(x, y)` in \(O(\alpha(N))\); inverse Ackermann time, which is literally \(\le 4\) array lookups for any \(N\) in the observable universe[^sedgewick-alg]!

---

## 5. Hash tables, heaps, and graphs in the Atlas platform

In **Atlas**, these data structures power three critical control-plane engines:
- **Kahn's Topological Sort for Multi-Package Firmware DAGs**: Modern edge devices do not install a single monolithic binary; an update consists of a DAG of 15 interdependent packages (`Bootloader v2 -> KernelPatch v5 -> ContainerRuntime v3 -> MLModel v9`). Before publishing a rollout, Atlas runs **Kahn's Algorithm** to verify the package graph is acyclic and emits the exact parallel installation waves to the device agent.
- **4-ary Min-Heap Timer Wheel for Rollout Timeout Scheduling**: Each Rollout Coordinator tracks tens of thousands of in-flight device installation timeouts in a Min-Heap ordered by `ExpirationTimestampTicks`, waking up only when the root timer expires.
- **External $K$-Way Merge Sort in Telemetry Compaction**: When compacting daily SSTable segments on storage nodes, Atlas streams sorted runs through a Min-Heap merger, merging 50 GB of telemetry records using less than 64 MB of working RAM.

---

## Before you move on

**Hash Tables** achieve \(O(1)\) average lookup via **Separate Chaining** (contiguous `buckets[] + entries[]` in .NET `Dictionary`) or **Open Addressing** (Linear Probing with **Tombstones** and **Robin Hood** PSL balancing), protected against \(O(N^2)\) hash-flooding DoS attacks by **randomized SipHash seeds**.
**Binary and $d$-ary Heaps** pack a complete tree into a flat array (`2i+1`, `2i+2`), supporting \(O(\log N)\) `SiftUp`/`SiftDown`, \(O(N)\) bottom-up **Floyd's `Heapify`**, and \(O(N \log K)\) Top-$K$ streaming.
For sorting, **IntroSort** (`QuickSort + HeapSort + InsertionSort`) dominates in-memory sorting, while **External $K$-Way Merge Sort** uses a Min-Heap over buffered file runs to sort multi-terabyte datasets in bounded RAM.
On **Graphs**, **BFS** finds unweighted shortest paths, **3-color DFS** and **Kahn's In-Degree Queue** detect directed cycles and compute **Topological Sorts**, **Dijkstra / A\*** find non-negative weighted shortest paths, and **Union-Find** tracks dynamic connectivity in \(O(\alpha(N))\) time.

To test your understanding before moving to Module 21, analyze this changed case: suppose an engineer writes a directed cycle detector using standard DFS with a **single boolean `bool[] visited` array** (marking `visited[u] = true` when entering `u`, and returning `hasCycle = true` whenever an outgoing edge from `u` points to any neighbor `v` where `visited[v] == true`).
Construct a 3-node Directed Acyclic Graph (`DAG`) where that algorithm **falsely reports a cycle**, and explain why distinguishing **`Visiting (On Current Recursion Stack)`** from **`Visited (Already Completed Backtracking)`** is mandatory for directed graphs.
If you cannot sketch that 3-node diamond/triangle DAG (`A -> B`, `A -> C`, `B -> C`) in ten seconds, review **Section 4** before continuing.

---

### Sources

[^skiena-adm]: **Tier 2: Steven S. Skiena, *The Algorithm Design Manual* (Springer, 3rd ed., 2020), Chapters 3, 4, 7, & 8**: Hash tables, heaps, mergesort/quicksort/external sorting, BFS/DFS, topological sort, Dijkstra's algorithm, and minimum spanning trees.
[^sedgewick-alg]: **Tier 2: Robert Sedgewick and Kevin Wayne, *Algorithms* (Addison-Wesley, 4th ed., 2011), Chapters 2, 3, & 4**: Separate chaining vs linear probing, Floyd's linear-time heap construction proof, Introsort, DAG cycle detection, Kahn's algorithm, and Union-Find with path compression.
