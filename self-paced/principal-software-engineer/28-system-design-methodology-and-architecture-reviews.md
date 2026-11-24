# 28: System Design Methodology and Architecture Reviews

Knowing all twenty-seven preceding modules (quorums, consensus, B+Trees, LSM-Trees, consistent hashing, Bloom filters, caching failure modes, rate limiters, sagas, DDD bounded contexts, Kafka, capacity math, control/data plane stamps, multi-tenant fan-out, Parquet/Delta Lake, embedded DSL/Rust policy engines, testing in production, and MCP/hybrid-RAG agents) gives you the complete vocabulary of Principal engineering.
Yet in a 45-to-60-minute System Design interview (or a high-stakes production architecture review), **how you sequence and communicate your design** matters just as much as what you know.
Senior candidates fail Principal system design loops in two predictable ways: either they jump straight into drawing a Kafka topic and a Redis cache in minute two without scoping the functional requirements or calculating whether the dataset is 10 GB or 10 PB, or they spend forty minutes listing surface-level boxes across ten microservices (`breadth`) without ever diving deep into partition keys, race conditions, failure modes, and CAP/PACELC trade-offs (`depth`).

This module gives you a battle-tested, six-phase methodology for driving any system design session or architecture review from a blank whiteboard to a defensible distributed architecture, followed by a complete, end-to-end worked walkthrough of our running example, the **Atlas** platform.

> **Prerequisite:** Parts I through VI ([Modules 01](./01-distributed-systems-models-and-failure-modes.md) through [27](./27-production-ai-and-agentic-engineering-systems.md)), especially [Module 18: Capacity Planning and Asymptotic Analysis](./18-capacity-planning-and-asymptotic-analysis.md) and [Module 22: Control Plane vs Data Plane and Cell-Based Architectures](./22-control-plane-vs-data-plane-and-cell-architectures.md).

---

## 1. The six-phase Principal system design methodology

Whether you have 45 minutes in an interview or 60 minutes leading a Principal architecture review, pace your time deliberately across six structured phases[^ik-sd][^ddia-ch1]:

| Phase | Target time (45-min session) | Core objective & output artifact |
| --- | --- | --- |
| **Phase 1: Functional Requirements** | `3 - 5 min` | Define 3 to 5 core **User Stories** in plain English; explicitly declare what is **Out of Scope**. |
| **Phase 2: Technical (Non-Functional) Requirements & Sizing** | `4 - 6 min` | Pin down scale (`DAU/Devices`, `Read/Write RPS`, `Storage`), latency SLAs (`p99`), and **CAP / PACELC** trade-offs per workflow. |
| **Phase 3: Microservice Identification (`Depth vs Breadth`)** | `3 - 5 min` | Group user stories around core domain objects/aggregates; classify the problem as **Depth-Oriented** or **Breadth-Oriented**. |
| **Phase 4: Logical Block Diagram & Data Flows** | `5 - 7 min` | Draw the service blocks and data flows, explicitly distinguishing **Synchronous (`--->`)** from **Asynchronous (`- - ->`)** paths. |
| **Phase 5: Tiered Deep Dive (`App + Cache + Storage`)** | `20 - 25 min` | Deep-dive into the 1 or 2 hardest services: API contracts, exact DB schemas/keys, sharding, caching, and scaling. |
| **Phase 6: Bottlenecks, Failure Modes & Trade-Off Defense** | `5 - 8 min` | Eliminate every **Single Point of Failure (SPOF)**, neutralize hotspots, and defend CP/AP, recovery, and multi-region choices. |

Let us examine the exact mechanics of Phases 1 through 5 before executing them on Atlas.

---

## 2. Phase 1 & Phase 2: Functional scoping and constraint discovery

Never start drawing boxes when given a vague prompt like *"Design a global IoT update service"* or *"Design Uber / Twitter / a Distributed Rate Limiter."*
A Principal engineer spends the first five minutes **eliminating ambiguity** so every architectural choice later in the session traces back to an agreed requirement.

### Phase 1: Functional Requirements (User Stories in Plain English)

