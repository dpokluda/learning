# 29 — Reference: glossary, FAQ, and sources

This module is not meant to be read straight through. It is the part of the book you come back to: a
C#-to-Rust translation table, a glossary of the vocabulary, answers to the questions that recur, and the
consolidated source list.

> **Prerequisite:** none — dip in whenever.

## The C# → Rust translation table

The single densest thing in the book. Read the "where the analogy breaks" column carefully; that is where the
bugs live.

| C# / .NET | Rust | Where the analogy breaks |
|---|---|---|
| `class` (reference type) | `struct` + `impl` | Rust structs are values; heap allocation is explicit via `Box` |
| `struct` (value type) | `struct` | Rust structs move by default; `Copy` makes them duplicate |
| `interface` | `trait` | Traits can be implemented for types you don't own, and can carry default methods and associated types |
| abstract class | trait with default methods | No fields in traits, and no implementation inheritance |
| `IDisposable` / `using` | `Drop` | Automatic at scope exit, cannot be forgotten, cannot be async |
| finalizer | *nothing* | No GC, so no finalization queue |
| `null` | `Option<T>` | A real type you must destructure, not an annotation |
| nullable reference types | `Option<T>` | `Option` is enforced by the type checker, not a warning |
| exceptions / `try`-`catch` | `Result<T, E>` + `?` | Errors are ordinary return values; no unwinding through your code |
| `finally` | `Drop` | Runs on both success and error paths, without a keyword |
| `TryParse(out x)` | `-> Result<T, E>` or `Option<T>` | The value comes back in the return, not an `out` parameter |
| `InnerException` | `#[source]` / `anyhow` context chain | Chain is built by the caller adding context, not by the thrower |
| `IEnumerable<T>` | `Iterator` | Lazy in both, but Rust's compiles to a loop with no virtual dispatch |
| LINQ | iterator adaptors | No `IQueryable`, no expression trees, no remote translation |
| `List<T>` | `Vec<T>` | — |
| `T[]` | `[T; N]` (fixed) or `&[T]` (slice) | Array size is part of the type |
| `Dictionary<K,V>` | `HashMap<K,V>` | Not ordered; `BTreeMap` when you need sorted iteration |
| `SortedDictionary<K,V>` | `BTreeMap<K,V>` | — |
| `HashSet<T>` | `HashSet<T>` | — |
| `Queue<T>` / `Stack<T>` | `VecDeque<T>` / `Vec<T>` | — |
| `ConcurrentDictionary<K,V>` | `dashmap::DashMap` | Not in `std`; sharded, with a nesting deadlock hazard |
| `string` | `String` (owned) / `&str` (borrowed) | Two types, not one; UTF-8 bytes, not UTF-16 |
| `StringBuilder` | `String` with `push_str` | `String` is already a growable buffer |
| `object` | `dyn Any` (rare) | Almost never used; generics or enums instead |
| generics | generics | Monomorphised, so no reflection over `T` and no `typeof(T)` |
| `where T : IComparable` | `where T: Ord` | Traits can be implemented externally, so bounds are more flexible |
| extension methods | trait + `impl` for a foreign type | Can satisfy a trait bound; extension methods cannot satisfy an interface |
| `ref` / `out` | `&mut T` | Lifetime-checked; you cannot store one beyond its validity |
| `in` parameter | `&T` | — |
| GC | ownership + `Drop` | Deterministic, no pauses, and enforced at compile time |
| `WeakReference<T>` | `Weak<T>` | Paired with `Rc`/`Arc`, not with the GC |
| `Lazy<T>` | `LazyLock<T>` / `OnceLock<T>` | In `std` since 1.80 / 1.70 |
| `lock` statement | `Mutex<T>` | The lock *owns* the data, so you cannot access it unlocked |
| `ReaderWriterLockSlim` | `RwLock<T>` | — |
| `Interlocked` | `std::sync::atomic` | Explicit memory ordering required |
| `Task<T>` | `Future<Output = T>` | Lazy — does nothing until polled or spawned |
| `async` / `await` | `async` / `.await` | No built-in runtime; you choose and start one |
| `Task.Run` | `tokio::spawn` | Requires a running runtime |
| `CancellationToken` | `tokio_util::sync::CancellationToken`, or drop the future | Dropping a future cancels it — no cooperative check needed |
| `SynchronizationContext` | *nothing* | No context capture; `Send` bounds instead |
| `IAsyncEnumerable<T>` | `Stream` (from `futures`) | Not in `std` |
| `System.Threading.Channels` | `std::sync::mpsc`, `tokio::sync::mpsc`, `crossbeam` | Pick by runtime |
| PLINQ | `rayon` | Data races are a compile error, not a caution |
| thread-safety by convention | `Send` / `Sync` | Checked by the compiler |
| assembly (`.dll`) | crate | A crate is closer to a C# *project*; one compilation unit |
| NuGet package | crate on crates.io | Published versions are immutable and never deleted |
| `namespace` | `mod` | Modules follow the file tree and control visibility |
| `internal` | `pub(crate)` | — |
| `public` | `pub` | Private is the default at every level |
| `.csproj` | `Cargo.toml` | Declarative; no MSBuild targets or imports |
| `packages.lock.json` | `Cargo.lock` | Committed for binaries, not for libraries |
| MSBuild targets | `build.rs` | An ordinary Rust program run before the build |
| conditional compilation symbols | `#[cfg(...)]` + features | Features are additive by contract |
| xUnit / NUnit | `#[test]` + `cargo test` | Built in; tests can live beside the code and see privates |
| `InternalsVisibleTo` | a child `#[cfg(test)] mod` | Unnecessary — child modules already see parents' privates |
| BenchmarkDotNet | `criterion` | — |
| FsCheck | `proptest` / `quickcheck` | — |
| XML doc comments | `///` markdown | Doc examples are compiled and run as tests |
| Roslyn analyzers | `clippy` | One community-standard lint set |
| `dotnet format` | `rustfmt` | Effectively no configuration debate |
| source generators | `#[derive]` and macros | Operate on the syntax tree at compile time |
| `System.Text.Json` | `serde` + `serde_json` | Compile-time codegen, no reflection |
| `System.CommandLine` | `clap` | — |
| `ILogger<T>` | `tracing` | Spans, and no DI — a global subscriber instead |
| `IConfiguration` | `config` / `figment` | No reload-on-change, no DI |
| `HttpClient` | `reqwest::Client` | Both should be created once and reused |
| ASP.NET Core minimal APIs | `axum` | — |
| EF Core | `sqlx` (Dapper-like) or SeaORM | sqlx checks SQL at compile time |
| Dapper | `sqlx` | — |
| `Guid` | `uuid::Uuid` | Not in `std` |
| `DateTime` / `DateTimeOffset` | `chrono::DateTime<Tz>` / `time` | Timezone is in the type, not a `Kind` enum |
| `TimeSpan` | `std::time::Duration` (unsigned) / `chrono::Duration` (signed) | `std`'s cannot be negative |
| `Stopwatch` | `std::time::Instant` | — |
| `Regex` | `regex` | Linear time guaranteed; no backreferences or lookaround |
| `Random` | `rand` | Not in `std` |
| `P/Invoke` / `DllImport` | `extern "C"` + `unsafe` | Edition 2024 requires `unsafe extern` and `#[unsafe(no_mangle)]` |

