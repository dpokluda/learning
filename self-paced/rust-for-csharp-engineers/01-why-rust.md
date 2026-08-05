# 01 — Why Rust exists

You already know how to build software that works. The question this module answers is narrower and
more useful: what problem was Rust invented to solve, why does that problem justify a language whose
compiler will reject programs you *know* are correct, and how do you decide when that trade is worth
making?

> **Prerequisite:** none beyond professional experience with C# and .NET.

The temptation for an experienced engineer meeting Rust is to evaluate it as a syntax — to notice that
`match` is a nicer `switch`, that `Option<T>` resembles nullable reference types, that traits look like
interfaces, and to conclude that Rust is C# with unfamiliar punctuation and an annoying compiler. That
conclusion is comfortable and wrong, and it is the single most reliable predictor of an engineer
bouncing off the language in week three. Rust's syntax is the least interesting thing about it. What
makes Rust a different kind of language is that it moved a category of correctness checking from
*runtime* to *compile time*, and it did so for a category that every other mainstream language handles
either with a garbage collector or not at all.

## The problem: memory safety without a garbage collector

Every language has to answer the question *when is it safe to free this memory?* There are historically
two answers. C and C++ say: you tell us, and if you get it wrong you get use-after-free, double-free,
buffer overruns, and data races, which together account for the majority of critical security
vulnerabilities in systems software. Microsoft's own security engineers put memory-safety issues at
roughly 70% of the CVEs assigned across its products over more than a decade, and the Chromium
team independently reached the same figure. That is not a story about careless programmers;
those are among the most carefully reviewed codebases on Earth.

C#, Java, Go, and JavaScript give the other answer: a garbage collector will work it out at runtime.
This is a genuinely excellent answer, and it is why you have probably never debugged a use-after-free
in your career. You pay for it, though, in three currencies. You pay memory — a GC needs headroom to be
efficient, so a managed heap typically runs at some multiple of live-set size. You pay latency
predictability — even .NET's background server GC has pause characteristics you must design around when
tail latency matters. And you pay a hard floor on where the language can go: you cannot write a kernel,
a device driver, a real-time audio processor, or a WebAssembly module measured in kilobytes on top of a
runtime that needs to stop the world and walk an object graph.

Rust's answer is the interesting one: **determine memory lifetime statically, at compile time, with no
runtime cost at all.** Every value has exactly one owner; when the owner goes out of scope, the value is
freed; and the compiler enforces a set of rules about references that make it impossible to hold a
reference to freed memory. There is no garbage collector, no reference counting by default, and no
runtime bookkeeping. The generated code frees memory at exactly the points a careful C programmer would
have inserted `free()` — because the compiler inserted those calls for you, and proved they were
correct.

The consequence that surprises people is that this same machinery, almost unchanged, also eliminates
data races. A data race requires two threads accessing the same memory with at least one writing and no
synchronisation. Rust's borrowing rules already say you may have either many shared readers or exactly
one writer, never both — a rule invented for memory safety. Apply it across threads and data races
become a compile error. Rust calls this *fearless concurrency*, and the marketing name undersells it:
the guarantee is real and it is checked, not conventional.

## What "zero-cost abstraction" actually means

Rust inherits from C++ a principle worth stating precisely, because "zero-cost" is widely
misunderstood. The claim is *not* that abstractions are free in some absolute sense. It is: **you do not
pay for what you do not use, and what you do use, you could not have hand-coded better.**

A concrete comparison makes this land. In C#, `IEnumerable<T>` and LINQ are a genuinely great
abstraction, but they have a runtime shape: `list.Where(x => x.IsActive).Select(x => x.Name)` allocates
iterator state-machine objects, dispatches through interface calls per element, and boxes where value
types meet generic interfaces. The JIT is good and will devirtualise some of this, but the abstraction
has a cost you can measure.

The Rust equivalent looks nearly identical:

```rust
struct Item { is_active: bool, name: String }

fn active_names(items: &[Item]) -> Vec<&str> {
    items
        .iter()
        .filter(|x| x.is_active)
        .map(|x| x.name.as_str())
        .collect()
}
```

but compiles differently. `filter` and `map` are generic over the closure type, and each closure is a
distinct anonymous type known statically. Monomorphisation stamps out a specialised version of the
pipeline with the closure bodies inlined, and what reaches the optimiser is a single loop over a
contiguous buffer with no indirect calls and no per-element allocation. The idiomatic code and the
hand-written loop generate substantially the same machine code. That is the actual claim.

