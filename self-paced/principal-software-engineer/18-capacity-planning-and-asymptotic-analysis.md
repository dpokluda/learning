# 18: Capacity Planning and Asymptotic Analysis

In a Principal architecture review or system design interview, proposing a distributed architecture without sizing the numbers is guesswork.
Should your metadata table live on a single PostgreSQL primary or be sharded across 32 nodes?
Can a single C# worker fleet of 10 servers absorb `50,000 requests/second` when each request waits `150 ms` on a downstream call, or will the thread pool starve in two seconds?
Will storing 3 years of raw device telemetry cost `$500/month` on 2 TB of SSDs or `$500,000/month` on 2 petabytes of NVMe?
You answer every one of those questions in under three minutes by combining **asymptotic complexity (`Big-O`)**, **physical hardware latency numbers**, **Little's Law**, and **back-of-the-envelope capacity math**.
This module bridges theoretical algorithmic complexity with real hardware constants and gives you a repeatable, step-by-step framework for sizing compute, memory, network bandwidth, and multi-year storage.

> **Prerequisite:** [Module 01: Distributed Systems Models and Failure Modes](./01-distributed-systems-models-and-failure-modes.md) and [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md).

---

## 1. Big-O complexity in real systems: worst-case, amortized, and hardware locality

**Big-O notation**, written $O(f(n))$, describes the asymptotic upper bound on how an algorithm's execution time or memory footprint grows as the input size $n$ scales toward infinity[^skiena-adm][^sedgewick-alg].
While every software engineer knows the standard complexity hierarchy, a Principal engineer evaluates Big-O through the lens of **production cluster behavior**:

| Complexity class | Name | Scaling behavior when $n$ grows from $10^3$ to $10^6$ ($1000\times$) | Production examples and architectural impact |
| --- | --- | --- | --- |
| **$O(1)$** | Constant | Operations stay flat ($1\times$) regardless of dataset size. | Hash table lookup (average), array index access, appending to a WAL segment, Token Bucket refill check. |
| **$O(\log n)$** | Logarithmic | Operations grow from $\approx 10$ steps to $\approx 20$ steps ($2\times$). | Binary search on a sorted array, Consistent Hash Ring token lookup, B+Tree page lookup ($\log_b n$), Heap insert/extract. |
| **$O(n)$** | Linear | Operations grow $1000\times$ in direct proportion to $n$. | Unindexed full table scan, computing a payload CRC32C checksum, Rendezvous Hashing across $n$ servers, Floyd's `Heapify`. |
| **$O(n \log n)$** | Log-Linear | Operations grow $\approx 2000\times$. | MergeSort, QuickSort (average), HeapSort, external merge-sort compaction of SSTables. |
| **$O(n^2)$** | Quadratic | Operations explode by **$1{,}000{,}000\times$**! | All-to-all heartbeat mesh across $n$ nodes, nested-loop unindexed joins, Bubble/Selection/Insertion sort on random inputs. |
| **$O(2^n)$ / $O(n!)$** | Exponential / Factorial | Intractable above $n \approx 30\text{ to }40$ (or $n \approx 12$ for $n!$). | Unpruned subset generation, exact Traveling Salesman / optimal graph partitioning without heuristics. |

Two nuances of asymptotic analysis frequently separate textbook answers from production reality:

### 1. Worst-Case vs Average-Case vs Amortized Complexity

Suppose a `List<T>` (dynamic array) in C# is full at capacity $N$, and you call `.Add(item)`.
That single `.Add` call must allocate a new array of size $2N$ and copy all $N$ existing elements, taking **$O(N)$ worst-case time**.
Yet we say appending to a `List<T>` is **$O(1)$ amortized time**: why?
Because after doubling from $N$ to $2N$, the next $N$ consecutive `.Add` calls execute in $O(1)$ array-slot assignments without resizing; spreading the $N$ copy operations across the $N$ cheap insertions yields a constant average cost of $\le 2$ operations per insert[^skiena-adm].
In production systems, **amortized $O(1)$ is great for batch throughput (`p50`), whereas worst-case spikes govern tail latency (`p99.9`)**!
For example, LSM-Tree writes (Module 06) are amortized sequential operations, but if background SSTable compaction falls behind and stalls incoming writes, the `p99.9` write latency spikes for seconds.