## Glossary

**Algebraic data type (ADT).** A type formed by combining others: a `struct` is a *product* (all fields at
once), an `enum` is a *sum* (exactly one variant). C#'s nearest equivalent is a class hierarchy or the
records-plus-switch discriminated-union workaround.

**Associated type.** A type placeholder inside a trait, fixed by each implementor — `type Item` on
`Iterator`. Use it when there is exactly one sensible choice per implementor; use a generic parameter when a
type could implement the trait several ways.

**Blanket implementation.** An `impl<T: Bound> Trait for T` covering every type meeting a bound. `impl<T:
Display> ToString for T` is the canonical one. There is no C# equivalent, because you cannot retroactively
make types implement an interface.

**Borrow.** A reference to a value you do not own. Shared (`&T`) borrows may coexist; a mutable (`&mut T`)
borrow is exclusive.

**Borrow checker.** The compiler pass that verifies references never outlive their referents and that
aliasing rules hold.

**Coherence / orphan rule.** You may implement a trait for a type only if you own the trait or the type. This
prevents two crates providing conflicting impls, and it is why the newtype pattern is so common.

**Crate.** The unit of compilation and of publication — a library or a binary. Closer to a C# *project* than
to a NuGet package.

**Dangling reference.** A reference to freed memory. Impossible in safe Rust.

