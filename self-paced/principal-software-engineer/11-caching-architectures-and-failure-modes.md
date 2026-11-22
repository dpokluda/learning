# 11: Caching Architectures and Failure Modes

Horizontal scaling lets you add more database servers, but **caching** lets you avoid touching the database altogether for 90% to 99% of your read traffic.
As we will see in Module 18's hardware latency numbers, reading 1 KB from local in-process RAM takes ~100 nanoseconds, reading from an in-memory distributed cache like Redis over the local datacenter network takes ~500 microseconds, and querying a disk-backed database under load takes 2 to 20 milliseconds; a \(20{,}000\times\) span from top to bottom.
Caches exploit the **locality of reference** principle: data requested once (temporal locality) or near recently requested data (spatial locality) is highly likely to be requested again soon.
Yet caching is also the #1 source of catastrophic **metastable outages** in high-scale cloud services: a system that runs comfortably at 98% cache hit rate can collapse in three seconds if a hot key expires, a cache cluster reboots, or a concurrent write race poisons the cache with stale data forever.
This module examines where caches sit, compares the five read/write caching topologies, contrasts eviction policies from LRU to `W-TinyLFU`, and shows how to engineer defenses against the four production cache failure modes: **Cache Stampede (Thundering Herd)**, **Cache Penetration**, **Cache Avalanche**, and **Stale-Cache Write Races**.

> **Prerequisite:** [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md), [Module 08: Data Partitioning and Hashing Schemes](./08-data-partitioning-and-hashing-schemes.md), and [Module 09: Probabilistic Data Structures at Scale](./09-probabilistic-data-structures-at-scale.md).

---

## 1. Caching tiers: from edge CDN to L1/L2 application caches

A high-scale system does not have "a cache"; it has a **hierarchy of caches**, each trading staleness and invalidation difficulty against latency and bandwidth savings:

1. **Client & Browser Cache**: Controlled via HTTP `Cache-Control: max-age=...` and `ETag` / `If-None-Match` conditional requests. Zero network bandwidth on a hit, but impossible to invalidate immediately across millions of disconnected clients until TTL expiration.
2. **Content Delivery Network (CDN) & Static Content Hosting**: Geo-distributed edge POPs (Cloudflare, Azure Front Door, CloudFront) serving immutable static assets (firmware binaries, bundles, media) directly from edge storage without touching your origin compute fleet[^azure-caching].
3. **In-Process Application Cache (L1 Cache)**: A bounded in-memory structure (`IMemoryCache` in .NET, Caffeine in Java) inside each stateless worker process. Delivers ~100 ns latency with zero network serialization and zero hot-key network bottlenecks, at the cost of holding independent copies per worker (requiring short TTLs or a pub/sub invalidation bus so workers do not diverge for long).
4. **Distributed Shared Cache (L2 Cache)**: A partitioned cluster of dedicated memory nodes (Redis, Garnet, Memcached) shared by all application workers. Provides a single coherent cache view across the application fleet (~0.5 ms latency), partitioned via Consistent or Rendezvous Hashing (Module 08).
5. **Storage Engine Internal Cache**: The database's own B+Tree Buffer Pool or LSM-Tree Block Cache + OS Page Cache in RAM.

---

## 2. The five read/write caching topologies

How do your application code, your cache, and your authoritative database coordinate reads and writes?
Five patterns define the design space[^azure-caching]:

```
1. Cache-Aside (Lazy Loading):               2. Read-Through / Write-Through:
==============================               ================================
      +---> [ Distributed Cache ]            [ App ] <---> [ Cache Provider ] <---> [ Database ]
      |       (1. Read miss;                 (App talks ONLY to Cache; Cache synchronously
   [ App ]     3. Populate cache)             loads on miss and writes through to DB on write)
      |
      +---> [ Primary Database  ]            3. Write-Back (Write-Behind):
              (2. Load on miss;              =============================
               Write + Invalidate)           [ App ] --Write--> [ Cache (ACK!) ] --Async Batch--> [ DB ]
```

Let us compare all five patterns across read behavior, write behavior, consistency, and durability:

| Pattern | Read path | Write path | Strengths | Weaknesses / risks |
| --- | --- | --- | --- | --- |
| **Cache-Aside (Lazy Loading)** | App checks cache first; on a miss, app reads from DB, writes result into cache with a TTL, and returns it. | App writes directly to the DB first, then **deletes (invalidates)** the key in the cache. | Most common pattern; resilient to cache node failure (falls back to DB); caches only what is actually read; cached shape can differ from DB row. | First read of any key is always a miss ("cold penalty"); vulnerable to **stampedes** and **read/write race conditions** if naive. |
| **Read-Through** | App asks cache for key; on a miss, the cache library/proxy itself loads from the DB, caches it, and returns it. | Paired with Write-Through or explicit invalidation. | Keeps data-loading and request-coalescing logic cleanly inside the cache abstraction layer. | Requires cache provider plugin or wrapper library that understands your DB schema. |
| **Write-Through** | Any read hits the already-populated cache. | Every write synchronously updates **both** the cache and the underlying DB before returning success. | Cache is always warm and consistent for newly written data; zero durability loss on crash. | **Higher write latency** (every write waits for both cache and DB); pollutes the cache with "write-once, never-read" data unless paired with a short TTL. |
| **Write-Around** | App reads via Cache-Aside or Read-Through. | Writes go **directly to the DB**, intentionally bypassing the cache (leaving the item uncached until read). | Prevents bulk write jobs or write-heavy audit logs from flooding the cache and evicting hot read items. | Immediate subsequent read of newly written data always suffers a cache miss. |
| **Write-Back (Write-Behind)** | Reads hit the cache immediately (which holds the latest dirty state). | App writes **only to the cache** and receives an immediate `200 OK`; cache flushes batched mutations to the DB asynchronously. | **Highest write throughput and lowest write latency**; coalesces 1,000 rapid counter updates into 1 DB write. | **Data loss risk (`RPO > 0`)**: if the cache node crashes before flushing its dirty queue to the DB, acknowledged writes are lost unless backed by a replicated WAL. |

---

## 3. Cache eviction policies: from LRU to Window-TinyLFU

Because RAM is finite, a cache must evict existing entries when it reaches capacity.
Which entry should be evicted to maximize the future hit ratio?

1. **FIFO (First-In, First-Out)** and **LIFO (Last-In, First-Out)**: Evicts the oldest-inserted or newest-inserted item regardless of how often it is accessed. Simple circular buffer, poor hit rates on skewed workloads.
2. **LRU (Least Recently Used)**: Evicts the item that has gone the longest time without being accessed. Implemented in \(O(1)\) time per `Get`/`Put` by combining a **Hash Map** (`Key -> DoublyLinkedListNode`) with a **Doubly Linked List** ordered by access recency (every `Get` splices the node to the head of the list; eviction drops the tail node).
   - *The fatal flaw of pure LRU*: **Scan Pollution**. If a background batch job performs a one-time scan over 1 million cold records, those 1 million one-hit-wonder items are placed at the head of the LRU list and **evict your entire hot working set**!
3. **LFU (Least Frequently Used)**: Counts how many times each item has been requested and evicts the item with the lowest access count. LFU resists scan pollution, but pure LFU suffers from **Frequency Aging (Cache Stagnation)**: an item that was accessed 1,000,000 times during last week's marketing campaign accumulates such a high count that it sits in the cache forever, even after nobody requests it anymore.
4. **Window-TinyLFU (`W-TinyLFU`)**: Introduced by Einziger et al. (2017) and used in production libraries like Caffeine (Java) and Ristretto (Go)[^tinylfu-2017], `W-TinyLFU` solves both scan pollution and frequency aging at once! It places a tiny **Window LRU** (`1%` of cache size) in front of a **Segmented LRU (SLRU)** main cache (`99%` of cache size), separated by a **Count-Min Sketch (Module 09) Admission Filter** with periodic counter halving (aging):
   - When an item is evicted from the 1% Window LRU, it is only admitted into the main 99% cache if its estimated frequency in the Count-Min Sketch is strictly higher than the frequency of the victim at the tail of the main cache! One-time scans flow through the 1% window and are discarded without ever touching the 99% hot working set, while halved counters smoothly age out yesterday's spikes.

---

## 4. The four production cache failure modes (and how to defeat them)

Any senior engineer can draw a Redis box next to a database box.
What distinguishes a Principal engineer is designing explicitly for the four ways that Redis box will take down the database in production:

### Failure Mode 1: Cache Stampede (Thundering Herd / Dogpiling)

Suppose a hot configuration bundle or homepage catalog key (`Key = "GlobalConfig"`) is read `20,000 times per second` across 50 application servers, and each DB query to rebuild it takes `100 ms` of heavy multi-table SQL joins.
At `12:00:00.000`, `"GlobalConfig"`'s 5-minute TTL expires in Redis.
Within that single 100 ms window before the first worker finishes rebuilding the cache, **2,000 concurrent requests across the 50 servers all see `Cache Miss` simultaneously and fire 2,000 identical heavy SQL queries at the database at once!**
The database CPU spikes to 100%, query latency stretches from 100 ms to 10 seconds, all 200,000 incoming requests in that 10-second window *also* miss and pile onto the database, and the entire service dies.

