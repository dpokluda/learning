# 17: Distributed Streaming and Log-Centric Systems: Kafka

In Module 13 we saw that asynchronous messaging and event streams are the glue that keeps microservices decoupled.
Yet traditional enterprise message brokers (built around JMS or AMQP queues) were designed like miniature transactional B-tree databases: the broker tracked per-message delivery state for every individual consumer, deleted each message from disk the moment it was acknowledged, and choked whenever queues backed up beyond RAM or throughput climbed into millions of messages per second.
In 2011, Jay Kreps, Neha Narkhede, and Jun Rao at LinkedIn published **Kafka: a Distributed Messaging System for Log Processing**[^kafka-2011], reimagining the message broker from first principles around the simplest, fastest data structure in distributed systems: **the partitioned, replicated, append-only commit log** (Module 06).
This module dissects how Apache Kafka achieves millions of writes and reads per second on commodity hardware using sequential disk I/O, OS pagecache, and **`sendfile` zero-copy transfers**, how **Partitions and Consumer Groups** unify competing-consumer queues and pub/sub broadcasts in a single abstraction, how **In-Sync Replicas (ISR)**, the **High-Water Mark (HWM)**, and **Leader Epochs** guarantee durability without split-brain truncation bugs, how **Idempotent and Transactional Producers** achieve exact-once stream processing, and why Kafka replaced **ZooKeeper** with **KRaft** consensus.

> **Prerequisite:** [Module 04: Consensus, Leases, and Split-Brain](./04-consensus-leases-and-split-brain.md), [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md), and [Module 13: Microservices, Messaging, and Event-Driven Architecture](./13-microservices-messaging-and-event-driven-architecture.md).

---

## 1. Topics, partitions, offsets, and why ordering is per-partition

In Kafka, messages (**Records**, each consisting of a key, a value payload, a timestamp, and optional headers) are categorized into named feeds called **Topics**[^kafka-2011][^kafka-docs].
Because a single topic (such as `atlas-device-telemetry`) may ingest 2 million records per second; far exceeding the network and disk bandwidth of a single server (**Broker**); each Topic is horizontally sharded into one or more **Partitions**:

```
Topic: "atlas-device-events" (Split into 3 Partitions across Brokers)
=====================================================================
Partition 0:  [ Offset 0 ] [ Offset 1 ] [ Offset 2 ] [ Offset 3 ] [ Offset 4 ] ---> (Append new writes here)
Partition 1:  [ Offset 0 ] [ Offset 1 ] [ Offset 2 ] --->
Partition 2:  [ Offset 0 ] [ Offset 1 ] [ Offset 2 ] [ Offset 3 ] --->
```

Each **Partition** is an ordered, immutable sequence of records stored as a **Segmented Write-Ahead Log** on disk (Module 06), where every record inside the partition is assigned a sequential 64-bit integer ID called its **Offset** (`0, 1, 2, ...`).
Three fundamental architectural consequences follow directly from this layout[^kafka-2011][^ddia-ch11]:

1. **Ordering is guaranteed strictly *within a single Partition*, NEVER across an entire Topic**: Records `Offset 0`, `Offset 1`, and `Offset 2` inside `Partition 0` are totally ordered forever, but there is no ordering guarantee between `Offset 2` of `Partition 0` and `Offset 2` of `Partition 1`.
2. **Deterministic Key Hashing Preserves Per-Entity Order**: How do you ensure that all lifecycle events for `DeviceId = "D-402"` (`Provisioned -> Updated -> Rebooted`) are processed in strict causal order while still scaling across 64 partitions? The producer sets the record's **Key** to `"D-402"`, and the Kafka producer hashes the key (`Murmur2(key) % numPartitions`) to route **every event for `"D-402"` to the exact same partition**!
3. **Reads Do Not Delete Data (Dumb Broker, Smart Consumer)**: Unlike a traditional queue that deletes a message when popped, Kafka brokers retain log segments for a configurable retention window (say `7 days` or `1 TB` per partition, or indefinitely via key-based **Log Compaction**, Module 06) regardless of who has read them! A consumer simply pulls sequential batches starting from its current `Offset` (`FetchRequest(Topic, Partition, Offset=42)`) and commits its progress offset to an internal compacted topic (`__consumer_offsets`). If a consumer bug corrupts a downstream materialized view, you can deploy a bug fix, **rewind the consumer's offset back by 6 hours, and replay the stream from disk**!

