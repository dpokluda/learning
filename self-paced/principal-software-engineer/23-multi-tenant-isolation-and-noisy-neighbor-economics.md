# 23: Multi-Tenant Isolation and Noisy-Neighbor Economics

In a single-tenant enterprise system, capacity planning is straightforward: one customer owns the database, the message bus, and the worker nodes, and pays for 100% of the provisioned hardware.
In a **multi-tenant cloud platform**, tens of thousands of organizations (ranging from a developer testing a free trial with 5 devices to a global enterprise operating 20 million endpoints) share the same logical service.
Multi-tenancy unlocks massive economies of scale by multiplexing spiky tenant workloads onto shared compute and storage pools, yet it introduces three of the nastiest engineering problems in cloud architecture:
1. **Security, Compliance, and Cost-Attribution Boundaries**: Regulated financial, healthcare, and government customers often refuse to store their sensitive state inside a multi-tenant storage account or run evaluation code on shared multi-tenant compute, demanding physical isolation and customer-managed encryption keys (`CMK`) while still wanting a managed SaaS experience.
2. **Multi-Tenant Fan-Out Amplification on Ingestion Pipelines**: When a streaming telemetry pipeline naively mixes events from hundreds of tenants inside the same micro-batch blob and then splits them out to per-tenant storage partitions, random I/O and lock contention can collapse throughput by **87% or more**.
3. **Noisy-Neighbor Starvation**: Without stratified admission control and fair scheduling, a single mega-tenant triggering a bulk compliance scan or retry storm can exhaust shared worker queues and starve 10,000 smaller tenants.

This module examines the **four-tier Multi-Tenancy Isolation Spectrum** (including **Customer-Hosted Compute & Storage via Managed Resource Groups**), **cross-boundary identity delegation**, the **quantitative physics of Multi-Tenant Fan-Out Amplification** (and how Claim-Check batching and tenant-tier routing achieve a **\(78\times\) throughput speedup**), and **Noisy-Neighbor defense via stratified token buckets and weighted fair queuing**.

> **Prerequisite:** [Module 08: Data Partitioning and Hashing Schemes](./08-data-partitioning-and-hashing-schemes.md), [Module 12: Rate Limiting, Throttling, and Resiliency](./12-rate-limiting-throttling-and-resiliency.md), and [Module 22: Control Plane vs Data Plane and Cell-Based Architectures](./22-control-plane-vs-data-plane-and-cell-architectures.md).

---

## 1. The Multi-Tenancy Isolation Spectrum: Pool, Silo, and Customer-Hosted Compute/Storage

Multi-tenancy is not a binary choice between "everyone shares one giant database table" and "every customer gets a separate cloud deployment."
A Principal architect designs across a **four-tier isolation spectrum**, matching each tier to customer regulatory requirements and unit economics[^azure-multitenant][^aws-saas-isolation]:

```
TIER 1: Shared Pool          TIER 2: Stamp / Pod-Partitioned   TIER 3: Provider-Hosted Silo   TIER 4: Customer-Hosted (BYOS / MRG)
(Logical TenantId Column)    (Cell / Dedicated Shard)          (Dedicated Stamp in SaaS Sub)  (Compute & Storage in Customer Sub!)
+-----------------------+    +-------------+ +-------------+   +--------------------------+   +----------------------------------+
|  SaaS Subscription    |    |  Cell 01    | |  Cell 02    |   |  SaaS Subscription       |   |  SaaS Control Plane (Orchestrator)|
|  [Shared Compute Pool]|    | [Tenants    | | [Enterprise |   |  [Dedicated Compute Pool]|   +-----------------|----------------+
|  [Shared DB: TenantId]|    |  1 .. 5000] | |  Tenant X]  |   |  [Dedicated Storage Acct]|                     | (Delegated RBAC)
+-----------------------+    +-------------+ +-------------+   +--------------------------+                     v
 * Lowest COGS / unit cost    * Bounded blast radius            * Full infra isolation         +----------------------------------+
 * Highest noisy-neighbor     * Dedicated DB partition          * SaaS provider still pays     |  Customer Subscription (MRG)     |
   & data-bleed risk            for large tenants                 & holds storage custody      |  * Customer-Owned Storage / DB   |
                                                                                               |  * In-Subscription Container Job |
                                                                                               |  * Customer-Managed Keys (CMK)   |
                                                                                               +----------------------------------+
```