### 2. Why Hardware Cache Locality Beats Pointer-Chasing Constants

Big-O notation intentionally drops constant factors: an algorithm taking $1000 \cdot n$ nanoseconds and an algorithm taking $0.5 \cdot n$ nanoseconds are both $O(n)$.
On modern CPUs, **the largest constant factor in software is the memory hierarchy gap (`200x` between L1 cache and main RAM)**!
Consider searching for an integer across $n = 512$ sorted items:
- A balanced pointer-based **Binary Search Tree (`Node` objects scattered on the GC heap)** performs $\log_2(512) = 9$ comparisons (`O(log n)`), but every pointer hop chases a random heap address that misses L1/L2 CPU cache (`9 * 100 ns = 900 ns`).
- A contiguous **flat array scan or SIMD/B-Tree node search** pulls 64-byte CPU cache lines sequentially via the hardware prefetcher at `~0.5 ns` per element, completing in a fraction of the time despite doing more comparisons!
That is why production storage engines and high-performance libraries pack keys into **contiguous arrays (`Span<T>`, B+Tree pages, open-addressed hash tables, array-backed binary heaps)** rather than pointer-linked node graphs.

---

## 2. Latency numbers every Principal engineer should know

Originally compiled by Jeff Dean at Google and popularized by Peter Norvig, the **Hardware Latency Hierarchy** is the ruler by which you judge every system design proposal[^dean-numbers]:

| Operation | Physical time | Relative scale (if `1 ns = 1 second`) | Architectural takeaway |
| --- | --- | --- | --- |
| **L1 cache reference** | `0.5 ns` | `0.5 seconds` | Instantaneous; keep hot inner-loop state in CPU registers/L1. |
| **Branch mispredict** | `5 ns` | `5 seconds` | Branchless code and sorted predicates matter in tight loops. |
| **L2 cache reference** | `7 ns` | `7 seconds` | Contiguous structs (`Span<T>`) stay inside L1/L2 cache lines. |
| **Mutex lock / unlock (uncontended)** | `25 ns` | `25 seconds` | Uncontended locks are cheap; **contended** locks trigger OS context switches (`~3-5 us`)! |
| **Main memory (RAM) reference** | `100 ns` | `1.5 minutes` | Pointer chasing across random GC heap objects costs `200x` L1! |
| **Compress 1 KB with LZ4 / Snappy** | `3,000 ns` (`3 us`) | `50 minutes` | Compressing payloads before network/disk I/O is almost always a net win! |
| **Send 2 KB over commodity network** | `20,000 ns` (`20 us`) | `5.5 hours` | Serialization + kernel socket traversal is `200x` slower than RAM. |
| **NVMe SSD random read (4 KB)** | `50,000 ns` (`50 us`) | `14 hours` | Modern NVMe SSDs are `200x` faster than spinning disks on random seeks. |
| **Read 1 MB sequentially from RAM** | `250,000 ns` (`250 us`) | `2.9 days` | Memory bandwidth is `~4-20+ GB/s`; copying giant buffers repeatedly hurts. |
| **Round trip within same datacenter** | `500,000 ns` (`0.5 ms`) | `5.8 days` | **10 sequential microservice RPCs = `5 ms` of pure wire wait!** |
| **Read 1 MB sequentially from NVMe SSD** | `1,000,000 ns` (`1 ms`) | `11.5 days` | Sequential SSD reads (`1-4 GB/s`) are only `4x` slower than RAM scans! |
| **Spinning disk (HDD) seek** | `10,000,000 ns` (`10 ms`) | `4 months` | Random I/O on spinning disks caps throughput at `~100 IOPS` per drive. |
| **Read 1 MB sequentially from HDD** | `20,000,000 ns` (`20 ms`) | `7.6 months` | On HDDs (and object storage), only sequential large blocks are viable. |
| **Cross-continent packet RTT (US <-> EU)** | `150,000,000 ns` (`150 ms`) | **`4.8 years`!** | Speed of light in fiber; synchronous cross-continent consensus costs `150 ms` minimum! |

