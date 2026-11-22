# 07: Indexes and Data Modeling: SQL vs NoSQL

A storage engine can only find data efficiently along the physical sort order of its primary key.
If you store 100 million device records keyed by `DeviceId` and a dashboard queries `WHERE FirmwareVersion = '4.2.1' AND Region = 'eu-central'`, a database without an appropriate index has no choice but to read all 100 million records from disk (a full table or full partition scan).
Yet every index you add to accelerate reads imposes an immediate tax on writes, memory, and storage footprint.
At the same time, choosing between a relational database (**SQL**) and the four families of non-relational stores (**Key-Value, Document, Wide-Column, and Graph NoSQL**), and organizing how your object-oriented service layer maps domain state to those tables, determines whether your system remains maintainable five years after launch.
This module explains how primary/clustered, secondary, composite, and covering indexes work physically on disk, how cloud architectures implement **Index Tables** and **Materialized Views** across partitioned stores, when to choose SQL versus NoSQL, and how Martin Fowler's enterprise data-source and object-relational patterns structure the persistence boundary.

> **Prerequisite:** [Module 05: Consistency and Transaction Isolation Models](./05-consistency-and-transaction-isolation-models.md) and [Module 06: Write-Ahead Logs and Storage Engines](./06-write-ahead-logs-and-storage-engines.md).

---

## 1. Index anatomy: clustered, secondary, composite, and covering indexes

Every database index is an auxiliary data structure (almost always a B+Tree or LSM-Tree) that maps one or more search attributes to the location of the matching record, speeding up reads at the cost of extra disk space and extra writes on every `INSERT`, `UPDATE`, and `DELETE`[^ddia-ch3][^petrov-dbi].
To design indexes that actually eliminate disk I/O rather than merely satisfying a linter, you must understand four physical distinctions:

### Clustered (Primary) Index vs Non-Clustered (Secondary) Index

Where do the actual row columns live on disk?
- **Clustered Index (Index-Organized Table)**: In SQL Server (by default on `PRIMARY KEY`), MySQL InnoDB (always on `PRIMARY KEY`), and BigTable/Cassandra (on `(PartitionKey, ClusteringKey)`), **the leaf pages of the clustered index *are* the table itself**. The full row payload is stored inline inside the B+Tree leaf pages sorted physically by the clustered key. Consequently, a table can have **at most one** clustered index, and range scans on the clustered key read contiguous pages sequentially with zero extra pointer hops.
- **Heap Table + Non-Clustered Indexes**: In PostgreSQL, all table rows live in an unsorted (or visibility-ordered) **Heap File** identified by a physical tuple ID (`ctid = (pageNumber, slotNumber)`), and even the primary key index is a non-clustered B+Tree pointing to heap `ctid`s.
- **Secondary (Non-Clustered) Index**: A separate B+Tree sorted by the indexed column(s). What sits at the leaf of a secondary index? In a heap-based engine (PostgreSQL), the leaf stores the physical heap `ctid`. In a clustered engine (InnoDB, SQL Server with a clustered PK), **the leaf of every secondary index stores the Clustered Primary Key value** (not a physical page pointer, so that splitting a clustered B+Tree page does not require updating every secondary index!).

```
Secondary Index Lookup WITH Bookmark / Key Lookup (2 B+Tree Traversals):
========================================================================
Query: SELECT DeviceId, Status, LastSeenUtc FROM Devices WHERE TenantId = 'T9'

  [ Secondary Index on (TenantId) ]
     Leaf holds: (TenantId = 'T9' -> ClusteredPK: DeviceId = 'D-402')
                                              |
           +----------------------------------+  (Key / Bookmark Lookup!)
           v
  [ Clustered Index on (DeviceId) ]
     Root -> Internal -> Leaf holds: (DeviceId = 'D-402', Status = 'Healthy', LastSeenUtc = ...)
```

