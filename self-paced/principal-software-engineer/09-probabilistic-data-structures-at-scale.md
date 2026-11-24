# 09: Probabilistic Data Structures at Scale

Exactness is expensive.
If a service needs to check whether a 64-byte string key exists in a set of 1 billion keys, storing those 1 billion keys in an exact `HashSet<string>` requires over 64 GB of RAM (plus object header and bucket pointer overhead).
If you need to track the request frequency of every unique IPv6 address or URL across a stream of 10 billion events per hour to detect heavy hitters, an exact `Dictionary<string, long>` will exhaust server memory in minutes.
And if you need to count the number of unique devices (`COUNT(DISTINCT DeviceId)`) that connected across 50 regions today, shipping raw sets of hundreds of millions of IDs across the network to intersect them is prohibitively slow.

At Principal scale, the first question to ask when confronted with a massive state problem is: **"Do we actually need 100% mathematical exactness, or can we trade a tiny, provably bounded error rate (`< 1%`) for a $50\times$ to $1000\times$ reduction in memory?"**
When the answer is the latter, we reach for **probabilistic data structures (sketches)**.
This module covers the three essential probabilistic structures in systems engineering: **Bloom Filters** (for set membership), **Count-Min Sketch** (for streaming frequency estimation and heavy hitters), and **HyperLogLog** (for streaming cardinality estimation).

> **Prerequisite:** [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md) (specifically SSTable read paths) and [Module 08: Data Partitioning and Hashing Schemes](./08-data-partitioning-and-hashing-schemes.md).

---

## 1. Bloom filters: zero false negatives in under 10 bits per key

Suppose an LSM-Tree tablet server (Module 06) holds 20 immutable SSTable files on disk, and a client queries a key `K` that happens to exist only in the oldest SSTable (or does not exist in the database at all).
Without an in-memory filter, the storage engine would have to binary-search the index and perform a disk read on all 20 SSTables just to discover that 19 of them do not contain `K`[^bigtable-2006].
We want a tiny in-memory structure per SSTable that answers the question *"Could key `K` possibly be in this file?"* with two asymmetric guarantees:
1. **Zero False Negatives (`FN = 0%`)**: If the filter says *"No, `K` is not in the set,"* then `K` is **100% guaranteed not to be in the set**, and we can safely skip the disk read!
2. **Bounded False Positives (`FP = p`, e.g., `1%`)**: If the filter says *"Maybe, `K` might be in the set,"* there is a small probability $p$ that `K` is not actually there and we perform one wasted disk read, which the SSTable block index immediately resolves cleanly.

Invented by Burton Howard Bloom in 1970, a **Bloom Filter** provides that exact asymmetric contract[^bloom-1970]:

```
Bloom Filter: m = 16 bits (initialized to 0), k = 3 hash functions (h1, h2, h3)

Insert("Atlas"):   h1 -> 2,   h2 -> 7,   h3 -> 13
Insert("Quorum"):  h1 -> 5,   h2 -> 7,   h3 -> 11

Bit Index:   0   1   2   3   4   5   6   7   8   9  10  11  12  13  14  15
           +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
Bit Array: | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 0 |
           +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                     ^           ^       ^               ^       ^
                     |           |       |               |       |
  Query("Raft"):     |           |       |               |       |
    h1("Raft") -> 2 (1)          |       |               |       |
    h2("Raft") -> 9 -------------+-------+---> Bit 9 is 0! DEFINITELY NOT IN SET!

  Query("Paxos"):
    h1("Paxos") -> 2 (1), h2("Paxos") -> 5 (1), h3("Paxos") -> 11 (1)
    All 3 bits are 1 (set by "Atlas" and "Quorum") -> FALSE POSITIVE ("Maybe in set")
```

### Mechanics and the Kirsch-Mitzenmacher Optimization

