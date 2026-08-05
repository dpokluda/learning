# 00 — Start here

There are two kinds of Rust material available to you right now, and they fail in opposite directions. The first teaches Rust as though you have never programmed: three chapters on what a variable is before anything interesting happens. The second assumes you are coming from C++, and spends its energy on the things a C++ programmer finds surprising — which is a different list from the things that surprise you. This book is aimed precisely at the gap: you are a senior engineer, you have shipped real systems, and you know a great deal that is *almost* true in Rust.

That "almost" is the organizing idea. Most of what you know transfers. Generics transfer, with a different implementation strategy and a stricter contract. Pattern matching transfers, and gets better. `async`/`await` transfers syntactically and then diverges sharply once you ask who is running the state machine. Interfaces become traits, and then traits keep going into territory C# has no word for. But a handful of things do not transfer at all, and they are all downstream of one decision: **Rust has no garbage collector, and it makes the compiler prove memory safety instead.** Ownership, moves, borrowing, lifetimes, `Send`/`Sync`, `Drop`, why `String` and `&str` are different types, why `Rc<RefCell<T>>` exists and why reaching for it is usually a mistake — every one of those is a consequence of that single decision. Understand the decision and the language stops feeling arbitrary.

The book's bet is that the fastest route for someone with your background is to name the C# analogue every single time, and then say where it breaks. A partial analogy left unqualified is worse than none, because it will hold for a week and then fail on a Friday afternoon.

## Who this book assumes you are

You are a working C# developer with production experience. You are comfortable with generics, LINQ, `async`/`await`, `IDisposable`, nullable reference types, records, and pattern matching, and you have opinions about at least two of them. You have probably wondered what it would be like to not need a GC, and you may have read that Rust is difficult without getting a clear account of *which part* is difficult.

You do not need C or C++. You do not need prior Rust. You do not need a particular OS. What you do need is a tolerance for being told, several times, that an instinct you have relied on for a decade does not apply here — and the willingness to type the examples rather than read them, because the borrow checker only teaches you when it is rejecting *your* program.

[`SETUP.md`](./SETUP.md) gets you a working toolchain in about half an hour.

## How the book is arranged

The modules build strictly on one another, and Part 1 in particular is a single argument rather than a set of independent topics — module 06 does not make sense without module 05. Read Part 1 in order. Part 2's chapters are far more independent and can be read on demand, though error handling and `serde` turn up everywhere.

Time estimates assume active reading — typing the examples rather than skimming them — plus the drill chapter for that module. Halve them if you only read.

### Part 1 — Foundations and the language core

| # | Module | What you'll be able to do afterwards | Time |
|---|---|---|---|
| [01](./01-why-rust.md) | Why Rust exists | Judge honestly when Rust beats C# and when it doesn't | 45 min |
| [02](./02-toolchain-and-cargo.md) | The toolchain and project model | Drive rustup, cargo, clippy, rustfmt, and read docs.rs fluently | 1 h |
| [03](./03-syntax-orientation.md) | Syntax orientation | Read Rust without stumbling; understand expressions, shadowing, overflow | 1.5 h |
| [04](./04-strings-and-slices.md) | Strings, slices, and `Vec` | Stop fighting `String` vs `&str` — the single biggest early stumbling block | 1.5 h |
| [05](./05-ownership-and-moves.md) | Ownership and moves | Predict what moves, what copies, and when values are dropped | 2.5 h |
| [06](./06-borrowing-and-lifetimes.md) | Borrowing and lifetimes | Win borrow-checker fights instead of cloning your way out | 3.5 h |
| [07](./07-structs-enums-matching.md) | Structs, enums, and pattern matching | Model domains with algebraic data types instead of class hierarchies | 2 h |
| [08](./08-traits-and-generics.md) | Traits and generics | Use static and dynamic dispatch deliberately; navigate the orphan rule | 3 h |
| [09](./09-standard-traits.md) | The standard traits | Implement `From`, `Display`, `Deref`, `Drop` and friends idiomatically | 2 h |
| [10](./10-collections-and-iterators.md) | Collections and iterators | Write iterator pipelines with LINQ fluency and Rust semantics | 2 h |
| [11](./11-error-handling.md) | Error handling | Design with `Option`/`Result`/`?` instead of exceptions | 2.5 h |
| [12](./12-smart-pointers.md) | Smart pointers and interior mutability | Reach for `Box`/`Rc`/`Arc`/`RefCell` correctly — and rarely | 2 h |
| [13](./13-modules-and-crates.md) | Modules, crates, and workspaces | Lay out real projects; understand features and semver | 1.5 h |
| [14](./14-testing-and-docs.md) | Testing and documentation | Write unit, integration, and doc tests; benchmark and property-test | 1.5 h |
| [15](./15-concurrency.md) | Concurrency | Use threads, channels, and rayon with `Send`/`Sync` confidence | 2.5 h |
| [16](./16-async-and-tokio.md) | Async Rust and Tokio | Understand why Rust has no built-in runtime and write correct async code | 3.5 h |
| [17](./17-unsafe-ffi-interop.md) | Unsafe, FFI, and .NET interop | Call Rust from C# and know exactly what `unsafe` does and doesn't promise | 2 h |

**Part 1 subtotal: about 35 hours.**

### Part 2 — The ecosystem