Let us compare the architectural trade-offs across all four tiers:

| Isolation Tier | Compute & Storage Custody | How Tenant Isolation Is Enforced | COGS & Cost Attribution | Best Fit |
| --- | --- | --- | --- | --- |
| **1. Shared Pool (Logical)** | Provider subscription; shared worker pool & shared DB tables. | Every query/row carries `TenantId` in its partition key; enforced by app middleware and Row-Level Security (`RLS`). | **Lowest baseline cost** (zero idle waste), but hard to attribute exact CPU/IOPS per tenant. | Free tier, small-to-medium Standard tenants with bursty, low-volume workloads. |
| **2. Pod / Stamp-Partitioned** | Provider subscription; shared cell compute with dedicated DB shards for large tenants. | Shuffle sharding on compute; large tenants get dedicated database partitions or Kafka topics. | Moderate cost; predictable per-shard sizing. | Growing commercial tenants needing guaranteed IOPS without full silo overhead. |
| **3. Provider-Hosted Silo** | Provider subscription; dedicated cell/cluster per enterprise customer. | Dedicated VMs/pods and storage accounts inside the SaaS provider's cloud estate. | High fixed base cost; billed via enterprise minimum commit. | Large enterprises needing dedicated capacity but comfortable with SaaS data custody. |
| **4. Customer-Hosted (`BYOS / Managed Resource Group`)** | **Customer's own cloud subscription!** Control plane stays in SaaS provider; Data Plane compute & storage live in customer boundary. | Physical cloud subscription boundary, customer VNet, **Customer-Managed Keys (`CMK`)**, and customer audit logs. | **100% direct cost attribution**: compute and storage I/O bill directly to the customer's cloud invoice! | Sovereign, financial, defense, or mega-scale governance workloads requiring zero data exfiltration. |

### Deep Dive into Tier 4: The Customer-Hosted (`Managed Resource Group / BYOS`) Architecture

Look closely at **Tier 4 (Customer-Hosted Compute & Storage)**, known in Azure as the **Managed Application / Managed Resource Group (`MRG`)** pattern and in AWS/GCP as the **Bring-Your-Own-Account (`BYOA / BYOS`)** data-plane model[^azure-multitenant]:
Why do regulated enterprise customers and cloud-scale governance platforms love this pattern?
Because it solves **three hard Principal problems simultaneously**:
1. **Data Sovereignty & Zero Exfiltration**: The customer's raw resource inventory, compliance evaluation state, and sensitive logs are stored inside a database/storage account physically located inside **the customer's own subscription and virtual network**. If the customer revokes the provider's delegated access or rotates their Customer-Managed Key (`CMK`) in Key Vault, access is severed immediately.
2. **Elimination of Cross-Tenant Noisy Neighbors**: Because each customer's data plane runs on containerized worker jobs (`Azure Container Apps / AKS / AWS Fargate`) and storage provisioned inside their own Managed Resource Group, a customer running a massive 50-million-resource evaluation spike consumes *only their own subscription's quota and compute*, with **zero impact on any other customer**.
3. **Direct COGS Pass-Through**: Instead of the SaaS provider paying for petabytes of multi-tenant storage and complex per-query chargeback metering, the underlying cloud compute and storage resources are billed directly to the customer's subscription at native cloud rates!

---

## 2. Cross-boundary delegation and asynchronous resource projection