You have three proven engineering defenses against a cache stampede:
1. **In-Process Request Coalescing (`Singleflight`)**: Inside each application process, wrap cache misses in a keyed lock/future dictionary (`ConcurrentDictionary<string, Lazy<Task<T>>>` in C#). If 100 threads on Server A miss `"GlobalConfig"` simultaneously, **only the 1st thread executes the downstream fetch**; the other 99 threads await the exact same `Task<T>` in memory! This immediately cuts DB load from `2,000 queries` down to at most `50 queries` (1 per app server).
2. **Distributed Cache Leases (Meta's Memcached Lease Pattern)**[^nishtala-memcached-2013]: To reduce the `50 queries` (1 per server) down to **1 query across the entire fleet**, when Redis/Memcached returns a cache miss to Server 1, it atomically grants Server 1 a short 2-second **Lease Token** (`SET lock:GlobalConfig token NX PX 2000`). When Servers 2 through 50 miss 1 millisecond later, the cache tells them `"Miss, but Lease is held by another worker; wait 20 ms and retry cache (or serve slightly stale value)"`.
3. **Logical Expiration / Probabilistic Early Expiration (`XFetch` / Stale-While-Revalidate)**[^xfetch-2015]: Never let a hot key physically vanish at its expiration second! Instead, store the key in Redis with a physical TTL of `60 minutes`, and embed a `logicalExpiresAt` timestamp of `5 minutes` inside the JSON payload. While `now < logicalExpiresAt`, all readers return immediately. As soon as the first reader observes `now >= logicalExpiresAt` (or probabilistically slightly before, using Vattani's `XFetch` formula `now - delta * beta * ln(rand()) >= expiry`), that single reader acquires a non-blocking background refresh lock to recompute the value asynchronously while **all other concurrent readers continue serving the slightly stale cached value with zero latency and zero DB stampede!**

### Failure Mode 2: Cache Penetration (Querying Non-Existent Keys)

Suppose an attacker (or a buggy client loop) sends `50,000 requests/sec` for random or sequential `DeviceId` values that **do not exist in your database** (`DeviceId = -1`, `DeviceId = randomGuid`).
Because the key does not exist in the database, a naive Cache-Aside implementation checks Redis (`Miss`), queries the database (`0 rows returned`), **writes nothing to Redis**, and returns `404 Not Found`.
Every single subsequent request for that non-existent key *also* misses Redis and hits the database directly!

**Defenses against Cache Penetration:**
1. **Negative Caching (`Null / Sentinel Object` with short TTL)**: When the database returns `0 rows` for `DeviceId = X`, explicitly cache a sentinel marker (`"__NOT_FOUND__"`) in Redis with a short TTL (say `30 to 60 seconds`) so repeated requests for the same missing key hit Redis.
2. **Bloom Filter Shielding (Module 09)**: If the attacker generates a brand-new random GUID on every request, negative caching alone won't save you because no GUID repeats! Pin a compact **Bloom Filter** of all valid existing IDs in memory (or in Redis). Before checking the cache or DB, check `if (!bloomFilter.MightContain(id)) return 404;`; blocking 99%+ of random penetration queries in nanoseconds.

### Failure Mode 3: Cache Avalanche (Mass Simultaneous Expiration or Shard Loss)

Suppose a startup warmup script or daily cron job loads 500,000 catalog items into Redis at `00:00:00` with an exact `TTL = 3600 seconds`.
At `01:00:00`, all 500,000 keys expire in the exact same second; or a Redis shard holding 25% of your keys crashes; causing a massive wall of misses across all keys simultaneously.

**Defenses against Cache Avalanche:**
1. **TTL Jitter**: Always add random jitter to cache expiration times (`actualTtl = baseTtl + Random.Shared.Next(0, baseTtl / 5)`, e.g. `60 min + random(0..12 min)`) so keys loaded together expire smoothly over a wide window.
2. **Two-Tier L1 + L2 Caching + Circuit Breaker**: Pair the distributed L2 Redis cache with a small in-process L1 cache (`W-TinyLFU`) and protect the database behind a concurrency **Bulkhead / Rate Limiter** (Module 12) so that if a Redis shard dies, the database only accepts its safe maximum QPS and sheds excess load gracefully rather than crashing.

### Failure Mode 4: Stale-Cache Race Condition on Concurrent Read/Write

In Cache-Aside, why do we **delete (invalidate)** the cache key after updating the database (`1. Update DB -> 2. Delete Cache`), rather than **updating** the cache (`1. Update DB -> 2. Set Cache`)?
Because if two concurrent writers `W1 (x=1)` and `W2 (x=2)` update the DB in order `W1 then W2`, but their network calls to Redis interleave in reverse order (`W2 sets Cache=2`, then `W1 sets Cache=1`), the cache is poisoned with `x=1` permanently until TTL expiry!

Yet even `Update DB -> Delete Cache` has a famous subtle race condition when a **slow cache-miss reader** overlaps with a fast writer[^nishtala-memcached-2013]:

```
Reader Thread (Cold Cache Miss)                  Writer Thread (Updating X = 2)
----------------------------------------------   ----------------------------------------------
1. Read Cache(X) -> MISS!
2. Read DB(X) -> gets old value X = 1
   [ Reader thread pauses / GC for 20ms ]
                                                 3. Write DB(X = 2) -> Committed!
                                                 4. Delete Cache(X) -> Key removed!
5. Resume: Set Cache(X = 1) -> POISONED!
```

How does Facebook/Meta's Memcached architecture (`Lease Set`) prevent Step 5 from poisoning the cache with `X = 1`?
When the Reader misses at Step 1, the cache issues a 64-bit monotonic **Lease Token `L = 99`** associated with key `X`.
When the Writer executes `Delete Cache(X)` at Step 4, the cache invalidates any outstanding lease token for `X`.
When the delayed Reader finally executes `LeaseSet(X = 1, LeaseToken = 99)` at Step 5, the cache sees that lease `99` was invalidated by a concurrent delete and **silently ignores the stale `Set`**, leaving the cache clean!

---

## 5. Caching architecture in the Atlas platform

In **Atlas**, the Device Configuration Delivery path serves `250,000 reads/sec` using a hardened multi-tier cache:
- **L1 In-Process Cache (`W-TinyLFU`, 30-second TTL)** sits inside every regional gateway pod to absorb hot-tenant configuration spikes in ~100 nanoseconds without hitting the network.
- **L2 Distributed Redis Cluster (Partitioned via Consistent Hashing)** holds tenant configurations and active rollout manifests with a **60-minute physical TTL and 5-minute logical Stale-While-Revalidate expiration**, combined with in-process **`Singleflight` request coalescing** and **distributed lease tokens (`CAS/version check`)** so a Patch Tuesday rollout spike never stampedes PostgreSQL.
- **Bloom Filter + Negative Caching** at the gateway blocks invalid device serial numbers before they reach L2 Redis.

---

## Before you move on

Caching accelerates reads by orders of magnitude across a hierarchy of tiers (Client, CDN, L1 In-Process, L2 Distributed, and Storage Buffer Pool).
On the read/write path, **Cache-Aside** loads lazily on miss and invalidates on write; **Read-Through / Write-Through** centralize synchronization inside the cache provider; **Write-Around** bypasses the cache on write-heavy streams to prevent pollution; and **Write-Back** buffers writes in memory for maximum throughput at the cost of crash-loss risk unless logged.
For eviction, pure **LRU** suffers from scan pollution and pure **LFU** suffers from frequency stagnation, whereas **`W-TinyLFU`** uses a Count-Min Sketch admission filter to keep both out of the main working set.
Finally, production caches require explicit defenses against **Stampedes** (via `Singleflight` coalescing, leases, and logical Stale-While-Revalidate expiration), **Penetration** (via negative caching and Bloom filters), **Avalanches** (via TTL jitter), and **Stale Write Races** (via lease/version tokens on cache population).

To test your understanding before moving to Module 12, analyze this changed case: an engineer implements a Cache-Aside update handler as **`1. Delete Cache(key)` -> `2. Update Database(key, newValue)`** (deleting from the cache *before* updating the database, instead of *after*).
Trace what happens when a concurrent reader requests `key` just 1 millisecond after Step 1 (`Delete Cache`) completes, while Step 2 (`Update Database`) is still executing and takes 15 milliseconds to commit.
Why does this race happen on *almost every single concurrent read* (unlike the rare slow-reader race in `Update DB -> Delete Cache`), and how would a **Delayed Double Delete** or **Lease Token** behave here?
If you cannot trace that timeline immediately, review **Section 4** before continuing.

---

### Sources

[^azure-caching]: **Tier 3: Microsoft Azure Architecture Center, *Cache-Aside Pattern and Caching Guidance***: Read-through, write-through, write-behind, cache-aside, and static content hosting patterns ([https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside](https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside)).
[^tinylfu-2017]: **Tier 1: Gil Einziger, Roy Friedman, and Ben Manes, "TinyLFU: A Highly Efficient Cache Admission Policy" (*ACM Transactions on Storage*, Vol. 13, No. 4, 2017)**: Specification of Count-Min Sketch frequency admission and the `W-TinyLFU` cache architecture.
[^nishtala-memcached-2013]: **Tier 1: Rajesh Nishtala et al., "Scaling Memcache at Facebook" (*USENIX NSDI*, 2013, pp. 385–398)**: Seminal operational paper introducing Memcache **leases** to solve both thundering herds (stampedes) and stale-set race conditions on Cache-Aside invalidation.
[^xfetch-2015]: **Tier 1: Andrea Vattani, Flavio Chierichetti, and Kumar Punera, "Optimal Probabilistic Cache Stampede Prevention" (*VLDB*, Vol. 8, No. 8, 2015, pp. 886–897)**: Derivation of the `XFetch` probabilistic early expiration algorithm.