Internalize the **Humanized Time Scale** column (`1 ns = 1 second`):
- Reading from local RAM is reaching for your coffee mug (`90 seconds`).
- Making **one synchronous RPC inside the same datacenter** (`0.5 ms`) is **waiting nearly six days**!
- Making **one synchronous consensus round trip from Virginia to Frankfurt** (`150 ms`) is **waiting nearly five years**!
Once you feel those ratios viscerally, you immediately spot why an N+1 loop of 50 sequential datacenter RPCs (`50 * 0.5 ms = 25 ms` minimum wire time) or synchronous cross-continent two-phase locking destroys an interactive API.

---

## 3. Back-of-the-envelope capacity planning: Little's Law, compute, and storage

When scoping a system design, do not reach for a calculator to compute `86,400 * 365 * 3.14`.
Use **powers of ten** and **three golden shortcuts** that get you within 15% of the exact number in your head:

### Golden Shortcut 1: Time and Byte Conversions

```
1 Day    = 86,400 seconds      ~ 10^5 seconds  (100,000 s)
1 Month  = 30 days             ~ 2.5 x 10^6 seconds
1 Year   = 365 days            ~ 3 x 10^7 seconds
3 Years  ~ 1,000 days          ~ 10^8 seconds  (100,000,000 s)
```

Because $1\text{ day} \approx 10^5\text{ seconds}$, converting between **Daily Volume** and **Average Requests Per Second (RPS)** is instant mental arithmetic:
- `1 million requests/day` $\approx \frac{10^6}{10^5} = \mathbf{10\text{ RPS}}$.
- `100 million requests/day` $\approx \frac{10^8}{10^5} = \mathbf{1{,}000\text{ RPS}}$.
- `10 billion requests/day` $\approx \frac{10^{10}}{10^5} = \mathbf{100{,}000\text{ RPS}}$.

Always pair average RPS with a **Peak-to-Average Multiplier** (typically $2\times\text{ to }5\times$ for diurnal human traffic, or $10\times$ for event-driven rollouts) when sizing compute and network bandwidth!

For storage units, memorize the powers of $10^3$:
- **$10^3$**: `KB` (Kilobyte): typical small metadata record or telemetry event (`0.5 to 2 KB`).
- **$10^6$**: `MB` (Megabyte): high-res compressed photo (`1 MB`), 1,000 JSON documents of `1 KB`.
- **$10^9$**: `GB` (Gigabyte): firmware binary (`200 MB to 1 GB`), HD video clip, in-memory cache shard (`16 to 64 GB`).
- **$10^{12}$**: `TB` (Terabyte): comfortable single relational/NoSQL database shard size (`1 to 2 TB`).
- **$10^{15}$**: `PB` (Petabyte): 1,000 database shards; large data-warehouse / blob archive scale.
- **$10^{18}$**: `EB` (Exabyte): hyperscale cloud provider storage fabric.

### Golden Shortcut 2: Little's Law and Application Server Sizing

In queueing theory, **Little's Law** states that for any stable system in steady state, the average number of concurrent requests in flight inside the system ($L$) equals the average arrival rate ($\lambda$, in requests/second) multiplied by the average time a request spends in the system ($W$, in seconds)[^littles-law-1961]:

$$
L = \lambda \times W
$$