Look closely at that diagram: if a secondary index on `TenantId` matches 5,000 devices, and the `SELECT` clause asks for `Status` and `LastSeenUtc` (which are not in the secondary index), the database must perform **5,000 random Key Lookups (Bookmark Lookups)** back into the clustered B+Tree (or Heap) to fetch those two columns!
Above a surprisingly small selectivity threshold (often just 1% to 3% of the table), 5,000 random 16 KB page reads are slower than sequentially scanning the entire clustered table, which is why the query optimizer suddenly abandons your index and falls back to a full table scan.

### Composite (Concatenated) Indexes and the Leftmost Prefix Rule

A **composite index** on `(TenantId, Status, LastSeenUtc)` sorts entries lexicographically: first by `TenantId`, then within each `TenantId` by `Status`, and then within each `(TenantId, Status)` pair by `LastSeenUtc`.
Because of that physical sort order, a composite B+Tree index obeys two strict rules:
1. **The Leftmost Prefix Rule**: An index on `(A, B, C)` accelerates queries filtering on `(A)`, on `(A, B)`, and on `(A, B, C)`, because those prefixes form contiguous slices of the leaf pages. It is **useless** for a query filtering only on `WHERE B = 10` or `WHERE C = 20` without `A`, because values of `B` are scattered across every distinct value of `A`.
2. **Equality Before Range**: Once a query applies a range predicate (`>`, `<`, `BETWEEN`) on a column in the index, any subsequent columns to the right in the index cannot be used for B+Tree range narrowing. For example, if your query is `WHERE TenantId = 'T9' AND LastSeenUtc > '2026-11-01' AND Status = 'Unhealthy'`, the index column order **must be `(TenantId, Status, LastSeenUtc)`** (equality columns first, range column last), not `(TenantId, LastSeenUtc, Status)`!

### Covering Indexes (`INCLUDE` Columns)

How do we eliminate the 5,000 random Key Lookups in our earlier query without bloating the B+Tree's internal routing nodes?
By creating a **Covering Index**[^ddia-ch3]:

```sql
CREATE INDEX IX_Devices_Tenant_Status
ON Devices (TenantId, Status)
INCLUDE (LastSeenUtc, FirmwareVersion);
```