How does a centralized SaaS Control Plane safely provision, update, and orchestrate Data Plane containers and databases that live inside **thousands of separate customer subscriptions** without storing long-lived administrator passwords?

```
CUSTOMER SUBSCRIPTION (Trust Boundary A)                   SAAS PROVIDER CONTROL PLANE (Trust Boundary B)
+-----------------------------------------------------+    +----------------------------------------------------+
|  1. Customer deploys Managed Onboarding Template    |    |                                                    |
|     (Grants scoped RBAC on Managed Resource Group   |<---| 2. Control Plane assumes Managed Identity /        |
|      to Provider's Managed Application Identity)    |    |    Federated OIDC Token scoped strictly to MRG     |
|                                                     |    |                      |                             |
|  +-----------------------------------------------+  |    |                      | (3. Async Provisioning Saga |
|  | Managed Resource Group (Customer-Owned)       |  |    |                      |     via Queue + Outbox)     |
|  |  * [ Customer-Owned NoSQL / Cosmos / Blob ]   |<-|----+----------------------+                             |
|  |  * [ Customer-Hosted Evaluation Containers ]  |  |    |                                                    |
|  |    (Evaluates policies locally inside sub!)   |  |    |                                                    |
|  +-----------------------------------------------+  |    +----------------------------------------------------+
+-----------------------------------------------------+
```

Three mechanisms make cross-boundary orchestration secure and resilient:
1. **Scoped Delegated Identity (Zero Stored Secrets)**: During onboarding, the customer deploys a declarative template (`ARM / Bicep / Terraform`) that creates a **Managed Resource Group** in their subscription and grants a tightly scoped Role-Based Access Control (`RBAC`) permission on *only* that resource group to the SaaS Control Plane's **Managed Identity (Workload Identity Federation / OIDC)**. No client secrets or certificates are ever copied or stored in a database.
2. **Asynchronous Provisioning Saga (`HTTP 202` + Idempotent Reconciliation Loop)**: Provisioning a database, a storage account, and a container environment inside a customer's subscription takes `2 to 5 minutes` and can fail mid-flight if the customer's subscription hits a regional vCPU quota limit or a custom organizational policy blocks public IPs. Therefore, the Control Plane never provisions resources synchronously on an HTTP thread; it records a desired-state spec (`ProvisioningState = Provisioning`) and drives an idempotent, Kubernetes-style **Reconciliation Loop (Saga)** that converges the customer's Managed Resource Group toward the target state and surfaces actionable quota errors via the `HTTP 202` operation status endpoint (Module 22).
3. **Asynchronous Resource Projection and In-Subscription Evaluation**: As resources change inside the customer's cloud environment, change-notification streams project those resource states into the customer-owned data store inside the Managed Resource Group, where the customer-hosted evaluation container executes policy rules locally without shipping sensitive resource payloads across the internet to a shared multi-tenant cluster!

---

## 3. Quantifying multi-tenant fan-out amplification (and the $78\times$ throughput architecture)

Even when you use a shared or pod-partitioned ingestion pipeline (Tiers 1 and 2) to ingest high-volume telemetry or audit logs before landing them in per-tenant storage partitions, a subtle **Multi-Tenant Fan-Out Trap** can destroy your throughput.
Let us walk through the exact production math from a high-scale multi-tenant telemetry pipeline to see why naive micro-batching collapses under multi-tenant skew, and how to re-architect it for a **\(78\times\) throughput gain (`1.3 MB/s -> 102.2 MB/s`)**[^ddia-ch10][^kleppmann-logs]:

### The Physics of the 87% Multi-Tenant Fan-Out Throughput Collapse

