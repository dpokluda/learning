# Exercise Companion: How To Use It

This folder is the **active-recall and hands-on verification** half of the course.
The twenty-nine narrative modules ([`../00-START-HERE.md`](../00-START-HERE.md)) build your conceptual and operational mental model; the exercises in this folder prove to you that you can apply those invariants from memory and encode them in working code.

## Contents of `exercises/`

| Path | What it is |
| --- | --- |
| [`00-HOW-TO-USE.md`](./00-HOW-TO-USE.md) | This guide: the study loop, offline C# test-suite instructions, and how to practice from starter stubs. |
| [`EXERCISE-COMPANION.md`](./EXERCISE-COMPANION.md) | Twenty-nine exercise chapters (one for every teaching module `01` through `29`). Each chapter has **Part A (Active Recall Questionnaire)** of six sharp conceptual and architectural questions and **Part B (Hands-On C# Coding Drill)** with an explicit prose invariant and starter stub. |
| [`ANSWER-BOOK.md`](./ANSWER-BOOK.md) | Complete worked answers to all 174 Part A questionnaire prompts and full, self-contained C# solutions to all 29 Part B coding exercises. Keep it closed until you have attempted both parts yourself. |
| [`PrincipalEngineerDrills/`](./PrincipalEngineerDrills/) | The runnable offline C# (.NET 10, MSTest 4) verification project containing `Chapter01.cs` through `Chapter29.cs` (`66` classical state-based unit tests, zero external runtime dependencies, zero warnings under `TreatWarningsAsErrors`). |

## Why these exercises are shaped the way they are

At the Principal and Staff engineer level, coding exercises should not be "call a library API and print the output."
Every Part B exercise in this companion asks you to **implement a distributed systems, storage, caching, resiliency, or algorithmic invariant from first principles** in plain, dependency-free in-memory C#:
- Transforming a fair-loss link that drops and duplicates packets into a reliable link via sender retry and receiver idempotency keys (`Chapter01`).
- Comparing Vector Clocks (`HappensBefore`, `HappensAfter`, `Concurrent`) and advancing a Hybrid Logical Clock when the OS wall clock steps backward (`Chapter02`).
- Executing strict quorum reads with digest Read Repair, Sloppy Quorum Hinted Handoff, and logarithmic Merkle Tree diffing (`Chapter03`).
- Running Single-Decree Paxos (`Prepare/Promise` and `Accept/Accepted`) and blocking a GC-paused zombie leader at the storage layer with monotonic Fencing Tokens (`Chapter04`).
- Demonstrating the Write Skew anomaly under MVCC Snapshot Isolation and preventing it with Serializable Snapshot Isolation (`SSI`) read-set validation (`Chapter05`).
- Framing binary Write-Ahead Log records with checksums that detect and truncate torn tail writes on crash recovery, plus an LSM-Tree `MemTable + SSTable + Tombstone Compaction` engine (`Chapter06`).
- Building Consistent Hash Rings with Virtual Nodes, stateless Rendezvous (`HRW`) hashers, Kirsch-Mitzenmacher Bloom Filters, Count-Min Sketches, Lease-Protected Caches, Token Bucket and Sliding Window Counter Rate Limiters, Circuit Breakers, Transactional Outboxes, Saga Orchestrators, Composite Applicability ASTs, Regional Privacy Mapping Tables, GFS/Chubby/BigTable/Cassandra/Kafka primitives, AVL Trees, Prefix Tries, 2D Spatial Quad-Trees, Robin Hood Hash Tables, Floyd's $O(N)$ Heapify, classic sorting algorithms, Kahn's Topological Sort, Dijkstra, Union-Find, Prim's MST, 1D 0/1 Knapsack DP, Zero-Dependency Stamp Caches, Shuffle Sharding, Deficit Round-Robin schedulers, Columnar RLE/Dictionary + Bloom Predicate Pushdown scanners, Multi-Criteria Driver Ranking engines, Multi-Stage `< 5s` Emergency Kill-Switches, and MCP Schema / Hybrid RRF retrievers (`Chapter07` through `Chapter29`).

Once you have written the 40 lines of C# that enforce a monotonic fencing token, a cache lease token, or a Raft/Paxos quorum intersection yourself, you will never forget how the invariant works or fail to spot when a production design proposal violates it.

## The study loop per module

When you finish reading a main module (`01` through `29`), follow this four-step loop before moving on to the next module:

1. **Close the module and answer Part A in writing**: Open [`EXERCISE-COMPANION.md`](./EXERCISE-COMPANION.md) to that module's section and write down your answers to all six Part A questions from memory. Do not merely nod at the questions; forcing yourself to articulate *why* `R + W > N` still fails linearizability or *why* standard Bloom filters cannot delete items reveals every gap in your understanding immediately.
2. **Implement Part B in C#**: Copy the starter stub from [`EXERCISE-COMPANION.md`](./EXERCISE-COMPANION.md) (or replace the body in `PrincipalEngineerDrills/ChapterNN.cs`) and implement the types under test until `dotnet test --filter ClassName~ChapterNN` turns green.
3. **Compare against [`ANSWER-BOOK.md`](./ANSWER-BOOK.md)**: Check both your written Part A explanations and your Part B code against the worked solutions.
4. **Reread the module section if you fumbled either half**: Confusion in a later module is almost always a missing invariant from an earlier one.

## Running the verified C# suite

The [`PrincipalEngineerDrills/`](./PrincipalEngineerDrills/) folder is already set up and verified.
To run all 29 chapters (`66` unit tests):

```powershell
cd E:\projects\learning\self-paced\principal-software-engineer\exercises\PrincipalEngineerDrills
dotnet test
```

To run a single chapter while practicing:

```powershell
dotnet test --no-restore --filter ClassName~Chapter04
```

If you want to practice from a blank slate without modifying the reference solution in `PrincipalEngineerDrills/`, create a sibling scratch project once:

```powershell
dotnet new mstest -o MyPracticeDrills
```

Copy the `<Project>` settings from [`../SETUP.md`](../SETUP.md), paste each starter stub from [`EXERCISE-COMPANION.md`](./EXERCISE-COMPANION.md) into `MyPracticeDrills/ChapterNN.cs`, and work until `dotnet test` passes!
