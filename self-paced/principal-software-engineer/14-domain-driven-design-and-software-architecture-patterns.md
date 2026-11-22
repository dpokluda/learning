# 14: Domain-Driven Design and Software Architecture Patterns

Distributed systems fail from the outside in when network links partition or storage engines saturate, but they fail from the *inside out* when their domain boundaries, module dependencies, and class abstractions rot into an unmaintainable ball of mud.
A Principal engineer must be fluent at every altitude of software design: from object-oriented **SOLID principles** and **Gang of Four (GoF) design patterns** inside a single codebase, to Martin Fowler's **Domain Logic patterns** (`Transaction Script`, `Domain Model`, `Service Layer`), up to Eric Evans's **Domain-Driven Design (DDD)** (`Ubiquitous Language`, `Bounded Contexts`, `Aggregates`), and finally across cloud structural, migration, and data-residency patterns (`Gateway Routing/Aggregation/Offloading`, `Backends for Frontends`, `Sidecar`, `Ambassador`, `Anti-Corruption Layer`, `Strangler Fig`, `Valet Key`, `Geode`, and `Privacy Mapping Tables`).
This module connects all four altitudes into a unified architectural discipline.

> **Prerequisite:** [Module 07: Indexes and Data Modeling: SQL vs NoSQL](./07-indexes-and-data-modeling-sql-vs-nosql.md) and [Module 13: Microservices, Messaging, and Event-Driven Architecture](./13-microservices-messaging-and-event-driven-architecture.md).

---

## 1. OOP principles, SOLID in the large, and UML communication

Object-oriented design uses **encapsulation** (bundling state with the behavior that mutates it behind an access boundary), **abstraction**, **polymorphism**, and **composition** to keep software changeable over time[^hf-dp][^clean-code].
Before reaching for any named pattern, anchor the core design principles from *Head First Design Patterns* and Robert C. Martin's **SOLID** principles as architectural rules rather than textbook slogans[^hf-dp][^clean-code]:

- **Encapsulate what varies**: Identify the aspect of a workflow that changes frequently (such as a pricing formula, a storage provider, or an applicability rule) and isolate it behind a stable contract so the surrounding orchestration code never has to be edited when a new variant arrives.
- **Favor composition over inheritance (`Has-A` beats `Is-A`)**: Deep inheritance hierarchies are brittle because changing a base class silently alters every subclass and forces awkward overrides when two axes of behavior vary independently. Composing small focused collaborators (`RolloutValidator`, `RetryPolicy`, `TelemetrySink`) lets you mix and match behavior at runtime or compile time without class explosion.
- **The Law of Demeter (Principle of Least Knowledge: "Don't talk to strangers")**: A method on object `A` should invoke methods only on `A` itself, parameters passed into the method, objects `A` instantiates, or `A`'s direct component fields; never chaining through internal sub-objects (`order.Customer.Account.BillingAddress.Validator.Validate()`), which couples `A` to the internal structure of three intermediate classes.
- **The Hollywood Principle ("Don't call us, we'll call you")**: High-level frameworks and domain orchestrators control the overall execution flow and invoke low-level plug-in hooks when needed, preventing low-level components from calling back up into high-level state arbitrarily.

Applied at the module and service boundary, the five **SOLID** principles prevent coupling rot:
1. **Single Responsibility Principle (SRP)**: A module or class should have *only one reason to change* (more precisely, it should be accountable to only one business actor or operational concern). Separating domain validation logic from SQL persistence and HTTP formatting ensures a database migration never breaks domain unit tests.
2. **Open/Closed Principle (OCP)**: A component should be *open for extension, but closed for modification*: adding a new rollout rule type or notification channel should mean adding a new class implementing an interface, not editing a 600-line `switch` statement across five files.
3. **Liskov Substitution Principle (LSP)**: Any subtype or interface implementation must honor the full behavioral contract (preconditions, postconditions, and invariants) expected by callers of the base abstraction. A `ReadOnlyRepository` inheriting from `IRepository` and throwing `NotSupportedException` on `.Save()` violates LSP and plants a runtime landmine.
4. **Interface Segregation Principle (ISP)**: Callers should not be forced to depend on methods they do not use. Split bloated "god interfaces" (`IDeviceService` with 40 methods) into role-specific contracts (`IDeviceProvisioner`, `IDeviceHealthReader`, `IDeviceCommandSender`).
5. **Dependency Inversion Principle (DIP)**: High-level business policy modules must **not** depend on low-level infrastructure modules (SQL, Redis, Kafka SDKs); both must depend on abstractions owned by the high-level domain layer.