An empty Bloom filter consists of a bit array of $m$ bits, all initialized to `0`, and $k$ independent uniform hash functions $h_1, h_2, \dots, h_k$, each mapping an input key to a bit position in $\{0, 1, \dots, m - 1\}$:
- **To `Add(item)`**: Compute the $k$ bit positions $h_1(\text{item}), \dots, h_k(\text{item})$ and set all $k$ bits to `1`.
- **To `MightContain(item)`**: Compute the same $k$ bit positions. If **any** of the $k$ bits is `0`, return `false` (*definitely absent*). If **all** $k$ bits are `1`, return `true` (*possibly present*).

Computing $k = 7$ separate cryptographic or Murmur hashes per lookup would waste CPU cycles.
In 2006, Adam Kirsch and Michael Mitzenmacher proved a wonderful practical result (**Kirsch-Mitzenmacher Double Hashing**)[^kirsch-mitzenmacher-2006]: you only need to compute **two** independent 64-bit hashes $g_1(x)$ and $g_2(x)$ (or split a single 128-bit MurmurHash3 / xxHash128 output into two 64-bit halves!) and synthesize all $k$ hash positions using linear combination:

$$
h_i(x) = \bigl(g_1(x) + i \cdot g_2(x)\bigr) \bmod m \qquad \text{for } i = 0, 1, \dots, k - 1
$$

This produces the exact same asymptotic false-positive rate as $k$ completely independent hash functions at a fraction of the CPU cost.

### Deriving the False-Positive Rate and Optimal Sizing Formulas

In a Principal design review, you should be able to derive and apply the sizing math of a Bloom filter in thirty seconds[^bloom-1970]:
1. When we insert 1 element with 1 hash function into an $m$-bit array, the probability that a specific bit is *not* set to `1` is $1 - \frac{1}{m}$.
2. With $k$ hash functions and $n$ inserted elements ($kn$ total bit-setting operations), the probability that a specific bit is **still `0`** after all $n$ insertions is:
   $$
   P(\text{bit is } 0) = \left(1 - \frac{1}{m}\right)^{kn} \approx e^{-kn/m}
   $$
   (using the standard calculus identity $\lim_{m \to \infty} (1 - 1/m)^{-m} = e$).
3. Therefore, the probability that a specific bit is `1` is $1 - e^{-kn/m}$. For a non-existent query element to trigger a **false positive**, *all $k$* of its hashed bit positions must independently happen to be `1`:
   $$
   p = P(\text{False Positive}) \approx \left(1 - e^{-kn/m}\right)^k
   $$
4. Given a memory budget of $m/n$ bits per element, what number of hash functions $k$ minimizes the false positive rate $p$? Taking the derivative of $\ln p$ with respect to $k$ and setting it to zero yields the **optimal number of hash functions**:
   $$
   k_{\text{opt}} = \frac{m}{n} \ln 2 \approx 0.693 \cdot \frac{m}{n}
   $$
   (At optimal $k$, exactly half of the bits in the array are `1` and half are `0`, maximizing Shannon entropy!)
5. Substituting $k_{\text{opt}}$ back into the false-positive equation and solving for the required bits per element $m/n$ for a target false-positive rate $p$ gives:
   $$
   \frac{m}{n} = -\frac{\ln p}{(\ln 2)^2} \approx -1.44 \log_2(p)
   $$

Plug in real numbers to build permanent intuition:
- Want a **1% false-positive rate (`p = 0.01`)**? You need $-1.44 \times \log_2(0.01) \approx \mathbf{9.58\text{ bits per element}}$ (say **10 bits/key**) and $k = 0.693 \times 10 \approx \mathbf{7\text{ hash functions}}$.
- For **1 billion keys**, an exact 64-byte string set takes `> 64 GB`; a 1% Bloom filter takes $10^9 \times 10\text{ bits} = 1.25\text{ GB}$ of RAM,regardless of whether each key is 8 bytes or 8 kilobytes!
- Want a **0.1% false-positive rate (`p = 0.001`)**? Add just 4.8 more bits per key ($\approx \mathbf{14.4\text{ bits/key}}$, $k = 10$).

