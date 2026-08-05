# Setup

You need less here than a .NET setup would lead you to expect. There is no SDK selector, no global install of a runtime, and no equivalent of the machine-wide GAC — Rust installs per-user, every project pins its own dependencies, and the whole thing uninstalls cleanly. Budget about half an hour, most of which is a download.

## Install the toolchain

Rust is installed and updated through **rustup**, which manages toolchains the way `dotnet` manages SDKs, except that switching versions is per-directory and instant. Go to [rustup.rs](https://rustup.rs) and follow the one-liner for your platform.

On **Windows**, download and run `rustup-init.exe`. Take the default option (`1) Proceed with standard installation`), which gives you the stable MSVC toolchain. One prerequisite catches people: the MSVC toolchain uses the Microsoft linker, so you need the **C++ build tools** installed. If you already have Visual Studio with the "Desktop development with C++" workload, you have them. If you don't, rustup will offer to install the Visual Studio Build Tools for you — accept, because the alternative is a link error at your first `cargo build` that says nothing useful about its cause.

On **macOS or Linux**:

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

macOS additionally needs the Xcode command-line tools (`xcode-select --install`) for the linker, for the same reason Windows needs the MSVC build tools.

Restart your shell afterwards so `~/.cargo/bin` (or `%USERPROFILE%\.cargo\bin`) is on `PATH`.

## Pin the version this book targets

Everything here was written and **compile-verified against Rust 1.95.0 on edition 2024**. Rust's stable train releases every six weeks and is rigorous about backward compatibility, so a newer stable will work fine; an older one may not, because edition 2024 requires at least 1.85.

```bash
rustup default stable
rustup update
rustc --version        # expect 1.95.0 or newer
cargo --version
```

The two components you want beyond the defaults are the linter and the formatter, both of which the `default` profile already installs, plus the language server your editor will use:

```bash
rustup component add clippy rustfmt
rustup component add rust-analyzer
```

**clippy** is worth internalising early. It is closer to a very opinionated Roslyn analyzer set than to a style checker, and a large fraction of "how do Rust programmers actually write this?" is answered by running `cargo clippy` and reading what it suggests. Every piece of code in this book is clean under `cargo clippy -- -D warnings`, and yours should be too.

## Set up your editor

Use **rust-analyzer**. It is the language server, and Rust without it is a substantially worse experience than C# without IntelliSense — inferred types are invisible in the source, and rust-analyzer's inlay hints are what make them readable.

In **VS Code**, install the `rust-lang.rust-analyzer` extension and nothing else. Specifically, do not also install the older `rust-lang.rust` extension; the two conflict.

In **Visual Studio**, there is no first-class Rust support. Use VS Code or **RustRover** (JetBrains, free for non-commercial use) instead. If you live in Visual Studio for C#, expect this to be the most jarring part of the transition, and expect it to stop mattering within a week.

Two settings pay for themselves immediately: turn on inlay type hints, and set clippy as the check command so you get lints as you type rather than at build time. In VS Code's `settings.json`:

```json
{
  "rust-analyzer.check.command": "clippy",
  "rust-analyzer.inlayHints.typeHints.enable": true
}
```

## Verify the installation

Create a throwaway project and run it. This confirms the compiler, cargo, the linker and the test runner all work — the linker in particular, which is the piece most likely to be missing:

```bash
cargo new hello --bin
cd hello
cargo run
```

You should see `Hello, world!`. Then confirm the test runner and the linter:

```bash
cargo test          # 0 tests, but it must build and link
cargo clippy
cargo fmt --check
```

If all four succeed, you are done; delete the directory. If `cargo run` fails with a linker error, the C++ build tools (Windows) or command-line tools (macOS) are missing — go back a section.

## The exercise projects

The [exercise companion](./exercises/00-HOW-TO-USE.md) ships two cargo projects with deliberately different dependency stories.

`exercises/drills/` covers Part 1, the language core, in fourteen chapters. Its `[dependencies]` section is empty, and it uses the test runner built into the toolchain, so there is genuinely nothing to install and no network needed at any point:

```bash
cd exercises/drills
cargo test
```

Expect a large number of failures on a fresh checkout. That is the point — the bodies are `todo!()` and you are meant to fill them in. Three chapters (5, 9 and 13) do not even compile until you fix them, and the compiler's message is the exercise; [`exercises/00-HOW-TO-USE.md`](./exercises/00-HOW-TO-USE.md) explains each one so you don't mistake it for a corrupt download.

`exercises/crate-drills/` covers Part 2 and necessarily contains an ecosystem. Build it **once while you have a connection**, after which everything works from the local package cache:

```bash
cd exercises/crate-drills
cargo build            # once, online — fetches and compiles the pinned crates
cargo test --offline   # thereafter, no network
```

The first build compiles a few hundred transitive crates and takes several minutes on a laptop. This is normal and is the single biggest culture shock coming from NuGet: Rust ships source, not binaries, so your first build of a dependency tree is a real compile. Subsequent builds hit the cache and are fast.

You can filter to one chapter in either project, since each chapter is a module:

```bash
cargo test ch05
```

One libtest wrinkle worth knowing before it wastes an afternoon: a filter containing a space silently matches nothing useful and runs the whole suite. Keep filters to a single token.

## The capstone crate

[`code/polcheck/`](./code/polcheck/) is a complete, working CLI — the program [Module 27](./27-capstone-polcheck.md) builds. It is a normal crate, so it needs one online build and then behaves like any other:

```bash
cd code/polcheck
cargo test             # 29 tests
cargo run -- --help
```

It is also clean under `cargo clippy -- -D warnings` and `cargo fmt --check`, which makes it a useful thing to deliberately break: change something, see what the compiler and clippy say, and put it back.

## Versions this book targets

Version numbers matter more here than they would in a .NET book, because several of these crates have had recent breaking changes and pre-1.0 crates are allowed to break on a minor bump.

| Component | Version |
|---|---|
| rustc / cargo | 1.95.0 |
| Edition | 2024 |
| clap / clap_complete | 4.6 |
| anyhow | 1.0 |
| thiserror | 2.0 |
| serde / serde_json | 1.0 |
| tokio / tokio-util | 1.53 / 0.7 |
| reqwest | 0.13 |
| axum | 0.8 |
| tracing / tracing-subscriber | 0.1 / 0.3 |
| rand | 0.10 |
| rayon | 1.12 |
| sqlx | 0.9 |

Every `Cargo.toml` in this book pins these, so the APIs described in the prose are the APIs you will get.

## A note on version drift

A great deal of the Rust material you will find on the open web is written against older versions of these crates, and the breakage is rarely obvious. `reqwest` 0.13 removed `query` and `form` from its default features. `axum` 0.8 changed path parameters from `/:id` to `/{id}`. `rand` 0.10 renamed `thread_rng()` to `rng()` and `gen()` to `random()`. `clap` 4 replaced `#[clap(...)]` with `#[arg(...)]` and `#[command(...)]`.

The habit to form: when a snippet from a blog post doesn't compile, **suspect the version before you suspect yourself**, and go to [docs.rs](https://docs.rs) for the exact version in your `Cargo.toml` rather than to a search engine. docs.rs hosts every published version of every crate, and the version selector at the top-left of any page is the fastest way to find out whether the function you're looking for still exists.

---

Once `cargo test` passes in a throwaway project, go to [Module 01](./01-why-rust.md).