A covering index contains every column referenced by the query's `SELECT`, `WHERE`, and `ORDER BY` clauses inside its leaf pages, allowing the storage engine to satisfy the query with an **Index-Only Scan**; never touching the clustered table or heap at all.
Using the `INCLUDE` clause stores `LastSeenUtc` and `FirmwareVersion` *only in the leaf pages* of the secondary B+Tree (omitting them from the internal routing pages so the tree's branching factor stays high).

---

## 2. Cloud indexing patterns: Index Tables and Materialized Views

When you move from a single relational database to a horizontally partitioned cloud data store (such as Azure Table Storage, DynamoDB, Cassandra, or sharded Cosmos DB), the storage engine partitions rows across physical machines by `PartitionKey`.
Because secondary values live on different physical machines from their primary records, the database cannot simply maintain a single local B+Tree across the cluster without distributed two-phase commit on every write.
Two cloud design patterns solve this problem explicitly[^azure-patterns]:

### The Index Table Pattern

When your primary table is partitioned by `DeviceId` (`MainTable: PartitionKey = DeviceId`) and you need fast queries by `SerialNumber` or `(TenantId, FirmwareVersion)`, you create a separate **Index Table** whose `PartitionKey` and `RowKey` are the queryable attributes[^azure-patterns]:
- **Narrow Index Table (Pointer Only)**: Stores `(PartitionKey = TenantId, RowKey = FirmwareVersion + '_' + DeviceId) -> DeviceId`. A reader queries the Index Table to get the matching `DeviceId`s and then issues parallel point reads against the Main Table.
- **Wide Index Table (Covering / Duplicated Payload)**: Duplicates the frequently read columns (`Status`, `LastSeenUtc`) directly inside the Index Table row, trading storage space and write fan-out for single-hop reads.

### The Materialized View Pattern

When a dashboard or API requires aggregations, multi-entity joins, or reshaping data across multiple tables or event streams (for example, "count of healthy vs unhealthy devices per tenant, region, and firmware version"), computing that aggregation on the fly across 500 million rows is impossible within an interactive SLA.
The **Materialized View Pattern** precomputes the exact query projection ahead of time; triggered asynchronously via Change Data Capture (CDC), transactional outbox events, or streaming processors; and writes the result into a dedicated read-optimized store[^azure-patterns][^ddia-ch3].
If the materialized view is corrupted or the UI requirements change, the view table can be dropped and rebuilt from the authoritative source of truth.

---

## 3. SQL vs NoSQL: data models and architectural trade-offs

Choosing between **SQL (relational)** and **NoSQL (non-relational)** is not a question of old technology versus modern technology; it is a decision about **your dominant access pattern, your relationship structure, and your consistency/partitioning boundary**[^ddia-ch2]:

| Data model | Canonical examples | How data is structured and queried | Where it excels | Where it hurts |
| --- | --- | --- | --- | --- |
| **Relational (SQL)** | PostgreSQL, SQL Server, MySQL, CockroachDB, Spanner | Normalized tables of rows and columns with strict schemas, foreign-key referential integrity, arbitrary ad-hoc SQL joins, and multi-row ACID transactions. | Complex business domains with many-to-many relationships, financial/billing ledgers, ad-hoc reporting, and evolving query patterns. | Horizontal write scaling across shards requires breaking joins/foreign keys or paying distributed consensus latency (`PC/EC`). |
| **Key-Value Store** | Redis, Amazon Dynamo, Aerospike, etcd | Opaque or structured values addressed solely by a unique primary key (`Get(key)`, `Put(key, val)`). | Sub-millisecond caching, session stores, distributed locks/configuration, and simple high-throughput state lookups. | No secondary querying or cross-key relationships without scanning or maintaining manual index keys. |
| **Document Store** | MongoDB, Azure Cosmos DB, Couchbase | Self-contained hierarchical JSON/BSON documents (aggregates) grouped into collections, indexed by document ID and secondary JSON paths. | Aggregate-oriented domain entities (product catalogs, user profiles, device twins) with one-to-many nested trees and evolving schemas. | Many-to-many relationships across documents require application-side joins or denormalized duplication that can drift out of sync. |
| **Wide-Column Store** | Google BigTable, Apache Cassandra, HBase, ScyllaDB | Multi-dimensional sorted map: rows grouped by `PartitionKey`, sorted within a partition by `ClusteringKey`, with dynamic column families stored in LSM-Trees. | Massive write-heavy time-series telemetry, audit logs, messaging histories, and predictable key-range access at petabyte scale. | Queries **must** match the exact `(PartitionKey, ClusteringKey)` design; ad-hoc filtering on non-key columns forces catastrophic cluster scans. |
| **Graph Database** | Neo4j, Amazon Neptune, JanusGraph | Nodes (entities) and directed/undirected Edges (relationships) with properties, optimized for index-free adjacency and multi-hop graph traversal. | Fraud rings, social graphs, identity/permission inheritance trees, and network topology dependency analysis. | Partitioning an arbitrary graph across machines without cutting hot edges (`minimum cut` NP-hard) is notoriously difficult. |

**The Principal Rule of Data Modeling:**
In a relational database, you **model the data first** (normalizing into 3rd Normal Form so every fact lives in one place) and write the queries later.
In a Wide-Column or Document NoSQL database at scale, you **model the queries first** (designing each table/collection around a specific access pattern so every hot-path read hits a single partition key in a single network hop) and accept controlled denormalization on the write path[^ddia-ch2].

---

## 4. Fowler's enterprise data-source and object-relational patterns

Inside an application service that talks to a database, how should your code organize domain logic and SQL/storage access?
In *Patterns of Enterprise Application Architecture* (PoEAA), Martin Fowler cataloged the architectural patterns that still govern every modern .NET, Java, and Go service[^fowler-poeaa]:

### Data Source Architectural Patterns

1. **Table Data Gateway**: A single stateless class that holds all the SQL/storage queries (`Find(id)`, `Insert(...)`, `Update(...)`) for a single database table, returning simple DTOs or record sets. Simple, explicit, and ideal when paired with a Transaction Script.
2. **Row Data Gateway**: An object that wraps a single database row (`DeviceGateway`), holding the column fields and methods like `.Insert()` and `.Update()` directly on the row instance, but containing **no domain business logic**.
3. **Active Record**: Combines a Row Data Gateway with domain logic in the same class: the `Device` object holds the database columns, knows how to save/load itself (`device.Save()`), and also implements business methods (`device.EvaluateCompliance()`). Active Record (popularized by Ruby on Rails and simple CRUD models) is fast to build for simple domains, but tightly couples domain testing to the database schema.
4. **Data Mapper**: Completely separates the in-memory **Domain Model** (`Device` aggregate with pure business logic and zero knowledge of SQL or persistence attributes) from the database schema via an independent Mapper layer (such as Entity Framework Core's `DbContext` mapping configurations or a hand-written repository mapper)[^fowler-poeaa]. When domain rules are rich and tested offline (as in this book), **Data Mapper + Repository** is the gold standard.

### Object-Relational Behavioral and Structural Patterns

When using a Data Mapper or ORM, three behavioral patterns govern correctness and performance within a request scope[^fowler-poeaa]:
- **Unit of Work**: Tracks every entity loaded, created, modified, or deleted during a business operation and flushes all database changes atomically inside a single database transaction when `.CommitAsync()` (`SaveChangesAsync()` in EF Core) is called.
- **Identity Map**: Maintains a request-scoped dictionary of `(EntityType, PrimaryKey) -> ObjectInstance` inside the Unit of Work so that if the same database row is queried twice during one request, both callers receive the exact same in-memory object reference; preventing self-conflicting updates within a single request.
- **Lazy Load**: Replaces an expensive child collection or reference with a virtual proxy or loader stub that only queries the database if the code actually touches the property. *Principal warning:* In high-throughput services, implicit Lazy Loading is an anti-pattern because iterating over 100 parent objects and touching a lazy property triggers 100 sequential synchronous database round trips (**the `N + 1` query problem**). Always prefer explicit eager loading or targeted projection queries.

Finally, when mapping an object-oriented class hierarchy (for example, an abstract `RolloutTarget` with subclasses `DeviceGroupTarget`, `CanaryCohortTarget`, and `SingleDeviceTarget`) to relational tables, Fowler defines three **Inheritance Mapping** layouts[^fowler-poeaa]:
1. **Single Table Inheritance (Table-Per-Hierarchy / TPH)**: One table holds all subclasses with a `Discriminator` column and nullable columns for subclass-specific fields. **Fastest queries** (zero joins to polymorphic-load), at the cost of nullable columns.
2. **Class Table Inheritance (Table-Per-Type / TPT)**: One base table holds shared fields, and one table per subclass holds subclass fields joined by primary key. Normalized, but **slowest reads** because loading a polymorphic list requires a multi-table SQL `JOIN`.
3. **Concrete Table Inheritance (Table-Per-Concrete-Class / TPC)**: Each concrete class gets its own completely independent table duplicating base and derived columns. Fast queries for a single concrete type, but requires `UNION ALL` for polymorphic queries and complicates cross-type foreign keys.

---

## 5. Indexing and data modeling in the Atlas platform

In **Atlas**, polyglot persistence and explicit indexing patterns prevent any single database from being forced into an access pattern it was not built for:
- **Relational Control Plane (`PostgreSQL + Data Mapper + Unit of Work`)**: Tenant hierarchies, RBAC role assignments, and rollout definitions use normalized relational tables mapped via clean Data Mappers. Polymorphic rollout rules use **Single Table Inheritance (TPH)** (plus a `JSONB` parameters column) so loading a rollout never performs a 6-way TPT join.
- **Device Twin Store (`Document Store + Index Table`)**: Each of the 500M devices has a hierarchical JSON Twin document partitioned by `DeviceId`. Because operators frequently query devices by `(TenantId, Status, FirmwareVersion)`, Atlas maintains an asynchronous **Covering Index Table** partitioned by `TenantId` with composite sort key `(Status, FirmwareVersion, DeviceId)` and included `LastSeenUtc`, plus a pre-aggregated **Materialized View** of fleet health counts updated by the telemetry stream.

---

## Before you move on

Indexes trade write amplification and storage footprint for read speed by pre-sorting keys in a B+Tree or LSM-Tree.
A **Clustered Index** orders the physical table rows themselves (allowing at most one per table), whereas a **Secondary Index** maps secondary columns back to the clustered primary key (or heap pointer), requiring an extra **Key Lookup** per matching row unless you make the index a **Covering Index** via `INCLUDE` columns.
**Composite Indexes** obey the **Leftmost Prefix Rule** and require equality predicates to precede range predicates.
In distributed NoSQL stores, cross-partition secondary lookups and aggregations are implemented via explicit **Index Tables** and **Materialized Views**.
In application code, simple CRUD services can use **Table Data Gateways** or **Active Record**, while complex domains isolate pure business rules using **Data Mappers**, **Unit of Work**, and **Identity Maps**, avoiding implicit **Lazy Load** `N + 1` traps.

To test your understanding before moving to Module 08, analyze this changed case: a PostgreSQL/SQL Server table `Orders` has 50 million rows and a composite non-clustered index on `(CustomerId, CreatedAtUtc, Status)`.
A hot API endpoint executes:
```sql
SELECT OrderId, TotalAmount
FROM Orders
WHERE CustomerId = @CustId
  AND CreatedAtUtc >= '2026-01-01'
  AND Status = 'Pending';
```
Identify **two distinct flaws** in how that index serves that query (one involving the order of `CreatedAtUtc` vs `Status` in the key, and one involving `TotalAmount` not being covered), and write the exact `CREATE INDEX` statement that turns the execution plan into a single contiguous Index-Only Range Scan with zero key lookups.
If either flaw is unclear, revisit **Section 1** before continuing.

---

### Sources

[^ddia-ch3]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 3: "Storage and Retrieval"**: Clustered indexes, secondary indexes, concatenated/composite indexes, covering indexes, and materialized views.
[^petrov-dbi]: **Tier 2: Alex Petrov, *Database Internals* (O'Reilly, 2019), Chapters 2–4**: Index-organized tables vs heap files, primary vs secondary B+Tree indirection, and composite key ordering.
[^azure-patterns]: **Tier 3: Microsoft Azure Architecture Center, *Cloud Design Patterns: Data Management***: Official specification of the Index Table Pattern and Materialized View Pattern ([https://learn.microsoft.com/en-us/azure/architecture/patterns/category/data-management](https://learn.microsoft.com/en-us/azure/architecture/patterns/category/data-management)).
[^ddia-ch2]: **Tier 2: Martin Kleppmann, *Designing Data-Intensive Applications* (O'Reilly, 2017), Chapter 2: "Data Models and Query Languages"**: Relational vs Document vs Graph models, impedance mismatch, normalization vs denormalization, and schema-on-read vs schema-on-write.
[^fowler-poeaa]: **Tier 2: Martin Fowler, *Patterns of Enterprise Application Architecture* (Addison-Wesley, 2002)**: Domain Model, Table Data Gateway, Row Data Gateway, Active Record, Data Mapper, Unit of Work, Identity Map, Lazy Load, and Single/Class/Concrete Table Inheritance.