| # | Module | What you'll be able to do afterwards | Time |
|---|---|---|---|
| [18](./18-clap.md) | clap | Build real CLIs with derive, subcommands, validation, completions | 1.5 h |
| [19](./19-anyhow-and-thiserror.md) | anyhow and thiserror | Apply the "thiserror for libraries, anyhow for binaries" rule properly | 1.5 h |
| [20](./20-serde.md) | serde | Model any wire format, including the awkward ones | 2.5 h |
| [21](./21-tokio-in-practice.md) | Tokio in practice | Configure runtimes, do async I/O, shut down gracefully | 2 h |
| [22](./22-reqwest-and-axum.md) | reqwest and axum | Call HTTP services and build one | 2 h |
| [23](./23-tracing-and-logging.md) | tracing and logging | Instrument with spans and structured fields, not `Console.WriteLine` | 1.5 h |
| [24](./24-configuration.md) | Configuration | Layer files, environment, and flags the way `IConfiguration` does | 1 h |
| [25](./25-sqlx.md) | Database access with sqlx | Run compile-time-checked SQL and understand the tradeoff vs EF Core | 1.5 h |
| [26](./26-crate-field-guide.md) | A field guide to the crate ecosystem | Know which crate to reach for, and which are traps | 1.5 h |

**Part 2 subtotal: 15 hours.**

### Part 3 — Putting it together

| # | Module | What you'll be able to do afterwards | Time |
|---|---|---|---|
| [27](./27-capstone-polcheck.md) | Capstone: building `polcheck` | Ship a complete, tested, packaged CLI end to end | 4 h |
| [28](./28-idioms-and-antipatterns.md) | Idioms, patterns, and anti-patterns | Write Rust that Rust programmers recognise as good | 2.5 h |
| [29](./29-reference.md) | Reference | Look things up: glossary, C#↔Rust table, sources | — |

**Part 3 subtotal: about 6.5 hours.**

**Total: roughly 55–60 hours** of active study. An hour a night gets you through in about two months. [`README.md`](./README.md) has three worked pacing schedules if you want a plan rather than a number.

The [exercise companion](./exercises/00-HOW-TO-USE.md) runs alongside, one chapter per teaching module, each pairing a written questionnaire with a coding exercise. Answers live in a [separate folder](./exercises/answers/) so that peeking requires a deliberate act.

## If you only have a few hours

Read modules [01](./01-why-rust.md), [05](./05-ownership-and-moves.md), [06](./06-borrowing-and-lifetimes.md), [11](./11-error-handling.md) and [28](./28-idioms-and-antipatterns.md), in that order. That is the minimum viable mental-model transplant: why Rust exists, ownership, borrowing, error handling, and the summary of mental-model shifts. Everything else in the book is elaboration on those five.

If you have one hour rather than several, read [05](./05-ownership-and-moves.md) alone. Ownership is the idea whose absence makes every other part of Rust look like gratuitous difficulty, and whose presence makes them look inevitable.

## The running example

One example is threaded through the entire book: **`polcheck`**, a governance and compliance CLI. It reads a set of resource records and a set of rules, evaluates one against the other, and reports what is compliant and what isn't. By the capstone it fetches rule bundles over HTTP, is configurable, is instrumented with structured logging, is tested, and ships as a single binary.

The domain is deliberately small enough to hold in your head and rich enough to motivate every feature in the book. Rules form a recursive tree, which forces algebraic data types and pattern matching. Evaluation walks borrowed data, which forces you to understand borrowing. Rule sets get shared across threads, which forces `Arc` and `Send`/`Sync`. Parsing rules can fail in structured ways, which forces `Result` and real error design. You will meet the same four types in module 03 that you meet again in module 27, and watching them acquire capabilities is a large part of the point.

## What to trust, and how much

Not all sources deserve equal weight, and part of becoming competent in a new ecosystem is developing that discrimination quickly. Every module ends with a `### Sources` block, and the entries fall into four tiers.

**Tier 1 — normative.** The [Rust Reference](https://doc.rust-lang.org/reference/), the [standard library API docs](https://doc.rust-lang.org/std/), and the [Rustonomicon](https://doc.rust-lang.org/nomicon/) for unsafe code. These describe what the language *is*. When they contradict anything in this book, they are right and this book is wrong.

**Tier 2 — official teaching material.** [The Book](https://doc.rust-lang.org/book/), [Rust by Example](https://doc.rust-lang.org/rust-by-example/), the [Async Book](https://rust-lang.github.io/async-book/), and the [API Guidelines](https://rust-lang.github.io/api-guidelines/). Authoritative and pedagogical, but occasionally simplified or slightly behind the compiler.

**Tier 3 — crate documentation.** [docs.rs](https://docs.rs) pages and official crate sites like [serde.rs](https://serde.rs) and [tokio.rs](https://tokio.rs). Authoritative *for that crate at that version* — which is exactly why the version pin in [`SETUP.md`](./SETUP.md) matters, and why so much web material about these crates is quietly wrong.

**Tier 4 — practitioner writing.** Blog posts and RFC discussions, cited only for design rationale and always corroborated against a higher tier.

Where the community genuinely disagrees — on how much `unsafe` is acceptable, on whether async Rust's ergonomics are acceptable yet, on how much to lean on `Arc<Mutex<T>>` in application code — the book says so rather than manufacturing a consensus.

## A note on how to read this

Type the code rather than copying it, and let it fail. Rust is unusual in how much of its teaching is delivered by the compiler: a borrow-checker error is not an obstacle between you and the program, it is the explanation, and reading the full message including the notes and the suggestion is a skill worth building deliberately in the first week. Run `cargo clippy` habitually, because a large fraction of "how do Rust programmers actually write this?" is answered by reading what it suggests.

Do the drills before reading the answers; the failure is where the learning is. And when something does not click, the fastest fix is almost always to go back one module rather than forward one, because in this subject confusion is nearly always a missing prerequisite rather than a hard present idea. If you find yourself fighting the borrow checker in module 12, the problem is module 06.

Start with [Module 01](./01-why-rust.md).
