# 12: Rate Limiting, Throttling, and Resiliency

In a multi-tenant distributed system, your biggest availability threat is rarely a hardware failure; it is **load**: a runaway retry loop in a customer's script, a sudden regional reconnect storm after a cellular carrier blip, or one degraded downstream dependency that slows down from 5 ms to 5 seconds and ties up every worker thread in your service.
Without explicit admission control and fault isolation, a localized spike in one tenant or one dependency spreads upstream through connection pools and retry queues until the entire platform collapses into a **metastable failure state** (a state where the system remains 100% wedged even *after* external traffic returns to normal, because all capacity is consumed by retrying timed-out work)[^aws-builders-load][^azure-resiliency].
This module distinguishes rate limiting, throttling, and load shedding, walks through the five canonical rate-limiting algorithms and their distributed Redis implementations, and synthesizes the cloud resiliency patterns that keep systems alive under stress: **Exponential Backoff with Jitter**, **Circuit Breakers**, **Bulkhead Isolation**, **Timeout Propagation**, and **Health Endpoint Monitoring**.

> **Prerequisite:** [Module 02: Time, Clocks, and Ordering of Events](./02-time-clocks-and-ordering-of-events.md) (specifically monotonic vs wall clocks) and [Module 11: Caching Architectures and Failure Modes](./01-distributed-systems-models-and-failure-modes.md).

---

## 1. Rate limiting vs throttling vs load shedding

Although engineers frequently lump these three terms together, they operate at different points in the request lifecycle and optimize for different goals[^azure-resiliency][^aws-builders-load]:

| Mechanism | Who enforces it and why | Typical HTTP signal | What it protects |
| --- | --- | --- | --- |
| **Rate Limiting (Client / Edge Pacing)** | Enforced by a well-behaved **caller, SDK, or outbound proxy** (Ambassador/Sidecar) to pace outbound requests so the caller does not exceed a downstream quota. | Local queue delay or local rejection before wire transmission. | Prevents the caller from triggering downstream throttling errors and smooths batch jobs. |
| **Throttling (Per-Tenant Fairness)** | Enforced by the **server or API Gateway** per `TenantId`, `ApiKey`, or client IP when that specific caller exceeds their provisioned quota, even if the server has spare global capacity. | `429 Too Many Requests` with `Retry-After: <seconds>` and `x-ratelimit-remaining`. | **Multi-tenant fairness ("Noisy Neighbor" protection)** and billing tier enforcement. |
| **Load Shedding (Server Survival)** | Enforced by the **server host** when *aggregate* CPU, memory, queue depth, or in-flight concurrency crosses a critical survival water mark, regardless of whether individual tenants are within their normal quotas! | `503 Service Unavailable` (shedding non-critical/low-priority requests first). | **Total fleet liveness**: guarantees the server continues completing critical work at `100%` success rate rather than timing out `100%` of requests. |

Read that distinction between **Throttling** and **Load Shedding** twice:
A service can have perfectly configured per-tenant **throttling** (say, 100 RPS per tenant across 10,000 tenants) and still melt down during a regional outage if 2,000 tenants simultaneously send their completely legal 80 RPS while 50% of your backend shards are offline!
**Throttling** enforces per-customer contracts; **Load Shedding** protects the physical server by rejecting low-priority requests (such as background telemetry uploads or UI analytics) in under 10 microseconds so high-priority requests (such as emergency rollback commands or health checks) always succeed[^aws-builders-load].

---

## 2. The five canonical rate-limiting algorithms

How do you count and bound request arrival rates in code?
Every rate limiter and throttler implements one of five algorithms, each with a distinct memory footprint, burst behavior, and boundary accuracy:

```
1. Token Bucket (Cap B=4, Refill r=2/sec):       2. Leaky Bucket Queue (Cap B=4, Leak r=2/sec):
   [ +2 tokens/sec ] ---> ( o o o o )                Burst In ---> | req | req | ---> Smooth 2/sec Out
   (Allows immediate burst of B=4 requests!)        (Clamps output to flat constant rate; zero burst!)

3. Fixed Window Counter (Limit = 100 / min):     4. Sliding Window Counter (Limit = 100 / min):
   |--- 12:00..12:01 ---|--- 12:01..12:02 ---|      |-- Prev Window (80) --|-- Curr Window (30) --|
                [100 req][100 req]                                  |<----- 60s Sliding Window ---->|
                (12:00:59 to 12:01:01 = 200 req!)                   Weight = 80 * (1 - 0.25) + 30 = 90 <= 100
```

### 1. Token Bucket

