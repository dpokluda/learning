# 25: Policy Engines, DSLs, and Cross-Language Runtimes (.NET + Rust FFI)

How do you allow thousands of tenants, product teams, or compliance administrators to define rich, dynamic business logic (targeting rules, hardware driver rankings, multivariate ML experiment guardrails, RBAC/ABAC security policies) that executes **millions of times per second on your Tier-1 hot path in under a millisecond**, without deploying code and without letting a buggy rule crash a worker or trigger an infinite loop?

If you hardcode those rules in procedural `if / else` C# statements, every rule change requires a full CI/CD code deployment.
If you call out over the network to an external rules microservice (`GroupsService` or `ExperimentService`) on every request, network tail latency and downstream outages sink your Tier-1 availability (Module 22).
And if you allow users to upload arbitrary Turing-complete scripts (`JavaScript`, `Lua`, or dynamically compiled C# `Roslyn` assemblies), a single runaway `while (true)` loop or accidental heap allocation spike can freeze your entire worker fleet.

The Principal engineering solution is an **embedded, deterministic Domain-Specific Language (DSL) or Policy-as-Code Engine**: rules are authored as declarative data, statically validated by a compiler/grammar at publish time, distributed to Data Plane stamps via snapshots and change queues (Module 22), and evaluated in memory in microseconds using **AST short-circuiting, inverted bitset/Bloom indexes, or zero-GC native runtimes (`Rust` via C-ABI FFI)**.
This module walks through **three real-world Principal case studies** (a `< 30 ms` 10,000-expression applicability and 1.4B-device ranking engine, a formal domain grammar for multivariate ML experimentation, and content-agnostic policy evaluation with `Open Policy Agent / Rego` and `CEL`), followed by a deep dive into **safe `.NET / C#` + `Rust` Cross-Language FFI Runtime Architecture (`LibraryImport`, opaque handles, pinned spans, and zero-GC hot paths)**.

> **Prerequisite:** [Module 09: Probabilistic Data Structures at Scale](./09-probabilistic-data-structures-at-scale.md), [Module 14: Domain-Driven Design and Software Architecture Patterns](./14-domain-driven-design-and-software-architecture-patterns.md), and [Module 22: Control Plane vs Data Plane and Cell-Based Architectures](./22-control-plane-vs-data-plane-and-cell-architectures.md).

---

## 1. Case Study 1: Sub-millisecond applicability and 1.4B-device multi-criteria ranking engines

Consider the scale of a planet-scale OS update, driver catalog, or IoT deployment engine serving **1.4 billion heterogeneous devices**:
When a single device connects to sync its updates, it presents a **Device Context** containing **`1,000+` hardware and software attributes** (PCI/USB hardware IDs, PnP device tree identifiers, CPU architecture, BIOS revision, OS build number, OEM manufacturer, enterprise ring, geographic region, and installed package versions).
Against that single connecting device, the catalog contains **`130,000+` driver and firmware update rules**, and an active evaluation batch must evaluate **10,000 complex boolean applicability expressions in `< 30 ms` total (`< 3 ms` per 1,000 expressions, or `< 3 microseconds` per expression)**!

If you evaluate 130,000 AST rules against 1,000 device attributes naively (`130,000 * 1,000 = 130,000,000` string comparisons per request!), a single device sync takes **over 800 milliseconds of pure CPU time**, melting a 1,000-core cluster at just `1,200 RPS`.

How do you drop that evaluation latency by **`300x`** so that 130,000 catalog rules and 10,000 boolean ASTs evaluate in **under `3 to 30 milliseconds`**?
You build a **four-stage filtering and evaluation pipeline**:

```
[ Connecting Device Context: 1,000+ Hardware IDs & System Attributes ]
                                  |
                                  v
+-----------------------------------------------------------------------------------+
| STAGE 1: Dictionary Interning & Inverted Bitset / Bloom Pre-Screening             |
|  * Map all 1,000 device Hardware ID strings ONCE to 32-bit integer Symbol IDs.    |
|  * Probe Inverted Index (SymbolId -> RoaringBitset of candidate Catalog Rule IDs) |
|    and Catalog Bloom Filter: prunes 130,000 rules down to ~150 candidate rules!   |
+-----------------------------------------------------------------------------------+
                                  | (~150 Candidate Rules)
                                  v
+-----------------------------------------------------------------------------------+
| STAGE 2: Flattened Postfix / Short-Circuit AST Evaluation (< 3 us / expression)   |
|  * Evaluate pre-compiled AST nodes ordered by selectivity (cheapest/most likely   |
|    to fail first); memoize shared sub-expressions across the 10,000 rules!        |
+-----------------------------------------------------------------------------------+
                                  | (~12 Applicable Drivers / Updates)
                                  v
+-----------------------------------------------------------------------------------+
| STAGE 3: Deterministic Multi-Criteria Lexicographical Ranking                     |
|  * Rank matching candidates by composite priority tuple:                          |
|    (FeatureScore ASC, HardwareMatchRank ASC, SignerTier ASC, DriverDate DESC,     |
|     DriverVersion DESC) -> Selects the single winning package deterministically!  |
+-----------------------------------------------------------------------------------+
```

Look at the four algorithmic optimizations that make this pipeline sub-millisecond:

1. **Symbol Interning (Eliminating String Comparisons on the Hot Path)**:
   Before evaluation begins, every distinct attribute name (`"Architecture"`, `"OemId"`, `"OsBuild"`) and every known hardware identifier (`"PCI\\VEN_10DE&DEV_2684"`) in the catalog is interned into a contiguous 32-bit integer **`SymbolId` (`int`)** when the Stamp Data Plane hydrates its snapshot (Module 22).
   When a device request arrives, its attributes are hashed once into `SymbolId`s, turning expensive UTF-8 string comparisons inside the AST loop into **1-cycle integer array lookups (`context.Values[symbolId]`)**!
2. **Bloom Filter & Inverted Index Pre-Screening (`130,000 -> ~150` Rules)**:
   Every driver rule requires at least one matching `HardwareId` from the device's PnP tree.
   Instead of iterating over all 130,000 catalog rules and asking *"Does the device have this rule's HardwareId?"*, we invert the lookup at catalog-compile time into an **Inverted Index (`Dictionary<int, int[]> hardwareSymbolToRuleIds`)** fronted by a **Bloom Filter** of all catalog-referenced hardware symbols.
   In \(O(K)\) lookups (where \(K \approx 1{,}000\) is the device's attribute count, **not** the 130,000 catalog size!), we intersect the device's hardware symbols against the inverted index and immediately eliminate `99.8%` of the catalog!
3. **Selectivity-Ordered Short-Circuit AST + Sub-Expression Memoization**:
   Across 10,000 applicability expressions, many sub-expressions repeat thousands of times (for example, `OsArch == 'amd64' AND OsBuild >= 26100`).
   By assigning each unique AST subtree a canonical **Directed Acyclic Graph (`DAG`) Node ID** (the **Flyweight Pattern** from Module 14!), evaluating `Node #42` (`OsBuild >= 26100`) **once** caches its boolean result in a stack-allocated bitset (`Span<ulong> memoBits`), so the remaining 8,500 expressions that share `Node #42` read its truth value in **1 CPU instruction**!
   Furthermore, inside every `AND` node, children are sorted at compile time by **cost and false-selectivity** (checking a single integer equality `Sku == Enterprise` *before* evaluating a regex or version-range check), maximizing short-circuit exits.
4. **Multi-Criteria Lexicographical Ranking**:
   When multiple candidate drivers or policies match the same hardware component (for instance, a generic inbox class driver, a vendor OEM driver, and a newer WHQL-signed hotfix driver), a deterministic **Multi-Criteria Ranking Comparator** orders them by a packed 64-bit sort key or lexicographical tuple (`FeatureScore`, `HardwareIdSpecificityRank`, `SignerTrustLevel`, `ReleaseTimestamp`, `SemanticVersion`) so the exact same winning driver is chosen on every stamp with zero ambiguity.

---

## 2. Case Study 2: Guardrailing multivariate ML experimentation with a formal domain grammar

Now consider a second high-scale problem: a cloud storefront or product platform runs hundreds of concurrent **multivariate machine-learning experiments and feature-flighting treatments** across millions of active users.
Each experiment treatment modifies UI layouts, ranking weights, or backend service parameters when a targeting rule matches.
What goes wrong if data scientists and product engineers define experiment treatments using ad-hoc JSON blobs or untyped configuration dictionaries?
- **Mutually Exclusive Treatment Collisions**: Experiment A activates a new checkout flow while Experiment B removes a required payment field on the same cohort, crashing the page at runtime!
- **Invalid Type or Domain Combinations**: A treatment assigns a negative weight or references an undefined feature slot that only surfaces 6 hours into a production flight when a rare user cohort hits that branch.

To make runtime experimentation crash-proof, we design a **Formal Domain Grammar and Static Type-Checking Compiler** (`EBNF / ANTLR / Recursive-Descent Parser`) that sits at the **Control Plane publish gate**[^fowler-dsl]:

```text
Formal Experimentation Guardrail Grammar (Simplified EBNF):
-----------------------------------------------------------
ExperimentSpec := "experiment" Identifier "for"ScopeDeclaration "{"
                    CohortRule
                    TreatmentBlock+
                    GuardrailInvariant+
                  "}" ;
CohortRule     := "when" BooleanExpr ;
TreatmentBlock := "treatment" Identifier "allocation" Percentage "{" Assignment+ "}" ;
GuardrailInvariant := "require" BooleanExpr ("else" FallbackAction)? ;
```

When an experiment author submits a treatment specification to the Control Plane:
1. **Syntactic & Static Type Verification**: The compiler parses the grammar into a typed AST and verifies against the **Feature Schema Registry** that every referenced attribute and treatment slot exists, has compatible types (`Version`, `Percentage`, `Enum`, `FloatRange[0.0..1.0]`), and leaves no unhandled enum branches.
2. **Static Mutual-Exclusion & Conflict Detection (Namespace / Slot Locking)**: Each treatment block declares the exact set of **Feature Slots (`ModifiedSlots`)** it mutates. Before activating an experiment, the compiler checks the active experiment graph: if two concurrent experiments have overlapping cohort predicates and mutate the same non-composable `FeatureSlot` without an explicit priority-resolution rule, **the Control Plane rejects the publish at compile time**!
3. **Runtime Guardrail Invariants with Instant Fallback**: Even if an ML model produces a dynamic score at runtime, the compiled `GuardrailInvariant` wraps the output (`require score >= 0.0 && score <= 1.0 && latencyBudgetMs < 15 else fallback DefaultTreatment`), guaranteeing that no ML anomaly can violate hard rendering or safety invariants.

---

## 3. Case Study 3: Content-agnostic policy evaluation (`Open Policy Agent / Rego`, `CEL`, and `Cedar`)

What if your platform is a **content-agnostic governance or authorization engine** (evaluating cloud resource compliance, Kubernetes admission control, API gateway zero-trust access, or CI/CD attestation) where the payload can be *any* arbitrary JSON/YAML document (`Azure Resource Manager` templates, `Terraform` plans, `Kubernetes` pod specs, `JWT` claims)?
Here, industry platforms converge on declarative **Policy-as-Code languages**: **Open Policy Agent (`OPA / Rego`)**, **Google Common Expression Language (`CEL`)**, and **AWS `Cedar`**[^opa-docs][^cel-spec]:

| Property | Why Turing-Complete Scripts (`JS / Lua / Python`) Fail | How Declarative Policy Engines (`Rego / CEL / Cedar`) Guarantee Safety |
| --- | --- | --- |
| **Termination Guarantee** | An accidental `while (true)` or recursive call hangs the worker thread forever. | **Non-Turing-Complete (Total Functional / Datalog)**: unbounded `while` loops and arbitrary recursion are syntactically impossible! Every loop iterates strictly over a finite input collection, proving mathematically that evaluation terminates in bounded steps. |
| **Determinism & Hermeticity** | Scripts can call `DateTime.Now`, `Random()`, or `httpClient.GetAsync(...)`, producing non-repeatable results and network hangs. | **Pure Hermetic Evaluation**: evaluation is a pure mathematical function `Decision = Evaluate(CompiledPolicy, InputJson, DataContextJson)` with zero ambient I/O or network calls allowed during evaluation. |
| **Partial Evaluation** | Must re-execute the entire script from scratch on every request. | **Partial Evaluation**: when `DataContext` is known ahead of time, the compiler simplifies the policy AST into a residual fast-path expression (or SQL/Parquet pushdown predicate!). |

---

## 4. Cross-language runtime boundaries: embedding Rust inside .NET / C# via FFI (`LibraryImport`)

Suppose your cloud control and data plane microservices are built in **.NET / C#** (taking advantage of ASP.NET Core's high-performance HTTP/gRPC stack, Azure SDK ecosystem, and rich dependency injection), and your service must evaluate **thousands of complex `Rego` (or DSL) policies per second against multi-megabyte JSON resource payloads** inside every Data Plane pod.

Why might evaluating complex graph/unification policies like `Rego` in a managed garbage-collected runtime (or spawning an out-of-process Go `opa` sidecar over `localhost` HTTP) become a bottleneck at extreme scale?
1. **Why Out-of-Process Sidecars Hurt at High QPS**: Sending a `2 MB` JSON payload from your C# container to a local Go `opa` sidecar over `localhost` HTTP/gRPC on every request serializes and copies `2 MB` across user/kernel socket buffers twice, burning 40% of your vCPU on loopback IPC and adding `2 to 8 ms` of tail latency!
2. **Why Managed Heap Allocations Hurt Inside Tight Policy Evaluation Loops**: Evaluating a Datalog/Rego rule tree builds thousands of transient intermediate bindings, sets, and virtual-DOM nodes per evaluation. In a garbage-collected runtime, allocating 50 MB of short-lived evaluation graphs per second across 32 threads triggers frequent **Gen-0/Gen-1/Gen-2 Garbage Collection pauses**, spiking `p99.9` evaluation latency from `1 ms` to `80 ms`!

### The Best of Both Worlds: .NET Host + In-Process Zero-GC `Rust` Engine (`Regorus` / Native FFI)

By writing (or embedding) the policy evaluation engine in **Rust** (such as Microsoft's open-source **`Regorus`** fast Rego interpreter in Rust, or a custom Rust AST engine) and calling it **in-process from C# via C-ABI Native Interop (`[LibraryImport]` in .NET 7–10)**, you get:
- **Zero Loopback Network IPC** (runs in the exact same OS process and thread address space in nanoseconds).
- **Zero .NET Garbage Collector Pressure** (Rust allocates its evaluation arena deterministically on its own native allocator and frees it immediately when the call returns, with **zero GC pauses**!).

However, crossing the **Managed (`.NET`) <-> Unmanaged (`Rust`) FFI Boundary** safely in production requires obeying five strict systems rules so you never cause a segmentation fault (`AccessViolationException`), a native memory leak, or a GC moving-pointer corruption[^dotnet-pinvoke][^rust-ffi]:

```
.NET / C# MANAGED RUNTIME (GC Heap & Stack)               RUST NATIVE LIBRARY (cdylib, C-ABI, No GC)
===========================================               ==========================================
1. SafeHandle (`RegoEngineHandle : SafeHandle`)  ------->  Opaque Pointer `*mut Engine` (`Box::into_raw`)
   * Guarantees `engine_destroy(ptr)` is called            * Rust owns native struct lifetime; freed ONLY
     even if C# throws or finalizes!                         when C# calls `engine_destroy(ptr)`!

2. Zero-Copy Input (`ReadOnlySpan<byte> utf8`)   ------->  Borrowed Slice `(*const u8, usize)`
   * Pinned on stack for duration of synchronous           * Rust reads `*const u8` during the synchronous
     `[LibraryImport]` call (`fixed` pointer).               FFI call and NEVER stores the pointer after return!

3. Error & Result Boundary (`extern "C"`)        <-------  `catch_unwind(|| ...)` at EVERY `extern "C"` entry!
   * Never let a Rust `panic!` unwind across FFI           * Unwinding a Rust panic into .NET's stack frame
     (Immediate Undefined Behavior / Process Crash!).        is UB; catch and return an error code/buffer!
```

Let us examine the exact Rust `extern "C"` side and the idiomatic .NET 10 `[LibraryImport]` + `SafeHandle` wrapper side by side:

```rust
// Rust Side (Cargo.toml: crate-type = ["cdylib"])
use std::panic::catch_unwind;
use std::slice;

pub struct PolicyEngine {
    // Pre-compiled ASTs and arena buffers owned by Rust
}

#[no_mangle]
pub extern "C" fn policy_engine_create() -> *mut PolicyEngine {
    let engine = Box::new(PolicyEngine {});
    Box::into_raw(engine)
}

#[no_mangle]
pub unsafe extern "C" fn policy_engine_destroy(engine_ptr: *mut PolicyEngine) {
    if !engine_ptr.is_null() {
        // Retake ownership and drop deterministically
        drop(Box::from_raw(engine_ptr));
    }
}

// Returns: 1 = Allowed, 0 = Denied, < 0 = Error code (never panics across FFI!)
#[no_mangle]
pub unsafe extern "C" fn policy_engine_evaluate_utf8(
    engine_ptr: *const PolicyEngine,
    input_utf8_ptr: *const u8,
    input_utf8_len: usize,
) -> i32 {
    let result = catch_unwind(|| {
        if engine_ptr.is_null() || input_utf8_ptr.is_null() {
            return -1;
        }
        let _engine = &*engine_ptr;
        let input_bytes = slice::from_raw_parts(input_utf8_ptr, input_utf8_len);
        // Parse UTF-8 in place with zero copy and evaluate pre-compiled AST...
        if input_bytes.len() > 2 { 1 } else { 0 }
    });

    result.unwrap_or(-99) // Catch any Rust panic and translate to error code -99
}
```

And here is the matching production C# wrapper using source-generated `[LibraryImport]` and `SafeHandleZeroOrMinusOneIsInvalid` (which guarantees native memory is freed even under asynchronous thread aborts or exceptions):

```csharp
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static partial class NativePolicyInterop
{
    private const string NativeLibName = "atlas_policy_engine";

    [LibraryImport(NativeLibName, EntryPoint = "policy_engine_create")]
    internal static partial IntPtr CreateEngine();

    [LibraryImport(NativeLibName, EntryPoint = "policy_engine_destroy")]
    internal static partial void DestroyEngine(IntPtr enginePtr);

    [LibraryImport(NativeLibName, EntryPoint = "policy_engine_evaluate_utf8")]
    internal static partial int EvaluateUtf8(
         SafePolicyEngineHandle engine,
         ReadOnlySpan<byte> inputUtf8,
         nuint inputLength);
}

public sealed class SafePolicyEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePolicyEngineHandle() : base(ownsHandle: true)
    {
        SetHandle(NativePolicyInterop.CreateEngine());
    }

    protected override bool ReleaseHandle()
    {
        NativePolicyInterop.DestroyEngine(handle);
        return true;
    }
}
```

Look at why passing `ReadOnlySpan<byte> inputUtf8` directly to a source-generated `[LibraryImport]` method is so fast in modern .NET:
1. Instead of marshaling a C# `string` (which is UTF-16 in memory and would force the runtime to allocate a new null-terminated UTF-8 heap buffer and copy every byte on every P/Invoke call!), your ASP.NET Core / Kafka pipeline already receives JSON payloads as **UTF-8 bytes (`ReadOnlySpan<byte>`)**.
2. The `[LibraryImport]` source generator pins the `ReadOnlySpan<byte>` on the thread stack using a `fixed` pointer for the microsecond duration of the synchronous native call, passing the raw `(byte*, length)` pointer directly to Rust's `slice::from_raw_parts` with **zero heap allocations and zero memory copies**!
3. When you also need **sandboxed multi-tenant isolation** for untrusted third-party policy plugins, you can compile the exact same Rust policy crate to **`wasm32-unknown-unknown` (WebAssembly)** and execute it inside `.NET` via **`Wasmtime`** with deterministic **fuel metering** (bounding the exact number of CPU instructions a policy is allowed to execute!).

Let us also write a complete, self-contained C# implementation of our **Stage 1 + Stage 2 + Stage 3 Applicability & Multi-Criteria Driver Ranking Engine** that you can run and test directly in `PrincipalEngineerDrills`:

```csharp
public enum ComparisonOp : byte
{
    Equals = 1,
    GreaterThanOrEqual = 2
}

public abstract record ApplicabilityNode;

public sealed record AttributeCheckNode(int AttributeSymbolId, ComparisonOp Op, int TargetValue) : ApplicabilityNode;

public sealed record LogicalAndNode(IReadOnlyList<ApplicabilityNode> Children) : ApplicabilityNode;

public sealed record CatalogDriverRule(
    string DriverId,
    int RequiredHardwareSymbolId,
    ApplicabilityNode RuleAst,
    int FeatureScore,          // Lower is better (e.g., 1 = OEM Certified, 10 = Generic)
    int HardwareRank,          // Lower is more specific (0 = Exact PnP ID, 5 = Compatible ID)
    long DriverVersionSortable // Higher is newer
);

public sealed class HighSpeedApplicabilityEngine
{
    private readonly HashSet<int> _hardwareBloomFilter = new HashSet<int>();
    private readonly Dictionary<int, List<CatalogDriverRule>> _invertedHardwareIndex =
        new Dictionary<int, List<CatalogDriverRule>>();

    public void IndexCatalog(IEnumerable<CatalogDriverRule> catalogRules)
    {
        foreach (CatalogDriverRule rule in catalogRules)
        {
            _hardwareBloomFilter.Add(rule.RequiredHardwareSymbolId);
            if (!_invertedHardwareIndex.TryGetValue(rule.RequiredHardwareSymbolId, out List<CatalogDriverRule>? list))
            {
                list = new List<CatalogDriverRule>();
                _invertedHardwareIndex[rule.RequiredHardwareSymbolId] = list;
            }

            list.Add(rule);
        }
    }

    public CatalogDriverRule? EvaluateAndSelectBestDriver(
        ReadOnlySpan<int> deviceHardwareSymbols,
        IReadOnlyDictionary<int, int> deviceAttributes,
        out int rulesEvaluatedCount)
    {
        rulesEvaluatedCount = 0;
        CatalogDriverRule? bestMatch = null;

        foreach (int hwSymbol in deviceHardwareSymbols)
        {
            // Stage 1a: Fast Bloom pre-screen
            if (!_hardwareBloomFilter.Contains(hwSymbol))
            {
                continue;
            }

            // Stage 1b: Inverted index lookup (prunes 130,000 rules to only rules matching hwSymbol)
            if (!_invertedHardwareIndex.TryGetValue(hwSymbol, out List<CatalogDriverRule>? candidates))
            {
                continue;
            }

            foreach (CatalogDriverRule candidate in candidates)
            {
                rulesEvaluatedCount++;

                // Stage 2: Short-circuit AST evaluation
                if (!EvaluateAst(candidate.RuleAst, deviceAttributes))
                {
                    continue;
                }

                // Stage 3: Multi-criteria lexicographical ranking
                if (bestMatch is null || IsBetterCandidate(candidate, bestMatch))
                {
                    bestMatch = candidate;
                }
            }
        }

        return bestMatch;
    }

    private static bool EvaluateAst(ApplicabilityNode node, IReadOnlyDictionary<int, int> attrs)
    {
        switch (node)
        {
            case AttributeCheckNode check:
                if (!attrs.TryGetValue(check.AttributeSymbolId, out int actual))
                {
                    return false;
                }

                return check.Op switch
                {
                    ComparisonOp.Equals => actual == check.TargetValue,
                    ComparisonOp.GreaterThanOrEqual => actual >= check.TargetValue,
                    _ => false
                };

            case LogicalAndNode andNode:
                foreach (ApplicabilityNode child in andNode.Children)
                {
                    if (!EvaluateAst(child, deviceAttributes: attrs))
                    {
                        return false; // Immediate short-circuit!
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private static bool IsBetterCandidate(CatalogDriverRule candidate, CatalogDriverRule currentBest)
    {
        if (candidate.FeatureScore != currentBest.FeatureScore)
        {
            return candidate.FeatureScore < currentBest.FeatureScore;
        }

        if (candidate.HardwareRank != currentBest.HardwareRank)
        {
            return candidate.HardwareRank < currentBest.HardwareRank;
        }

        return candidate.DriverVersionSortable > currentBest.DriverVersionSortable;
    }
}
```

---

## 5. Policy engines, DSLs, and Rust FFI in the Atlas platform

In **Atlas**, all three policy-engine architectures run in production:
- **In-Memory Applicability & Driver Ranking Engine (`< 3 ms` per 1,000 Expressions)**: When 500M edge devices sync with Atlas, each gateway stamp runs the **Symbol-Interned + Bloom/Inverted-Index + Short-Circuit AST + Multi-Criteria Ranking Engine** in memory, matching `1,000+` device hardware attributes against `130,000+` catalog packages in under `2.5 milliseconds` with zero external RPCs.
- **Experimentation Guardrail Compiler at the Control Plane Gate**: Data scientists configuring multivariate edge telemetry and rollout treatments submit specs in Atlas's **Experimentation Domain Grammar**, which statically rejects slot collisions and wraps dynamic outputs in fallback guardrails before the snapshot is published.
- **In-Process `.NET 10 + Rust (Regorus)` Compliance Evaluator**: For enterprise governance policies authored in **OPA Rego**, Atlas's C# Data Plane workers invoke a pre-compiled **Rust Rego Engine** via zero-copy `ReadOnlySpan<byte>` `[LibraryImport]` FFI (`SafePolicyEngineHandle`), evaluating complex multi-rule compliance bundles in `< 400 microseconds` with **zero .NET Gen-2 GC allocations**.

---

## Before you move on

Replacing procedural `if/else` branches and synchronous external microservice calls with **embedded, deterministic Policy Engines and DSLs** eliminates network tail latency while empowering rapid rule iteration.
At planet scale (`1.4B` devices, `130,000+` rules), evaluating **10,000 applicability expressions in `< 30 ms`** requires a four-stage architecture: **Symbol Interning (`string -> int`)**, **Bloom Filter + Inverted Index Pre-Screening**, **Short-Circuit AST Evaluation with Flyweight DAG Memoization**, and **Deterministic Multi-Criteria Lexicographical Ranking**.
For multivariate ML experimentation, a **Formal Domain Grammar** enforces static type safety, detects mutually exclusive feature-slot collisions at publish time, and wraps treatments in runtime fallback guardrails.
For content-agnostic compliance, declarative engines like **OPA Rego, CEL, and Cedar** guarantee **termination** (no unbounded loops) and **hermetic determinism**.
And when embedding a native **Rust** evaluation engine inside **.NET / C#** to eliminate GC pauses on hot paths, safe **C-ABI `[LibraryImport]` FFI** requires **`SafeHandle`** lifetime ownership, stack-pinned **`ReadOnlySpan<byte>` UTF-8** slices for zero-copy input, and **`std::panic::catch_unwind`** at every `extern "C"` boundary.

To test your mastery before moving to Module 26, analyze two bugs in a naive C#-to-Rust FFI design:
1. An engineer declares `[DllImport("engine")] static extern int Evaluate(IntPtr engine, string jsonInput);` and calls it `50,000 times/sec` with a `100 KB` C# `string`. Why does passing `string` across P/Invoke allocate **`5 GB/sec`** of temporary unmanaged buffers even though the Rust engine is zero-GC, and how does changing the signature to `[LibraryImport]` with `ReadOnlySpan<byte> utf8Json` reduce marshaling allocations to **zero bytes**?
2. Inside the Rust `extern "C"` function, a developer writes `let parsed = serde_json::from_slice(bytes).unwrap();` without `std::panic::catch_unwind`. What happens to your entire multi-tenant .NET worker process if a single malformed JSON payload causes `.unwrap()` to panic across the `extern "C"` boundary?
If both answers are immediate, proceed to Module 26.

---

### Sources

[^fowler-dsl]: **Tier 2: Martin Fowler, *Domain-Specific Languages* (Addison-Wesley, 2010)**: Internal vs external DSLs, recursive-descent/ANTLR grammar design, semantic model validation, and compile-time guardrail verification.
[^opa-docs]: **Tier 3: Open Policy Agent (`OPA`) & Microsoft `Regorus` Documentation ([https://www.openpolicyagent.org/docs/latest/philosophy/](https://www.openpolicyagent.org/docs/latest/philosophy/), [https://github.com/microsoft/regorus](https://github.com/microsoft/regorus))**: Datalog/Rego termination and hermetic evaluation guarantees, AST compilation, and embedding a fast Rust Rego interpreter (`Regorus`) inside host runtimes.
[^cel-spec]: **Tier 1: Google Common Expression Language (`CEL`) Specification ([https://github.com/google/cel-spec](https://github.com/google/cel-spec)) & AWS Cedar Policy Language Specification ([https://www.cedarpolicy.com/](https://www.cedarpolicy.com/))**: Non-Turing-complete linear-time expression evaluation and formal verification of authorization policies.
[^dotnet-pinvoke]: **Tier 3: Microsoft .NET Documentation, *Native Interoperability Best Practices & Source-Generated P/Invoke (`LibraryImport`)* ([https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices))**: `SafeHandle` lifetime management, `ReadOnlySpan<byte>` zero-copy pinning, and avoiding UTF-16 to UTF-8 string marshaling allocations.
[^rust-ffi]: **Tier 2: *The Rustonomicon: Foreign Function Interface (FFI) and Unwinding* ([https://doc.rust-lang.org/nomicon/ffi.html](https://doc.rust-lang.org/nomicon/ffi.html))**: `#[repr(C)]`, `Box::into_raw` / `Box::from_raw` ownership transfer, and why unwinding a panic across an `extern "C"` ABI boundary is Undefined Behavior requiring `std::panic::catch_unwind`.
