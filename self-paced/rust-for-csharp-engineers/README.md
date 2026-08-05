# Rust for C# Engineers — A Self-Paced Study Book

A complete course on Rust for senior .NET engineers, from ownership to a shipped CLI. Twenty-nine narrative modules, a twenty-chapter offline exercise companion with full worked answers, and a real capstone crate you can build and break.

This is written as a **book**, not a syntax reference. It assumes you already know what a closure is, how generics work, why `IDisposable` exists, and what `async`/`await` compiles into. What it does not assume is that any of that knowledge transfers cleanly — because in several important places it doesn't, and the places where your instincts are *almost* right are exactly where you will lose a day to the borrow checker. Every module contrasts Rust with the nearest C# analogue and then tells you where the analogy breaks down. That second half is the valuable part.

**Start reading:** [`00-START-HERE.md`](./00-START-HERE.md) · **Get set up:** [`SETUP.md`](./SETUP.md)

---

## What you will be able to do

By the end of this book you should be able to predict what moves, what copies, and when a value is dropped, without running the program; win a borrow-checker fight by restructuring the code rather than cloning your way out of it; model a domain with algebraic data types and exhaustive `match` instead of a class hierarchy, and feel the compiler catch the case you forgot; choose deliberately between static dispatch and `dyn Trait`, and navigate the orphan rule when a blanket impl won't let you do the obvious thing; write iterator pipelines with LINQ fluency but Rust semantics, including the `collect::<Result<Vec<_>, _>>()` trick that has no C# equivalent; design error types with `thiserror` for a library and `anyhow` for a binary, and know precisely why that split exists; reach for `Box`, `Rc`, `Arc`, `RefCell` and `Mutex` correctly and — more importantly — rarely; explain why Rust ships no async runtime, and write correct Tokio code including graceful shutdown and the `Send` bounds that bite; drive `serde` through the awkward wire formats, not just the tidy ones; instrument a service with `tracing` spans rather than log lines; call Rust from C# across a C ABI and say exactly what `unsafe` does and does not promise; and read someone else's Rust and recognise whether it is idiomatic or merely compiling.

## Prerequisites

You need to be a working C# developer with real production experience — comfortable with generics, LINQ, `async`/`await`, `IDisposable`, nullable reference types, records and pattern matching. The book leans on that knowledge constantly, and its explanations are shaped as deltas from it rather than as first principles.

You do not need prior Rust exposure, and you do not need C or C++. Manual memory management is explained from scratch, because the point of Rust is that you get its safety guarantees without having had to learn the unsafe version first. You do not need a particular operating system; everything works on Windows, macOS and Linux, and [`SETUP.md`](./SETUP.md) covers all three.

You need about an hour of setup, most of which is a download. See [`SETUP.md`](./SETUP.md).

## Module index

The estimates below assume you read actively — typing the examples rather than skimming them — and do the drill chapter for each module. Halve them if you only read.

### Part 1 — Foundations and the language core