---

## 2. Consumer groups: unifying Observe (pub/sub) and Consume (queues)

In Module 13 we saw that messaging systems traditionally required two different primitives: a **Queue** for competing consumers (each message processed by one worker) and a **Topic** for publish-subscribe observation (each message broadcast to all subscribers).
Kafka unifies both patterns through a single concept: the **Consumer Group**[^kafka-2011][^ddia-ch11]:

```
Topic "atlas-events" (4 Partitions)        Consumer Group A (Competing Queue: 4 instances)
+-------------+                            +---------------------------------------------+
| Partition 0 | -------------------------> | Consumer A1 (Owns P0)                       |
| Partition 1 | -------------------------> | Consumer A2 (Owns P1)                       |
| Partition 2 | -------------------------> | Consumer A3 (Owns P2)                       |
| Partition 3 | -------------------------> | Consumer A4 (Owns P3)                       |
+-------------+                            | [Consumer A5 -> IDLE! Only 4 partitions!]   |
   |  |  |  |                              +---------------------------------------------+
   |  |  |  |
   |  |  |  |                              Consumer Group B (Independent Pub/Sub Observer: 2 instances)
   |  |  |  +----------------------------> +---------------------------------------------+
   |  |  +-------------------------------> | Consumer B1 (Owns P0, P1)                   |
   |  +----------------------------------> | Consumer B2 (Owns P2, P3)                   |
   +-------------------------------------> +---------------------------------------------+
```

Every Kafka consumer belongs to a named `group.id` (**Consumer Group**), governed by one ironclad assignment invariant:
> **Within a single Consumer Group, each Topic Partition is assigned to at most ONE live consumer instance at any time.**

Trace what that single rule achieves:
- **Competing Consumers (Load-Balanced Queue Semantics)**: Inside `Consumer Group A`, all 4 instances (`A1..A4`) share the work of the topic; each partition belongs to exactly one consumer (`P0 -> A1`, `P1 -> A2`, ...), guaranteeing both horizontal load distribution and **Sequential Convoy** per-partition ordering without any distributed locking between workers!
- **Why `Consumers > Partitions` Leaves Workers Idle**: Because a partition cannot be split across two consumers in the same group without destroying offset ordering, **the number of partitions in a topic is the hard upper bound on intra-group consumer parallelism** (`A5` in the diagram above receives zero partitions and sits idle as a warm failover standby).
- **Publish-Subscribe (Observe Semantics)**: When a second microservice (`AuditService`) wants to observe the exact same stream independently, it simply connects with a **different `group.id` (`Consumer Group B`)**! `Group B` tracks its own offsets independently and reads the full stream out of the OS pagecache without interfering with `Group A`.

### Rebalance Protocols: Eager vs Cooperative Incremental

When a consumer instance joins or crashes (detected via missed heartbeats to the broker acting as the **Group Coordinator**), the group executes a **Rebalance** to reassign partitions across the surviving consumers[^kafka-docs]:
- **Eager Rebalance (Legacy Stop-the-World)**: Every consumer in the group is forced to **revoke all of its currently assigned partitions simultaneously**, pause processing, rejoin the group, and receive a brand-new assignment. On a 100-node consumer fleet, a single pod rolling-restarting pauses all 100 pods for several seconds!
- **Cooperative Sticky (Incremental) Rebalance**: Consumers **keep processing their unaffected partitions continuously** during the rebalance; only the specific subset of partitions that actually need to move from the departing or overloaded node are revoked and reassigned in a quick two-phase handshake.

---

## 3. Replication, In-Sync Replicas (ISR), High-Water Mark, and Leader Epoch

How does Kafka guarantee that acknowledged messages are never lost when a broker's disk dies?
Each Partition is replicated across $N$ brokers (the replication factor, typically $N = 3$) using a **Leader-Follower** model[^kafka-docs][^joshi-pds]:
- Exactly one broker is elected as the **Partition Leader**, and the remaining $N - 1$ brokers act as **Followers**.
- All producer writes (and classically all consumer reads, though modern Kafka also supports **Follower Fetching** from the closest rack replica to save cross-AZ bandwidth) go to the Partition Leader.
- Followers replicate by acting like ordinary Kafka consumers: they send sequential `FetchRequest`s to the Leader and append the received batches to their own local segmented WAL in exact offset order.