### Why Standard Bloom Filters Cannot Delete (and the Counting / Cuckoo Variants)

Why can you not delete an item `"Atlas"` from a standard Bloom filter by setting its $k$ bits back to `0`?
Look back at the diagram above: bit `7` was set to `1` by *both* `"Atlas"` and `"Quorum"`.
If you clear bit `7` to `0` when deleting `"Atlas"`, a subsequent lookup for `"Quorum"` will see `0` at bit `7` and falsely report that `"Quorum"` is not in the set; **introducing a false negative and breaking the core invariant of the filter!**
- In LSM-Trees (BigTable, RocksDB, Cassandra), this is a non-issue because **SSTables are immutable**: you never delete from an SSTable's Bloom filter; when compaction writes a new merged SSTable, it builds a brand-new Bloom filter from scratch.
- When you genuinely need a mutable in-memory filter that supports deletes, use either a **Counting Bloom Filter** (replacing each 1-bit slot with a 4-bit saturating counter that increments on `Add` and decrements on `Remove`, costing $4\times$ memory) or a **Cuckoo Filter** (storing compact $f$-bit fingerprints in a cuckoo hash table, supporting fast lookups, deletions, and lower space overhead at low false-positive rates)[^cuckoo-2014].

---

## 2. Count-Min Sketch: streaming frequencies in fixed memory

A Bloom filter answers a boolean question: *"Have I seen `x` before?"* (`0` or `1`).
What if you need to answer a counting question over an unbounded stream of billions of events: *"Approximately how many times has item `x` appeared in the stream (`EstimateCount(x)`), and which items are the heavy hitters exceeding 1% of total traffic?"*

Introduced by Graham Cormode and S. Muthukrishnan in 2005, the **Count-Min Sketch (CMS)** generalizes the Bloom filter from a 1D bit array into a **2D integer matrix of depth $d$ rows and width $w$ columns**, initialized to all zeros[^cms-2005]:

```
Count-Min Sketch: depth d = 3 hash functions (h1, h2, h3), width w = 8 counters per row

Stream events applied: Increment("URL_A") x 50, Increment("URL_B") x 10, Increment("URL_C") x 5

         Col 0   Col 1   Col 2   Col 3   Col 4   Col 5   Col 6   Col 7
       +-------+-------+-------+-------+-------+-------+-------+-------+
Row h1 |   0   |   0   |  50   |   0   |  15   |   0   |   0   |   0   |  <- h1("URL_B")=4 collided with "URL_C"! (10+5=15)
       +-------+-------+-------+-------+-------+-------+-------+-------+
Row h2 |   0   |  10   |   0   |   0   |   0   |  55   |   0   |   0   |  <- h2("URL_B")=1 (no collision: 10)
       +-------+-------+-------+-------+-------+-------+-------+-------+
Row h3 |  60   |   0   |   0   |   5   |   0   |   0   |   0   |   0   |  <- h3("URL_B")=0 collided with "URL_A"! (10+50=60)
       +-------+-------+-------+-------+-------+-------+-------+-------+

EstimateCount("URL_B") = min( Row1[4]=15, Row2[1]=10, Row3[0]=60 ) = 10!
```

### Operations and Error Bounds

Each row $r \in \{0, \dots, d - 1\}$ has its own independent hash function $h_r : \text{Key} \to \{0, \dots, w - 1\}$:
- **To `Increment(item, count = 1)`**: For each row $r$ from `0` to $d - 1$, compute column $c = h_r(\text{item})$ and add `count` to `table[r, c]`.
- **To `EstimateFrequency(item)`**: For each row $r$ from `0` to $d - 1$, read `table[r, h_r(item)]` and return the **minimum** across all $d$ rows:
  $$
  \hat{f}(\text{item}) = \min_{0 \le r < d} \text{table}\bigl[r, h_r(\text{item})\bigr]
  $$