State the system's behavior from the external actor's perspective using concise **User Stories** (`Actor -> Action -> Outcome`), and organize them by the distinct personas interacting with the system:
- Ask clarifying questions that actually **change the architecture**:
  - *"Are updates pushed in real time (< 1 second) to connected devices, or do devices poll periodically, or both?"* (Decides between WebSockets/gRPC streams vs stateless HTTP Short Polling, Module 10.)
  - *"What is the maximum payload size: 2 KB JSON configs or 500 MB firmware binaries?"* (Decides whether payloads flow through the API/message bus or require the **Valet Key + Claim Check** pattern, Modules 13 & 14.)
  - *"Do we need strict per-entity ordering or idempotent eventual convergence?"* (Decides between Kafka partition-keyed **Sequential Convoys** vs unordered competing consumers.)

### Phase 2: Technical (Non-Functional) Requirements & Back-of-the-Envelope Math

Once functional scope is locked, write down four quantitative numbers using the shortcuts from Module 18 (`1 day ~ 10^5 sec`, `3 years ~ 10^8 sec`):
1. **Read & Write Throughput (Average and Peak RPS)**: Compute writes/sec and reads/sec separately. State the read-to-write ratio (`100:1` vs `1:10`) because a `100:1` read-heavy system is a **caching and replication** problem, whereas a `1:10` write-heavy system is an **LSM-Tree, partitioning, and load-leveling queue** problem!
2. **Storage Footprint (Daily and 3-Year Total)**: Multiply `Writes/sec * PayloadSize * 10^8 sec * ReplicationFactor`. Immediately state the architectural consequence: *"At 6 TB total over 3 years, this metadata fits in a handful of relational shards; at 13.5 PB over 90 days, this telemetry stream requires a horizontally partitioned wide-column LSM-Tree + columnar Parquet/Delta Lake tiering."*
3. **Latency SLA (`p50` and `p99`)**: State concrete targets (for example, `p99 < 50 ms` for edge config reads; `p99 < 250 ms` for control-plane policy writes).
4. **CAP / PACELC Posture per Workflow**: Never say *"the whole system is CP"* or *"the whole system is AP."* Explicitly split the workflows: **billing, quota enforcement, and rollout safety locks are `PC / EC` (CP)**, while **telemetry ingestion and status reporting are `PA / EL` (AP)**.

---

## 3. Phase 3 & Phase 4: Microservice identification (`Depth vs Breadth`) and logical diagramming

How do you move from a list of user stories to a clean microservice boundary without guessing?
Extract the **core business nouns (Domain Aggregates, Module 14)** from your user stories and bucketize the functional requirements around them[^ik-sd].

Immediately classify which of the two fundamental system archetypes you are designing:
- **Archetype A: Depth-Oriented System (`1 to 2 Core Microservices`)**: Problems like *"Design a Distributed Key-Value Store,"* *"Design a Rate Limiter,"* *"Design a Web Crawler,"* or *"Design a Metrics Time-Series DB."* Here, drawing 12 microservices is a mistake; there are only 1 or 2 services, and 80% of your session must go deep into **storage engine internals (WAL, MemTable, SSTables, Parquet Row Groups), consensus/quorums, consistent hashing, and concurrency control**.
- **Archetype B: Breadth-Oriented System (`4 to 6 Core Microservices`)**: Problems like *"Design an E-Commerce Checkout,"* *"Design a Video Streaming Platform,"* or *"Design the Atlas Fleet & Rollout Platform."* Here, you establish the clean end-to-end **Logical Block Diagram** across all 4 to 6 bounded contexts first, showing how synchronous and asynchronous messages flow between them, and then **explicitly pick the 1 or 2 most critical microservices for your Phase 5 Deep Dive**.

When drawing your **Logical Block Diagram** in Phase 4, always make three visual distinctions explicit:
1. Separate the **Control Plane** (low-volume, high-consistency `PC/EC` configuration and orchestration) from the **Data Plane** (high-volume, low-latency `PA/EL` request and telemetry execution hydrated via periodic snapshots and incremental delta queues, Module 22).
2. Draw **solid arrows (`--->`)** for synchronous request-response calls and **dashed arrows (`- - ->`)** for asynchronous queue/event-stream flows.
3. Never draw an application server talking directly to 5 downstream services synchronously on a write path if an asynchronous **Transactional Outbox -> Kafka Topic** can decouple them!