Used by Amazon API Gateway, Envoy, Stripe, and Azure Resource Manager, the **Token Bucket** algorithm models capacity as a bucket that holds at most $B$ tokens (the **burst capacity**) and refills continuously at a steady rate of $r$ tokens per second (the **sustained rate**):
- When a request of cost $c$ (usually $c = 1$) arrives at monotonic timestamp $t_{\text{now}}$, we first lazily compute how many tokens have been generated since `lastRefillTime`:
  $$
  \text{tokens} \leftarrow \min\!\Bigl(B, \;\text{tokens} + (t_{\text{now}} - t_{\text{lastRefill}}) \times r\Bigr), \qquad t_{\text{lastRefill}} \leftarrow t_{\text{now}}
  $$
- If $\text{tokens} \ge c$, we deduct $\text{tokens} \leftarrow \text{tokens} - c$ and **allow** the request.
- If $\text{tokens} < c$, we **reject** (`429`) and compute the exact wait duration $\text{retryAfter} = \frac{c - \text{tokens}}{r}$.

Notice why lazy refill on request arrival is so clean: **it requires zero background timer threads!**
Every tenant bucket is just two 64-bit numbers (`double tokens`, `long lastTimestampTicks`) in memory (`16 bytes` per tenant).
Token Bucket is the industry default for API throttling because real human and service traffic is naturally bursty: a client that has been idle for 10 seconds can immediately burst $B$ parallel requests to load a dashboard, and is then smoothly clamped to $r$ requests/sec thereafter.

### 2. Leaky Bucket

The **Leaky Bucket** metaphor is used in two slightly different ways in networking and services:
- **Leaky Bucket as a Meter**: Mathematically dual to a Token Bucket where incoming requests add water to a bucket of capacity $B$ that drains at constant rate $r$, rejecting requests when adding water would overflow $B$.
- **Leaky Bucket as a FIFO Queue (Traffic Shaping)**: Incoming requests enter a bounded FIFO queue of size $B$ and a worker thread dequeues and processes them at a **strictly constant fixed interval** $1/r$ seconds. Unlike Token Bucket (which lets a burst of $B$ requests hit the downstream service instantaneously), a Leaky Bucket queue **eliminates downstream burstiness completely**, turning spiky arrivals into a flat, uniform stream at the cost of queueing latency.

### 3. Fixed Window Counter

Divides time into fixed calendar buckets (for example, 1-minute windows keyed by `tenantId:202611211410`) and increments an integer counter per request (`INCR` with `EXPIRE 60`).
It takes only 1 integer per key, but suffers from the **Boundary Burst Bug**: if a tenant has a limit of `100 requests/minute` and sends 100 requests at `14:10:59` (the last second of Window 1) and another 100 requests at `14:11:01` (the first second of Window 2), both fixed windows see `count = 100 <= 100`, yet your backend just absorbed **200 requests in 2 seconds ($2\times$ the intended rate limit)**!

### 4. Sliding Window Log

To eliminate the boundary bug with 100% mathematical precision, the **Sliding Window Log** stores the exact timestamp of every accepted request inside a sorted set (such as a Redis `ZSET` keyed by `tenantId`).
When a request arrives at $t_{\text{now}}$ for a window of length $W$ (say 60 seconds):
1. Remove all timestamps older than $t_{\text{now}} - W$ (`ZREMRANGEBYSCORE key -inf (t_now - W)`).
2. Count the remaining entries (`ZCARD key`).
3. If `count < limit`, insert $t_{\text{now}}$ (`ZADD key t_now requestId`) and allow the request; otherwise reject.

Sliding Window Log is 100% accurate across any window boundary, but its **memory and CPU cost is $O(\text{limit})$ per tenant**: if a high-volume tenant has a limit of `50,000 requests/minute`, storing 50,000 64-bit timestamps in a sorted tree per tenant consumes megabytes of RAM.

### 5. Sliding Window Counter (Weighted Approximation)

Used by Cloudflare at edge scale, the **Sliding Window Counter** achieves `> 99.9%` of the accuracy of a Sliding Window Log while using only **two integers (`O(1)` memory)** per tenant!
Keep only the counter for the **previous fixed window** (`prevCount`) and the **current fixed window** (`currCount`).
When a request arrives at fraction $\alpha \in [0, 1)$ through the current window of length $W$ (for example, 15 seconds into a 60-second window, so $\alpha = 15/60 = 0.25$), the sliding 60-second window covers the last $1 - \alpha = 75\%$ of the previous window plus the elapsed $25\%$ of the current window.
We estimate the request count in the sliding window as:

$$
\text{estimatedCount} = \text{prevCount} \times (1 - \alpha) + \text{currCount}
$$

If $\text{estimatedCount} < \text{limit}$, we increment `currCount` and allow the request!

| Algorithm | Memory per tenant | Allows instantaneous burst? | Vulnerable to $2\times$ boundary spike? | Best suited for |
| --- | --- | --- | --- | --- |
| **Token Bucket** | $O(1)$ (2 numbers) | Yes (up to bucket capacity $B$) | No | General API throttling & burst-friendly quotas |
| **Leaky Bucket (Queue)** | $O(B)$ queue slots | No (clamps output to flat rate $r$) | No | Protecting fragile legacy downstream databases |
| **Fixed Window Counter** | $O(1)$ (1 integer) | Yes | **Yes ($2\times$ at window edge)** | Coarse daily/monthly billing quotas |
| **Sliding Window Log** | $O(\text{Limit})$ timestamps | Yes (within exact window) | No (100% exact) | Low-volume, strict security/login/OTP limits |
| **Sliding Window Counter** | $O(1)$ (2 integers) | Yes (smoothly weighted) | No ($\approx 99.9\%$ accurate) | High-scale edge HTTP rate limiting |

---

## 3. Distributed rate limiting in practice: atomicity and fleet scaling

Suppose you run 40 stateless API Gateway pods in a region, and a tenant's quota is `1,000 requests/second`.
You have two architectural choices for coordinating that limit across the 40 pods:

1. **Local Per-Pod Buckets (Zero Network Calls)**: Each of the 40 pods runs an in-memory Token Bucket or Sliding Window Counter in C#. If the L7 load balancer distributes traffic evenly (or routes by `Hash(TenantId)`), each pod enforces `1,000 / 40 = 25 RPS` (or the single consistent-hashed pod enforces `1,000 RPS`). *Remember the ARM fleet-multiplier lesson:* if you configure every pod with the full `1,000 RPS` limit locally without dividing by pod count or consistent-hashing by tenant, the effective fleet limit silently multiplies to `40 * 1,000 = 40,000 RPS`!
2. **Centralized Redis State with Atomic Lua Scripting (or Local Batching)**: When requests for a single tenant can land on any pod and quotas must be globally exact, pods share state in a partitioned Redis cluster.
   - *Critical concurrency bug to avoid:* In the C# Redis `CreateTransaction()` snippet often seen in tutorials, reading `SortedSetLengthAsync` or `StringGet` *outside* or *inside* a non-scripted pipeline and branching in C# before `Execute()` creates a **Read-Modify-Write race condition** under concurrent requests (or blocks on an unexecuted task)!
   - In production Redis, always execute the check-and-decrement atomically inside a single **server-side Redis Lua script (`EVALSHA`)** (which Redis executes single-threaded and atomically without network round trips between read and write):
     ```lua
     -- Atomic Token Bucket in Redis Lua
     local tokens_key = KEYS[1]
     local ts_key = KEYS[2]
     local capacity = tonumber(ARGV[1])
     local refill_rate = tonumber(ARGV[2])
     local now_ms = tonumber(ARGV[3])
     local requested = tonumber(ARGV[4])
     -- Read current state, compute refill, and deduct atomically if >= requested
     ```
   - To avoid calling Redis on every single HTTP request at `500,000 RPS`, pods use **Local Token Reservation (Batching)**: a pod claims a batch of `50 tokens` at a time from Redis and hands them out in local C# memory until the local batch runs out.

---

## 4. Cloud resiliency patterns: Backoff + Jitter, Circuit Breakers, and Bulkheads

When a downstream service throttles you (`429`) or suffers a transient failure (`503` / socket timeout), how your client reacts determines whether the incident self-heals in seconds or spirals into a multi-hour outage[^aws-builders-backoff][^azure-resiliency]:

### 1. Retry with Exponential Backoff and Full Jitter

Never retry immediately in a tight loop, and **never use fixed or deterministic exponential backoff (`1s, 2s, 4s, 8s`) without random jitter**!
Why? Suppose a downstream database blips for 1 second at `12:00:00`, causing 10,000 concurrent callers to fail at the exact same millisecond.
With deterministic backoff (`1s, 2s, 4s`), all 10,000 callers sleep for exactly `1.000s` and **slam the database in a synchronized 10,000-request hammer blow at `12:00:01`**, knocking it down again, then slamming it together at `12:00:03`, then `12:00:07`!

As Marc Brooker demonstrated in the AWS Builders' Library, adding **Full Jitter** breaks up synchronized waves cleanly[^aws-builders-backoff]:

$$
T_{\text{sleep}} = \text{UniformRandom}\!\Bigl(0, \;\min\!\bigl(T_{\text{max}}, \;T_{\text{base}} \times 2^{\text{attempt}}\bigr)\Bigr)
$$

Pair backoff + jitter with two mandatory guardrails:
- **Idempotency requirement**: Only automatically retry operations that are **idempotent** (safe to apply multiple times with the same idempotency key or `ETag`, Module 01).
- **Retry Budgets (Token-Capped Retries)**: Each client pod maintains a local retry token bucket: every normal request deposits `0.1` tokens, and every retry consumes `1.0` token. This mathematically guarantees that retries can **never increase downstream traffic by more than 10%**, even during a total downstream outage!

### 2. The Circuit Breaker Pattern

When a downstream dependency is completely dead or timing out after 10 seconds, waiting 10 seconds on every incoming request ties up your worker threads and cascades the failure to your callers.
A **Circuit Breaker** wraps the outbound call in a three-state machine[^fowler-cb][^azure-resiliency]:

```
                  [ Failure rate >= threshold (e.g. >50% over 20 calls) ]
       +-------------------------------------------------------------------------+
       |                                                                         v
+--------------+                                                          +--------------+
|    CLOSED    |                                                          |     OPEN     |
| (Normal flow)|                                                          | (Fail-fast!) |
+--------------+                                                          +--------------+
       ^                                                                         |
       |          +---------------------------------------------------+          |
       +--------- |                    HALF-OPEN                      | <--------+
   [Probe calls   | (Allow N trial requests; if OK -> Closed;         |   [Break duration
    succeed!]     |  if any fails -> immediately re-open!)            |    timer expires]
                  +---------------------------------------------------+
```

- **Closed**: Requests flow normally; a sliding window tracks failure/timeout percentage.
- **Open**: Once failures cross the threshold, the circuit trips **Open** and **fails fast in 0 ms** (or returns a cached fallback / graceful degradation) without touching the network for a cooldown period (`breakDuration`, say 15 seconds).
- **Half-Open**: When `breakDuration` expires, the breaker admits a small bounded number of **trial probe requests**. If they succeed, the breaker resets to **Closed**; if they fail, it trips immediately back to **Open**.

### 3. Bulkhead Isolation (Resource & Shuffle Sharding)

Named after the watertight compartments of a ship's hull, the **Bulkhead Pattern** partitions shared resources (thread pools, HTTP connection pools, memory queues, and worker pods) into isolated pools so that if Dependency A hangs or Tenant X floods the system, it can only exhaust its own compartment, leaving Dependencies B and C and all other tenants 100% healthy[^azure-resiliency].
At hyperscale, AWS and Azure combine Bulkheads with **Shuffle Sharding**: instead of assigning each tenant to a single fixed shard of 2 workers out of 100, each tenant is assigned a pseudorandom subset of 4 workers (using two steps of Rendezvous Hashing!).
With $\binom{100}{4} = 3{,}921{,}225$ unique 4-worker combinations, if a poisonous tenant crashes all 4 of its assigned workers, another tenant has only a $1 \text{ in } 3.9\text{ million}$ chance of sharing all 4 workers; isolating 99.99997% of customers without paying for dedicated single-tenant clusters[^aws-shuffle-sharding]!

### 4. Timeout Propagation and Health Endpoint Monitoring

Two final operational rules complete the resiliency suite:
- **Deadline / Timeout Propagation**: Never make an outbound network call without an explicit `CancellationToken` timeout, and propagate the remaining caller deadline downstream (e.g., via gRPC `grpc-timeout`). If the API Gateway has a 2,000 ms timeout and Stage 1 took 1,800 ms, Stage 2 must be given at most `200 ms`, not a fresh 2,000 ms after the caller has already disconnected!
- **Shallow Liveness vs Deep Readiness Health Checks**: In **Health Endpoint Monitoring**, never fail your container's **Liveness Probe** (`/health/live`) just because a downstream database is temporarily slow; doing so causes Kubernetes or the cloud orchestrator to kill and restart all your pods simultaneously during a database blip! Use `/health/live` strictly to verify the local process is not deadlocked, and use `/health/ready` (or load-balancer outlier detection) to control traffic routing[^azure-resiliency].

---

## 5. Throttling and resiliency in the Atlas platform