Why do we take the **minimum** across all $d$ rows?
Because every increment is positive, hash collisions with other keys in row $r$ can only **increase** `table[r, h_r(item)]` above the true frequency $f(\text{item})$; a counter can **never** be smaller than $f(\text{item})$.
Therefore, every row provides an upper bound $\text{table}[r, h_r(\text{item})] \ge f(\text{item})$, and the **smallest** of those $d$ upper bounds is the tightest, least-collided estimate of the true count!

How do you choose the width $w$ and depth $d$ for a desired accuracy?
Suppose the stream has total event count $N = \sum_{x} f(x)$, and you want the overestimation error to be at most $\varepsilon N$ with probability at least $1 - \delta$[^cms-2005]:

$$
w = \left\lceil \frac{e}{\varepsilon} \right\rceil \approx \frac{2.718}{\varepsilon}, \qquad d = \left\lceil \ln\left(\frac{1}{\delta}\right) \right\rceil
$$

Then with probability $\ge 1 - \delta$, the estimate $\hat{f}(x)$ satisfies:

$$
f(x) \le \hat{f}(x) \le f(x) + \varepsilon N
$$

Look at how tiny that matrix is: to guarantee that no item's count is overestimated by more than **0.1% of total stream volume ($\varepsilon = 0.001$)** with **99.9% confidence ($\delta = 0.001$)**, you need only $w = \lceil 2.718 / 0.001 \rceil = 2{,}719$ columns and $d = \lceil \ln(1000) \rceil = 7$ rows; just **$2{,}719 \times 7 \times 8\text{ bytes} \approx 152\text{ KB}$ of memory**, whether your stream has 1 million or 100 billion events!
Furthermore, because Count-Min Sketch is a linear sketch, **two sketches with identical `(d, w)` and hash seeds from different servers can be merged simply by adding their matrices element-wise** (`merged[r, c] = nodeA[r, c] + nodeB[r, c]`).
To maintain the **Top-$K$ Heavy Hitters** in real time, you pair a Count-Min Sketch with a small **Min-Heap of size $K$** (Module 20): whenever `Increment(item)` returns an estimated frequency greater than the smallest item at the root of the Min-Heap, you update or insert `item` in the Min-Heap.

---

## 3. HyperLogLog and streaming quantiles

Rounding out the probabilistic toolkit are two structures for cardinality and latency distribution:

### HyperLogLog (HLL) for Cardinality Estimation (`COUNT(DISTINCT)`)

Suppose you want to know how many *distinct* devices connected to Atlas today, where the same device might send 500 heartbeats.
A Count-Min Sketch counts total frequency per item; a Bloom filter tests membership; neither tells you the number of unique items without either storing every item or pre-knowing $n$.
Invented by Philippe Flajolet et al. (2007) and built natively into Redis (`PFADD / PFCOUNT`), BigQuery, and PostgreSQL, **HyperLogLog** estimates the cardinality of a set of billions of items using **1.5 KB to 16 KB of memory**[^hll-2007].

The intuition behind HyperLogLog is coin flipping:
If you hash every incoming item to a uniform 64-bit binary integer, half of all unique hashes start with `0...`, a quarter start with `00...`, an eighth start with `000...`, and only $1 \text{ in } 2^\rho$ unique hashes start with $\rho - 1$ leading zeros followed by a `1`.
Therefore, if the longest run of leading zeros you have observed across the stream is $\rho_{\max} = 20$, a naive estimate of the number of unique items seen is $2^{20} \approx 1\text{ million}$!
Because a single lucky hash could skew $\rho_{\max}$, HyperLogLog uses **stochastic averaging**: it uses the first $b$ bits of the 64-bit hash (for example, $b = 14$, giving $m = 2^{14} = 16{,}384$ buckets/registers of 6 bits each = **12 KB total**) to select a register $j \in [0, m-1]$, counts the position $\rho$ of the first `1`-bit in the remaining $64 - b$ bits of the hash, and updates `register[j] = max(register[j], rho)`.
Taking the bias-corrected **harmonic mean** across all $m$ registers (`alpha_m * m^2 / sum(2^(-register[j]))`) suppresses outlier hashes and yields a standard error of $\approx \frac{1.04}{\sqrt{m}}$ (**0.81% error in 12 KB of RAM**).
Like Count-Min Sketch, two HLL sketches from different regions merge losslessly by taking the element-wise maximum of their `16,384` registers!

