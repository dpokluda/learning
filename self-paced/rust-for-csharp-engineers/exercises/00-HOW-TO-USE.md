# How to use the exercise companion

The book teaches; this companion is where you find out whether it took. It is
built around a simple loop, and the loop only works if you run it in order.

**Answer Part A from memory, in writing, before you open any code.** Six
questions per chapter, all of them about the ideas rather than the syntax. The
point of writing the answers out rather than thinking them is that writing
exposes the places where you have a vague shape instead of a model — and those
places are exactly what a C#-shaped intuition tends to leave behind when it maps
onto Rust. If you find yourself writing "it's basically like `IDisposable`", stop
and ask where the analogy breaks, because the answer book will.

**Then do Part B in your editor.** Each chapter of the drill projects is a Rust
source file whose types, traits and signatures are all present and whose bodies
are `todo!()`. Below them sits a test module that is the specification: read it
before you write anything, because it states precisely what each function must
do, down to the exact wording of error messages. Work until `cargo test` is
green for that chapter.

**Only then read the answer book.** Every worked solution in `answers/` was
extracted programmatically from a source file that compiled, passed `cargo test`,
and passed `cargo clippy -- -D warnings` on the pinned toolchain — so what you
are comparing yourself against is real, running code rather than something
plausible-looking. If your solution differs but passes, that is usually fine and
occasionally better; if you fumbled either half, go back and reread the module
before moving on.

## The two projects

The companion ships two cargo projects, and the split matters.

| Project | Chapters | Dependencies | Network |
|---|---|---|---|
| `drills/` | Part 1 language core — 14 chapters | **None at all** | Never |
| `crate-drills/` | Part 2 ecosystem — 6 chapters | Pinned crates | Once, to fetch |

`drills/` has an empty `[dependencies]` section. It uses the test runner built
into the toolchain, so there is nothing to install, nothing to restore, and
nothing to download — `cargo test` works on a machine that has never seen the
internet. That is deliberate: everything Part 1 teaches is implemented from
scratch on the standard library, because writing the mechanism yourself is what
makes ownership, trait dispatch and interior mutability stop being vocabulary.

`crate-drills/` is about the ecosystem, so it necessarily has an ecosystem in it.
Run `cargo build` once while you have a connection; after that `cargo test
--offline` works entirely from the local package cache. Every version in its
`Cargo.toml` is pinned to what the book was written and verified against, so the
APIs described in the prose are the APIs you will get.

## Running a single chapter

Both projects are one module per chapter, and the module name filters cleanly:

```text
cd exercises/drills
cargo test ch05           # ownership and moves
cargo test ch11           # error handling

cd ../crate-drills
cargo test ch20           # serde
cargo test ch22           # axum, reqwest and tracing
```

One libtest wrinkle worth knowing before it wastes your afternoon: a filter
containing a space silently matches nothing useful and runs the whole suite.
Keep filters to a single token.

## Three chapters begin with a compile error, on purpose

In `drills/`, chapters 5, 9 and 13 do not build until you fix them, and the
compiler's message *is* the exercise:

- **`ch05`** fails because a type is used as though it were `Copy` and is not.
  The error tells you a value was moved; deciding whether the right fix is
  deriving `Copy`, cloning, or borrowing is the ownership lesson in miniature.
- **`ch09`** fails because a type is missing derives it needs to be sorted and
  defaulted. Read which trait the compiler says is unsatisfied and why.
- **`ch13`** fails on a visibility violation — a field the test reaches for is
  not visible where the test lives. `pub(crate)` versus `pub` is the whole
  chapter.

Everything else in both projects builds cleanly out of the box. That is the rule
rather than the exception: you should be working against *failing assertions*,
not against a wall of type errors, because a failing assertion tells you what the
code should do and a type error only tells you that it does not compile.

Two chapters in `crate-drills/` ship placeholder attributes — `ch19` has
`#[error("TODO: ...")]` on every variant and `ch20` has a `#[default]` — for the
same reason. Replace them.

## One thing that will surprise you

In `drills/ch17`, the FFI chapter, the two `extern "C"` functions return
placeholder values rather than calling `todo!()`. A panic cannot unwind across a
C ABI boundary; instead the process aborts. A `todo!()` there would not fail one
test, it would kill the entire test binary with an access violation and tell you
nothing. That is a real constraint on FFI code and the reason production FFI
entry points wrap their bodies in `catch_unwind` — a lesson that cost this book
one confusing debugging session to learn.