**Dynamically sized type (DST).** A type whose size is unknown at compile time, such as `str` or `[T]`. Only
usable behind a pointer: `&str`, `Box<[T]>`.

**Edition.** An opt-in language dialect (2015, 2018, 2021, 2024) letting Rust make breaking syntax changes
without splitting the ecosystem. Crates of different editions interoperate freely — something C#'s
`LangVersion` does not fully achieve.

**Elision.** The rules letting you omit lifetime annotations in common cases.

**Fearless concurrency.** The claim that `Send`/`Sync` plus ownership make data races a compile error.

**Interior mutability.** Mutating through a shared reference, via `Cell`, `RefCell`, `Mutex`, or `RwLock`.
The rules move from compile time to runtime.

**Lifetime.** The compile-time region during which a reference is valid. `'a` names one; `'static` means the
whole program.

**Monomorphisation.** Generating a specialised copy of generic code per concrete type at compile time. The
reason generics are zero-cost and the reason there is no runtime type information.

**Move.** Transferring ownership. The source becomes unusable — the single biggest departure from C#.

**NLL (non-lexical lifetimes).** Borrows end at last use rather than at the closing brace, which is why a
great deal of natural-looking code compiles.

**Object safety / dyn compatibility.** The rules a trait must satisfy to be used as `dyn Trait`: no generic
methods, no `Self` return by value, and so on.

**Panic.** An unrecoverable error that unwinds (or aborts). For bugs, not for expected failure.

**Pin.** A guarantee that a value will not move in memory. Needed for self-referential futures.

**Send / Sync.** Auto-traits marking types safe to move between threads (`Send`) and safe to share by
reference across threads (`Sync`).

**Shadowing.** Rebinding a name with a new `let`, possibly of a different type. Not mutation.

**Slice.** A borrowed view into a contiguous sequence: `&[T]`, `&str`. Closest to `ReadOnlySpan<T>`.

**Trait object.** `dyn Trait` behind a pointer — a fat pointer of data plus vtable. The C# interface-dispatch
model, made explicit.

**Turbofish.** The `::<T>` syntax for supplying type arguments where inference cannot: `parse::<u32>()`.

**Zero-cost abstraction.** An abstraction that compiles to code no worse than the hand-written equivalent.
Iterators and generics qualify; `dyn Trait` deliberately does not.

## Frequently asked questions

**How long until I am productive?** For an experienced C# engineer: a week to read code, two to three weeks to
write it without constant compiler fights, two to three months to feel fluent. The borrow checker is the
whole learning curve, and it is front-loaded — everything after ownership is comparatively easy.

**Do I have to understand lifetimes?** You have to understand *borrowing*. Explicit `'a` annotations are
needed less often than you fear, because elision handles most cases. Write concrete types first, use owned
data when unsure, and add lifetimes when the compiler asks.

**Is Rust actually faster than C#?** For CPU-bound work, usually yes but not dramatically — a well-optimised
.NET program is within a small factor. The reliable wins are elsewhere: no GC pauses (so predictable tail
latency), lower and flatter memory use, near-zero startup time, and smaller deployment artifacts. If your
problem is throughput on a warm server, C# is competitive. If it is p99 latency, memory ceilings, cold start,
or shipping a binary with no runtime, Rust wins clearly.

