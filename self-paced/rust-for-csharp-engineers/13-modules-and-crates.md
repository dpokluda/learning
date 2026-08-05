# 13 — Modules, crates, and workspaces

Code organisation is the area where Rust and .NET look superficially similar and differ in one deep way.
Both have a compilation unit (crate / assembly), a namespacing mechanism (module / namespace), a visibility
system (`pub` / `public`), a package manager (Cargo / NuGet), and a manifest (`Cargo.toml` / `.csproj`).
But .NET decouples the namespace from the file and the assembly — a `namespace Foo.Bar` can appear in any
file in any assembly — while Rust ties the module tree to the file tree and makes the crate the unit of
both compilation *and* privacy. Once you internalise that, the rest is mechanical.

> **Prerequisite:** [12 — Smart pointers and interior mutability](12-smart-pointers.md).

## The vocabulary, mapped

| Rust | .NET | Notes |
|---|---|---|
| crate | assembly (`.dll`) | unit of compilation *and* privacy |
| module | namespace | but tied to the file tree |
| package | NuGet package | one `Cargo.toml`; may contain several crates |
| workspace | solution (`.sln`) | shared lockfile and `target/` |
| `Cargo.toml` | `.csproj` | manifest and dependencies |
| `Cargo.lock` | `packages.lock.json` | resolved exact versions |
| `pub` | `public` | |
| (default) | `private` | private is the default |
| `pub(crate)` | `internal` | |
| `pub(super)` | (no analogue) | visible to the parent module |
| `use` | `using` | |
| `pub use` | type forwarding | re-export: the API-design tool |

The two rows worth pausing on are **crate = unit of privacy** and **`pub use` = re-export**.

In .NET, `internal` means "visible within this assembly", which makes the assembly your privacy boundary,
and splitting one assembly into two for build reasons breaks `internal` access — hence
`InternalsVisibleTo`. Rust's `pub(crate)` is the same idea, and the same consequence follows: a crate is
the largest unit that can share private state, so crate splitting is an API-design decision, not just a
build one.

`pub use` has no everyday C# equivalent and is the single most important idiom in this module. It lets the
*file layout* and the *public API* be completely different things, which is why almost every good Rust
crate has a deep private module tree and a flat public surface.

## Modules and the file tree

A module is declared with `mod`, and the compiler looks for its contents in a predictable place:

```text
src/
├── main.rs           // crate root for a binary
├── lib.rs            // crate root for a library
├── engine.rs         // module `engine`
├── engine/
│   ├── rules.rs      // module `engine::rules`
│   └── report.rs     // module `engine::report`
└── config.rs         // module `config`
```

`src/lib.rs` says `mod engine;` and `mod config;`. `src/engine.rs` says `mod rules;` and `mod report;`.
Every module must be *declared* by its parent — a `.rs` file that nobody declares is simply not compiled,
which surprises people who expect the C# behaviour of "every file in the project is part of the build".
Forgetting the `mod` line is the single most common "why isn't my code being compiled?" moment.

The older layout used `engine/mod.rs` instead of `engine.rs`; both still work, but the `engine.rs` +
`engine/` form is preferred because it avoids a directory full of files all called `mod.rs`.

Modules can also be inline, which is how test modules are written:

```rust
mod validation {
    pub fn is_valid_tag(key: &str) -> bool {
        !key.is_empty() && key.chars().all(|c| c.is_ascii_alphanumeric() || c == '-')
    }

    pub mod strict {
        pub fn is_valid_tag(key: &str) -> bool {
            super::is_valid_tag(key) && key.len() <= 16
        }
    }
}

fn main() {
    assert!(validation::is_valid_tag("owner-team"));
    assert!(!validation::strict::is_valid_tag("a-very-long-tag-key-indeed"));
}
```