*(A note on **UML diagrams**: You do not need formal CASE tools, but knowing when to sketch each of the four core UML diagram types keeps design docs sharp: **Use Case Diagrams** capture external actors and functional goals; **Class Diagrams** capture static entity relationships and composition/inheritance; **Sequence Diagrams** capture time-ordered message exchanges and race conditions across components; and **Activity Diagrams** capture branching control-flow state machines.)*

---

## 2. The Gang of Four (GoF) patterns that carry weight in modern services

You do not need to memorize all 23 Gang of Four patterns as ceremony; in modern C#, many micro-patterns (`Iterator`, `Observer`, simple `Strategy` lambdas) are built directly into language features (`IEnumerable<T>`, delegates, high-order functions)[^gof-1994][^hf-dp].
However, fifteen GoF patterns show up constantly in production systems architecture, and recognizing their exact trade-offs is essential:

| Category | Pattern | Core problem it solves & modern service example |
| --- | --- | --- |
| **Creational** | **Factory Method / Simple Factory** | Encapsulates complex conditional instantiation logic (`PaymentProcessorFactory.Create(region)`) so callers depend only on the returned interface. |
| **Creational** | **Abstract Factory** | Creates *coherent families* of related objects (e.g., an `ICloudStorageProviderFactory` that creates matching `BlobStore`, `QueueClient`, and `KeyVault` for Azure vs AWS vs In-Memory Test). |
| **Creational** | **Builder** | Constructs complex immutable objects or test fixtures step by step (`new RolloutPolicyBuilder().WithCanary(0.05).WithMaxFailureRate(0.01).Build()`) instead of telescoping 12-parameter constructors. |
| **Creational** | **Prototype** | Clones a pre-computed, expensive-to-parse template object (such as a compiled AST or default configuration graph) rather than rebuilding it from scratch. |
| **Creational** | **Singleton** | Ensures a process-wide shared resource (such as `SocketsHttpHandler` connection pool or immutable compiled regex cache) has one instance. *Warning:* Never store mutable business state in a Singleton, as it breaks test isolation. |
| **Structural** | **Adapter (Wrapper)** | Translates an incompatible third-party SDK or legacy interface into the clean domain interface your application expects. |
| **Structural** | **Bridge** | Decouples an abstraction hierarchy (e.g., `RolloutController`) from its implementation hierarchy (`StorageDriver`) so both can evolve independently via composition. |
| **Structural** | **Composite** | Models recursive tree structures (such as boolean applicability expressions `AndNode(OrNode(Leaf, Leaf), NotNode(Leaf))` or organizational hierarchies) so callers treat leaves and composite subtrees uniformly via a single `.Evaluate(context)` interface. |
| **Structural** | **Decorator** | Wraps an interface implementation in another class implementing the *exact same interface* to layer cross-cutting behavior (`CachingDeviceRepo(LoggingDeviceRepo(RetryDeviceRepo(SqlDeviceRepo)))`) without modifying the inner class. |
| **Structural** | **Facade** | Provides a simplified, coarse-grained entry point over a complex subsystem of dozens of internal classes. |
| **Structural** | **Flyweight** | Shares immutable intrinsic state (such as shared string interns, parsed rule ASTs, or timezone definitions) across millions of lightweight context instances to slash RAM usage. |
| **Structural** | **Proxy** | Interposes a surrogate placeholder implementing the same interface to control access, enforce security/rate limits, or defer expensive initialization. |
| **Behavioral** | **Chain of Responsibility** | Passes a request along a pipeline of handlers (`AuthMiddleware -> RateLimitMiddleware -> ValidationMiddleware -> Handler`) where each stage can short-circuit or forward to `next()`. The backbone of ASP.NET Core and Envoy middleware pipelines! |
| **Behavioral** | **Command** | Reifies an operation and its parameters as a standalone serializable object (`ApplyFirmwareCommand`) that can be validated, queued, logged, retried, or undone. |
| **Behavioral** | **Mediator** | Centralizes complex many-to-many interactions between components through a single coordinator (such as an in-process request/notification dispatcher) so components do not reference each other directly. |
| **Behavioral** | **State** | Encapsulates state-specific transition rules (`DraftState -> CanaryState -> RollingBackState`) into distinct state objects so an entity's behavior changes cleanly as its lifecycle state transitions without massive `if/else` spaghetti. |
| **Behavioral** | **Strategy** | Defines a family of interchangeable algorithms (`ConsistentHashStrategy`, `RendezvousHashStrategy`, `RangeShardStrategy`) behind a common interface injected into the caller. |
| **Behavioral** | **Template Method / Visitor / Memento / Null Object** | **Template Method** defines an invariant algorithm skeleton with abstract step hooks; **Visitor** adds new operations (like validation, pretty-printing, or SQL compilation) over a fixed AST without modifying the node classes; **Memento** captures an opaque state snapshot for rollback; **Null Object** provides a no-op implementation (`NullLogger.Instance`) to eliminate `if (logger != null)` checks everywhere. |

