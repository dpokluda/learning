# 01: Distributed Systems Models and Failure Modes

Every distributed system is an exercise in maintaining useful invariants on top of hardware and networks that are constantly trying to break them.
When an application runs on a single machine, failures are mostly binary: either the process is running and memory accesses succeed in nanoseconds, or the machine crashes and the entire computation stops together.
The moment your system crosses a network cable, that all-or-nothing simplicity vanishes.
A remote node can crash after mutating state on disk but before sending its response over the wire, leaving the caller unable to tell whether the operation succeeded, failed, or is still sitting in a kernel socket buffer.
Before you can reason about replication, consensus, or caching, you need a precise mental model of what the network can do to your packets, how nodes fail, what timing guarantees you are allowed to assume, and how those physical constraints govern the scalability, reliability, and availability of the overall architecture.

> **Prerequisite:** None beyond production software engineering experience. This module establishes the foundational system models used throughout the rest of the book.

---

## 1. Network behavior and link models

Distributed systems communicate by passing messages over point-to-point bidirectional network links.
In a whiteboard diagram, a network call looks like a clean arrow between Service A and Service B.
In production, that arrow is a sequence of electrical or optical signals traversing NIC ring buffers, top-of-rack switches, routers, firewalls, and undersea fiber bundles, any of which can drop, delay, duplicate, reorder, or corrupt bytes.

To reason rigorously about protocol correctness without getting lost in hardware specifics, distributed computing classifies network links into three nested mathematical models, ordered from the most hostile to the best behaved[^kleppmann-lectures][^ddia-ch8]:

1. **Arbitrary (adversarial) links**: An active adversary on the network path can eavesdrop on packets, modify payloads in flight, drop selected messages, spoof sender identities, or replay old packets hours later. The public internet, untrusted cloud virtual networks, and compromised datacenter segments all behave as arbitrary links.
2. **Fair-loss links**: Messages are not maliciously forged, but the network may drop packets, duplicate packets, or deliver them out of order. The defining guarantee of a fair-loss link is *eventual delivery under repetition*: if a sender transmits a message infinitely often, it will be received infinitely often. In practical terms, a fair-loss link may suffer transient congestion or packet loss, but it is not permanently severed forever.
3. **Reliable (perfect) links**: A message is received if and only if it was sent, without corruption and without duplication (though messages may still be delayed or reordered unless a FIFO channel is layered on top).

The reason this three-layer taxonomy is so useful to a Principal engineer is that **you do not buy better hardware to move up the hierarchy; you apply protocol transformations in software**[^kleppmann-lectures]:

```
+-------------------------------------------------------------------+
|                     Reliable (Perfect) Link                       |
|      Guarantee: delivered exactly once if sent (no dup/loss)      |
+-------------------------------------------------------------------+
                                 ^
                                 |  Add: Retry loop + Idempotency key
                                 |       (sender retries; receiver deduplicates)
+-------------------------------------------------------------------+
|                          Fair-Loss Link                           |
|      Guarantee: may drop, duplicate, reorder; no forgery          |
+-------------------------------------------------------------------+
                                 ^
                                 |  Add: Cryptographic authentication & encryption
                                 |       (TLS / mTLS with sequence numbers & MACs)
+-------------------------------------------------------------------+
|                    Arbitrary (Adversarial) Link                   |
|      Reality: packets may be eavesdropped, forged, or replayed    |
+-------------------------------------------------------------------+
```

Read that diagram from bottom to top and two everyday engineering practices suddenly click into place as formal model reductions.
First, wrapping an **arbitrary link** in **Transport Layer Security (TLS / mTLS)** with message authentication codes (MACs) and monotonic record sequence numbers prevents an attacker from forging, mutating, or replaying records undetected.
An adversary can still sever the cable or drop encrypted packets, which turns a cryptographic verification failure into a dropped packet; therefore, **TLS transforms an arbitrary link into a fair-loss link**.
Second, layering an **automatic retry loop on the sender** together with **idempotency-key deduplication on the receiver** ensures that lost packets are retransmitted until acknowledged and duplicate deliveries are collapsed into a single side effect; therefore, **retry plus deduplication transforms a fair-loss link into a reliable link** (provided any network partition eventually heals).

A **network partition** (or netsplit) occurs when a subset of links drops or delays all messages between two groups of nodes for an extended duration[^ddia-ch8].
What makes partitions insidious in real clusters is that they are frequently **asymmetric** or **partial**: Node A can send packets to Node B, Node B can send packets to Node C, and yet Node A's packets to Node C are blackholed by a misconfigured BGP route or a failing top-of-rack switch port.
Any protocol that assumes "reachable" is an equivalence relation will fail in production.

---

## 2. Node failure models

