# 02 — The toolchain and project model

Before we can talk about ownership we need to be able to build and run something. This module maps the
Rust toolchain onto the .NET one you already know: `rustup` against the .NET SDK installers, `cargo`
against `dotnet` and NuGet combined, `clippy` against Roslyn analyzers, and `docs.rs` against a docs
experience that .NET has never quite had. The mapping is unusually clean, which makes this the easiest
module in Part 1 — enjoy it.

> **Prerequisite:** [01 — Why Rust exists](01-why-rust.md).

The headline difference is consolidation. In .NET your build story is spread across the SDK, MSBuild,
NuGet, a `.csproj` whose schema you have partially memorised, `dotnet` CLI verbs, and analyzer packages
wired in through props and targets. Rust puts essentially all of it in one tool, `cargo`, driven by one
declarative file, `Cargo.toml`, with no equivalent of MSBuild's imperative escape hatch. You gain
enormous consistency — every Rust project in the world is built with `cargo build` — and you lose the
ability to do genuinely arbitrary things at build time without dropping into a `build.rs` script.

## rustup: the toolchain multiplexer

`rustup` installs and switches between Rust toolchains. Its nearest .NET analogue is the combination of
the SDK installer and `global.json`, but it is considerably more capable, because a Rust "toolchain" is a
triple of channel, version, and host platform, and you routinely have several installed.

```powershell
rustup show                    # what's installed and what's active here
rustup update                  # update all installed toolchains
rustup default stable          # set the global default
rustup toolchain install nightly
rustup component add clippy rustfmt rust-src rust-analyzer
rustup target add x86_64-unknown-linux-musl   # cross-compilation target
```

Rust ships a new stable release **every six weeks**, without exception, and there is no notion of an LTS
release. This sounds alarming to anyone who has managed .NET version migrations and turns out to be a
non-event, because of a stability guarantee that Rust takes very seriously: code that compiles on stable
today will compile on every future stable release. Upgrading rustc is nearly always a no-op, and the
six-week cadence means each release is small.

Three channels exist. **Stable** is what you ship. **Beta** is the next stable, six weeks early, and
exists so you can catch regressions. **Nightly** is built every night and is the only channel where
unstable features (gated behind `#![feature(...)]`) can be used; some tooling — notably certain `rustfmt`
options and `cargo` unstable flags — requires it. Unlike .NET preview SDKs, nightly is not a
release-candidate track; it is a permanently unstable channel that some tools legitimately depend on.

You can pin a project to a toolchain with a `rust-toolchain.toml` at the repository root, which is the
direct analogue of `global.json` and is respected automatically by every cargo invocation:

```toml
[toolchain]
channel = "1.95.0"
components = ["clippy", "rustfmt"]
targets = ["x86_64-unknown-linux-musl"]
```

## Editions: the thing .NET has no equivalent of

An **edition** is Rust's mechanism for making breaking language changes without breaking the ecosystem,
and it has no real counterpart in C#. `<LangVersion>` is close but not the same, because C# language
versions are tied to compiler versions and interact with target frameworks in ways editions deliberately
avoid.

The rules are worth internalising because they are what makes the whole thing work. Four editions exist
so far: 2015, 2018, 2021, and 2024. **Every compiler supports every edition**, so a 1.95 compiler builds
2015-edition code perfectly well. The edition is declared per-crate in `Cargo.toml`, and — this is the
important part — **crates of different editions interoperate freely**. Your 2024-edition binary can
depend on a 2015-edition library and nothing anywhere needs to change. That property is what lets the
language make breaking changes like reserving new keywords or altering closure capture semantics without
ever fragmenting the ecosystem into incompatible halves.

```toml
[package]
name = "polcheck"
version = "0.1.0"
edition = "2024"
```