Paths are the other half. `crate::` is absolute from the crate root (`global::` in C#), `super::` goes up
one level (no C# analogue), `self::` is the current module, and a bare identifier resolves relative to the
current module. `use` brings a path into scope exactly like `using`:

```rust
use std::collections::{BTreeMap, HashMap};        // grouped, like using static lists
use std::fmt::Write as FmtWrite;                  // alias, like `using X = Y;`
use std::io::*;                                   // glob — avoid outside preludes and tests

fn main() {
    let mut s = String::new();
    write!(s, "{}", 1).unwrap();                  // needs FmtWrite in scope
    let _m: HashMap<u8, u8> = HashMap::new();
    let _b: BTreeMap<u8, u8> = BTreeMap::new();
    let _ = std::io::stdout();                    // the glob import justified
    assert_eq!(s, "1");
}
```

The rule from module 08 bears repeating because it is the practical consequence: **a trait's methods are
only callable if the trait is in scope**, so `use` is not merely a naming convenience.

## Visibility: private by default, and *deeply* private

Rust items are private unless marked `pub`, and privacy is relative to the module tree: a private item is
visible to its own module **and all descendants**, but not to its parent or siblings.

```rust
mod engine {
    pub struct Report {
        pub total: usize,          // public field
        pub(crate) internal_id: u64, // visible anywhere in this crate
        seed: u64,                 // private to `engine` and its children
    }

    impl Report {
        pub fn new(total: usize) -> Self {
            Self { total, internal_id: 1, seed: 42 }
        }
        pub fn seed(&self) -> u64 { self.seed }   // accessor for the private field
    }

    pub(crate) mod internals {
        /// Children can see the parent's private items.
        pub fn peek(r: &super::Report) -> u64 { r.seed }
    }
}

fn main() {
    let r = engine::Report::new(3);
    assert_eq!(r.total, 3);
    assert_eq!(r.internal_id, 1);       // pub(crate): fine here
    assert_eq!(r.seed(), 42);
    assert_eq!(engine::internals::peek(&r), 42);
    // r.seed would not compile: private to the engine module.
}
```

Two differences from C# stand out. **Struct fields are individually public or private**, and the default is
private — so `pub struct` does not mean "everything about it is public", it means the *type name* is
usable. That is the shape you want: expose the type, hide the representation, add accessors. And
**`pub(crate)` is the honest `internal`**, with the extra granularity of `pub(super)` and
`pub(in crate::some::path)` when you want something narrower.

There is no `protected`, because there is no inheritance. The trait-based equivalent is a trait method
without a default body, which subtypes must supply.

## Re-exports: the shape of a good crate

Here is the idiom that makes Rust crates pleasant to use. Organise your source however is convenient
internally, then curate a flat public API at the root with `pub use`:

```rust
// --- this is what src/lib.rs would look like ---
mod engine {
    pub mod rules {
        #[derive(Debug, PartialEq)]
        pub struct Rule { pub name: String }
        impl Rule {
            pub fn new(name: &str) -> Self { Self { name: name.to_owned() } }
        }
    }
    pub mod report {
        #[derive(Debug, PartialEq)]
        pub struct Finding { pub reason: String }
    }
}

// Curated public surface: callers write `polcheck::Rule`, not
// `polcheck::engine::rules::Rule`.
pub use engine::report::Finding;
pub use engine::rules::Rule;

fn main() {
    // From outside the crate these would be `polcheck::Rule` / `polcheck::Finding`.
    let r = Rule::new("require-owner");
    let f = Finding { reason: "missing".to_owned() };
    assert_eq!(r.name, "require-owner");
    assert_eq!(f.reason, "missing");
}
```

The equivalent C# manoeuvre is a type-forwarding attribute or just putting everything in one namespace
regardless of folder — and in practice C# codebases align namespace to folder, so the public API mirrors
the internal layout whether that is a good API or not. Rust decouples them, and the API guidelines are
explicit that you should exploit that.

The related idiom is the **prelude module**, which crates like `rayon` and `tokio` provide so users can
`use foo::prelude::*` and get the traits they need in scope:

```rust
pub mod prelude {
    pub use super::Reportable;
}

pub trait Reportable {
    fn report(&self) -> String;
}

impl Reportable for u32 {
    fn report(&self) -> String { format!("n={self}") }
}

fn main() {
    use prelude::*;
    assert_eq!(5u32.report(), "n=5");
}
```

## Packages, crates, and targets

A **package** is a directory with a `Cargo.toml`. It contains one or more **crates**, which are the actual
compilation units. The default layout is discovered by convention:

| Path | Crate type | Notes |
|---|---|---|
| `src/lib.rs` | library | at most one per package |
| `src/main.rs` | binary | the default binary |
| `src/bin/*.rs` | binary | additional binaries |
| `benches/*.rs` | bench target | `cargo bench` |
| `examples/*.rs` | example | `cargo run --example foo` |
| `tests/*.rs` | integration test | one crate per file |

The common shape for a tool is a library plus a thin binary: `src/lib.rs` holds everything testable and
`src/main.rs` does argument parsing and calls into it. That is the same instinct as putting logic in a
class library and keeping `Program.cs` thin, and the motivation is identical — you cannot write integration
tests against a binary's internals, only against a library.

The `examples/` directory has no .NET equivalent and is worth adopting: examples are compiled by
`cargo test`, so they cannot rot, and they show up in the rendered docs.

## `Cargo.toml`

The manifest, annotated:

```toml
[package]
name = "polcheck"
version = "0.1.0"
edition = "2024"                       # language edition, not toolchain version
rust-version = "1.85"                  # MSRV: minimum supported Rust version
license = "MIT OR Apache-2.0"
description = "Evaluate governance rules against resource inventories"
repository = "https://github.com/example/polcheck"
keywords = ["policy", "compliance", "cli"]
categories = ["command-line-utilities"]

[dependencies]
serde = { version = "1.0.229", features = ["derive"] }
serde_json = "1.0.151"
clap = { version = "4.6.5", features = ["derive", "env"] }
anyhow = "1.0.104"

[dev-dependencies]                     # test-only; not shipped to consumers
tempfile = "3.27.0"
assert_cmd = "2.2.2"

[build-dependencies]                   # for build.rs only
# vergen = "9"

[features]
default = ["json"]
json = ["dep:serde_json"]
yaml = []                              # opt-in extra

[profile.release]
opt-level = 3
lto = "thin"
codegen-units = 1
strip = "symbols"
```

Several of those deserve comment.

**`edition` is not a toolchain version.** Edition 2024 changes language defaults (let-chains, RPIT capture,
`gen` reservations) but a 2024 crate and a 2015 crate interoperate perfectly, and a current compiler builds
all editions. There is no equivalent of a .NET Framework / .NET Core split; it is closer to a C# `LangVersion`
that never forces a runtime migration.

**`rust-version` is the MSRV** — the minimum compiler you claim to support. Cargo enforces it, and bumping
it is conventionally a semver-minor event for libraries. `TargetFramework` is the nearest analogue but
implies much more.

**Version requirements are caret by default.** `"1.0.229"` means `>=1.0.229, <2.0.0` — Cargo will take
1.9.0 but never 2.0.0. This is NuGet's *minimum* version rule inverted: NuGet resolves to the lowest
version satisfying the constraint and unifies to a single version per assembly, while Cargo takes the
highest compatible and — crucially — **can link two semver-incompatible majors of the same crate into one
binary**. That is why `rand 0.8` and `rand 0.10` can coexist in a dependency graph, and why Rust has
nothing resembling .NET's assembly binding redirects or diamond-dependency hell. The cost is binary size
and the occasional confusing "expected `rand::Rng`, found `rand::Rng`" error when two versions leak into
one API.

**`Cargo.lock` should be committed for binaries and, since the ecosystem changed its mind, for libraries
too.** It only affects your own builds; consumers of a library re-resolve.

**Profiles are what `Debug`/`Release` configurations are**, but declarative and composable. `lto = "thin"`
plus `codegen-units = 1` is the standard "make it fast, accept slower builds" pair, and `strip` is roughly
"don't ship the PDB".

### Features

Features are Cargo's conditional compilation, and they are more central than anything in .NET. They are
**additive**: enabling a feature may only add API, never remove or change it, because Cargo unifies the
feature sets requested by all dependents into one build.

```rust
// In real code these gate whole modules.
#[cfg(feature = "yaml")]
pub fn parse_yaml(_s: &str) -> Option<()> { Some(()) }

#[cfg(test)]
mod tests {
    #[test]
    fn compiled_only_under_test() { assert!(true); }
}

fn main() {
    #[cfg(target_os = "windows")]
    let platform = "windows";
    #[cfg(not(target_os = "windows"))]
    let platform = "other";
    assert!(!platform.is_empty());
}
```

`#[cfg(...)]` is `#if` done properly: it operates on items after parsing, so the excluded code must still be
syntactically valid, and there is no textual preprocessor. `cfg!(...)` is the expression form, which
evaluates to a `bool` and lets the optimiser remove the dead branch.

The pattern you will meet constantly in dependency lists is `default-features = false`:

```toml
[dependencies]
reqwest = { version = "0.13.4", default-features = false, features = ["rustls", "json"] }
```

That says "do not give me the default TLS stack and everything else; give me exactly these". It is how you
keep dependency trees and compile times under control, and it has no NuGet equivalent at all — a NuGet
package is monolithic.

## Workspaces

A workspace is a solution: several packages sharing one `Cargo.lock`, one `target/` directory, and one
`cargo build` that builds them all.

```toml
# ./Cargo.toml — the workspace root
[workspace]
resolver = "3"
members = ["polcheck-core", "polcheck-cli", "polcheck-serde"]

# Dependency versions declared once, inherited by members.
[workspace.dependencies]
serde = { version = "1.0.229", features = ["derive"] }
anyhow = "1.0.104"
polcheck-core = { path = "polcheck-core" }

[workspace.package]
edition = "2024"
license = "MIT OR Apache-2.0"
version = "0.1.0"
```

```toml
# ./polcheck-cli/Cargo.toml — a member
[package]
name = "polcheck-cli"
edition.workspace = true
license.workspace = true
version.workspace = true

[dependencies]
polcheck-core.workspace = true
anyhow.workspace = true
```

`[workspace.dependencies]` plus `foo.workspace = true` is Directory.Packages.props / central package
management, and it solves the same problem: one place to bump a version. The shared `target/` means
dependencies compile once for the whole workspace rather than once per project, which is a much bigger
build-time win than the .NET equivalent because Rust compilation is expensive.

`resolver = "3"` is the edition-2024 feature resolver; it is what stops a dev-dependency's feature flags
leaking into your normal build.

Useful commands: `cargo build --workspace` builds everything, `cargo test -p polcheck-core` targets one
member, and `cargo run -p polcheck-cli` picks a binary when several exist.

## `build.rs`

A build script is a Rust program Cargo compiles and runs *before* your crate, and it is the analogue of an
MSBuild target or a `.targets` file — except it is Rust, so you debug it like normal code.

```rust,ignore
// build.rs, at the package root (not in src/).
use std::process::Command;

fn main() {
    // Tell Cargo when to re-run this script. Without these, it runs on every build.
    println!("cargo::rerun-if-changed=schema/rules.json");
    println!("cargo::rerun-if-env-changed=POLCHECK_BUILD_ID");

    let sha = Command::new("git")
        .args(["rev-parse", "--short", "HEAD"])
        .output()
        .ok()
        .and_then(|o| String::from_utf8(o.stdout).ok())
        .unwrap_or_else(|| "unknown".into());

    // Emit an environment variable readable at compile time via env!().
    println!("cargo::rustc-env=GIT_SHA={}", sha.trim());
}
```

```rust,ignore
// In src/main.rs:
fn main() {
    println!("polcheck {} ({})", env!("CARGO_PKG_VERSION"), env!("GIT_SHA"));
}
```

Build scripts communicate by printing `cargo::` directives to stdout — setting environment variables,
adding link flags, declaring `cfg` values, and telling Cargo what to watch for changes. They are used for
compiling C dependencies, generating code, running `bindgen`, and embedding build metadata. The single most
important discipline is emitting `rerun-if-changed`, because a script without it runs on every build and
silently doubles your compile times.

Note that `env!("CARGO_PKG_VERSION")` reads a compile-time environment variable, which is how version
strings get embedded without a generated `AssemblyInfo.cs`.

## Semver and publishing

Cargo takes semantic versioning seriously enough that the Cargo Book documents exactly which changes are
breaking, and it is a more precise list than anything .NET publishes. The ones that surprise people:
**adding a public field to a struct is breaking** (it breaks struct literal construction and exhaustive
patterns), **adding a variant to a public enum is breaking** (it breaks exhaustive `match`), and **adding
a method to a trait is breaking unless it has a default body**.

The tool for the first two is `#[non_exhaustive]`, which tells downstream crates they may not construct or
exhaustively match the type, reserving your right to extend it:

```rust
#[non_exhaustive]
#[derive(Debug)]
pub struct Config {
    pub strict: bool,
    pub max_findings: usize,
}

#[non_exhaustive]
#[derive(Debug)]
pub enum Severity { Low, High }

fn main() {
    // Within the defining crate you can still construct and match exhaustively.
    let c = Config { strict: true, max_findings: 10 };
    assert!(c.strict);
    assert_eq!(c.max_findings, 10);

    // Downstream crates would need a wildcard arm here.
    let s = Severity::High;
    let label = match s {
        Severity::Low => "low",
        Severity::High => "high",
    };
    assert_eq!(label, "high");
}
```

Publishing is `cargo publish` after `cargo login`, and it is worth knowing three things. **Versions are
immutable and cannot be deleted** — only `cargo yank`ed, which stops new dependents without breaking
existing lockfiles, exactly like NuGet unlisting. **Names are global and first-come**, with no `Company.Product`
namespacing convention, which is why crate names are so terse. And **docs.rs builds and hosts your rustdoc
automatically** on publish, for free, for every version — a service .NET has no equivalent of.

## `polcheck`'s layout

The structure the capstone will use, as a concrete target:

```text
polcheck/
├── Cargo.toml
├── Cargo.lock
├── build.rs                  # embeds the git SHA
├── src/
│   ├── lib.rs                # pub use curated API; mod declarations
│   ├── main.rs               # thin: clap parsing, calls into the lib
│   ├── model.rs              # Resource, Rule, Finding
│   ├── engine/
│   │   ├── mod.rs            # (or engine.rs) evaluate(), scan()
│   │   └── report.rs         # Reporter trait and impls
│   ├── config.rs             # layered configuration
│   └── error.rs              # thiserror-derived PolcheckError
├── tests/
│   ├── cli.rs                # end-to-end via assert_cmd
│   └── engine.rs             # public-API integration tests
├── benches/
│   └── scan.rs               # criterion
└── examples/
    └── minimal.rs
```

`src/lib.rs` would begin:

```rust,ignore
//! Evaluate governance rules against resource inventories.

mod config;
mod engine;
mod error;
mod model;

pub use config::Config;
pub use engine::{evaluate, scan, Reporter};
pub use error::PolcheckError;
pub use model::{Finding, Resource, Rule};
```

Four `mod` lines declaring private modules, then five `pub use` lines defining the entire public API. A
consumer writes `use polcheck::{Rule, scan};` and never learns that `engine` exists — which means you can
restructure `engine` freely without a breaking change. That decoupling is the whole point.

## Before you move on

The mapping is mostly mechanical: crate is assembly, module is namespace, `pub(crate)` is `internal`,
workspace is solution, `Cargo.toml` is `.csproj`. The differences that matter are that the module tree
follows the file tree and every module must be declared by its parent, that privacy is deep (a private item
is visible to descendants but not siblings), and that struct fields have individual visibility so `pub
struct` exposes only the name.

The idiom to actually adopt is `pub use`. Rust lets your file layout and your public API be different
things, and good crates exploit that with a deep private tree and a flat curated surface — plus a `prelude`
module when traits need importing. This is the tool that makes restructuring internals a non-breaking
change.

On the Cargo side, the substantive divergences from NuGet are that version requirements are caret ranges
resolved to the *highest* compatible version rather than the lowest, that two semver-incompatible majors of
one crate can coexist in a binary (which is why there is no binding-redirect equivalent), and that
**features** provide fine-grained conditional compilation with additive semantics — `default-features =
false` is a tool with no NuGet counterpart and it is how you keep builds fast. `build.rs` replaces MSBuild
targets with ordinary Rust, and its one non-negotiable discipline is emitting `rerun-if-changed`.

Finally, Cargo's semver rules are stricter than you expect: adding a public struct field or an enum variant
is breaking, and `#[non_exhaustive]` is how you reserve the right to do it later.

If you can explain why a `.rs` file with no `mod` declaration is not compiled, what `pub use` buys that a
C# namespace cannot, and why Cargo can link two major versions of one crate where NuGet cannot, you are
ready to test it all.

Next: [14 — Testing, documentation, and benchmarks](14-testing-and-docs.md).

### Sources

- *The Book*, ch. 7 "Managing Growing Projects with Packages, Crates, and Modules". <https://doc.rust-lang.org/book/ch07-00-managing-growing-projects-with-packages-crates-and-modules.html> — the module tree, paths, and `pub use`.
- *The Cargo Book*. <https://doc.rust-lang.org/cargo/> — manifest reference, profiles, workspaces, and build scripts.
- *The Cargo Book*, "SemVer Compatibility". <https://doc.rust-lang.org/cargo/reference/semver.html> — the authoritative list of which changes are major, minor, or patch.
- *The Cargo Book*, "Features". <https://doc.rust-lang.org/cargo/reference/features.html> — additivity, unification, `dep:` syntax, and `default-features`.
- *The Cargo Book*, "Build Scripts". <https://doc.rust-lang.org/cargo/reference/build-scripts.html> — the `cargo::` directive set and rerun conditions.
- *The Cargo Book*, "Resolver". <https://doc.rust-lang.org/cargo/reference/resolver.html> — version unification, multiple-major coexistence, and resolver v3.
- *Rust API Guidelines*, "Naming" and "Documentation". <https://rust-lang.github.io/api-guidelines/> — conventions for public surfaces and re-exports.
- *The Rust Reference*, "Visibility and Privacy". <https://doc.rust-lang.org/reference/visibility-and-privacy.html> — the normative rules, including `pub(in path)`.