Just as networks fail in distinct ways, the compute nodes executing our algorithms exhibit a spectrum of failure behaviors[^kleppmann-lectures][^tanenbaum-ds].
When you review a distributed design, you must ask which node failure model the design actually tolerates:

| Failure model | What the faulty node does | How it arises in production | Typical mitigation |
| --- | --- | --- | --- |
| **Fail-stop** | Halts permanently at some instant; other nodes can deterministically detect that it has halted. | Theoretical idealization; approximated only inside tightly coupled hardware with dedicated out-of-band power fencing. | Immediate failover upon hardware halt signal. |
| **Crash-stop** | Halts permanently and silently at any moment; other nodes cannot distinguish a crash from a slow network without timeouts. | Ephemeral containers or spot VMs that are terminated and never restarted with their prior identity. | Heartbeat timeouts, quorum replication (`N >= 2f + 1`). |
| **Crash-recovery** | Crashes at any moment, losing all volatile in-memory state, and resumes execution later with its durable disk state intact. | Process panics, OOM kills, OS reboots, power cycling, and rolling deployments on stateful servers. | Write-Ahead Logs (WAL), persistent epoch/term counters, idempotent replay. |
| **Omission (send / receive)** | Continues executing locally but fails to send outgoing messages (`send omission`) or fails to process incoming messages (`receive omission`). | Full NIC transmit/receive queues, kernel socket buffer exhaustion, asymmetric firewall rules. | End-to-end application-level acknowledgments and peer health routing. |
| **Temporal (timing)** | Produces functionally correct outputs, but delivers them too late to satisfy the protocol's timing contract. | Stop-the-world garbage collection (GC) pauses, CPU throttling, noisy-neighbor VM steal time, swapping to disk. | Monotonic leases, fencing tokens, tail-latency hedging. |
| **Byzantine (arbitrary)** | Deviates arbitrarily from the algorithm: sends corrupted data, sends contradictory messages to different peers, or acts maliciously. | Software memory corruption, silent disk/RAM bit flips without checksums, or compromised untrusted nodes. | Cryptographic checksums/signatures; Byzantine Fault Tolerance (`N >= 3f + 1`) when peers are untrusted. |

The most common architectural bug in enterprise cloud systems is designing a protocol for the **crash-stop** model and running it on **crash-recovery** and **temporal-failure** nodes.
In a crash-stop mental model, once a leader node stops responding for ten seconds, you assume it is dead forever and elect a replacement.
In reality, that leader may simply be frozen in a twelve-second .NET or JVM garbage-collection pause (a temporal failure), or it may reboot from a kernel panic and read its old configuration file off NVMe storage believing it is still the leader (a crash-recovery event).
Every stateful protocol in this book assumes the **crash-recovery** model unless explicitly noted otherwise.

---

## 3. Synchrony assumptions and the FLP impossibility boundary

Why can we not simply wait long enough to be 100% certain that an unresponsive node has crashed?
The answer depends on the **synchrony model** of the system[^ddia-ch8][^dwork-partial-sync]:

1. **Synchronous model**: Every message latency is strictly bounded by a known constant \(\Delta\), and every node executes instructions within a known clock drift bound \(\Phi\). If you send a ping and do not receive a reply within \(2\Delta + \Phi\), the remote node is guaranteed to be dead. Hard real-time avionics buses approximate this model, but commodity cloud networks over TCP/IP never do.
2. **Asynchronous model**: Messages can be delayed arbitrarily long, and nodes can pause execution for arbitrary durations. There is no upper bound \(\Delta\) on message delivery or processing speed.
3. **Partially synchronous model**: The system behaves like a synchronous system (messages arrive within \(\Delta\) and processes run at normal speed) *most of the time*, but it can unpredictably flip into asynchronous behavior for finite, unknown periods (during network congestion, routing convergence, or GC pauses) before eventually returning to synchrony after some unknown Global Stabilization Time (GST)[^dwork-partial-sync].

In 1985, Fischer, Lynch, and Paterson proved one of the most consequential theorems in computer science, known as the **FLP Impossibility Result**[^flp-1985]:
> In a purely **asynchronous** network model, no **deterministic** consensus protocol can guarantee both **safety** (never deciding conflicting values) and **liveness** (eventually reaching a decision) if even a **single node** is allowed to crash.

The intuition behind FLP is straightforward once you see it from a node's perspective.
In an asynchronous network, a node waiting for a vote from peer \(X\) has no way to tell whether \(X\) has crashed permanently or whether \(X\)'s packets are merely delayed by a slow router.
If the protocol waits forever for \(X\), it violates **liveness** when \(X\) is truly dead.
If the protocol gives up waiting and decides without \(X\), it risks violating **safety** if \(X\) is still alive on the other side of a delay and deciding a different value.