## Chapter index

Part 1 — the language core. No dependencies, no network, ever.

| Exercise | Covers | Answers |
|---|---|---|
| [03 — Syntax orientation](03-syntax.md) | [03](../03-syntax-orientation.md) | [answers](answers/03-syntax.md) |
| [04 — Strings and slices](04-strings.md) | [04](../04-strings-and-slices.md) | [answers](answers/04-strings.md) |
| [05 — Ownership and moves](05-ownership.md) | [05](../05-ownership-and-moves.md) | [answers](answers/05-ownership.md) |
| [06 — Borrowing and lifetimes](06-borrowing.md) | [06](../06-borrowing-and-lifetimes.md) | [answers](answers/06-borrowing.md) |
| [07 — Structs, enums, matching](07-structs-enums.md) | [07](../07-structs-enums-matching.md) | [answers](answers/07-structs-enums.md) |
| [08 — Traits and generics](08-traits-generics.md) | [08](../08-traits-and-generics.md) | [answers](answers/08-traits-generics.md) |
| [09 — The standard traits](09-standard-traits.md) | [09](../09-standard-traits.md) | [answers](answers/09-standard-traits.md) |
| [10 — Collections and iterators](10-collections.md) | [10](../10-collections-and-iterators.md) | [answers](answers/10-collections.md) |
| [11 — Error handling](11-errors.md) | [11](../11-error-handling.md) | [answers](answers/11-errors.md) |
| [12 — Smart pointers](12-smart-pointers.md) | [12](../12-smart-pointers.md) | [answers](answers/12-smart-pointers.md) |
| [13 — Modules and crates](13-modules.md) | [13](../13-modules-and-crates.md) | [answers](answers/13-modules.md) |
| [14 — Testing and documentation](14-testing.md) | [14](../14-testing-and-docs.md) | [answers](answers/14-testing.md) |
| [15 — Concurrency](15-concurrency.md) | [15](../15-concurrency.md) | [answers](answers/15-concurrency.md) |
| [17 — Unsafe, FFI, interop](17-unsafe-ffi.md) | [17](../17-unsafe-ffi-interop.md) | [answers](answers/17-unsafe-ffi.md) |

Part 2 — the ecosystem. One `cargo build` while online, then offline forever.

| Exercise | Covers | Answers |
|---|---|---|
| [16 — Async and tokio](16-async.md) | [16](../16-async-and-tokio.md), [21](../21-tokio-in-practice.md) | [answers](answers/16-async.md) |
| [18 — clap and anyhow](18-clap.md) | [18](../18-clap.md), [19](../19-anyhow-and-thiserror.md) | [answers](answers/18-clap.md) |
| [19 — thiserror](19-thiserror.md) | [19](../19-anyhow-and-thiserror.md) | [answers](answers/19-thiserror.md) |
| [20 — serde](20-serde.md) | [20](../20-serde.md) | [answers](answers/20-serde.md) |
| [22 — axum, reqwest, tracing](22-http-tracing.md) | [22](../22-reqwest-and-axum.md), [23](../23-tracing-and-logging.md) | [answers](answers/22-http-tracing.md) |
| [24 — Configuration with figment](24-config.md) | [24](../24-configuration.md) | [answers](answers/24-config.md) |

Modules [26 — Crate field guide](../26-crate-field-guide.md) and
[29 — Reference](../29-reference.md) have no drill chapter, because they are
lookup material rather than concepts — you test those by using them. Module
[25 — sqlx](../25-sqlx.md) has none either, since its compile-time-checked
queries need a live database or a prepared query cache, which would break the
offline guarantee.

## The capstone

The book's capstone is not a drill file; it is a whole program. Module
[27 — Capstone: polcheck](../27-capstone-polcheck.md) walks through
[`code/polcheck/`](../code/polcheck/), a real CLI built from clap, anyhow,
thiserror, serde, tokio, reqwest, figment and tracing together, with 29 tests of
its own.

Work through it by extending it rather than reading it. Three exercises that each
touch every layer: add a new `Condition` variant to the rule language and follow
the compile errors until the program is whole again — which is the single best
demonstration of what an exhaustive `match` over a closed enum buys you; add a
`--watch` flag that re-evaluates when the rule file changes, which forces you to
think about a tokio task's lifetime and shutdown; and add a second output format
behind the existing `ValueEnum`, which will show you exactly how much of the code
knew about formatting and should not have.

---

Back to [the book index](../00-START-HERE.md).