| # | Module | What it makes you able to do | Time |
|---|---|---|---|
| 01 | [Why Rust exists](./01-why-rust.md) | Judge honestly when Rust beats C# and when it doesn't | 45 min |
| 02 | [The toolchain and project model](./02-toolchain-and-cargo.md) | Drive rustup, cargo, clippy, rustfmt, and read docs.rs fluently | 1 h |
| 03 | [Syntax orientation](./03-syntax-orientation.md) | Read Rust without stumbling; understand expressions, shadowing, overflow | 1.5 h |
| 04 | [Strings, slices, and `Vec`](./04-strings-and-slices.md) | Stop fighting `String` vs `&str` — the single biggest early stumbling block | 1.5 h |
| 05 | [Ownership and moves](./05-ownership-and-moves.md) | Predict what moves, what copies, and when values are dropped | 2.5 h |
| 06 | [Borrowing and lifetimes](./06-borrowing-and-lifetimes.md) | Win borrow-checker fights instead of cloning your way out | 3.5 h |
| 07 | [Structs, enums, and pattern matching](./07-structs-enums-matching.md) | Model domains with algebraic data types instead of class hierarchies | 2 h |
| 08 | [Traits and generics](./08-traits-and-generics.md) | Use static and dynamic dispatch deliberately; navigate the orphan rule | 3 h |
| 09 | [The standard traits](./09-standard-traits.md) | Implement `From`, `Display`, `Deref`, `Drop` and friends idiomatically | 2 h |
| 10 | [Collections and iterators](./10-collections-and-iterators.md) | Write iterator pipelines with LINQ fluency and Rust semantics | 2 h |
| 11 | [Error handling](./11-error-handling.md) | Design with `Option`/`Result`/`?` instead of exceptions | 2.5 h |
| 12 | [Smart pointers and interior mutability](./12-smart-pointers.md) | Reach for `Box`/`Rc`/`Arc`/`RefCell` correctly — and rarely | 2 h |
| 13 | [Modules, crates, and workspaces](./13-modules-and-crates.md) | Lay out real projects; understand features and semver | 1.5 h |
| 14 | [Testing and documentation](./14-testing-and-docs.md) | Write unit, integration, and doc tests; benchmark and property-test | 1.5 h |
| 15 | [Concurrency](./15-concurrency.md) | Use threads, channels, and rayon with `Send`/`Sync` confidence | 2.5 h |
| 16 | [Async Rust and Tokio](./16-async-and-tokio.md) | Understand why Rust has no built-in runtime and write correct async code | 3.5 h |
| 17 | [Unsafe, FFI, and .NET interop](./17-unsafe-ffi-interop.md) | Call Rust from C# and know exactly what `unsafe` does and doesn't promise | 2 h |

**Part 1 subtotal: about 35 hours.**

### Part 2 — The ecosystem

| # | Module | What it makes you able to do | Time |
|---|---|---|---|
| 18 | [clap](./18-clap.md) | Build real CLIs with derive, subcommands, validation, completions | 1.5 h |
| 19 | [anyhow and thiserror](./19-anyhow-and-thiserror.md) | Apply the "thiserror for libraries, anyhow for binaries" rule properly | 1.5 h |
| 20 | [serde](./20-serde.md) | Model any wire format, including the awkward ones | 2.5 h |
| 21 | [Tokio in practice](./21-tokio-in-practice.md) | Configure runtimes, do async I/O, shut down gracefully | 2 h |
| 22 | [reqwest and axum](./22-reqwest-and-axum.md) | Call HTTP services and build one | 2 h |
| 23 | [tracing and logging](./23-tracing-and-logging.md) | Instrument with spans and structured fields, not `Console.WriteLine` | 1.5 h |
| 24 | [Configuration](./24-configuration.md) | Layer files, environment, and flags the way `IConfiguration` does | 1 h |
| 25 | [Database access with sqlx](./25-sqlx.md) | Run compile-time-checked SQL and understand the tradeoff vs EF Core | 1.5 h |
| 26 | [A field guide to the crate ecosystem](./26-crate-field-guide.md) | Know which crate to reach for, and which are traps | 1.5 h |

**Part 2 subtotal: 15 hours.**

### Part 3 — Putting it together

| # | Module | What it makes you able to do | Time |
|---|---|---|---|
| 27 | [Capstone: building `polcheck`](./27-capstone-polcheck.md) | Ship a complete, tested, packaged CLI end to end | 4 h |
| 28 | [Idioms, patterns, and anti-patterns](./28-idioms-and-antipatterns.md) | Write Rust that Rust programmers recognise as good | 2.5 h |
| 29 | [Reference](./29-reference.md) | Look things up: glossary, C#↔Rust table, sources | — |

**Part 3 subtotal: about 6.5 hours.**

**Total: roughly 55–60 hours** of active study. A determined engineer doing an hour a night gets through it in about two months.

## The exercise companion

Every teaching module has a matching drill chapter in [`exercises/`](./exercises/00-HOW-TO-USE.md), with a separate answer book per chapter under [`exercises/answers/`](./exercises/answers/). Read [`exercises/00-HOW-TO-USE.md`](./exercises/00-HOW-TO-USE.md) before starting.