Modern production consensus protocols (Paxos, Raft, Zab) escape the FLP trap by targeting the **partially synchronous model**[^ongaro-raft][^lamport-paxos].
They guarantee **safety unconditionally** (even when the network is completely asynchronous, messages are delayed for hours, and clocks are wildly wrong, the system will never commit contradictory state), while guaranteeing **liveness conditionally** (as soon as the network enters a synchronous window long enough for a round of heartbeats and votes to complete within the election timeout, the cluster makes progress).

---

## 4. System characteristics: scalability, reliability, availability, and tail latency

When translating these failure models into service-level architecture, we evaluate systems along four macroscopic characteristics[^ddia-ch1]:

### Scalability (vertical vs horizontal)

**Scalability** is not a binary label ("this system is scalable"); it is the ability of a system to maintain acceptable performance as load parameters grow[^ddia-ch1].
You can add capacity in two directions:
- **Vertical scaling (scale-up)** replaces a machine with a larger one (more CPU cores, more RAM, faster NVMe drives). Vertical scaling avoids network coordination overhead and keeps transactions local, but it hits hard physical limits on machine size, exhibits super-linear hardware cost curves, and leaves a single fault domain.
- **Horizontal scaling (scale-out)** distributes load across a fleet of commodity servers. Horizontal scaling removes the ceiling on total capacity and eliminates single-machine points of failure, but it forces you to solve every distributed coordination problem in Parts I and II of this book.

### Reliability vs availability

Engineers often use **reliability** and **availability** interchangeably, but they measure different things:
- **Availability** is the fraction of time (or fraction of requests) during which a system is operational and returning non-error responses under normal service definitions. It is commonly expressed in "nines" (for example, 99.9% availability allows 8.76 hours of downtime per year; 99.99% allows 52.6 minutes per year; 99.999% allows 5.26 minutes per year).
- **Reliability** is the probability that a system performs its required functions *correctly and without data loss or corruption* over a specified period across the full range of real-world fault conditions[^ddia-ch1].

Notice the asymmetry: **if a system is reliable, it is available; if a system is available, it is not necessarily reliable.**
A storage service that returns `200 OK` to every request while silently returning corrupted bytes or losing 1% of acknowledged writes has 100% availability and terrible reliability.
Reliability is achieved through **redundancy** (duplicating critical hardware and software components so no single failure halts the service) combined with end-to-end integrity checks (checksums, quorum validation, and deterministic recovery).

### Tail latency amplification

In a horizontally scaled microservice architecture, average latency (`p50`) is an almost useless metric; **tail latency (`p99` and `p99.9`) governs user experience and system stability**[^dean-barroso-tail].
Suppose a user request hits an aggregator service that must fan out in parallel to \(k = 50\) backend shards, and each shard independently experiences a 1% chance (`p99`) of taking longer than 500 ms due to a minor GC pause or disk queue spike.
Because the aggregator cannot return until the *slowest* of the 50 parallel calls completes, the probability that the overall request avoids the 500 ms tail spike on *all* 50 shards is:

\[
P(\text{all 50 fast}) = (1 - 0.01)^{50} = 0.99^{50} \approx 0.605
\]

Consequently, the probability that the user sees a slow (>500 ms) response jumps from **1% at the shard level to nearly 40% (`1 - 0.605 = 39.5%`) at the gateway level**.
Principal engineers design specifically to bound tail amplification by minimizing synchronous fan-out depth, co-locating related data in the same partition, and using hedged requests or bounded timeouts.

---

## 5. Applying the models to the Atlas platform

Let us ground these models in **Atlas**, our running example of a multi-tenant telemetry, configuration, and firmware rollout platform serving 500 million edge devices.

Atlas operates across two radically different network and node environments:
1. **The Edge-to-Cloud Ingestion Plane**: Half a billion IoT and client devices connect over cellular networks, factory Wi-Fi, and residential ISPs. This environment is an **arbitrary network link** (subject to middleboxes, packet loss, and spoofing) terminated by **crash-recovery nodes** with untrusted physical clocks. Atlas reduces this arbitrary link to a **reliable link** in two explicit steps: first, every device establishes a mutual TLS (mTLS) session using its hardware-backed X.509 client certificate (reducing arbitrary to fair-loss); second, every telemetry batch and rollout status report carries a deterministic `(DeviceId, SequenceNumber)` idempotency key that the ingestion gateway deduplicates in a short-term cache before appending to the regional log (reducing fair-loss to reliable).
2. **The Regional Control and Storage Plane**: Inside each cloud region, Atlas runs storage tablet servers and rollout coordinators. These servers are **crash-recovery nodes** subject to **temporal failures** (.NET garbage collection pauses, VM live migration pauses, and noisy-neighbor storage latency spikes) connected by a **partially synchronous datacenter network**. Because the network is only partially synchronous, Atlas never relies on simple sleep timers to guarantee exclusive access to a rollout shard; as we will see in Modules 02 through 04, it pairs logical epochs with storage-enforced fencing.

