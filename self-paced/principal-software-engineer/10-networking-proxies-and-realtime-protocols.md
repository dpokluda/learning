# 10: Networking, Proxies, and Real-Time Protocols

Every request from a user's browser, mobile app, or IoT device enters your cloud architecture through a chain of network protocols, cryptographic handshakes, proxies, and load balancers.
When a system only needs low-rate request-response CRUD, a developer can treat that network path as an invisible pipe.
At Principal scale (where 500 million devices maintain long-lived connections, TLS handshakes can saturate CPU cores during a reconnect storm, and choosing the wrong load-balancing layer or streaming protocol can multiply your server fleet by $10\times$), you must understand every hop from the TCP socket to the application handler.
This module walks through the mechanics of HTTPS and TLS 1.2/1.3 handshakes, contrasts forward and reverse proxies, compares L4 transport and L7 application load balancers and their routing algorithms, and evaluates the five protocols for real-time client-server communication: HTTP Short Polling, HTTP Long-Polling, Server-Sent Events (SSE), WebSockets, and HTTP/2 gRPC streams.

> **Prerequisite:** [Module 01: Distributed Systems Models and Failure Modes](./01-distributed-systems-models-and-failure-modes.md) (specifically transforming arbitrary links into fair-loss links via TLS) and [Module 08: Data Partitioning and Hashing Schemes](./08-data-partitioning-and-hashing-schemes.md).

---

## 1. HTTPS and TLS handshake mechanics

In Module 01 we established that **Transport Layer Security (TLS)** transforms an arbitrary, untrusted network link into a fair-loss link by providing three guarantees: **Authentication** (proving the server's; and in mutual TLS, the client's; identity via X.509 certificates), **Confidentiality** (encrypting payloads so eavesdroppers see only ciphertext), and **Integrity** (attaching a cryptographic Message Authentication Code so any tampered or replayed byte is immediately detected and dropped)[^rfc8446].