Look at how Little's Law sizes connection pools, thread pools, and application fleets:
1. **Concurrency / Connection Pool Sizing**: Suppose your service handles $\lambda = 5{,}000\text{ RPS}$, and each request calls a downstream database that takes $W = 20\text{ ms} = 0.02\text{ s}$. By Little's Law, across your fleet there are on average $L = 5{,}000 \times 0.02 = \mathbf{100\text{ concurrent DB queries in flight}}$ at any instant. If the database slows down from `20 ms` to `2 seconds` ($W = 2.0\text{ s}$) during a lock spike, required in-flight concurrency immediately jumps $100\times$ to $L = 5{,}000 \times 2.0 = \mathbf{10{,}000\text{ concurrent connections}}$; instantly exhausting your connection pool unless bounded by a Bulkhead or Circuit Breaker (Module 12)!
2. **Thread-Bound vs CPU-Bound Server Sizing (The Interview Kickstart Formula)**:
   Suppose a commodity cloud VM has **10 to 16 CPU cores**, and in a thread-per-request (or CPU/IO mixed) workload handles `10 to 20 active threads per core` (`~100 threads per server`).
   If each request takes $T$ milliseconds on a worker thread, one thread completes $\frac{1{,}000}{T}$ requests/sec, so 100 threads have a theoretical ceiling of $\frac{100{,}000}{T}\text{ RPS}$.
   Because production servers must run at **30% to 40% average utilization headroom** (to prevent queueing latency explosions as utilization approaches 100%, absorb diurnal peaks, and survive losing one availability zone out of three), the safe sustained throughput per server is roughly:
   $$
   \text{Safe RPS per Server} \approx \frac{1}{3} \times \frac{100{,}000}{T_{\text{ms}}} \approx \frac{30{,}000}{T_{\text{ms}}}
   $$
   - If a heavy synchronous request takes $T = 200\text{ ms}$ of active work, one server safely handles $\approx \frac{30{,}000}{200} = \mathbf{150\text{ RPS}}$.
   - For fast, non-blocking `async/await` C# endpoints where CPU time per request is $1\text{ to }3\text{ ms}$, a practical Principal rule of thumb is **~5,000 to 8,000 RPS per commodity 16-vCPU server**.

### Golden Shortcut 3: Multi-Year Storage and Shard Count Sizing

To size a database tier for **3 years of retention** ($\approx 1{,}000\text{ days} = 10^8\text{ seconds}$):

$$
\text{Total Raw Storage (3 Years)} = \text{WritesPerSecond} \times \text{BytesPerWrite} \times 10^8\text{ seconds}
$$

Multiply that raw number by the **Replication Factor (`N = 3`)** (plus `~30%` for indexes/WAL overhead) to get total physical disk capacity across the cluster, and divide by **`1 TB` per shard** to estimate the minimum number of storage shards required for data volume (checking separately whether **IOPS / write throughput** requires even more shards!).

---

## 4. Worked capacity plan for the Atlas platform

Let us execute a complete Principal-level back-of-the-envelope capacity plan for **Atlas** (`500 million` active connected devices globally):

1. **Telemetry Write Throughput (RPS)**:
   - Each of the $5 \times 10^8$ devices sends a health heartbeat once every `5 minutes` (`300 seconds`).
   - **Average Write RPS**: $\lambda_{\text{avg}} = \frac{5 \times 10^8}{300\text{ s}} \approx \mathbf{1.67\text{ million writes/sec}}$ globally.
   - **Peak Write RPS (`3x` reconnect/rollout burst)**: $\lambda_{\text{peak}} = 3 \times 1.67\text{M} \approx \mathbf{5.0\text{ million writes/sec}}$.
2. **Network Ingress Bandwidth**:
   - Each telemetry record is `1 KB` ($10^3$ bytes) uncompressed (`~300 bytes` compressed with LZ4).
   - **Average Ingress**: $1.67 \times 10^6 \times 1\text{ KB} \approx \mathbf{1.67\text{ GB/sec}}$ (`13.4 Gbps`).
   - **Peak Ingress**: $5.0 \times 10^6 \times 1\text{ KB} \approx \mathbf{5.0\text{ GB/sec}}$ (`40 Gbps` across 6 global regions, or `~6.7 Gbps` per region; easily handled by commodity 25 Gbps NICs).
3. **Stateless Ingestion Gateway Fleet Sizing**:
   - Using non-blocking C# `async/await` gateways rated at a conservative **8,000 RPS per 16-vCPU VM** (already factoring in 35% CPU headroom):
   - **Gateway VMs required for Peak (`5M RPS`)**: $\frac{5{,}000{,}000}{8{,}000} = \mathbf{625\text{ Gateway VMs globally}}$ (`~105 VMs` per region across 6 regions).