Suppose your ingestion pipeline receives raw compressed telemetry blobs (each `~10 MB` to `50 MB`) from edge aggregators and needs to parse, transform, and append the events into tenant-partitioned lakehouse files (`/tenant={tenantId}/date={yyyy-MM-dd}/part-*.parquet`).
- **Scenario A (Single-Tenant / Pre-Grouped Blob)**: An incoming `20 MB` blob contains events belonging to **1 single mega-tenant**. The worker parses the 20 MB stream in memory, compresses one contiguous `20 MB` columnar output block, and performs **1 single sequential blob write (`1 I/O operation`)** to `/tenant=T1/...`. Throughput is high (`~15.4 MB/s` per single-threaded worker).
- **Scenario B (Mixed Multi-Tenant Blob with 200 Tenants)**: Now an incoming `20 MB` blob contains interleaved events spanning **$M = 200$ distinct small and medium tenants** (`~100 KB` per tenant on average).
  Look at what happens inside a naive worker that processes that single blob and flushes per tenant:
  1. **\(200\times\) Write / Metadata I/O Amplification**: Instead of 1 sequential `20 MB` object-store commit, the worker must open, write, and commit **200 separate `100 KB` output files** across 200 different tenant directory paths! Because every cloud object-store `PutBlob / Flush` call carries a fixed network and metadata-transaction latency overhead \(T_{\text{meta}} \approx 15\text{--}40\text{ ms}\), committing 200 tiny files adds:
     \[
     200 \times 25\text{ ms} = \mathbf{5{,}000\text{ ms (5 full seconds of pure network/metadata wait time per blob!)}}
     \]
  2. **Memory Fragmentation & GC Thrashing**: Maintaining 200 concurrent in-memory compression buffers and string dictionaries simultaneously overflows L2/L3 CPU caches and forces buffers onto the .NET **Large Object Heap (`LOH`, $\ge 85{,}000$ bytes)**, triggering stop-the-world Generation-2 garbage collections.
  3. **Partition Lock Contention**: If 32 worker pods all process mixed blobs containing events for the same 200 tenants simultaneously, all 32 pods contend on the exact same per-tenant output partitions!

Measured in production benchmarks, simply changing an input blob from **1 tenant/blob** to **200 tenants/blob** caused single-worker pipeline throughput to collapse from **`15.4 MB/s` down to `2.0 MB/s` (an 87% throughput drop)**, and end-to-end unoptimized pipeline throughput sat at just **`1.3 MB/s`**!

### The Three-Part Architecture for a $78\times$ Throughput Gain (`1.3 MB/s -> 102.2 MB/s`)

How do you eliminate that multi-tenant fan-out bottleneck completely?
By combining three systems engineering techniques:

```
[ Raw Multi-Tenant Telemetry Blobs (e.g. 50 MB Compressed Stream) ]
                               |
                               v
   +-------------------------------------------------------+
   | 1. Claim-Check Multi-Part Blob Splitter / Coordinator |
   |    * Splits large blobs into parallel byte-range      |
   |      Claim-Check work items ({BlobUri, Offset, Len})  |
   +-------------------------------------------------------+
                               |
         +---------------------+---------------------+
         | (Kafka Partitioned by TenantTier / Hash)  |
         v                                           v
+---------------------------------+   +---------------------------------+
| 2. Mega-Tenant Fast-Path Pool   |   | 3. Small-Tenant Coalescing Pool |
|    (1 Tenant / Batch)           |   |    (200 Tenants / Batch)        |
|  * ArrayPool<byte> Zero-GC      |   |  * Sharded by Hash(TenantId)%P  |
|  * Direct parallel block flush  |   |  * In-memory shuffle buffer     |
|                                 |   |    coalesces across N blobs     |
|                                 |   |    until tenant buffer >= 8 MB! |
+---------------------------------+   +---------------------------------+
         \                                           /
          \---> [ Combined Throughput: 102.2 MB/s (78x Gain!) ]
```

1. **Claim-Check Byte-Range Splitting (Parallelizing Multi-Part Ingestion)**:
   Instead of passing multi-megabyte payloads through the message bus or letting a single thread chew through a giant blob sequentially, the ingestion coordinator writes (or references) the raw multi-part blob in object storage and emits lightweight **Claim-Check descriptors** (`{BlobUri, PartIndex, ByteOffset, ByteLength}`) onto a partitioned work queue so \(P\) parallel workers can stream and decompress independent byte ranges concurrently.