**When should I not use Rust?** When the work is a line-of-business CRUD application on .NET rails — you will
be slower and gain nothing. When your team has no Rust experience and the deadline is short. When you need a
mature ecosystem for a specific domain and it does not exist there. And when the bottleneck is a database or a
network, in which case language performance is irrelevant.

**Can I call Rust from C#?** Yes, and module 17 walks through it end to end with a verified example. Compile
Rust as a `cdylib`, expose `extern "C"` functions, and call them with `LibraryImport`. The rules are the ones
you already know from native interop: match the ABI, own the memory-freeing discipline, and never let a panic
cross the boundary.

**What about a Rust GUI?** The weakest part of the ecosystem. `egui`, `iced`, `slint`, and Tauri all exist and
are all less mature than WPF or WinUI. If your program is a desktop application, this is a serious
consideration.

**Why is my binary so large?** Debug builds include full symbols. A release build with `lto = true`,
`codegen-units = 1`, and `strip = true` will be far smaller — the capstone's is 6.1 MB. That is still large
next to a `.dll` but small next to a self-contained .NET publish.

**Why is compilation slow?** Monomorphisation, LTO, and macro expansion all cost. Use `cargo check` during
development (much faster than a full build), keep generic parameters modest, and split large crates into a
workspace so only what changed rebuilds. It will still be slower than `dotnet build`; this is the most
frequently voiced complaint about the language, and it is fair.

**Do I need `unsafe`?** Almost certainly not. Application code should contain none. It exists for FFI and for
the handful of data structures that cannot be expressed in safe Rust, and those are mostly already written for
you in `std` and in well-audited crates.

**Which async runtime?** tokio, unless you have a specific reason. It has the ecosystem — `reqwest`, `axum`,
`sqlx`, and `tonic` all assume it. Choosing otherwise means fighting your dependencies.

**How do I do dependency injection?** You mostly do not. Pass dependencies as arguments, use generics for
compile-time substitution, and `Box<dyn Trait>` when you need runtime substitution. There are DI crates and
they are rarely used. This feels alien for about a month and then feels like a simplification.

**Is `unwrap()` always bad?** No. It is fine in tests, in prototypes, in `main`, and where you can prove the
invariant holds. It is bad in library code and in long-running services, where it decides to kill the process
on someone else's behalf. Prefer `expect` with a message naming the invariant.

## Reading path after this book

The Rust Book is worth reading even now — you will absorb it in an afternoon and it will fill gaps. Then
*Rust for Rustaceans* (Jon Gjengset) is the right second book: it assumes you have the basics and covers the
things this book had to compress, including variance, unsafe abstractions, and API evolution. Gjengset's
"Crust of Rust" video series is exceptional for the same material in a different form.

For the async model, the Async Book plus Alice Ryhl's blog posts on tokio internals are the best available
explanations. For unsafe and the memory model, the Rustonomicon — read it once, before you think you need it.
And for staying current, *This Week in Rust* is the ecosystem's newsletter of record.

The single most effective thing you can do, though, is write something. Pick a tool you would otherwise write
in C# — a log parser, a small service, a build helper — and write it in Rust. The compiler is a patient
teacher, and the fights you have with it are the curriculum.

## Consolidated sources

**Official language documentation**

- The Rust Programming Language ("The Book"). <https://doc.rust-lang.org/book/>
- The Rust Reference. <https://doc.rust-lang.org/reference/>
- The Rustonomicon (unsafe Rust). <https://doc.rust-lang.org/nomicon/>
- Rust by Example. <https://doc.rust-lang.org/rust-by-example/>
- The Standard Library API. <https://doc.rust-lang.org/std/>
- The Edition Guide. <https://doc.rust-lang.org/edition-guide/>
- The Cargo Book. <https://doc.rust-lang.org/cargo/>
- The rustdoc Book. <https://doc.rust-lang.org/rustdoc/>
- The rustup Book. <https://rust-lang.github.io/rustup/>
- Asynchronous Programming in Rust. <https://rust-lang.github.io/async-book/>
- Rust API Guidelines. <https://rust-lang.github.io/api-guidelines/>
- Clippy lint index. <https://rust-lang.github.io/rust-clippy/master/>
- The Rustc Book (lints, targets, codegen). <https://doc.rust-lang.org/rustc/>