### The In-Sync Replica (ISR) Set and Producer `acks`

A Follower that is actively fetching from the Leader and has caught up to the Leader's log end within `replica.lag.time.max.ms` (for example, `30 seconds`) is a member of the partition's **In-Sync Replica (ISR) set**.
If a Follower crashes or falls behind by more than `30 seconds`, the Leader removes it from the ISR set and persists the shrunk ISR list to the cluster metadata log.

When a Producer sends a batch to the Partition Leader, the `acks` configuration controls when the Leader returns success:
1. **`acks = 0`**: Producer fires and forgets without waiting for any TCP/broker response. Maximum throughput, zero durability guarantee.
2. **`acks = 1`**: Leader appends the record to its **own local log** and responds immediately without waiting for any Follower. If the Leader crashes before Followers pull the record, **the acknowledged record is permanently lost**.
3. **`acks = all` (or `-1`) + `min.insync.replicas = 2`**: The Leader waits until **all replicas currently in the ISR set** (and at least `min.insync.replicas = 2` replicas total, so the Leader cannot acknowledge alone if both Followers died!) have fetched and written the record before advancing the **High-Water Mark (HWM)** and returning success to the Producer.
   - Pairing `replication.factor = 3`, `min.insync.replicas = 2`, `acks = all`, and `unclean.leader.election.enable = false` (forbidding out-of-sync replicas from ever being elected Leader) is the standard production recipe for **zero data loss (`RPO = 0`)**.

### Why `High-Water Mark` Alone Needed the `Leader Epoch` Fix

In Module 06 we saw that the **High-Water Mark (HWM)** is the highest offset replicated across all ISRs, and consumers are never allowed to read past `HWM`.
Historically, Kafka followers also used `HWM` during leader failover: when a Follower restarted after a crash, it truncated its local log back to its local `HWM` and fetched from the new Leader from there.
However, as Jun Rao and the Kafka team documented in **KIP-101**, relying on `HWM` alone for log truncation has a subtle split-brain race condition because **followers learn the updated `HWM` one fetch round-trip *after* they append the data!**

```
The Classic HWM Truncation Bug (Solved by Leader Epoch / KIP-101):
==================================================================
1. Leader B1 and Follower B2 both hold [Offset 0, Offset 1].
   B1 knows HWM = 2, but B2 has not yet received the next FetchResponse updating B2's HWM (still 1!).
2. Follower B2 restarts, truncates its log back to its stale local HWM = 1 (deleting Offset 1!),
   and right at that instant Leader B1 crashes!
3. B2 is elected the new Leader (since it was in the ISR!) with only [Offset 0],
   and accepts a new conflicting write at [Offset 1'] -> Committed Offset 1 was LOST and DIVERGED!
```

Kafka solved this permanently by introducing the **Leader Epoch** (a monotonic 32-bit **Generation Clock / Fencing Token**, Module 04)[^joshi-pds][^kafka-docs]:
Every time a new Partition Leader is elected, the controller increments `LeaderEpoch` (`Epoch 1`, `Epoch 2`, ...), and every broker records `(LeaderEpoch, StartOffset)` in a local checkpoint file.
Instead of blindly truncating to a stale local `HWM` on restart, a recovering follower sends `OffsetsForLeaderEpochRequest(followerEpoch)` to the active Leader, discovers the exact offset where `followerEpoch` ended on the authoritative Leader, and truncates **only** uncommitted divergent entries above that exact epoch boundary!

---

## 4. Zero-copy I/O, idempotent/transactional producers, and ZooKeeper to KRaft

Three final engineering mechanisms explain how modern Kafka scales to millions of partitions and strict stream-processing guarantees:

### 1. Why Kafka Is So Fast: OS Pagecache + `sendfile()` Zero-Copy