---

## 4. Phase 5: The three-tier deep dive (Application, Cache, and Storage)

For each microservice you deep-dive into during Phase 5, walk systematically from top to bottom through its three internal tiers (`Application -> Cache -> Storage`), answering both **"How does it work functionally?"** and **"How does it scale and survive failures?"**[^ik-sd]:

### 1. Application Tier Deep Dive

- **API Contracts**: Write 2 or 3 concrete HTTP REST or gRPC signatures with their exact route parameters, headers (`Idempotency-Key`, `If-Match: "<etag>"`), status codes (`200 OK`, `202 Accepted + Operation-Location + Retry-After`, `429 Too Many Requests + Retry-After`), and core request/response JSON fields.
- **Stateless vs Stateful Scaling**: If the application tier is **stateless**, state how many VMs/pods are needed (`Peak RPS / 8,000 RPS per VM` or `30,000 / T_ms`) behind an L7 load balancer using **Round Robin** or **Power of Two Random Choices (`P2C`)** and **Shuffle Sharding** (Module 22). If the tier holds **stateful connections** (WebSockets / gRPC bidirectional streams), explain how connections are routed via **Rendezvous / Consistent Hashing** and how a backplane pub/sub routes commands to the exact gateway holding `DeviceId`.

### 2. Cache Tier Deep Dive

- **Pattern & Key Design**: Specify the exact cache key (`tenant:{id}:config:{version}`), the topology (**Cache-Aside** with `Singleflight` request coalescing and distributed **Leases**, or **Zero-Dependency Stamp Snapshot + Delta Queue Hydration**, Module 22), and the two-tier layout (**L1 in-process `W-TinyLFU`** + **L2 Redis Cluster** partitioned via Consistent Hashing).
- **Invalidation & Failure Protection**: State how cache entries are invalidated on write (Outbox/CDC event or Delete-on-Write with Lease tokens), how **TTL Jitter** prevents cache avalanches, and how a **Bloom Filter** or short negative caching blocks cache penetration.

### 3. Storage Tier Deep Dive

- **Engine & Schema**: Justify **SQL (PostgreSQL/Spanner B+Tree)** vs **NoSQL (Cassandra/BigTable LSM-Tree or Document Store)** vs **Columnar Lakehouse (Parquet + Delta Lake / Iceberg)** from the access patterns and ACID requirements in Phase 2. Write out the exact table schema, **Primary / Partition Key**, **Clustering / Sort Key**, and any **Covering Secondary Indexes** or **Index Tables**.
- **Sharding, Replication & Hotspots**: Specify the shard key, how shards are placed on nodes (**Consistent Hashing with 256 Vnodes** or **Directory-based tablets**), the replication factor (`N = 3` across 3 Availability Zones), the write/read consistency mechanism (**Raft Leader-Follower with High-Water Mark** for `CP` vs **Tunable Quorum `LOCAL_QUORUM` + Read Repair + Merkle Trees** for `AP`), and how **Key Salting (`0..S-1`)** and **Tenant Batch Coalescing** (Module 23) neutralize mega-tenant or multi-tenant fan-out hotspots.

---

## 5. Complete worked walkthrough: designing the Atlas platform end to end

Let us now put the entire six-phase methodology into action on **Atlas**, bringing together every module in this book into one cohesive blueprint.

### Phase 1: Functional Requirements for Atlas