**Crate documentation** (each pinned to the version this book was verified against)

- anyhow. <https://docs.rs/anyhow/1/anyhow/>
- axum. <https://docs.rs/axum/0.8/axum/>
- bytes. <https://docs.rs/bytes/1/bytes/>
- chrono. <https://docs.rs/chrono/0.4/chrono/>
- clap. <https://docs.rs/clap/4/clap/>
- clap_complete. <https://docs.rs/clap_complete/4/clap_complete/>
- config. <https://docs.rs/config/0.15/config/>
- criterion. <https://docs.rs/criterion/0.8/criterion/>
- crossbeam-channel. <https://docs.rs/crossbeam-channel/0.5/crossbeam_channel/>
- dashmap. <https://docs.rs/dashmap/6/dashmap/>
- figment. <https://docs.rs/figment/0.10/figment/>
- itertools. <https://docs.rs/itertools/0.15/itertools/>
- parking_lot. <https://docs.rs/parking_lot/0.12/parking_lot/>
- proptest. <https://docs.rs/proptest/1/proptest/>
- rand. <https://docs.rs/rand/0.10/rand/>
- rayon. <https://docs.rs/rayon/1/rayon/>
- regex. <https://docs.rs/regex/1/regex/>
- reqwest. <https://docs.rs/reqwest/0.13/reqwest/>
- serde. <https://serde.rs/> and <https://docs.rs/serde/1/serde/>
- serde_json. <https://docs.rs/serde_json/1/serde_json/>
- sqlx. <https://docs.rs/sqlx/0.9/sqlx/>
- thiserror. <https://docs.rs/thiserror/2/thiserror/>
- tokio. <https://tokio.rs/> and <https://docs.rs/tokio/1/tokio/>
- tokio-util. <https://docs.rs/tokio-util/0.7/tokio_util/>
- tracing. <https://docs.rs/tracing/0.1/tracing/>
- tracing-subscriber. <https://docs.rs/tracing-subscriber/0.3/tracing_subscriber/>
- uuid. <https://docs.rs/uuid/1/uuid/>

**Ecosystem and discovery**

- crates.io. <https://crates.io/>
- docs.rs — generated documentation for every published crate. <https://docs.rs/>
- lib.rs — an alternative index with better categorisation. <https://lib.rs/>
- blessed.rs — an opinionated guide to which crate to use. <https://blessed.rs/crates>
- RustSec advisory database. <https://rustsec.org/>
- This Week in Rust. <https://this-week-in-rust.org/>

**Comparison points in .NET**

- .NET Framework Design Guidelines. <https://learn.microsoft.com/dotnet/standard/design-guidelines/>
- Asynchronous programming in C#. <https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/>
- Configuration in .NET. <https://learn.microsoft.com/dotnet/core/extensions/configuration>
- Logging in .NET. <https://learn.microsoft.com/dotnet/core/extensions/logging>
- Native interoperability. <https://learn.microsoft.com/dotnet/standard/native-interop/>

## Before you move on

There is nowhere left to move on to — this is the end of the book. What you have is a working model of a
language that makes different trade-offs from the one you know best: ownership instead of garbage collection,
values instead of exceptions, compile-time checks instead of runtime discipline, and explicitness instead of
ambient machinery.

The most useful thing you can carry forward is not a fact from the translation table but a habit: when Rust
refuses to compile something you would have written without thinking in C#, treat that as information rather
than obstruction. Nearly every borrow-checker fight is the compiler pointing at an ownership question your C#
version answered implicitly and possibly wrongly. Answering it explicitly is the work, and it is also the
payoff.

Go and write something.

### Sources

All sources are consolidated in the section above. Individual claims throughout the book carry their own
footnotes in each module's `### Sources` block.