This principle explains design decisions that otherwise look like gratuitous difficulty. Why is there no
implicit `ToString()` on every type? Because formatting a value may allocate, and Rust will not hide an
allocation behind a coercion. Why must you say `.clone()` explicitly? Because a deep copy is real work
and the language refuses to let it be invisible. Why are trait objects (`dyn Trait`) opt-in rather than
the default, when C# interfaces are always dynamically dispatched? Because a vtable indirection is a
cost, and you should ask for it. Rust's ergonomic rough edges are, very often, a refusal to hide a cost
from you.

## Where the C# analogies break down

It is worth naming the four analogies that will mislead you, so that when you meet them in later
modules you already distrust them.

**`Option<T>` is not nullable reference types.** C#'s NRTs are a static analysis layered onto a runtime
that still permits `null` everywhere; they warn, they can be suppressed with `!`, and they vanish at
runtime. `Option<T>` is an actual enum with two variants, checked by exhaustive `match`, and — thanks to
niche optimisation — an `Option<&T>` occupies exactly the same number of bytes as a `&T`. It is a real
type with no runtime cost, not an annotation.

**Traits are not interfaces.** They overlap, but traits can be implemented for types you do not own
(giving you something like extension methods, but with dynamic-dispatch capability and coherence rules),
they can carry associated types, and — crucially — a generic function bounded by a trait is
monomorphised rather than dispatched. Module 08 covers where this stops being a convenience and starts
being a different design vocabulary.

**`Result<T, E>` is not exceptions.** Exceptions are an out-of-band control-flow channel that is
invisible in a method signature; you cannot tell from `int Parse(string s)` that it can throw. A
`Result` is a value in the return type, so fallibility is part of the signature, and the compiler warns
if you ignore it. The `?` operator gives you the ergonomics of exception propagation without the
invisibility. Module 11 is entirely about this shift.

**`Drop` is not `IDisposable`, and it is definitely not a finalizer.** `IDisposable` is a convention
enforced by discipline and a `using` statement you can forget. `Drop` runs deterministically when the
owner goes out of scope, cannot be forgotten, and — unlike a finalizer — runs on a schedule you can
reason about, on the same thread, in a defined order. This is the one place where Rust's model is
strictly simpler than .NET's, and it is a genuine pleasure once you have it.

## When to reach for Rust, and when not to

Here is the honest version, which your instincts as a principal engineer will recognise as the only
useful kind.

Rust earns its cost when at least one of these is true. You are writing something where **a garbage
collector is not permissible or not available** — kernel and driver work, embedded targets, WebAssembly
where binary size is a hard constraint, or a plugin loaded into a host that owns the runtime. You have a
**tail-latency requirement that GC pauses violate**, and you have already exhausted the cheaper options
in .NET (`Span<T>`, pooling, `struct` design, server GC tuning) — this is a real category, but it is
smaller than people think, because modern .NET is very good. You need **predictable memory in a dense
multi-tenant setting**, where cutting per-instance footprint by a large factor changes your unit
economics. You are shipping a **native library consumed from several languages**, where Rust's
`extern "C"` surface plus no runtime makes it a far better producer of `.so`/`.dll` than .NET is. Or you
are building software where **memory-safety vulnerabilities are an existential risk** — parsers of
untrusted input, cryptography, network-facing protocol implementations — and you want the class of bug
eliminated rather than reviewed for.

Rust is the wrong choice when the dominant cost is **developer throughput on business logic**. A CRUD
service backed by a relational database, an internal tool, a line-of-business API — C# will get you
there faster, the ecosystem for that shape of problem is deeper and more mature, your team already knows
it, and the runtime characteristics were never the constraint. It is also the wrong choice when you need
a **large ecosystem of enterprise integrations**, when your team has **no appetite for a genuinely steep
learning curve** (budget one to three months to fluency for a strong engineer, and expect a real dip
first), or when you would be **rewriting working software** for reasons that don't survive being written
down as a number.

There is a middle path worth knowing about, and it is often the right one: keep the application in C#
and push only the hot, well-bounded piece into Rust behind a C ABI. Module 17 shows how to do this with
P/Invoke. This is how most organisations should adopt Rust — not as a rewrite, but as a surgical tool
applied where its properties actually pay.