2. **Tenant-Tier Routing & Shuffle Coalescing (Amortizing Per-Tenant Flush Overhead)**:
   Rather than letting every worker immediately flush tiny `100 KB` fragments for 200 tenants after every input blob, workers partition events by `Hash(TenantId) % PartitionCount` and accumulate events in a **bounded in-memory Tenant Batch Coalescer** across multiple input blobs!
   A tenant partition is flushed to object storage **only when its accumulated buffer reaches a target size threshold \(B_{\text{min}}\) (for example, `8 MB` to `32 MB`) OR a maximum staleness deadline \(T_{\max}\) (for example, `60 seconds`) elapses**.
   Look at the math: coalescing across 80 input blobs turns eighty `100 KB` flushes (80 metadata round trips) into **one single `8 MB` sequential flush (1 metadata round trip)**, eliminating `98.7%` of object-store I/O calls!
3. **Zero-Allocation `ArrayPool<byte>` / `RecyclableMemoryStream` Buffer Management**:
   Instead of allocating `new byte[]` buffers for every tenant batch (which hammers the .NET Large Object Heap), workers rent fixed slabs from `ArrayPool<byte>.Shared` and return them immediately after the flush completes, keeping Generation-2 GC pauses near zero.

Combining Claim-Check parallel multi-part splitting, tenant-shuffled batch coalescing, and pooled memory raised measured pipeline throughput from **`1.3 MB/s` to `102.2 MB/s` (a \(78.6\times\) speedup)** while maintaining sub-minute latency SLAs!

---

## 4. Noisy-neighbor defense: stratified tenant tiers and Weighted Fair Queuing

In any shared pool (or inside a shared cell), tenant size follows a heavy-tailed **Power-Law (Pareto / Zipf) distribution**:
- **Tier S (Free / Small Tenants, `90%` of tenant count)**: Generate `< 5%` of total traffic (`1 to 10 RPS` each).
- **Tier M (Medium / Enterprise Tenants, `9.5%` of tenant count)**: Generate `~35%` of total traffic (`100 to 2,000 RPS` each).
- **Tier L (Mega-Tenants / Whales, `0.5%` of tenant count)**: Generate **`~60%` of total platform traffic (`10,000 to 100,000+ RPS` each)**!

If you put a Tier-S tenant (`5 RPS`) and a Tier-L Mega-Tenant (`50,000 RPS`) into a single FIFO work queue, the Mega-Tenant enqueues 50,000 items in one second and forces the Tier-S tenant's 5 requests to wait behind all 50,000 items (**Head-of-Line Blocking / Noisy-Neighbor Starvation**).

To guarantee fairness and predictable SLAs across all three tiers, combine three scheduling defenses[^aws-saas-isolation]:
1. **Stratified Tenant-Tier Worker Pools (Physical Bulkheads by Tier)**: Route `Small`, `Medium`, and `Mega-Tenant` workloads to separate consumer groups / worker pools sized for their statistical profile (and isolate the top 10 Mega-Tenants onto dedicated Kafka partitions or dedicated cells).
2. **Per-Tenant Token Bucket + Dynamic Burst Quota**: Enforce a per-tenant **Token Bucket** at the ingress gateway (`rate = r_tier`, `burst = B_tier`), returning `HTTP 429 Too Many Requests` with a `Retry-After` header to the noisy tenant alone before its traffic ever enters shared backend queues.
3. **Deficit Round-Robin (`DRR`) / Weighted Fair Queuing (`WFQ`)**: Inside a shared worker pool, never pull from a single global FIFO queue. Maintain lightweight per-tenant virtual queues and schedule them via **Deficit Round-Robin**: in each scheduling round, every active tenant queue `i` receives a byte/work **`Quantum_i`** proportional to its tier weight (`Deficit[i] += Quantum[i]`), and the scheduler pops items from `Queue[i]` as long as `ItemCost <= Deficit[i]`, automatically preventing a tenant with 10,000 queued items from starving a tenant with 1 queued item!