In a naive broker written in Java or C#, serving a message from disk to a consumer socket requires **4 context switches and 4 memory copies**:
1. Disk controller DMAs bytes into the **OS Kernel Pagecache Buffer**.
2. Application `read()` copies bytes from Kernel space into a **User-Space Application Buffer** (polluting JVM/CLR GC heap!).
3. Application `write()` copies bytes from User space back into the **OS Kernel Socket Buffer**.
4. NIC driver DMAs bytes from the Kernel Socket Buffer out onto the network wire.

```
Traditional Read + Write (4 Copies):    [Disk] --DMA--> [Kernel Pagecache] --CPU--> [User Heap] --CPU--> [Socket Buf] --DMA--> [NIC]
Kafka Zero-Copy sendfile() (2 DMAs):    [Disk] --DMA--> [Kernel Pagecache] ------------(Descriptor only)-------------> --DMA--> [NIC]
```

Kafka eliminates Steps 2 and 3 completely[^kafka-2011]:
Because Kafka stores messages on disk in the **exact binary wire format** that producers send and consumers expect (batching and compressing records end-to-end with `LZ4` or `Zstandard`), the broker calls the Linux **`sendfile()`** (`FileChannel.transferTo`) system call.
The OS kernel instructs the NIC DMA engine to stream bytes **directly out of the OS Kernel Pagecache to the network card**, with **zero copies into user-space JVM memory and zero garbage-collection pressure**!
Even better, when consumers are caught up to the live tail of the stream, the bytes written by the Producer are already warm in the OS Pagecache, so `sendfile()` serves consumers at `10+ GB/sec` with **zero physical disk reads**!

### 2. Idempotent Producers and Transactional Stream Processing

What if a network blip drops the broker's ACK for a Producer batch, causing the Producer to retry and duplicate the batch in the log?
- **Idempotent Producer (`enable.idempotence = true`)**: The broker assigns each Producer instance a unique 64-bit **Producer ID (`PID`)**, and the Producer attaches a monotonically increasing **Sequence Number (`SeqNum`)** per `(PID, TopicPartition)` to every batch. The Partition Leader tracks the highest `SeqNum` per `PID` in memory and in the log; if a retried batch arrives with `SeqNum <= lastSeenSeqNum`, the broker silently deduplicates it and returns `OK`, guaranteeing **Exactly-Once Append Ordering per Partition**!
- **Transactional Producer (`transactional.id`)**: For stream-processing pipelines (`Read from Topic A -> Transform -> Write to Topic B and Commit Offset of A`), a **Transaction Coordinator** broker runs a two-phase commit protocol using an internal `__transaction_state` log and epoch fencing (`ProducerEpoch`) so that output records across multiple partitions and input consumer offset commits become visible (`READ_COMMITTED`) atomically as a single unit!

### 3. From ZooKeeper to KRaft (Kafka Raft Metadata Mode)

For its first decade, Kafka stored broker metadata, topic configurations, and ISR lists in an external **Apache ZooKeeper** cluster, with one Kafka broker elected as the **Controller** watching ZooKeeper znodes.
At scale (above ~200,000 partitions), that dual-system architecture became a severe bottleneck: whenever the Controller broker failed over, the new Controller had to synchronously read hundreds of thousands of znodes out of ZooKeeper and push giant full-state metadata RPCs (`UpdateMetadataRequest`) to every broker in the cluster, freezing partition leadership transitions for minutes!
Starting in Kafka 3.x and mandatory in Kafka 4.0, **KRaft (KIP-500)** removed ZooKeeper entirely and moved cluster metadata into an internal, event-sourced **Raft consensus topic (`__cluster_metadata`)** managed by 3 or 5 **Controller Brokers**[^kafka-docs]:
- The Active Controller is simply the **Raft Leader** of `__cluster_metadata`.
- Standbys and all data brokers continuously tail `__cluster_metadata` as an event stream, keeping an up-to-date in-memory metadata image at all times.
- When the Active Controller fails, the new Raft Leader is **already warm in memory** and takes over in milliseconds, scaling a single Kafka cluster comfortably to **millions of partitions**!

---

## 5. Kafka in the Atlas platform