Editions change surface syntax and some semantics, never the standard library or the type system. The
2024 edition, which this book uses throughout and which is the default for `cargo new` on Rust 1.95,
brought several changes you will actually notice: `unsafe extern "C" { ... }` blocks must now be marked
`unsafe` (module 17), `if let` temporaries drop at the end of the `if let` rather than living to the end
of the enclosing statement, and return-position `impl Trait` no longer implicitly captures all in-scope
lifetimes, which is why you will occasionally see `+ use<'_>` in signatures. `cargo fix --edition`
automates most migrations.

## Cargo: build tool, package manager, test runner, doc generator

`cargo` is `dotnet build`, `dotnet test`, `dotnet run`, `dotnet pack`, NuGet, and DocFX in one binary.
The verb mapping is almost boringly direct:

| Task | .NET | Rust |
|---|---|---|
| New application | `dotnet new console -o app` | `cargo new app` |
| New library | `dotnet new classlib -o lib` | `cargo new lib --lib` |
| Add a dependency | `dotnet add package Serde` | `cargo add serde --features derive` |
| Restore | `dotnet restore` | implicit (or `cargo fetch`) |
| Build | `dotnet build` | `cargo build` |
| Build optimised | `dotnet build -c Release` | `cargo build --release` |
| Run | `dotnet run` | `cargo run` |
| Test | `dotnet test` | `cargo test` |
| Format | `dotnet format` | `cargo fmt` |
| Static analysis | Roslyn analyzers | `cargo clippy` |
| Generate docs | DocFX / Sandcastle | `cargo doc --open` |
| Publish a package | `dotnet nuget push` | `cargo publish` |
| Install a global tool | `dotnet tool install -g` | `cargo install` |
| Type-check only | — | `cargo check` |

Two rows deserve comment. `cargo check` has no .NET equivalent worth naming: it runs the full front end
— parsing, name resolution, type checking, borrow checking — and stops before code generation. Because
codegen and LLVM optimisation dominate Rust build times, `cargo check` is often several times faster
than `cargo build`, and it is what your editor runs continuously. When you are in a
fix-the-compiler-errors loop, which in Rust you frequently are, `cargo check` is the loop you want.

The other is that **restore is implicit**. There is no separate restore step to forget; any build command
resolves and fetches as needed. Dependencies are compiled from source into your build, not consumed as
prebuilt binaries. This is the single biggest practical difference from NuGet and it has consequences:
your first build of a project with a large dependency tree takes minutes rather than seconds, because you
are compiling `tokio` and everything beneath it, not downloading a DLL. Subsequent builds reuse the
`target/` directory and are fast. There is no binary distribution of crates at all.

## Cargo.toml against .csproj

Here is a realistic manifest with the pieces annotated:

```toml
[package]
name = "polcheck"
version = "0.1.0"
edition = "2024"
rust-version = "1.85"              # MSRV: a *floor*, not a target framework
description = "Evaluate resources against governance rules"
license = "MIT OR Apache-2.0"
repository = "https://github.com/example/polcheck"

[dependencies]
serde = { version = "1", features = ["derive"] }
serde_json = "1"
clap = { version = "4", features = ["derive"] }

[dev-dependencies]                 # test/bench only; never in the shipped binary
assert_cmd = "2"
tempfile = "3"

[build-dependencies]               # available only to build.rs

[features]
default = []
remote = ["dep:reqwest"]           # optional capability toggle

[profile.release]
lto = true
codegen-units = 1
strip = "symbols"
```

The version strings look like exact pins and are not. `"1"` means **`>=1.0.0, <2.0.0`** — a bare version
string is a caret requirement, so `serde = "1"` and `serde = "^1"` are identical. This is closer
to NuGet's floating versions than to its default of pinning exactly, and it is why `Cargo.lock` matters.
That lock file records the exact resolved graph; you **commit it for binaries** and, by longstanding
convention, **do not commit it for libraries**, so downstream consumers resolve their own graph.
It is the direct analogue of `packages.lock.json`, except the convention is universal rather than opt-in.