## The example we will build

Throughout this book we build **`polcheck`**, a compliance-checking CLI. It reads resource records and a
set of rules, evaluates one against the other, and reports what passes and what fails. Here is the
entire domain, which you should skim rather than study — every construct in it is explained in later
modules, and it appears here only so you know where we are going.

```rust
use std::collections::HashMap;

/// A thing we want to check for compliance.
#[derive(Debug, Clone)]
pub struct Resource {
    pub id: String,
    pub kind: String,
    pub location: String,
    pub tags: HashMap<String, String>,
}

/// A rule is a recursive tree of conditions.
#[derive(Debug, Clone)]
pub enum Rule {
    RequireTag { key: String },
    TagEquals { key: String, value: String },
    LocationIn(Vec<String>),
    Not(Box<Rule>),
    All(Vec<Rule>),
    Any(Vec<Rule>),
}

/// The verdict for one resource against one rule.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Compliance {
    Compliant,
    NonCompliant { reason: String },
}

fn main() {
    let resource = Resource {
        id: "res-1".to_string(),
        kind: "storage".to_string(),
        location: "westus2".to_string(),
        tags: HashMap::from([("env".to_string(), "prod".to_string())]),
    };
    let rule = Rule::RequireTag { key: "owner".to_string() };
    println!("{resource:?}\n{rule:?}");
}
```

Three things are worth noticing even now. `Rule` is a **single type with six shapes**, not an abstract
base class with six subclasses — that is an algebraic data type, and it changes how you model domains.
`Box<Rule>` appears in `Not` because a recursive type needs a known size, which is a question C# never
makes you ask, since every class reference is already a pointer. And `Compliance` carries data in one
variant but not the other, which is how Rust expresses "failed, and here's why" without exceptions.

If that code looks like it has a lot of `.to_string()` noise in it, you are noticing the `String` versus
`&str` distinction, which is module 04 and the most common early frustration in the language. It is not
noise; it is the ownership model showing through into the type system. By module 06 it will read as
information rather than ceremony.

## Before you move on

The idea to carry forward is that Rust is not a syntax with a strict compiler bolted on — it is a
language built around one central bet, which is that memory lifetime and aliasing can be proven
statically, and that if you prove them you get memory safety, deterministic destruction, and data-race
freedom together, with no runtime to pay for. Everything that feels difficult in the next ten modules
descends from that bet. The borrow checker is not a validation layer sitting on top of a normal
language; it *is* the language.

The second idea is calibration. Rust is a specialist tool with a real adoption cost, and a principal
engineer's job is to know the shape of the problems where that cost is repaid — no GC permitted,
tail-latency floors, dense memory economics, cross-language native libraries, untrusted input — and to
say so plainly when a proposed Rust project is none of those.

If you can explain to a skeptical colleague why Rust eliminates data races using the same rules it uses
for memory safety, and articulate a project at your own organisation where Rust would be a *bad* choice
and why, you are ready for the toolchain.

Next: [02 — The toolchain and project model](02-toolchain-and-cargo.md).

### Sources

- Matt Miller, Microsoft Security Response Center, *"Trends, Challenges, and Strategic Shifts in the Software Vulnerability Mitigation Landscape"* (BlueHat IL 2019). Materials via <https://github.com/microsoft/MSRC-Security-Research>. Origin of the widely cited ~70% memory-safety figure for Microsoft CVEs; establishes that memory-safety bugs dominate critical vulnerabilities in large C/C++ codebases.
- The Chromium Project, *"Memory safety"*. <https://www.chromium.org/Home/chromium-security/memory-safety/> — independently reports that around 70% of serious Chromium security bugs are memory-safety problems, corroborating the MSRC figure from a separate codebase.
- *The Rustonomicon*, "Races". <https://doc.rust-lang.org/nomicon/races.html> — states precisely what Rust does and does not prevent: safe Rust prevents data races; it does not prevent general race conditions or deadlocks.
- *The Rust Programming Language* ("The Book"), ch. 13.4, "Comparing Performance: Loops vs. Iterators". <https://doc.rust-lang.org/book/ch13-04-performance.html> — the standard reference for the claim that iterator adaptors compile to loop-equivalent code.
- *The Book*, Introduction. <https://doc.rust-lang.org/book/ch00-00-introduction.html> — the project's own framing of who Rust is for and which audiences it targets.