---

## 3. Fowler's Domain Logic patterns and Eric Evans's Domain-Driven Design (DDD)

When structuring the business logic inside a service, Martin Fowler points out that **not every service needs the same internal architecture**[^fowler-poeaa]:
- **Transaction Script**: Organizes each API operation as a straightforward procedural script (`Validateinput -> Query DB -> Compute -> Save DB`). For simple CRUD or pass-through microservices with shallow business rules, a clean Transaction Script is simpler and easier to maintain than an over-engineered object graph (honoring `OPINIONS.md`: *build the smallest solution that meets the present need*).
- **Domain Model + Service Layer**: When business rules involve complex state transitions, subtle invariants across multiple entities, and rich combinatorics, procedural scripts duplicate rules and drift into bugs. Here you build a rich, persistence-ignorant **Domain Model** fronted by a thin **Service Layer** (which handles authentication, opens the Unit of Work transaction, loads the domain object, calls a domain method, and commits)[^fowler-poeaa].

### Strategic Domain-Driven Design: Ubiquitous Language and Bounded Contexts

In *Domain-Driven Design: Tackling Complexity in the Heart of Software*, Eric Evans showed that the hardest failure in large software projects is linguistic and organizational: calling the same concept by three different names across code and product specs, or worse, using the **same word (`"Device"` or `"Account"`) to mean completely different things across five teams in one giant shared model**[^evans-ddd].