1. **Device Telemetry Ingestion**: 500M edge and IoT devices periodically report health metrics (`CPU`, `Memory`, `Battery`, `FirmwareVersion`, `GPS`) to the cloud.
2. **Device Configuration & Twin Sync**: Devices pull their desired configuration state and report their applied state; operators can query any device's current Twin or regional fleet health aggregates.
3. **Safe Phased Firmware Rollouts & `< 5-Second` Kill-Switch**: Tenant administrators upload 200 MB firmware binaries, define target **Applicability Expressions** (e.g., `Sku == 'Industrial' AND Region == 'EU' AND Version < 4.2.0`) and phased canary schedules (`1% -> 10% -> 50% -> 100%`), and Atlas automatically halts and rolls back a rollout across `Scan`, `Download`, and `Pre-Install` stages in `< 5 seconds` if device error rates exceed a safety threshold.
4. **Multi-Tenant Data Residency & Customer-Hosted Option**: All customer-identifiable data and logs for European tenants must remain physically inside EU datacenters (`GDPR`), and regulated defense/banking tenants can host their data plane inside their own cloud subscription (`Managed Resource Group`).

### Phase 2: Non-Functional Requirements & Capacity Summary

- **Scale**: `500M` devices across 10 regional stamps (`50M devices/stamp`); **Telemetry Writes**: `1.67M RPS` average (`5.0M RPS` peak); **Config/Rollout Reads**: `250,000 RPS` average (`1.5M RPS` peak on Patch Tuesday); **Admin Control-Plane Writes**: `500 RPS`.
- **Storage**: Telemetry = `50 TB/day` raw (`4.2 TB/day` in Zstd-compressed **Parquet / Delta Lake** + `13.5 PB` hot 90-day Cassandra ring across $N = 3$ replicas); Device Twins = `2 TB` raw (`6 TB` replicated across 64 shards); Firmware Binaries = `500 TB` in Content-Addressable Blob Storage + CDN.
- **Latency & PACELC**:
  - *Control Plane & Rollout Safety Gate*: **`PC / EC` (Strict CP)**, `p99 < 200 ms` (`HTTP 202` LRO for binary ingestion). Split-brain or write skew on rollout activation is unacceptable.
  - *Data Plane Stamp Sync & Telemetry Ingestion*: **`PA / EL` (Statically Stable `99.999%` AP)**, `p99 < 50 ms`. Never drop edge telemetry or block device syncs when the central Control Plane is down.

### Phase 3 & Phase 4: Microservices and Logical Block Architecture

Atlas is a **Breadth-Oriented System** with two deep internal engines (`Statically Stable Stamp Data Plane` and `Rollout Orchestration Control Plane`), decomposed into five bounded-context microservices inside each sovereign regional stamp (`US`, `EU`, `APAC`):

```
                    [ 500M Edge & IoT Devices ]
                      /                     \
   (1. 200MB Binary  /                       \ (2. mTLS Telemetry, Twin Sync & Rollout Poll)
    via Valet Key)  v                         v
      [ Geo CDN + Blob Store ]       [ Anycast L4 LB ---> L7 Envoy Gateways ]
                 ^                    (TLS 1.3, Token Bucket, Shuffle Sharding,
                 |                     Privacy Mapping Pseudonymization, Kill-Switch Bitset)
                 |                            |                    |
                 | (Valet Key SAS)            | (Sync Read/Write)  | (Async Append)
                 |                            v                    v
      [ Firmware Catalog Svc ]     [ Device Twin & Sync Svc ]   [ Kafka (KRaft, 512 Partitions) ]
      (SHA-256 Dedup, Malware       (Stamp Snapshot + Delta       /              |              \
       Scan, HSM Sign, P2P DO)       Queue, Bloom + AST/Rust)    /               |               \
                 ^                            ^                 v                v                v
                 |                            |        [Twin Updater]  [Claim-Check Coalescer] [Rollout Health]
                 +------------+---------------+                        (Cassandra TWCS +        (HLL + CMS +
                              |                                         Parquet Delta Lake)      <5s Kill-Switch)
                   [ Rollout Orchestrator Svc ] <---------------------------------------------------+
                   (PostgreSQL SSI + Outbox + etcd Leases with Fencing Tokens)
```

### Phase 5: Deep Dive into the Rollout Orchestrator and Telemetry/Twin Pipeline

#### 1. API Contracts & Control Flows