### Streaming Quantiles (`t-digest`)

Never compute a cluster-wide `p99` latency by averaging the `p99` latencies of 50 individual servers (averaging percentiles is mathematically invalid when traffic across servers is uneven or multi-modal).
Instead, each server maintains a compact streaming quantile sketch such as Ted Dunning's **`t-digest`** (which clusters sorted sample centroids tightly near the extreme tails `q = 0.01` and `q = 0.99` and coarsely near the median `q = 0.50` in a few kilobytes) or a **Histogram of Exponential Buckets**, which can be merged across all 50 servers to compute the true global `p99` and `p99.9`.

| Probabilistic structure | Question it answers | Error type | Typical memory footprint | Mergeable across nodes? |
| --- | --- | --- | --- | --- |
| **Bloom Filter** | "Is key `x` in the set?" | False positives (`p`), **zero false negatives** | ~1.2 bytes (9.6 bits) per element for `p = 1%` | Yes (bitwise `OR` if same `m, k, seeds`) |
| **Count-Min Sketch** | "How many times did `x` occur?" | **Overestimation only** ($\le \varepsilon N$ w.p. $1-\delta$), zero underestimation | Fixed `100 KB - 1 MB` independent of key count | Yes (element-wise matrix addition `+`) |
| **HyperLogLog (HLL)** | "How many unique items were seen?" | Symmetric relative error ($\approx 1.04 / \sqrt{m}$) | Fixed `1.5 KB - 12 KB` for billions of items | Yes (element-wise register `max`) |
| **`t-digest`** | "What is the `p99` / `p99.9` value?" | High precision at tails (`q -> 0` or `1`), approximate at median | Fixed `2 KB - 10 KB` per metric stream | Yes (centroid merge) |

---

## 4. Probabilistic structures in the Atlas platform

In **Atlas**, all three sketches protect the hot path from memory exhaustion and unnecessary I/O:
- **Bloom Filters for SSTables and Cache Penetration Shielding**: Every immutable telemetry SSTable on disk has a 10-bit-per-key Bloom filter (`~1%` FP rate) pinned in RAM. In addition, the public Device Provisioning API maintains a regional Bloom filter of all valid registered `DeviceSerialNumber` values in memory at the API Gateway. When a misconfigured or malicious device floods the gateway with requests for non-existent serial numbers (a **cache penetration attack**, Module 11), the gateway's Bloom filter rejects 99% of non-existent IDs in 50 nanoseconds without touching Redis or PostgreSQL.
- **Count-Min Sketch + Min-Heap for Hot-Tenant Detection**: Every regional ingestion router feeds incoming `(TenantId, DeviceId)` keys into a rotating 1-minute Count-Min Sketch ($d = 5, w = 4096$). Whenever a key's estimated rate crosses the single-shard safe threshold, it is promoted into the **Hot-Key Registry**, triggering automatic key salting (Module 08) and per-tenant rate throttling (Module 12).
- **HyperLogLog for Real-Time Fleet Rollout Dashboards**: To display "Unique Devices Updated in the Last Hour" across 10,000 tenant rollouts without executing `SELECT COUNT(DISTINCT DeviceId)` over billions of rows, stream processors update one 12 KB HyperLogLog register array per rollout and merge them across regions in milliseconds.

---

## Before you move on