```csharp
public sealed class DeficitRoundRobinScheduler<TPayload>
{
    private sealed class TenantQueue
    {
        public string TenantId { get; }
        public int Quantum { get; }
        public int Deficit { get; set; }
        public Queue<(TPayload Item, int Cost)> Items { get; } = new Queue<(TPayload, int)>();

        public TenantQueue(string tenantId, int quantum)
        {
            TenantId = tenantId;
            Quantum = quantum;
        }
    }

    private readonly Dictionary<string, TenantQueue> _queues = new Dictionary<string, TenantQueue>(StringComparer.Ordinal);
    private readonly Queue<TenantQueue> _activeList = new Queue<TenantQueue>();

    public void RegisterTenant(string tenantId, int weightQuantum)
    {
        _queues[tenantId] = new TenantQueue(tenantId, weightQuantum);
    }

    public void Enqueue(string tenantId, TPayload item, int workCost)
    {
        TenantQueue q = _queues[tenantId];
        bool wasEmpty = q.Items.Count == 0;
        q.Items.Enqueue((item, workCost));
        if (wasEmpty)
        {
            _activeList.Enqueue(q);
        }
    }

    // Executes one fair scheduling step: guarantees a noisy tenant with 10,000 queued items
    // cannot monopolize the worker ahead of a quiet tenant with 1 item.
    public bool TryDequeueNext(out string? tenantId, out TPayload? item)
    {
        int activeCount = _activeList.Count;
        for (int checkedQueues = 0; checkedQueues < activeCount; checkedQueues++)
        {
            TenantQueue current = _activeList.Dequeue();
            current.Deficit += current.Quantum;

            if (current.Items.TryPeek(out (TPayload Item, int Cost) head) && head.Cost <= current.Deficit)
            {
                current.Items.Dequeue();
                current.Deficit -= head.Cost;
                if (current.Items.Count > 0)
                {
                    _activeList.Enqueue(current);
                }
                else
                {
                    current.Deficit = 0;
                }

                tenantId = current.TenantId;
                item = head.Item;
                return true;
            }

            if (current.Items.Count > 0)
            {
                _activeList.Enqueue(current);
            }
            else
            {
                current.Deficit = 0;
            }
        }

        tenantId = null;
        item = default;
        return false;
    }
}
```

---

## 5. Multi-tenant isolation and fan-out economics in the Atlas platform

In **Atlas**, all four isolation and scheduling mechanisms work in concert:
- **Customer-Hosted (`Managed Resource Group`) Tier for Regulated Defense & Banking Tenants**: While 99% of commercial Atlas tenants run in our regional multi-tenant cells (Tier 2), defense and banking customers onboard via the **Atlas Customer-Hosted Managed Application** (Tier 4). Atlas's Control Plane uses scoped **Managed Identity delegation** to provision an isolated data store and containerized applicability evaluator inside the customer's own cloud subscription, encrypted with the customer's HSM-backed Key Vault key (`CMK`).
- **Claim-Check + Coalesced Telemetry Ingestion (`100+ MB/s per Worker Pool`)**: Edge gateways upload compressed multi-tenant telemetry batches to regional landing blobs and emit **Claim-Check byte-range pointers** to Kafka. Stream workers coalesce small-tenant records in `ArrayPool<byte>`-backed memory buffers until each tenant partition reaches `16 MB` (or `45 seconds` max age), avoiding the 87% 200-tenant fan-out penalty.
- **Deficit Round-Robin Rollout Compiler**: When a Mega-Tenant queues 5,000 firmware delta-generation jobs at the same instant a small hospital tenant queues 1 emergency patch job, the **Deficit Round-Robin Scheduler** interleaves the hospital's job on the very next scheduling quantum rather than making it wait hours behind the Mega-Tenant's backlog.