- **Create/Activate Rollout (`POST /v1/tenants/{tenantId}/rollouts?api-version=2026-11-01`)**:
  - Headers: `Idempotency-Key: <guid>`, `Authorization: Bearer <jwt>`.
  - Body: `{ "packageDagId": "pkg-99", "targetGroupId": "grp-eu-1", "applicabilityRule": "os >= '4.1' && battery >= 30", "phases": [{"percent": 1, "bakeMinutes": 60}, {"percent": 10, "bakeMinutes": 120}, {"percent": 100}], "maxErrorRate": 0.005 }`.
  - Executes inside a **PostgreSQL transaction** with a **filtered unique index** (`UX_Active_Rollout_Per_Group`) to prevent **Write Skew** (Module 05), validates that the package dependency graph is acyclic using **Kahn's Topological Sort** (Module 20), parses `applicabilityRule` into an immutable **Composite AST** (Modules 14 & 25), writes `RolloutActivatedEvent` to the **Transactional Outbox** table (Module 13), and returns **`HTTP 202 Accepted` + `Operation-Location` + `Retry-After`** (Module 22).
- **Device Sync & Applicability Check (`POST /v1/devices/{deviceId}/sync`)**:
  - Hit by devices polling or streaming over gRPC inside a regional **Cell / Stamp** (Module 22).
  - Checks the in-memory **`DeniedPackageBitset` (`< 5s` Emergency Kill-Switch, Module 26)** first in `10 nanoseconds`.
  - Reads the stamp's pre-compiled catalog hydrated from the **Stamp-Local Snapshot + Incremental Change Queue** (Module 22).
  - Runs **Bloom Filter + Inverted Index Pre-Screening** and evaluates the **Short-Circuit AST / Rust FFI Policy Engine** locally in memory in `< 50 microseconds` (Module 25; zero external `GroupsService` RPC dependency!), checks if `Hash(RolloutId + ":" + DeviceId) % 100 < currentPhasePercent` to determine deterministic canary cohort membership, and if eligible, returns a 15-minute **Valet Key** CDN download URL plus **P2P Delivery Optimization** piece hashes.

#### 2. Coordination, Fencing, and Storage Schemas

- **Preventing Zombie Rollout Coordinators (Module 04)**: Each active rollout is driven by a regional `RolloutCoordinator` worker holding an **etcd (Raft) lease** with a 64-bit monotonic **`CoordinatorEpoch` fencing token**. When the `Rollout Health Monitor` (consuming from Kafka and maintaining a 12 KB **HyperLogLog** of updated devices and a **Sliding Window** error counter per rollout) detects `errorRate > 0.005`, it broadcasts an out-of-band **`< 5-second` Emergency Kill-Switch** frame to all stamps and increments `CoordinatorEpoch` in PostgreSQL (`WHERE @Epoch >= MaxEpoch`), fencing off any paused worker.
- **Cassandra Hot Store + Parquet/Delta Lake Cold Store (Modules 06, 16, 23, & 24)**:
  - Hot operational queries hit Cassandra (`PRIMARY KEY ((tenant_id, device_id, bucket_day), recorded_at, event_id)` with **Time-Window Compaction Strategy** and `LOCAL_QUORUM`).
  - Analytical and security-graph queries hit the **Delta Lake / Apache Parquet Lakehouse**, fed by **Claim-Check Batch Coalescing Workers** (`ArrayPool<byte>`, flushing `256 MB` Z-Ordered Parquet files with `Split-Block Bloom Filters` on `DeviceId`).

### Phase 6: Principal Trade-Off Defense Matrix

In an architecture review or Principal interview, the panel will push back on your design choices.
Here is how you defend the trade-offs explicitly:

| Adversarial question from reviewer | Principal engineering defense |
| --- | --- |
| *"Why evaluate applicability expressions inside the Device Sync Gateway instead of calling a centralized `GroupsService` microservice?"* | Calling an external `GroupsService` synchronously on `1.5M RPS` during a Patch Tuesday spike violates **Static Stability** (Module 22), turns a Tier-2 service into a Tier-1 SPOF, and adds datacenter RTT tail amplification. Hydrating pre-compiled **Composite ASTs** via **Periodic Snapshots + Incremental Change Queues** lets every stamp evaluate 10,000 rules locally in `< 3 ms` with `99.999%` autonomy. |
| *"Why use deterministic hashing (`Hash(RolloutId + DeviceId) % 100 < phasePct`) for canary cohorts instead of storing a `DeviceRolloutAssignment` row for all 500M devices in PostgreSQL?"* | Inserting 500 million assignment rows when a rollout starts would create a multi-minute write storm (`500 GB` of B+Tree churn). Deterministic hashing assigns devices to the `1% -> 10% -> 50%` cohorts with **zero database writes**, and because `Hash(...) % 100` is fixed per `(RolloutId, DeviceId)`, a device in the `1%` canary automatically stays in the `10%` and `50%` cohorts as the threshold expands! |
| *"Why not flush telemetry events directly to per-tenant Parquet files on every 10 MB incoming blob?"* | When a single `10 MB` blob spans `200 tenants`, flushing immediately performs 200 tiny `50 KB` object-store writes (`5 seconds` of metadata latency overhead), collapsing throughput by **87%** and creating the **Parquet Small-File Problem** (Modules 23 & 24). Coalescing per-tenant buffers in memory across batches via Claim-Check workers yields a **$78\times$ throughput increase** and writes optimal `256 MB` Row Groups. |
| *"What happens if a defective firmware update starts causing boot failures at 2:00 PM after 2 million devices have already downloaded the binary for a 3:00 AM maintenance window?"* | A normal catalog publish takes 30 minutes and only stops new `Scan` requests (`Stage A`). Atlas's **`< 5-Second` Multi-Stage Emergency Kill-Switch** (Module 26) simultaneously updates the gateway `DeniedPackageBitset` (`Stage A`), revokes CDN `Valet-Key` tokens (`Stage B`), and rejects the client agent's mandatory **`PreInstallCommit` check** (`Stage C`) at 3:00 AM before the binary is installed! |

---

## Before you move on

A Principal engineer drives a system design session by imposing structure before drawing boxes: **Phase 1 (Functional User Stories)**, **Phase 2 (Non-Functional SLAs, Capacity Math, and CAP/PACELC split)**, **Phase 3 (Domain Microservice Identification and Depth-vs-Breadth framing)**, **Phase 4 (Logical Block Diagram with Control/Data Plane split and Sync vs Async flows)**, **Phase 5 (Three-Tier Deep Dive across Application, Cache, and Storage)**, and **Phase 6 (Failure-Mode, Hotspot, and Trade-Off Defense)**.
Every box on the diagram must justify its existence by solving a concrete bottleneck calculated in Phase 2, and every stateful store must name its partition key, replication topology, and failure-recovery path.

To test your mastery of this methodology before moving to Module 29, take five minutes to run **Phases 1 through 3** on a changed problem: **"Design a Global Distributed Rate Limiter & Quota Service for a Multi-Region Cloud API Gateway (serving 2 million RPS across 10 regions)."**
First, is this a **Depth-Oriented** or **Breadth-Oriented** problem?
Second, what is its **PACELC** classification if a banking customer requires hard global quota caps versus if a SaaS customer prefers never blocking valid API traffic during a cross-region fiber cut?
Third, how do you combine **local in-memory Token Buckets (with batch token reservations)** and **regional Redis Lua clusters** so that `99%` of requests are checked in `< 100 nanoseconds` with zero network calls?
If you can lay out that argument cleanly, you are ready for any system design room.

---

### Sources

[^ik-sd]: **Tier 4: Interview Kickstart System Design Framework & Alex Xu, *System Design Interview: An Insider's Guide* (Vols. 1 & 2, ByteByteGo, 2020 / 2022)**: Structured 6-step system design methodology (Functional Requirements, Non-Functional Requirements & Back-of-the-Envelope Calculations, Microservice Identification, Logical Diagram, Application/Cache/Storage Tier Deep Dive, and Bottleneck/SPOF Resolution).
[^ddia-ch1]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapters 1, 5, 6, 8, & 9**: Synthesizing reliability, scalability, partitioning, replication, and consistency trade-offs in end-to-end systems.