Each chapter has two parts. **Part A** is a written questionnaire of six conceptual questions to answer from memory with the module closed — this is the part most people skip and the part that most reliably reveals what you only think you understood. **Part B** is a coding exercise: a source file whose types, traits and signatures are all present and whose bodies are `todo!()`, sitting above a test module that *is* the specification. You work until `cargo test` is green.

Do the drills. Rust is a language where reading code creates a dangerous illusion of understanding — the borrow checker only teaches you when it is rejecting *your* program. Every worked answer was extracted programmatically from a file that compiled, passed `cargo test`, and passed `cargo clippy -- -D warnings` before it shipped.

The companion is deliberately offline. Part 1's fourteen chapters live in a cargo project with an empty `[dependencies]` section, so `cargo test` needs nothing installed and no network at all. Part 2's six chapters necessarily pull in the crates they teach; run `cargo build` once while connected and everything afterwards works with `--offline`.

Three chapters in Part 1 deliberately do not compile until you fix them, and the compiler's message *is* the exercise. That is documented up front so you don't mistake it for a broken download.

## What ships alongside the prose

[`code/polcheck/`](./code/polcheck/) is the capstone as a real crate — the program [Module 27](./27-capstone-polcheck.md) walks through, built from clap, anyhow, thiserror, serde, tokio, reqwest, figment and tracing together, with 29 tests of its own and clean under `cargo clippy -- -D warnings` and `cargo fmt --check`. Build it, run it, then break it deliberately and watch what the compiler says.

## Suggested pacing

Roughly 57 hours of active study, plus the drills. Three sensible schedules:

**Thorough (12 weeks, ~5 hours/week).** One module per session, two or three sessions a week, with the drill immediately after each. Modules 06, 08 and 16 deserve two sessions each — borrowing, trait dispatch and async are the three places where slowing down pays most. This is the schedule the book is designed for.

**Intensive (2 weeks, full-time).** Two to three modules a day with every drill. Do not skip Part A of the drills; under time pressure it is the first thing to go and the thing whose absence you will feel when you hit the capstone.

**Survey (two evenings).** Read modules [01](./01-why-rust.md), [05](./05-ownership-and-moves.md), [06](./06-borrowing-and-lifetimes.md), [11](./11-error-handling.md) and [28](./28-idioms-and-antipatterns.md), and do the drills for 05 and 06. You will not be productive in Rust, but you will understand what the language is actually asking of you and why other people's Rust looks the way it does.

Whichever you pick, read Part 1 in order. It is a single argument rather than a set of independent topics — module 06 does not make sense without module 05. Part 2's chapters are far more independent and can be read on demand, though `serde` and error handling turn up everywhere. When something does not click, the fix is almost always to go back one module rather than forward.

## Sources

Every module ends with a `### Sources` block, and the entries are not equally authoritative. The book cites in four tiers — normative specifications, official teaching material, crate documentation, and practitioner writing — and [`00-START-HERE.md`](./00-START-HERE.md) explains how much weight to give each. The short version: when the [Rust Reference](https://doc.rust-lang.org/reference/) or the [standard library docs](https://doc.rust-lang.org/std/) contradict anything here, they are right and this book is wrong.

A standing warning that recurs through Part 2: a great deal of Rust material on the open web is written against older versions of the crates it describes, and several of them have had recent breaking changes. [`SETUP.md`](./SETUP.md) pins every version this book was verified against. When a snippet from a blog post doesn't compile, suspect the version before you suspect yourself.

## What this book will not do

It will not tell you Rust is better than C#. For an enormous fraction of the software you have written — line-of-business services, orchestration, anything where developer throughput dominates and a few hundred megabytes of RAM is free — C# is the better tool and Rust would be a costly mistake. [Module 01](./01-why-rust.md) is largely about being honest on this point, because engineers who adopt Rust for the wrong reasons tend to conclude that the borrow checker is the problem, when the real problem was the choice.

## License

Prose and code in this directory are provided under the repository's [LICENSE](../../LICENSE). All cited works belong to their respective authors; links are provided so you can read the originals.