---

## Before you move on

Multi-tenant architecture balances **unit-cost efficiency** against **blast-radius, security, and performance isolation**.
Across the **Multi-Tenancy Isolation Spectrum**, systems progress from **Shared Pool (`TenantId` column)** to **Stamp/Pod-Partitioned**, **Provider-Hosted Silo**, and **Customer-Hosted Compute & Storage (`Managed Resource Group / BYOS`)**, where the SaaS Control Plane orchestrates customer-owned storage and containerized evaluators inside the customer's subscription via **delegated Managed Identities** and **asynchronous `HTTP 202` reconciliation sagas**.
On high-throughput data planes, ingesting mixed-tenant blobs (`200 tenants/blob`) and flushing per tenant causes an **87% throughput collapse** due to \(200\times\) object-store metadata round trips and memory fragmentation; combining **Claim-Check multi-part splitting**, **in-memory tenant batch coalescing (size or time threshold)**, and **`ArrayPool<byte>` buffer pooling** restores sequential I/O and delivers a **\(78\times\) throughput gain**.
Finally, **Stratified Tenant Tiers**, **per-tenant Token Buckets**, and **Deficit Round-Robin (`DRR`)** scheduling prevent Pareto mega-tenants from starving smaller tenants.

To test your intuition before moving to Module 24, calculate this concrete fan-out trade-off: suppose a stream processor ingests `10 MB` mixed blobs where each blob contains events for `250 tenants` (`40 KB` per tenant per blob), and each cloud object-store `FlushBlob` operation has a fixed metadata latency overhead of `20 ms` plus a sequential transfer speed of `500 MB/s`.
1. If a single-threaded worker flushes all 250 tenant files immediately after every `10 MB` blob, how many milliseconds does it spend on `FlushBlob` metadata overhead vs actual byte transfer, and what is its effective throughput in `MB/s`?
2. If the worker instead coalesces across **50 blobs** in memory before flushing (`2 MB` per tenant file), what does the effective throughput jump to, and what crash-recovery mechanism from Module 06/17 allows the worker to hold 50 blobs in volatile RAM without risking data loss if the pod crashes before flushing?
*(Hint: check your Kafka consumer offset commit rule! Never commit the upstream Kafka/Event Hub offset until the coalesced batch is durably flushed to object storage!)*
If that calculation and offset-commit invariant are not automatic, review **Section 3** before continuing.

---

### Sources

[^azure-multitenant]: **Tier 3: Microsoft Azure Architecture Center, *Architectural Approaches for Multitenant Solutions* & *Azure Managed Applications / Managed Resource Groups* ([https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/overview](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/overview))**: Pool vs Silo vs Customer-Hosted Managed Resource Group (`MRG`) isolation models, delegated RBAC identities, and cost attribution.
[^aws-saas-isolation]: **Tier 3: AWS SaaS Factory & Builders' Library, *SaaS Tenant Isolation Strategies* & *Fairness in Multi-Tenant Systems* ([https://aws.amazon.com/builders-library/fairness-in-multi-tenant-systems/](https://aws.amazon.com/builders-library/fairness-in-multi-tenant-systems/))**: Stratified tenant tiers, noisy-neighbor containment, per-tenant token buckets, and Deficit Round-Robin / Weighted Fair Queuing.
[^ddia-ch10]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapters 10 & 11**: Batch and stream micro-batch coalescing, shuffle partitioning, and object-store small-file write amplification.
[^kleppmann-logs]: **Tier 1: M. Shreedhar and George Varghese, "Efficient Fair Queuing Using Deficit Round-Robin," *IEEE/ACM Transactions on Networking*, 4(3), 375-385 (1996)**: Original $O(1)$ Deficit Round-Robin (`DRR`) scheduling algorithm for variable-sized packets and multi-tenant work items.