In **Atlas**, multi-layered admission control prevented a major outage during a global firmware rollout:
- **Per-Tenant Token Bucket + Fleet Shuffle Sharding**: Each tenant is assigned a **Shuffle Shard** of 4 regional ingestion gateways (selected via Rendezvous Hashing) and a **Token Bucket** quota enforced via local token reservations backed by atomic Redis Lua scripts.
- **Priority Load Shedding at the Edge**: Every request arriving at an Atlas gateway is classified into three priority tiers: `P0 (Emergency Rollback & Control Plane)`, `P1 (Interactive Device Provisioning & Config Pulls)`, and `P2 (Background Telemetry & Diagnostic Uploads)`. When local host CPU or in-flight request latency crosses 80% of the SLA budget, an adaptive **Little's Law concurrency limiter** sheds `P2` traffic immediately with `503 Service Unavailable + Retry-After: jittered`, guaranteeing that `P0` rollback commands execute with `100%` reliability even under a $10\times$ traffic storm.

---

## Before you move on

Resilient cloud systems control load and isolate faults at every boundary.
**Rate limiting** paces outbound client traffic; **throttling** enforces per-tenant server quotas (`429`); and **load shedding** drops lower-priority requests (`503`) to keep the server alive when total capacity is saturated.
Among rate-limiting algorithms, **Token Bucket** allows controlled bursts of size $B$ at sustained rate $r$ in $O(1)$ memory; **Leaky Bucket queues** smooth traffic into a flat outflow rate; **Fixed Window Counters** suffer from a $2\times$ boundary burst flaw; **Sliding Window Logs** achieve 100% accuracy at $O(\text{Limit})$ memory cost; and **Sliding Window Counters** achieve `>99.9%` accuracy in just two integers per key.
On the resiliency side, outbound calls must combine **Exponential Backoff with Full Jitter**, **Retry Budgets (`<= 10%`)**, **Circuit Breakers (`Closed / Open / Half-Open`)**, **Bulkhead Isolation (and Shuffle Sharding)**, and **Deadline Propagation** to prevent localized hiccups from cascading into metastable retry storms.

To test your understanding before moving to Module 13, work through this changed case: suppose a service runs a **Sliding Window Counter** rate limiter with a limit of `100 requests per 60-second window`.
During the `12:00:00..12:01:00` window, a tenant made `80` requests.
During the `12:01:00..12:02:00` window, the tenant has already made `40` requests by `12:01:15` (15 seconds into the 60-second window), and a `41st` request arrives at `12:01:15`.
Calculate the exact weighted `estimatedCount` at `12:01:15`, state whether the `41st` request is allowed or rejected (`429`), and compute the earliest second in the `12:01..12:02` window at which that `41st` request *will* be allowed if no further requests succeed in between.
If you cannot set up and solve that linear equation in thirty seconds, review **Section 2 (Algorithm 5)** before continuing.

---

### Sources

[^aws-builders-load]: **Tier 3: David Yanacek, *Using Load Shedding to Avoid Overload* (AWS Builders' Library)**: Distinction between throttling and load shedding, priority-tiered admission control, and avoiding metastable overload ([https://aws.amazon.com/builders-library/using-load-shedding-to-avoid-overload/](https://aws.amazon.com/builders-library/using-load-shedding-to-avoid-overload/)).
[^azure-resiliency]: **Tier 3: Microsoft Azure Architecture Center, *Cloud Design Patterns: Resiliency***: Throttling, Rate Limiting, Retry, Circuit Breaker, Bulkhead, and Health Endpoint Monitoring patterns ([https://learn.microsoft.com/en-us/azure/architecture/patterns/category/resiliency](https://learn.microsoft.com/en-us/azure/architecture/patterns/category/resiliency)).
[^aws-builders-backoff]: **Tier 3: Marc Brooker, *Timeouts, Retries, and Backoff with Jitter* (AWS Builders' Library)**: Mathematical and simulation analysis of Full Jitter, Equal Jitter, Decorrelated Jitter, and retry budgets ([https://aws.amazon.com/builders-library/timeouts-retries-and-backoff-with-jitter/](https://aws.amazon.com/builders-library/timeouts-retries-and-backoff-with-jitter/)).
[^fowler-cb]: **Tier 2: Martin Fowler and Michael Nygard, *CircuitBreaker* / *Release It!* (Pragmatic Bookshelf, 2nd ed., 2018)**: Closed, Open, and Half-Open state machine and Bulkhead fault isolation.
[^aws-shuffle-sharding]: **Tier 3: Colm MacCárthaigh, *Workload Isolation Using Shuffle-Sharding* (AWS Builders' Library)**: Combinatorial fault isolation across shared worker fleets ([https://aws.amazon.com/builders-library/workload-isolation-using-shuffle-sharding/](https://aws.amazon.com/builders-library/workload-isolation-using-shuffle-sharding/)).