`[dev-dependencies]` is worth calling out because .NET has no clean equivalent — an xUnit reference in a
separate test project is the usual workaround. In Rust, test and benchmark code lives in the same crate,
and dev-dependencies are compiled only for `cargo test`/`cargo bench` and never appear in your shipped
artefact.

Two profiles exist by default: `dev` (used by `cargo build`) with optimisations off and debug assertions
on, and `release` (used by `cargo build --release`) with `opt-level = 3`. The gap between them is much
larger than between .NET Debug and Release, because so much of Rust's performance depends on inlining
generic and iterator code away. If you ever benchmark Rust and conclude it is slow, check that you passed
`--release` before you do anything else.

## The other files and directories

Cargo is convention-driven, and the conventions are worth learning because they are not configurable the
way MSBuild's are.

```text
polcheck/
├── Cargo.toml
├── Cargo.lock
├── build.rs            # optional build script, compiled and run before the crate
├── src/
│   ├── main.rs         # binary crate root  -> produces polcheck.exe
│   ├── lib.rs          # library crate root -> produces libpolcheck.rlib
│   └── bin/
│       └── helper.rs   # additional binaries
├── tests/              # integration tests: each file is its own crate
├── benches/            # benchmarks
├── examples/           # example programs, built by `cargo build --examples`
└── target/             # all build output; the .gitignore entry
```

A package containing both `src/lib.rs` and `src/main.rs` is extremely common, and it is the layout we use
for the capstone: the library holds the logic and is unit-testable and reusable, and the binary is a thin
shell that parses arguments and calls into it. Think of it as a class library and a console app sharing
one project directory, which .NET cannot do.

## rust-analyzer, clippy, and rustfmt

**rust-analyzer** is the language server, and it is what makes Rust bearable in an editor. Install the VS
Code extension of the same name, or use it through any LSP client. It gives you completion,
go-to-definition, inline type hints — genuinely important in a language with this much inference — and,
critically, it surfaces borrow-checker errors as you type rather than at build time. If your Rust
experience feels painful, check first that rust-analyzer is actually running; the difference is night
and day.

**clippy** is the analyzer suite, and it is far more opinionated than Roslyn's defaults. Where Roslyn
analyzers mostly flag correctness issues, clippy also teaches idiom — it will tell you that your `match`
should be an `if let`, that your manual loop should be an iterator chain, that you wrote `x.len() == 0`
where `x.is_empty()` reads better. Treat it as a free code reviewer during your first months; it is the
fastest way to learn what idiomatic Rust looks like.

```powershell
cargo clippy --all-targets --all-features -- -D warnings
```

That `-D warnings` promotes every lint to an error, and it is the standard CI invocation. Lints are
organised into groups — `correctness`, `suspicious`, `style`, `complexity`, and `perf` are on by default;
`pedantic` and `nursery` are opt-in. Configure levels in `Cargo.toml`, which is the modern
approach:

```toml
[lints.clippy]
pedantic = { level = "warn", priority = -1 }
missing_errors_doc = "allow"

[lints.rust]
unsafe_code = "forbid"
```

**rustfmt** is the formatter and, unlike `dotnet format`, it is effectively non-negotiable in the
community. It has few options and the culture is to leave them alone. This sounds authoritarian and is a
relief: no team ever argues about brace placement in Rust, and every codebase you open looks like every
other one. Run `cargo fmt --check` in CI and move on with your life.

## crates.io and docs.rs

`crates.io` is the package registry, and it maps onto nuget.org with two differences that matter.
Publication is **permanent** — you can yank a version to stop new projects resolving it, but you can
never delete or overwrite it, so a build that worked keeps working. And there is a single flat global
namespace with no prefix reservation, which is why crate names are short and often already taken.