Why does TLS not simply encrypt the entire HTTP stream using the server's public/private RSA or ECDSA key pair?
Because **asymmetric (public-key) cryptography is orders of magnitude more CPU-intensive** than symmetric cryptography, and using a static key directly without ephemeral key agreement lacks **Forward Secrecy** (if an attacker records encrypted traffic today and steals the server's private key two years from now, they could decrypt all historical traffic).
Instead, TLS uses asymmetric cryptography *only during the initial connection handshake* to authenticate the peer and agree on an ephemeral **symmetric session key**, and then switches to hardware-accelerated **Authenticated Encryption with Associated Data (AEAD)** ciphers (such as `AES-128-GCM`, `AES-256-GCM`, or `ChaCha20-Poly1305`) for all subsequent application data frames.

```
Classical TLS 1.2 Handshake (2 Network RTTs after TCP)      Modern TLS 1.3 Handshake (1 Network RTT after TCP)
======================================================      ==================================================
Client                                      Server          Client                                      Server
  |--- TCP SYN ------------------------------->|              |--- TCP SYN ------------------------------->|
  |<-- TCP SYN-ACK ----------------------------|              |<-- TCP SYN-ACK ----------------------------|
  |--- TCP ACK + ClientHello ----------------->|              |--- TCP ACK + ClientHello ----------------->|
  |    (TLS versions, CipherSuites, Random)    |              |    (CipherSuites + Ephemeral ECDHE KeyShare)|
  |                                            |              |                                            |
  |<-- ServerHello + Certificate --------------|              |<-- ServerHello (KeyShare) -----------------|
  |    + ServerKeyExchange + ServerHelloDone   |              |    + {EncryptedExtensions, Certificate,    |
  |                                            |              |       CertificateVerify, Finished}         |
  | [Verify X.509 Chain; Generate Pre-Master]  |              |                                            |
  |--- ClientKeyExchange + ChangeCipherSpec -->|              | [Verify Cert Chain; Derive Session Keys]   |
  |    + Finished ---------------------------->|              |--- {Finished} + Encrypted HTTP Request --->|
  |<-- ChangeCipherSpec + Finished ------------|              |<-- Encrypted HTTP Response ----------------|
  |--- Encrypted HTTP Request ---------------->|
```

Compare the two handshake flows above:
1. **TCP Connection Establishment (1 RTT)**: The client and server complete the standard TCP three-way handshake (`SYN`, `SYN-ACK`, `ACK`).
2. **ClientHello and SNI**: The client sends a `ClientHello` listing supported TLS versions, cipher suites, a cryptographic random nonce, and the **Server Name Indication (SNI)** extension (the plaintext hostname, such as `api.atlas.example.com`, so a load balancer hosting hundreds of domains on one IP knows which X.509 certificate to return).
3. **Certificate Chain Validation**: The server returns its leaf X.509 certificate (containing its public key, subject alternative names, and validity dates) plus any intermediate Certificate Authority (CA) certificates. The client validates that each certificate in the chain is cryptographically signed by its parent up to a trusted **Root CA** in the client's trust store, that the current UTC time falls within `NotBefore..NotAfter`, and that the hostname matches.
4. **Key Exchange and TLS 1.3 Optimization**: In older TLS 1.2, agreeing on cipher parameters and exchanging ephemeral keys required **two full network round trips (2 RTTs)** before the first byte of HTTP could be sent. **TLS 1.3 (RFC 8446)** eliminates an entire network round trip (**1-RTT handshake**) by having the client proactively guess the key-agreement curve (such as `X25519` or `P-256`) and send its **Ephemeral Elliptic-Curve Diffie-Hellman (ECDHE) Key Share** inside the very first `ClientHello` message[^rfc8446]. Furthermore, TLS 1.3 mandates Ephemeral Diffie-Hellman (removing static RSA key transport entirely), which guarantees **Forward Secrecy** on every connection.

**Principal Operational Insight (Mutual TLS and Session Resumption):**
When authenticating microservices inside a zero-trust service mesh or authenticating 500 million IoT devices in Atlas, the server also sends a `CertificateRequest` during the handshake, and the client proves possession of its client private key (**Mutual TLS / mTLS**).
Because verifying elliptic-curve signatures across millions of reconnecting devices after a regional blip can spike gateway CPU to 100%, production front doors enable **TLS Session Resumption via Pre-Shared Keys (PSK / Session Tickets)**, allowing returning clients to resume encrypted sessions without repeating full asymmetric certificate verification.

---

## 2. Forward proxies vs reverse proxies

A **proxy** is an intermediary network component that sits between a client and a destination server.
Although forward and reverse proxies both terminate and forward connections, they sit on opposite sides of the trust boundary and serve opposite architectural purposes:

```
  [ Client A ] \                              /---> [ App Server 1 ]
  [ Client B ] ---> [ Forward Proxy ] ===> (Internet) ===> [ Reverse Proxy ] ---> [ App Server 2 ]
  [ Client C ] /    (Represents Clients;                   (Represents Servers;   [ App Server 3 ]
                     hides client identity)                 hides server topology)
```

- **Forward Proxy (Client-Side Intermediary)**: Deployed inside a corporate network, a VPC egress subnet, or a client environment to act **on behalf of clients** talking to external servers. A forward proxy hides the individual client's internal IP address from the destination server, enforces outbound egress allowlists (preventing compromised containers from exfiltrating data to arbitrary internet hosts), caches shared external downloads (such as container images or OS packages), and logs outbound traffic for compliance.
- **Reverse Proxy (Server-Side Front Door)**: Deployed at the edge of your service infrastructure (using Nginx, Envoy, YARP, HAProxy, or cloud API gateways) to act **on behalf of your backend servers**[^fowler-poeaa]. External clients connect only to the reverse proxy's public IP/VIP and have no visibility into how many backend servers exist or how they are partitioned. A reverse proxy performs **TLS termination** (offloading cryptographic handshakes from application servers), HTTP/2 and HTTP/3 connection multiplexing, response compression (`Brotli` / `gzip`), Web Application Firewall (WAF) inspection, rate limiting, and request routing.

---

## 3. Load balancing: L4 vs L7, algorithms, and high availability

A **Load Balancer (LB)** distributes incoming network traffic across a pool of backend servers and continuously runs **Health Checks** (active TCP/HTTP probes and passive error-rate circuit breaking) so unhealthy servers are immediately drained from rotation.
In a layered cloud architecture, load balancers are typically deployed at three distinct tiers: between external users and edge gateways, between gateways and internal microservice fleets, and between stateless application workers and storage/cache proxy pools.

### Layer 4 (Transport) vs Layer 7 (Application) Load Balancing

The most consequential architectural choice in load balancing is whether the balancer operates at **Layer 4 (Transport / TCP / UDP)** or **Layer 7 (Application / HTTP / gRPC)**:

| Dimension | Layer 4 (Transport) Load Balancer | Layer 7 (Application) Load Balancer |
| --- | --- | --- |
| **What it inspects** | Only the TCP/UDP 5-tuple (`SourceIP`, `SourcePort`, `DestIP`, `DestPort`, `Protocol`). Never decrypts TLS or parses HTTP headers. | Terminates TLS and parses the full HTTP/1.1, HTTP/2, or gRPC request (URL path, `Host` header, cookies, JWT claims, headers). |
| **Connection model** | **1-to-1 TCP pass-through**: client and backend share a single logical TCP connection (via stateful NAT or Direct Server Return). | **2 separate TCP connections**: terminates client TCP/TLS at the proxy and multiplexes requests over a warm backend connection pool. |
| **Throughput & CPU cost** | **Extremely fast and cheap** (millions of packets/sec in kernel/eBPF/ASICs); zero TLS decryption overhead. | Higher CPU and memory cost (TLS termination, HTTP parsing, buffer management). |
| **Routing intelligence** | Cannot route by URL path or tenant header; sticky only at the TCP connection level (dangerous with HTTP/2 multiplexing!). | Routes by URL path (`/api/v1/telemetry` vs `/api/v1/rollouts`), tenant ID header, canary cookies, and per-RPC gRPC calls. |

Two advanced L4 scaling techniques are standard at hyperscale:
1. **Anycast BGP + Equal-Cost Multi-Path (ECMP)**: Multiple L4 load balancers advertise the *exact same virtual IP address (VIP)* to upstream routers via BGP. The network router hardware hashes the TCP 5-tuple and splits packets across the L4 instances at line rate with zero single-machine bottleneck.
2. **Direct Server Return (DSR)**: For asymmetric workloads where request packets are tiny (200 bytes) and response payloads are huge (a 200 MB firmware file or video chunk), the L4 load balancer rewrites only the destination MAC address (or encapsulates in IP-in-IP) so the backend server sends the massive response **directly back to the client over the internet gateway**, bypassing the load balancer completely on the return path!

### Load Balancing Routing Algorithms

Once the load balancer has filtered its pool down to healthy servers, how does it pick the target server for a new connection or request?
- **Round Robin / Weighted Round Robin**: Cycles sequentially through healthy backends (weighted by machine CPU capacity). Simple and effective when all requests have roughly uniform execution cost and stateless duration.
- **Least Connections / Weighted Least Connections**: Routes each new request to the server currently holding the fewest active in-flight requests/connections. Essential for **long-lived connections** (WebSockets, Long-Polling) or requests with high variance in processing time.
- **Least Response Time**: Combines active connection count with an exponentially weighted moving average (EWMA) of recent response latency, automatically steering traffic away from a node suffering a degraded disk or noisy neighbor before health checks even fail.
- **Power of Two Random Choices (`P2C`)**: In a distributed fleet of 100 L7 proxies (like Envoy), how does a proxy do "Least Connections" without every proxy constantly broadcasting its queue depth to all 99 peers? `P2C` picks **two backends uniformly at random** and sends the request to whichever of those two has the shorter local active queue! Mitzenmacher proved that picking the better of just *two* random choices reduces the maximum queue length across $N$ servers from $O(\frac{\ln N}{\ln \ln N})$ down to $O(\ln \ln N)$; nearly matching global omniscient load balancing with zero coordination overhead[^mitzenmacher-p2c-2001].
- **IP Hash / Consistent Hash**: Hashes the client IP, session ID, or `TenantId` header onto a Consistent Hash Ring or Rendezvous (HRW) set (Module 08) so requests for the same tenant or stateful session consistently land on the same backend server (maximizing local L1 cache hit rates).

---

## 4. Real-time client-server communication protocols

Suppose your service needs to push live updates (chat messages, stock prices, collaborative edits, or urgent firmware rollback commands) from the server to the client as soon as they happen.
Standard HTTP/1.1 is strictly client-initiated: a server cannot spontaneously open a TCP connection to a client sitting behind a home NAT router or corporate firewall.
We have five architectural patterns to bridge that gap:

```
1. Short Polling:       Client ---Req---> Server (Empty) ... [Wait 5s] ... Client ---Req---> Server (Data!)
2. Long-Polling:        Client ---Hanging GET---> Server ......... [Holds until Event] ......... ---> 200 OK!
3. Server-Sent Events:  Client ---GET (Accept: text/event-stream)---> Server ===Ev1===> ===Ev2===> (1-way stream)
4. WebSockets:          Client ---HTTP 101 Upgrade---> Server <==== Full-Duplex Binary/Text Frames ====>
5. gRPC / HTTP/2:       Client <==== Multiplexed Framed Bidirectional Streams over single TLS conn ====>
```

Let us compare the engineering trade-offs of each protocol:

| Protocol | Directionality | Transport mechanism | Where it excels | Operational caveats at scale |
| --- | --- | --- | --- | --- |
| **HTTP Short Polling** | Client-pulled request/response | Repeated periodic `GET` every $T$ seconds (returns immediate response, often empty `204/200`). | Clients that only wake up briefly or tolerate 1–5 minute staleness; completely stateless on the server. | High wasted HTTP header/TLS overhead and empty DB/cache checks when most polls return no new data; average notification latency is $T / 2$. |
| **HTTP Long-Polling ("Hanging GET")** | Simulated server push over request/response | Client sends `GET`; server **holds the request open** without replying until new data arrives or a 30–60s timeout expires, then client immediately re-polls. | Near-instant push latency through restrictive legacy proxies that block WebSockets. | Ties up an async request context per idle client; high reconnect churn when broadcasts wake up millions of hanging GETs simultaneously. |
| **Server-Sent Events (SSE)** | **Unidirectional** server-to-client push | Standard HTTP connection (`Content-Type: text/event-stream`) kept open indefinitely; browser `EventSource` auto-reconnects with `Last-Event-ID`. | Live dashboards, AI token streaming, news feeds, and status notifications where client writes use normal REST calls. | Text-only (UTF-8); unidirectional; on HTTP/1.1, browsers cap open SSE connections to 6 per domain (solved automatically over HTTP/2). |
| **WebSockets (`RFC 6455`)** | **Full-duplex** bidirectional frames | Starts as HTTP/1.1 `GET` with `Upgrade: websocket` -> `101 Switching Protocols`, converting the TCP socket into a persistent framed bidirectional pipe. | Low-latency interactive chat, multiplayer games, collaborative editing, and bidirectional IoT command/telemetry channels. | Stateful persistent connections complicate L4/L7 load balancer draining, require application-level `Ping/Pong` heartbeats to prevent NAT idle drops, and need pub/sub backplane routing across gateway nodes. |
| **gRPC Bidirectional Streaming (HTTP/2)** | **Full-duplex** multiplexed typed streams | Binary Protocol Buffer frames multiplexed over a single persistent HTTP/2 TLS connection with per-stream flow control. | High-performance microservice-to-microservice streaming and native/IoT agent communication. | Requires HTTP/2-aware L7 load balancers (naive L4 TCP load balancing pins all multiplexed streams from one client onto a single backend pod!). |

---

## 5. Networking, load balancing, and real-time protocols in Atlas

Look at how **Atlas** combines these networking patterns across its 500-million-device fleet:
1. **Tiered L4 + L7 Load Balancing with DSR for Firmware Payloads**: Incoming device connections hit an **Anycast BGP Layer 4 load balancer** first, which spreads TCP/TLS connections across a fleet of **Layer 7 Envoy Reverse Proxies**. The L7 proxies terminate mTLS (using TLS 1.3 and session tickets) and inspect the device's tenant and region claims. For small JSON control messages, L7 routes to the regional microservice fleet using **Power of Two Random Choices (`P2C`)**. For downloading 200 MB firmware binaries, Atlas offloads the download entirely to a geo-distributed **CDN + Blob Storage** using the **Valet Key Pattern** (Module 14) so multi-gigabit binary streams never traverse the control-plane proxies.
2. **Protocol Selection by Device Class**: Battery-constrained cellular sensors that wake up once every 15 minutes use **HTTP Short Polling** over TLS 1.3 (`0-RTT`/`1-RTT` session resumption) to check for pending configuration changes and immediately go back to deep sleep. Always-powered industrial edge gateways establish a persistent **gRPC Bidirectional Stream (or WebSocket)** to the Stateful Edge Gateway tier, allowing Atlas to push emergency rollback commands in under 200 milliseconds while receiving continuous health telemetry over the same socket.

---

## Before you move on

A request's network journey begins with **TLS**, which uses asymmetric certificates (`X.509`) and Ephemeral Diffie-Hellman (`ECDHE`) during a 1-RTT (TLS 1.3) or 2-RTT (TLS 1.2) handshake to establish forward-secret symmetric session keys.
**Forward proxies** represent clients talking outward, while **reverse proxies** represent backend servers to the outside world.
**Layer 4 load balancers** route TCP/UDP packets at line rate based on the 5-tuple (supporting Anycast ECMP and Direct Server Return), whereas **Layer 7 load balancers** terminate TLS and route individual HTTP/gRPC requests using algorithms like **Least Connections**, **Power of Two Random Choices (`P2C`)**, or **Consistent/Rendezvous Hashing**.
For real-time updates, **Short Polling** trades server simplicity for polling overhead, **Long-Polling** holds requests open until an event arrives, **Server-Sent Events (SSE)** streams unidirectional events over standard HTTP with automatic `Last-Event-ID` resume, and **WebSockets** and **HTTP/2 gRPC streams** provide full-duplex persistent channels.

To test your understanding before moving to Module 11, consider this changed case: your team migrates an internal microservice API from HTTP/1.1 REST to **gRPC over HTTP/2**, keeping your existing Kubernetes Service configured with default **Layer 4 TCP kube-proxy load balancing**.
During a load test where 4 caller pods talk to 20 backend pods, you observe that 4 of the 20 backend pods are running at 100% CPU while the other 16 backend pods sit at 0% CPU!
Can you explain why **HTTP/2 connection multiplexing** (sending all concurrent RPCs from one caller pod over a single long-lived TCP connection) breaks **Layer 4 TCP-level load balancing**, and why fixing it requires either an **L7 HTTP/2-aware proxy** or client-side gRPC subchannel load balancing?
If that interaction is not immediately clear, revisit **Section 3** and **Section 4**.

---

### Sources

[^rfc8446]: **Tier 1: E. Rescorla, *RFC 8446: The Transport Layer Security (TLS) Protocol Version 1.3* (IETF, August 2018)**: Normative specification of the 1-RTT TLS 1.3 handshake, mandatory ephemeral Diffie-Hellman forward secrecy, PSK session resumption, and AEAD record encryption ([https://datatracker.ietf.org/doc/html/rfc8446](https://datatracker.ietf.org/doc/html/rfc8446)).
[^fowler-poeaa]: **Tier 2: Martin Fowler, *Patterns of Enterprise Application Architecture* (Addison-Wesley, 2002)**: Reverse proxy, front controller, and gateway routing fundamentals.
[^mitzenmacher-p2c-2001]: **Tier 1: Michael Mitzenmacher, "The Power of Two Choices in Randomized Load Balancing" (*IEEE Transactions on Parallel and Distributed Systems*, Vol. 12, No. 10, 2001, pp. 1094–1104)**: Proof that sampling two random servers and choosing the less loaded one exponentially reduces maximum queue length from $O(\frac{\ln N}{\ln \ln N})$ to $O(\ln \ln N)$.