When exact state requires gigabytes of RAM or cross-cluster data shuffling, probabilistic data structures trade a small, mathematically controlled error bound for dramatic memory and latency savings.
A **Bloom Filter** uses an $m$-bit array and $k$ hash functions (efficiently synthesized via Kirsch-Mitzenmacher double hashing) to answer set membership with **zero false negatives** and false-positive rate $p \approx (1 - e^{-kn/m})^k$, minimized when $k = (m/n) \ln 2$ and $m/n \approx -1.44 \log_2 p$.
A **Count-Min Sketch** uses a $d \times w$ counter matrix and returns the **minimum** across $d$ hashed rows to estimate item frequencies with **zero underestimation** and bounded overestimation $\le \varepsilon N$ (`w = ceil(e / epsilon), d = ceil(ln(1 / delta))`).
And **HyperLogLog** tracks maximum leading-zero bit runs across $m = 2^b$ registers to estimate `COUNT(DISTINCT)` cardinality in 12 KB of RAM.

Before moving to Part III (Cloud Architecture, Networking, Caching, and Resiliency), test your understanding on this changed case: suppose an engineer suggests using a standard Bloom filter as a **deduplication filter** in an Atlas billing pipeline ("Before charging a customer for an event ID, check `if (bloomFilter.MightContain(eventId))`; if `true`, skip billing as a duplicate; if `false`, bill the customer and add `eventId` to the Bloom filter").
Why is using a Bloom filter in *that* direction a serious bug that will silently under-bill customers on 1% of valid unique events, whereas using a Bloom filter to check *"Should we query the exact deduplication database table to see if `eventId` was already billed?"* is 100% safe and saves 99% of database lookups?
If you cannot explain how the asymmetry between **False Positives** and **False Negatives** flips the correctness of those two designs, revisit **Section 1** before continuing.

---

### Sources

[^bigtable-2006]: **Tier 1: Fay Chang et al., "Bigtable: A Distributed Storage System for Structured Data" (*ACM OSDI*, 2006, pp. 205–218)**: Use of in-memory Bloom filters on Tablet Servers to avoid disk seeks on SSTables for non-existent rows or columns.
[^bloom-1970]: **Tier 1: Burton H. Bloom, "Space/Time Trade-offs in Hash Coding with Allowable Errors" (*Communications of the ACM*, Vol. 13, No. 7, 1970, pp. 422–426)**: Original paper introducing the Bloom filter and deriving its space and false-positive equations.
[^kirsch-mitzenmacher-2006]: **Tier 1: Adam Kirsch and Michael Mitzenmacher, "Less Hashing, Same Performance: Building a Better Bloom Filter" (*ESA*, 2006, pp. 456–467)**: Proof that two hash functions $g_1(x) + i \cdot g_2(x) \bmod m$ suffice to simulate $k$ hash functions without increasing asymptotic false-positive probability.
[^cuckoo-2014]: **Tier 1: Bin Fan, Dave G. Andersen, Michael Kaminsky, and Michael D. Mitzenmacher, "Cuckoo Filter: Practically Better Than Bloom" (*ACM CoNEXT*, 2014, pp. 75–88)**: Fingerprint-based cuckoo hashing supporting deletions with high space efficiency.
[^cms-2005]: **Tier 1: Graham Cormode and S. Muthukrishnan, "An Improved Data Stream Summary: The Count-Min Sketch and Its Applications" (*Journal of Algorithms*, Vol. 55, No. 1, 2005, pp. 58–75)**: Formal definition of Count-Min Sketch, `(epsilon, delta)` error bound proofs, and heavy-hitter algorithms.
[^hll-2007]: **Tier 1: Philippe Flajolet, Éric Fusy, Olivier Gandouet, and Frédéric Meunier, "HyperLogLog: The Analysis of a Near-Optimal Cardinality Estimation Algorithm" (*AofA*, 2007, pp. 127–146)**: Harmonic-mean register cardinality estimator with $1.04/\sqrt{m}$ relative error.