---

## Before you move on

A distributed system's correctness is only as strong as the realism of the failure model it assumes.
You transform an arbitrary, untrusted network link into a fair-loss link using TLS encryption and message authentication, and you transform a fair-loss link into a reliable link using sender retry loops paired with receiver idempotency deduplication.
On the compute side, production servers do not fail cleanly by halting forever (`crash-stop`); they suffer `temporal failures` (pausing for seconds during GC or VM migration) and `crash-recovery` cycles (wiping RAM and restarting from durable disk state).
Because real networks are `partially synchronous` rather than synchronous, deterministic consensus protocols must guarantee safety unconditionally during asynchronous network glitches while relying on bounded-latency windows for liveness.

To test your intuition before moving on, consider a changed case: suppose an engineer on your team claims that because two microservices inside the same cloud virtual network communicate over TCP (which already has sequence numbers, ACKs, and retransmission), the application layer has a "reliable link" and does not need idempotency keys or application retries.
Can you explain why TCP only guarantees ordered delivery *within a single live socket session*, and why a crash-recovery restart or a load-balancer connection reset after the server commits to its database still drops the TCP ACK and forces an application-level retry that will double-apply the mutation without an idempotency key?
If that failure sequence is not yet crystal clear, revisit **Section 1** and **Section 2** before starting Module 02.

---

### Sources

[^kleppmann-lectures]: **Tier 4: Martin Kleppmann, *Distributed Systems Lecture Series* (University of Cambridge, Lectures 1–2: Introduction and Models of Distributed Systems)**: Formal definitions of arbitrary, fair-loss, and reliable links; TLS and retry+dedup reductions; crash-stop, crash-recovery, and Byzantine node models; and synchronous, partially synchronous, and asynchronous timing assumptions. Public lecture notes at [https://www.cl.cam.ac.uk/teaching/2122/ConcDisSys/dist-sys-notes.pdf](https://www.cl.cam.ac.uk/teaching/2122/ConcDisSys/dist-sys-notes.pdf).
[^ddia-ch8]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 8: "The Trouble with Distributed Systems"**: Unreliable networks, asymmetric partitions, process pauses (GC, VM suspension, disk I/O), and system models with respect to timing and faults.
[^tanenbaum-ds]: **Tier 2: Andrew S. Tanenbaum and Maarten van Steen, *Distributed Systems* (4th ed., 2023), Chapter 8: "Fault Tolerance"**: Taxonomy of fail-stop, crash, omission (send/receive), timing/temporal, and arbitrary/Byzantine failures.
[^dwork-partial-sync]: **Tier 1: Cynthia Dwork, Nancy Lynch, and Larry Stockmeyer, "Consensus in the Presence of Partial Synchrony" (*Journal of the ACM*, Vol. 35, No. 2, 1988, pp. 288–323)**: Foundational formalization of the partially synchronous system model and Global Stabilization Time (GST).
[^flp-1985]: **Tier 1: Michael J. Fischer, Nancy A. Lynch, and Michael S. Paterson, "Impossibility of Distributed Consensus with One Faulty Process" (*Journal of the ACM*, Vol. 32, No. 2, 1985, pp. 374–382)**: Proof that no deterministic consensus protocol can guarantee both safety and liveness in an asynchronous network if even a single node may crash.
[^ongaro-raft]: **Tier 1: Diego Ongaro and John Ousterhout, "In Search of an Understandable Consensus Algorithm (Extended Version)" (*USENIX ATC*, 2014)**: Separation of unconditional safety from timing-dependent liveness under partial synchrony.
[^lamport-paxos]: **Tier 1: Leslie Lamport, "The Part-Time Parliament" (*ACM Transactions on Computer Systems*, Vol. 16, No. 2, 1998, pp. 133–169)**: Safety guarantees of Paxos independent of timing assumptions.
[^ddia-ch1]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 1: "Reliable, Scalable, and Maintainable Applications"**: Definitions of reliability, scalability, load parameters, and percentile latency metrics.
[^dean-barroso-tail]: **Tier 1: Jeffrey Dean and Luiz André Barroso, "The Tail at Scale" (*Communications of the ACM*, Vol. 56, No. 2, 2013, pp. 74–80)**: Mathematical analysis of tail-latency amplification across parallel fan-out requests and mitigation strategies (hedged requests, tied requests, micro-partitioning).