In **Atlas**, **Apache Kafka (running in KRaft mode)** serves as the central nervous system connecting the Edge Ingestion Gateways to downstream storage, analytics, and rollout safety controllers:
- **Partitioning by `Hash(TenantId + ":" + DeviceId)`**: All device lifecycle and rollout status events are published with `Key = TenantId + ":" + DeviceId` across 512 partitions per region, guaranteeing that per-device state transitions arrive in strict FIFO order at the Device Twin updater.
- **Multi-Group Observation**: Three independent Consumer Groups read the exact same `atlas-device-events` log in parallel out of the broker OS pagecache: `group-twin-writer` updates the Device Twin document store, `group-cassandra-archiver` bulk-flushes time-series SSTable batches, and `group-rollout-health-monitor` updates real-time HyperLogLog and error-rate counters to trigger automatic rollout circuit-breaking if a canary firmware build spikes in crash rate.
- **Zero-Loss Rollout Audit Log**: The `atlas-rollout-commands` topic runs with `replication.factor = 3`, `min.insync.replicas = 2`, `acks = all`, and `enable.idempotence = true`, backed by **Leader Epoch** checkpointing so no approved rollout command can ever be duplicated or lost during a broker reboot.

---

## Before you move on

**Apache Kafka** achieves massive streaming throughput and replayability by structuring every topic as a set of **partitioned, append-only segmented commit logs**.
Producers append records to the **Partition Leader** (routing related entities to the same partition via **Key Hashing** to guarantee strict intra-partition ordering), while **Consumer Groups** allow workers within the same group to divide partitions as **Competing Consumers** (`1 partition -> at most 1 consumer in a group`) and allow different groups to **Observe** the full stream independently by tracking their own **Offsets**.
On the durability path, **Followers** fetch from the Leader, the **In-Sync Replica (ISR)** set tracks caught-up replicas, **`acks = all` + `min.insync.replicas`** guarantees quorum persistence, the **High-Water Mark (HWM)** bounds consumer visibility, and the monotonic **Leader Epoch** prevents log truncation divergence on failover.
Performance comes from sequential disk writes, OS **Pagecache** locality, and **`sendfile()` zero-copy** transfers, while **KRaft** unifies cluster metadata into an internal Raft log.

To test your understanding before moving to Part V (Algorithms and Data Structures at Principal Scale), consider this changed case: an engineering team has a Kafka topic `device-commands` configured with **12 Partitions**, and their `command-processor` Consumer Group currently runs **12 consumer pods** (`1 pod per partition`), each processing `1,000 messages/sec` (`12,000 messages/sec` total).
During a traffic spike to `24,000 messages/sec`, the on-call engineer scales the Kubernetes Deployment of `command-processor` from **12 pods up to 24 pods**, expecting throughput to double.
First, why does scaling from 12 pods to 24 pods produce **zero increase in throughput** (leaving 12 pods completely idle)?
Second, if the engineer then dynamically increases the Kafka topic from **12 Partitions to 24 Partitions** while live traffic is flowing, what happens to the **per-key ordering guarantee (`Murmur2(DeviceId) % numPartitions`)** for devices whose events are already sitting in the original 12 partitions while new events immediately start hashing modulo 24?
If both answers are not crystal clear, revisit **Section 1** and **Section 2** before continuing.

---

### Sources

[^kafka-2011]: **Tier 1: Jay Kreps, Neha Narkhede, and Jun Rao, "Kafka: a Distributed Messaging System for Log Processing" (*ACM NetDB Workshop*, 2011)**: Partitioned append-only log design, consumer groups, offset tracking, OS pagecache integration, and `sendfile()` zero-copy network transfer.
[^kafka-docs]: **Tier 3: Apache Kafka Official Documentation (*Design & Implementation*, KIP-98, KIP-101, KIP-429, KIP-500)**: Replication, In-Sync Replicas (ISR), High-Water Mark, Leader Epoch truncation safety (KIP-101), Idempotent & Transactional Producers (KIP-98), Incremental Cooperative Rebalancing (KIP-429), and KRaft consensus metadata architecture (KIP-500) ([https://kafka.apache.org/documentation/#design](https://kafka.apache.org/documentation/#design)).
[^ddia-ch11]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 11: "Stream Processing"**: Partitioned logs vs traditional message brokers, consumer offsets, log compaction, and stream-table duality.
[^joshi-pds]: **Tier 2: Unmesh Joshi, *Patterns of Distributed Systems* (Addison-Wesley, 2023)**: High-Water Mark, Generation Clock (Leader Epoch), and Replicated Log patterns.