Strategic DDD resolves this through three constructs[^evans-ddd]:
1. **Ubiquitous Language**: Engineers and domain experts agree on a single, precise vocabulary (`Rollout`, `Cohort`, `ApplicabilityExpression`, `Tombstone`, `Quarantine`) and use those *exact* nouns and verbs in design docs, conversations, class names, method signatures, and events; eliminating mental translation between "product speak" and "code speak."
2. **Subdomain Classification**: Not every part of a system deserves bespoke architecture. Divide the domain into **Core Subdomains** (the proprietary heart where your system delivers unique competitive value, such as Atlas's safety-guarded rollout engine; invest your best engineers and rich DDD here), **Supporting Subdomains** (custom but simple CRUD helpers; use simple Transaction Scripts), and **Generic Subdomains** (authentication, billing, email delivery; buy or adopt standard platforms rather than reinventing them).
3. **Bounded Contexts and Context Mapping**: Instead of forcing one massive enterprise-wide `Device` class to hold 150 fields for Provisioning, Telemetry, Firmware Rollouts, and Finance Billing, draw explicit **Bounded Contexts**. Inside the **Provisioning Context**, a `Device` is an X.509 certificate thumbprint, hardware SKU, and manufacturing state; inside the **Billing Context**, a `Device` is simply a billable meter ID and tier associated with a `TenantId`. Each Bounded Context owns its own model, its own database, and its own microservice boundary!

### Tactical Domain-Driven Design: Entities, Value Objects, Aggregates, and Repositories

Inside a rich Core Bounded Context, Tactical DDD enforces invariants using four building blocks[^evans-ddd]:
- **Entity**: An object defined by its **continuous identity (`Id`)** across time and state transitions (such as a `Rollout` or `Device`). Two Entities with the same `Id` represent the same logical thing even if their mutable status field changed.
- **Value Object**: An **immutable** object defined purely by the **structural equality of its attributes**, with no lifecycle identity of its own (such as `SemVerVersion(4, 2, 1)`, `Money(100, "USD")`, `GeoRegion("eu-central-1")`, or `HashDigest`). Value Objects validate their invariants in their constructor and can never be mutated into an invalid state; C# `readonly record struct` and `sealed record` are purpose-built for Value Objects!
- **Aggregate and Aggregate Root**: The single most important tactical rule in DDD! An **Aggregate** is a cluster of Entities and Value Objects (for example, a `Rollout` root entity together with its `RolloutPhase` list and `SafetyThreshold` value objects) that acts as a **single atomic transactional consistency boundary**[^evans-ddd]:
  1. External code may hold references only to the **Aggregate Root** (`Rollout`), never mutating internal child entities directly behind the root's back.
  2. Every invariant inside the Aggregate is enforced synchronously in memory whenever a command method on the Aggregate Root (`rollout.AdvanceToNextPhase(metrics)`) executes.
  3. **One Aggregate = One Database Transaction / One Partition Key**: Never modify multiple Aggregates inside a single cross-partition transaction; instead, keep Aggregates small, reference other Aggregates *by ID only* (`Guid TargetGroupId`, not a direct object graph reference!), and coordinate changes across Aggregates asynchronously via **Domain Events** and **Transactional Outbox / Sagas** (Module 13).
- **Repository**: An interface defined in the domain layer (`IRolloutRepository`) that loads and saves **whole Aggregates** by ID, hiding the underlying Data Mapper and database queries.

---

## 4. Cloud structural, migration, and multi-tenant data-residency patterns

Zooming out to the cloud deployment topology, eleven architectural patterns from the Azure Architecture Center and enterprise production systems govern how services are composed, migrated, and geo-isolated[^azure-design]:

```
1. Edge & API Composition Patterns:
   [ Mobile App ] ---> [ Mobile BFF ] \
                                       +---> [ API Gateway (Routing / Offloading / Aggregation) ]
   [ Web Portal ] ---> [ Web BFF    ] /

2. Pod-Level Decomposition Patterns (Kubernetes / Container Host):
   +---------------------------------------------------------------+
   | Pod Boundary (localhost loopback)                             |
   |  [ Main App Container ] <---> [ Ambassador (Outbound Proxy) ] |
   |           |             <---> [ Sidecar (Telemetry / Certs) ] |
   +---------------------------------------------------------------+

3. Legacy Migration Patterns:
   Client ---> [ Strangler Fig Router ] ---> (New Routes) ---> [ Modern Microservice ]
                         |
                         +---> (Unmigrated Routes) ---> [ Anti-Corruption Layer ] ---> [ Legacy Monolith ]
```

### Edge, Pod, and Access Offloading Patterns

- **Gateway Routing, Gateway Offloading, and Gateway Aggregation**: An edge gateway routes requests from a single public endpoint to multiple internal services (**Gateway Routing**), centralizes cross-cutting mechanics like TLS termination, mTLS validation, token verification, and rate limiting (**Gateway Offloading**), and fans out a coarse client request to 3 internal services and combines the result into one payload to save mobile round trips (**Gateway Aggregation**)[^azure-design].
- **Backends for Frontends (BFF)**: Instead of building one bloated general-purpose API gateway that tries to serve a constrained IoT device, a mobile app, and a rich admin web portal with the same payload shape, deploy separate **BFF** services tailored to each client interface experience[^azure-design].
- **Sidecar and Ambassador**: A **Sidecar** is a helper process/container co-deployed in the same pod/host as the primary application to handle operational tasks (log shipping, X.509 certificate rotation, dynamic configuration reload from an **External Configuration Store**) without polluting application code or requiring language-specific libraries. An **Ambassador** is a specialized sidecar that acts as a local **outbound proxy** on `localhost`, transparently adding retries, circuit breaking, mTLS, and shard routing to all outbound network calls made by the application container[^azure-design].
- **Valet Key Pattern**: When a client needs to upload or download a 500 MB file, never stream those bytes through your compute microservice! Instead, the client calls your control-plane API, which authenticates the caller, checks authorization, and mints a cryptographically signed, time-limited, permission-scoped token (**Valet Key**, such as an Azure Storage SAS URI or AWS S3 Presigned URL, e.g., `Write-Only to blob X for 10 minutes`), returning that URL so the client talks directly to the cloud storage plane[^azure-design].

### Monolith Decomposition: Strangler Fig and Anti-Corruption Layer (ACL)

How do you migrate a 15-year-old critical monolith serving 1 billion devices into modern microservices without a risky multi-year "big bang" rewrite?
By combining two patterns[^fowler-strangler][^evans-ddd][^azure-design]:
1. **Strangler Fig Pattern**: Place a routing proxy in front of the legacy monolith. Extract **one Bounded Context at a time** into a new microservice. Shadow/mirror traffic to verify parity, then flip the router rule for that specific vertical slice (`1% -> 10% -> 100%`) to the new service while all remaining routes continue hitting the monolith. Repeat slice by slice until the legacy monolith has no routes left and can be retired safely.
2. **Anti-Corruption Layer (ACL)**: While the new microservice still needs to read or synchronize data with the remaining legacy monolith, never let the monolith's cryptic 1990s table names and muddled concepts leak into your clean new Domain Model! Build an explicit **Anti-Corruption Layer** (combining `Facade`, `Adapter`, and `Translator`) at the boundary that translates legacy schemas into your new Bounded Context's Ubiquitous Language.

### Global Active-Active Compute and Multi-Tenant Data Residency (`Geode` + `Privacy Mapping Tables`)

Finally, when operating a global cloud service across strict legal jurisdictions (such as EU GDPR data residency), two patterns work in tandem:
- **Geode Pattern**: Deploys stateless compute and replicated lookup services into a ring of geographical nodes (**geodes**) around the world so any region can serve requests with low local latency and survive the loss of an entire region[^azure-design].
- **Privacy Mapping Tables (Pseudonymization at the Regional Boundary)**: Suppose an enterprise platform like Atlas offers a global control plane or central telemetry pipeline, but European law mandates that **EU Customer Identifiable Data (`CustomerId`, `UserEmail`, `DeviceFileName`, `IPAddress`) must never leave EU datacenters**, even inside diagnostic logs or cross-region operational metrics!
  Instead of rewriting every logging statement across 50 services, you enforce **Privacy Mapping at the EU regional ingestion boundary**:
  1. Inside the sovereign EU datacenter, Customer Identifiable Data (`CID`) is replaced with an opaque, randomly generated **Pseudonymous Token (`OpaqueGuid`)**, and the mapping `OpaqueGuid <-> PlaintextCID` is stored exclusively inside an encrypted **Privacy Mapping Table** that physically never leaves the EU region.
  2. Internal operational logs and telemetry pipelines process only `OpaqueGuid` (which contains zero PII/CID by itself).
  3. When an authorized EU customer or EU support engineer views a dashboard inside the EU portal, the EU BFF joins the metrics back against the local EU Privacy Mapping Table to display human-readable names; achieving 100% GDPR data-residency compliance by construction!

---

## 5. Architecture and DDD in the Atlas platform

Every pattern in this module maps directly onto **Atlas**'s evolution:
- **Strategic DDD Decomposition**: Atlas split its original monolithic service into four clean Bounded Contexts: `Identity & Provisioning`, `Device Twin & Telemetry`, `Firmware Catalog & Delta Generation`, and `Rollout Orchestration`.
- **Composite + Visitor for Local Applicability Expressions**: In the `Rollout Orchestration` context, determining whether a firmware package applies to a device requires evaluating complex boolean rules (`OSBuild >= 22621 AND (Sku == 'Industrial' OR Region == 'EU') AND NOT BatteryPercent < 30`). Atlas models these rules as an immutable **Composite AST** of Value Objects and evaluates or compiles them using a **Visitor**, executing applicability evaluation locally in microseconds without calling an external groups service!
- **Valet Key + Privacy Mapping Tables**: Devices download firmware directly from regional CDN/Blob storage using 15-minute **Valet Key** SAS tokens, and all European tenant identifiers and file metadata are pseudonymized through regional **Privacy Mapping Tables** kept strictly inside `eu-central` and `eu-west`.

---

## Before you move on

Software architecture spans four connected scales.
At the class and module level, **SOLID principles** (`SRP`, `OCP`, `LSP`, `ISP`, `DIP`), **composition over inheritance**, and **GoF patterns** (`Factory`, `Builder`, `Adapter`, `Composite`, `Decorator`, `Facade`, `Proxy`, `Chain of Responsibility`, `Command`, `State`, `Strategy`, `Visitor`) keep code extensible and testable without layer bloat.
At the service level, Fowler's **Transaction Script** fits simple CRUD services while **Domain Model + Service Layer** fits complex business rules.
At the system level, Eric Evans's **Domain-Driven Design** isolates models into **Bounded Contexts** with a strict **Ubiquitous Language** and enforces transactional consistency inside small **Aggregates** composed of **Entities** and immutable **Value Objects**.
At the cloud level, **Gateway Routing/Offloading/Aggregation**, **BFFs**, **Sidecars**, **Ambassadors**, **Valet Keys**, **Strangler Fig + Anti-Corruption Layers**, **Geodes**, and **Privacy Mapping Tables** structure resilient, compliant global deployments.

To test your understanding before moving to Part IV (Seminal Distributed Systems Case Studies), consider this changed case: a team designing a new `DeviceGroup` Aggregate in DDD decides to put `List<DeviceMember> Members` as a direct collection inside the `DeviceGroup` Aggregate Root so that calling `group.AddDevice(device)` can check `if (Members.Count < MaxGroupSize)` inside the Aggregate's in-memory transaction.
In production, a large enterprise tenant creates a `DeviceGroup` with **2,000,000 devices**.
What happens to memory, database lock contention, and `Unit of Work` serialization when two API calls concurrently call `group.AddDevice(...)` on a `DeviceGroup` that loads 2 million child rows into RAM?
How do Vaughn Vernon's and Eric Evans's Aggregate sizing rules resolve this by modeling `DeviceGroupMembership` as its own separate entity/record referencing `GroupId` by ID, and enforcing approximate or partitioned quota checks instead?
If you cannot explain why giant collection Aggregates fail at scale, revisit **Section 3** before continuing.

---

### Sources

[^hf-dp]: **Tier 2: Eric Freeman and Elisabeth Robson, *Head First Design Patterns* (O'Reilly, 2nd ed., 2020)**: Core object-oriented design principles (encapsulate what varies, program to an interface, favor composition over inheritance, loose coupling, Law of Demeter, Hollywood Principle) and practical GoF explanations.
[^clean-code]: **Tier 2: Robert C. Martin, *Clean Architecture* / *Agile Software Development, Principles, Patterns, and Practices* (Prentice Hall, 2002 / 2017)**: Formulation and architectural application of the five SOLID principles (SRP, OCP, LSP, ISP, DIP).
[^gof-1994]: **Tier 2: Erich Gamma, Richard Helm, Ralph Johnson, and John Vlissides, *Design Patterns: Elements of Reusable Object-Oriented Software* (Addison-Wesley, 1994)**: The canonical catalog of 23 Creational, Structural, and Behavioral design patterns.
[^fowler-poeaa]: **Tier 2: Martin Fowler, *Patterns of Enterprise Application Architecture* (Addison-Wesley, 2002)**: Transaction Script, Domain Model, Table Module, Service Layer, Remote Facade, and DTO patterns.
[^evans-ddd]: **Tier 2: Eric Evans, *Domain-Driven Design: Tackling Complexity in the Heart of Software* (Addison-Wesley, 2003)**: Ubiquitous Language, Bounded Contexts, Context Mapping, Anti-Corruption Layer, Entities, Value Objects, Aggregates, Repositories, and Domain Services.
[^azure-design]: **Tier 3: Microsoft Azure Architecture Center, *Cloud Design Patterns: Design, Implementation, and Security***: Ambassador, Anti-Corruption Layer, Backends for Frontends, External Configuration Store, Gateway Aggregation/Offloading/Routing, Geode, Sidecar, Strangler Fig, and Valet Key patterns ([https://learn.microsoft.com/en-us/azure/architecture/patterns/](https://learn.microsoft.com/en-us/azure/architecture/patterns/)).
[^fowler-strangler]: **Tier 4: Martin Fowler, *StranglerFigApplication* (martinfowler.com, 2004)**: Incremental legacy replacement behind an intercepting routing facade.