4. **Hot Telemetry Storage Sizing (90 Days in Cassandra LSM-Tree + 3-Year Cold Archive)**:
   - **Daily Raw Telemetry Volume**: $1.67 \times 10^6\text{ writes/s} \times 10^5\text{ s/day} \times 300\text{ B (LZ4 compressed)} \approx \mathbf{50\text{ TB/day}}$.
   - **90-Day Hot Operational Retention (`Replication Factor N = 3`)**: $50\text{ TB/day} \times 90\text{ days} \times 3 = \mathbf{13.5\text{ PB}}$ across the global Cassandra fleet. At `2 TB` of NVMe per node, that requires **~6,750 storage nodes** globally (`~1,125` per region), which also trivially absorbs the `1.67M writes/sec` (`~750 writes/sec per replica node`, well below LSM-Tree capacity of `20,000+ writes/sec/node`).
   - **Device Twin Current-State Store**: $5 \times 10^8\text{ devices} \times 4\text{ KB per Twin} = \mathbf{2\text{ TB raw}}$ (`6 TB` with $N = 3$ replication), fitting comfortably in **64 shards** (sized for IOPS rather than disk volume!).

---

## Before you move on

Asymptotic **Big-O** analysis tells you how an algorithm scales as input $n$ grows, while **hardware latency numbers** and **cache-locality constants** govern real-world execution speed (`0.5 ns` L1 cache vs `100 ns` RAM vs `50 us` NVMe vs `0.5 ms` intra-DC RTT vs `150 ms` cross-continent RTT).
In capacity planning, memorizing $1\text{ day} \approx 10^5\text{ seconds}$ and $3\text{ years} \approx 10^8\text{ seconds}$, applying **Little's Law (`L = lambda * W`)** to size concurrency and connection pools, keeping server fleets at **30% to 40% average utilization headroom** (`~30,000 / T_ms` or `~5,000-8,000 RPS` for fast async services), and sizing shards for **both storage volume (`~1-2 TB/shard`) and peak IOPS** lets you turn vague requirements into concrete engineering blueprints in minutes.

To test your understanding before moving to Module 19, solve this quick capacity problem in your head: a new audit microservice receives **200 million write events per day**, where each event is **2 KB**.
1. What is the **average write RPS** and the **peak write RPS** (assuming a $4\times$ peak-to-average ratio)?
2. If each synchronous write takes **50 ms (`0.05 s`)** end-to-end, what is the average number of **concurrent in-flight requests $L$** across the fleet at peak load according to **Little's Law**?
3. How many terabytes of **raw storage** will 3 years (`1,000 days`) of these events consume, and how many **1 TB shards** (with Replication Factor $N = 3$) are needed?
If any of those three steps takes more than sixty seconds, review **Section 3** before continuing.

---

### Sources

[^skiena-adm]: **Tier 2: Steven S. Skiena, *The Algorithm Design Manual* (Springer, 3rd ed., 2020), Chapters 1–2**: Big-O asymptotic notation, worst-case vs average-case vs amortized complexity, and back-of-the-envelope algorithmic estimation.
[^sedgewick-alg]: **Tier 2: Robert Sedgewick and Kevin Wayne, *Algorithms* (Addison-Wesley, 4th ed., 2011), Chapter 1**: Mathematical analysis of algorithms, tilde approximations, and memory/cache cost models.
[^dean-numbers]: **Tier 4: Jeff Dean (*Designs, Lessons and Advice from Building Large Distributed Systems*, LADIS 2009) and Peter Norvig (*Teach Yourself Programming in Ten Years*)**: Canonical hardware latency hierarchy ("Numbers Everyone Should Know").
[^littles-law-1961]: **Tier 1: John D. C. Little, "A Proof for the Queuing Formula: $L = \lambda W$" (*Operations Research*, Vol. 9, No. 3, 1961, pp. 383–387)**: Fundamental theorem relating steady-state queue occupancy, arrival rate, and residence time.