`docs.rs` is the better story, and there is genuinely nothing like it in .NET. Every crate published to
crates.io has its API documentation **built automatically, for every version, from the source**, and
hosted at `https://docs.rs/<crate>/<version>`. There is no opt-in, no separate publishing step,
and no possibility of the docs drifting from the code, because they are generated from it. Get in the
habit of going straight to `docs.rs/tokio/1.53.1` rather than searching the web, because — as the version
table in the README warns — much of the web is describing an older version.

The same generator produces your own docs. `cargo doc --open` builds documentation for your crate *and
its entire dependency graph* and opens it locally, which means you have offline API docs for everything
you depend on, at exactly the versions you resolved.

## Your first build

Let's make the domain model from module 01 into a real project, so the rest of the book has somewhere to
run.

```powershell
cargo new polcheck
cd polcheck
cargo run
```

`cargo new` creates the manifest, a `src/main.rs` with a hello-world, and initialises a git repository
with a `.gitignore` containing `/target`. Replace `src/main.rs` with the module 01 domain model and run
it again. Then try the tools:

```powershell
cargo check                 # fast type + borrow check
cargo clippy                # idiom and correctness lints
cargo fmt                   # canonical formatting
cargo doc --open            # your docs plus every dependency's
cargo build --release       # optimised binary in target/release/
```

One habit to build now, because it pays enormous dividends later: **read the compiler errors properly.**
Rust's diagnostics are the best of any mainstream language — they include the span, an explanation, and
very often a suggested fix that is literally correct — and the reflex from other languages of skimming
for the file and line number throws away most of their value. When you get an error you do not
understand, `rustc --explain E0502` prints a full explanatory article with examples. In the borrow-checker
modules ahead, this will be your primary teacher.

## Before you move on

The mental shift here is small but real: Rust replaces a federation of tools with a single one, and
replaces MSBuild's imperative extensibility with convention plus a declarative manifest. You give up
arbitrary build-time logic and get, in exchange, the property that every Rust project builds, tests,
formats, lints, and documents with the same five commands. Editions are the piece with no .NET
counterpart, and the property that makes them work is that every compiler supports every edition and
crates of different editions link together freely.

Practically, three habits matter from here on. Use `cargo check` as your inner loop, because it skips
codegen and is dramatically faster. Benchmark only with `--release`, because the debug profile does not
inline the abstractions Rust's performance story depends on. And go to `docs.rs` at a specific version
rather than to a search engine, because crate APIs in Part 2 have moved recently and most published
examples are stale.

If you can explain what an edition is and why a 2024-edition crate can depend on a 2015-edition one, and
you can say what `cargo check` does that `cargo build` doesn't, you're ready to read some Rust.

Next: [03 — Syntax orientation](03-syntax-orientation.md).

### Sources

- *The rustup book*. <https://rust-lang.github.io/rustup/> — channels, toolchain files, components, and cross-compilation targets.
- *The Edition Guide*. <https://doc.rust-lang.org/edition-guide/> — what editions are and the interoperability guarantee; the 2024 change list is at <https://doc.rust-lang.org/edition-guide/rust-2024/index.html>.
- *The Cargo Book*, "Specifying Dependencies". <https://doc.rust-lang.org/cargo/reference/specifying-dependencies.html> — establishes that a bare version string is a caret requirement.
- *The Cargo Book*, "Cargo.toml vs Cargo.lock". <https://doc.rust-lang.org/cargo/guide/cargo-toml-vs-cargo-lock.html> — the commit-for-binaries, omit-for-libraries convention.
- *The Clippy book* and lint list. <https://doc.rust-lang.org/clippy/> and <https://rust-lang.github.io/rust-clippy/master/> — lint groups and their default levels.
- *The Cargo Book*, "The `lints` section". <https://doc.rust-lang.org/cargo/reference/manifest.html#the-lints-section> — configuring lint levels in `Cargo.toml`.
- *About docs.rs*. <https://docs.rs/about> — automatic documentation builds for every published crate version.
