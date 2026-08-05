# 14 — Testing, documentation, and benchmarks

Testing is where Rust's tooling story is most immediately better than .NET's, and the reason is that
almost all of it is built in. There is no test framework to choose, no NuGet package to reference, no
`<IsTestProject>` flag, no runner to install. `#[test]` is a language attribute, `cargo test` is a
first-party command, and — the part with no .NET equivalent at all — the code examples in your
documentation are compiled and executed as tests, so your docs cannot silently rot.

> **Prerequisite:** [13 — Modules, crates, and workspaces](13-modules-and-crates.md).

## Unit tests live next to the code

The idiomatic place for a unit test is a `#[cfg(test)] mod tests` block **in the same file as the code it
tests**. That looks wrong to a C# developer used to a parallel `MyProject.Tests` assembly, and it is
deliberate: because a child module can see its parent's private items, tests in the same file can test
private functions without any `InternalsVisibleTo` equivalent.

```rust
pub fn normalize_tag(key: &str) -> String {
    key.trim().to_ascii_lowercase()
}

fn is_reserved(key: &str) -> bool {          // private
    matches!(key, "id" | "type" | "location")
}

#[cfg(test)]
mod tests {
    use super::*;                            // pull the parent module into scope

    #[test]
    fn normalizes_case_and_whitespace() {
        assert_eq!(normalize_tag("  Owner "), "owner");
    }

    #[test]
    fn detects_reserved_keys() {
        assert!(is_reserved("id"));          // private function: visible to the child module
        assert!(!is_reserved("owner"));
    }

    #[test]
    #[should_panic(expected = "index out of bounds")]
    fn panics_are_testable() {
        let v: Vec<i32> = Vec::new();
        let _ = v[0];
    }

    #[test]
    #[ignore = "slow; run with --ignored"]
    fn expensive() {
        assert_eq!(2 + 2, 4);
    }
}

fn main() {
    assert_eq!(normalize_tag("A"), "a");
}
```

`#[cfg(test)]` means the module is compiled only under `cargo test`, so test code adds nothing to your
release binary — the compile-time equivalent of a separate test assembly, without the assembly.

The assertion vocabulary is small: `assert!`, `assert_eq!`, `assert_ne!`, and their `debug_` variants which
compile out in release. All accept a trailing format message. There is no `Assert.Throws` because
`should_panic` covers panics and `Result` errors are just values you match on:

```rust
fn parse_severity(s: &str) -> Result<u8, String> {
    s.parse::<u8>().map_err(|_| format!("bad severity '{s}'"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_non_numeric() {
        let err = parse_severity("high").unwrap_err();
        assert!(err.contains("bad severity"));
    }

    /// Tests can return Result, so `?` works and an Err is a failure.
    #[test]
    fn accepts_numeric() -> Result<(), String> {
        assert_eq!(parse_severity("3")?, 3);
        Ok(())
    }
}

fn main() { assert!(parse_severity("3").is_ok()); }
```

That last pattern — a test returning `Result<(), E>` so you can use `?` instead of a chain of `unwrap()`s —
is worth adopting immediately. xUnit gained something similar with async `Task` tests, but the ergonomics
here are better.

The comparison table, for orientation:

| xUnit / NUnit | Rust |
|---|---|
| `[Fact]` / `[Test]` | `#[test]` |
| `[Theory]` + `[InlineData]` | a loop, or `rstest` |
| `Assert.Equal(a, b)` | `assert_eq!(a, b)` |
| `Assert.Throws<T>` | `#[should_panic(expected = "...")]` |
| `[Skip]` / `[Ignore]` | `#[ignore = "reason"]` |
| test project | `#[cfg(test)] mod tests` |
| `InternalsVisibleTo` | (unnecessary) |
| `IClassFixture`, constructor | a helper `fn` (no lifecycle hooks) |
| `dotnet test` | `cargo test` |

The absence to plan around is **parameterised tests**. There is no built-in `[Theory]`; the std answer is a
loop over an array of cases, which is honestly fine and produces one test that reports the failing case:

```rust
fn normalize_tag(key: &str) -> String { key.trim().to_ascii_lowercase() }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn table_driven() {
        let cases = [
            ("Owner", "owner"),
            ("  ENV  ", "env"),
            ("already", "already"),
        ];
        for (input, expected) in cases {
            assert_eq!(normalize_tag(input), expected, "input was {input:?}");
        }
    }
}

fn main() {}
```

The `rstest` crate provides real `#[case]` attributes if you want them. Note the trailing message argument
— without it, a table-driven failure tells you the values but not which row, which is exactly the papercut
`[InlineData]` exists to avoid.

There are also **no lifecycle hooks**: no `[SetUp]`, no constructor-per-test, no `IDisposable` teardown.
The idiom is a helper function returning a fixture, and `Drop` handles cleanup — a `tempfile::TempDir` in a
local variable deletes itself when the test ends, which is tidier than `IDisposable` because you cannot
forget it.

## Integration tests

Files in `tests/` are each compiled as a **separate crate** that links your library as an external
dependency. That means they see exactly what a real consumer sees — only the public API — which makes them
the right place to test that your public surface is actually usable.

```rust,ignore
// tests/engine.rs
use polcheck::{scan, Resource, Rule};

#[test]
fn scan_reports_missing_tags() {
    let resources = vec![Resource::new("res-1", "storage", "westus2")];
    let rules = vec![Rule::require_tag("owner")];

    let findings = scan(&rules, &resources);

    assert_eq!(findings.len(), 1);
    assert!(findings[0].reason.contains("owner"));
}
```

Shared helper code goes in `tests/common/mod.rs` rather than `tests/common.rs`, because every top-level
`.rs` file in `tests/` becomes its own test binary and you do not want an empty one. That subdirectory rule
is a small piece of trivia that trips up everyone once.

Because each file is a separate binary, integration tests are compiled and linked independently, which is
why a large `tests/` directory can dominate build time. Consolidating related tests into fewer files is the
standard fix.

## `cargo test`

The runner's behaviour differs from `dotnet test` in two ways worth knowing before they confuse you.

**Tests run in parallel by default, one thread per test**, and **stdout is captured and only shown for
failures.** Both are usually what you want and occasionally not:

```powershell
cargo test                          # everything: unit, integration, and doc tests
cargo test scan                     # only tests whose name contains "scan"
cargo test -- --nocapture           # show println! output from passing tests
cargo test -- --test-threads=1      # serialize, e.g. when tests share a directory
cargo test -- --ignored             # run only the #[ignore]d ones
cargo test --lib                    # unit tests only
cargo test --test engine            # one integration test file
cargo test --doc                    # only documentation examples
cargo test --workspace              # every member
```

The `--` separates Cargo's arguments from the test harness's. Everything after it goes to the binary
`libtest` produced, which is the same shape as `dotnet test -- RunConfiguration...` but used far more often.

## Documentation tests: the feature .NET does not have

This is the part to get excited about. Doc comments use `///` (for the following item) or `//!` (for the
enclosing item), they are markdown, and **every code block inside them is compiled and run by
`cargo test`**.

```rust
/// Normalises a tag key for comparison.
///
/// Keys are trimmed and lowercased, so `"  Owner "` and `"owner"` compare equal.
///
/// # Examples
///
/// ```
/// # use doccheck::normalize_tag;
/// assert_eq!(normalize_tag("  Owner "), "owner");
/// assert_eq!(normalize_tag("ENV"), "env");
/// ```
///
/// # Panics
///
/// Never panics.
pub fn normalize_tag(key: &str) -> String {
    key.trim().to_ascii_lowercase()
}

fn main() {
    assert_eq!(normalize_tag(" X "), "x");
}
```

Contrast that with C#'s `<example>` XML doc tag, which is inert text that no compiler ever looks at. Every
.NET codebase has XML docs containing examples that stopped compiling three refactors ago. In Rust that is
structurally impossible: change the signature and `cargo test` fails on your own documentation.

The mechanics are worth knowing because they explain the odd syntax you see in real crates. A doc example
with no `fn main` gets wrapped in one automatically, so most examples are just statements. Lines beginning
`# ` are **hidden from the rendered docs but still compiled**, which is how you show a clean example while
including the boilerplate imports it needs. And the fence's info string controls behaviour:

| Fence | Behaviour |
|---|---|
| ` ```rust ` or bare ` ``` ` | compile and run |
| ` ```no_run ` | compile but do not run (network, long-running) |
| ` ```ignore ` | do not even compile (pseudocode) |
| ` ```compile_fail ` | assert that it does **not** compile |
| ` ```should_panic ` | assert that it panics |
| ` ```text `, ` ```toml `, … | not Rust; not compiled |

`compile_fail` deserves a special mention: it lets you *test* that an API misuse is rejected, which is a
form of assertion C# simply cannot express. This book uses it throughout to prove that the borrow-checker
errors it describes are real.

The conventional section headings — `# Examples`, `# Panics`, `# Errors`, `# Safety` — are what the API
guidelines expect, and `# Safety` is mandatory on any public `unsafe fn`. Intra-doc links use bracket
syntax (`[`Rule`]`, `[`scan`](crate::scan)`) and are checked at build time, so a link to a renamed item is a
warning rather than a 404.

Crate-level documentation uses `//!` at the top of `lib.rs`, and lints let you enforce coverage:

```rust,ignore
//! # polcheck
//!
//! Evaluate governance rules against resource inventories.
//!
//! ```
//! use polcheck::{Rule, Resource, evaluate};
//! let rule = Rule::require_tag("owner");
//! let resource = Resource::new("res-1", "storage", "westus2");
//! assert!(!evaluate(&rule, &resource).is_compliant());
//! ```

#![warn(missing_docs)]                   // every public item needs a doc comment
#![warn(rustdoc::broken_intra_doc_links)]
```

`cargo doc --open` builds and opens the rendered site. The output is the same format as docs.rs, which
builds it automatically for every published version — so writing good docs has an immediate, public payoff
that .NET's ecosystem has never quite matched.

## Property-based testing with `proptest`

Table-driven tests check the cases you thought of. Property tests check invariants against generated
inputs, shrinking any failure to a minimal reproducer. If you have used FsCheck this is familiar; the
Rust ecosystem's two options are `proptest` (generation-based, like Hypothesis) and `quickcheck` (typed,
like the Haskell original). `proptest` is the more widely used.

```toml
[dev-dependencies]
proptest = "1.11.0"
```

```rust,ignore
use proptest::prelude::*;

fn normalize_tag(key: &str) -> String {
    key.trim().to_ascii_lowercase()
}

proptest! {
    /// Idempotence: normalising twice is the same as normalising once.
    #[test]
    fn normalize_is_idempotent(s in ".*") {
        let once = normalize_tag(&s);
        let twice = normalize_tag(&once);
        prop_assert_eq!(once, twice);
    }

    /// Normalisation never lengthens the key.
    #[test]
    fn normalize_never_grows(s in "\\PC*") {
        prop_assert!(normalize_tag(&s).len() <= s.len());
    }

    /// Round-tripping through the parser preserves the value.
    #[test]
    fn severity_roundtrips(n in 0u8..=5) {
        let text = n.to_string();
        prop_assert_eq!(text.parse::<u8>().unwrap(), n);
    }
}
```

Two things make proptest worth the dependency. Inputs are described by **strategies** — `0u8..=5`, a regex
for strings, `prop::collection::vec(any::<u32>(), 0..100)` — which are composable. And when a case fails,
proptest **shrinks** it to the smallest failing input and writes it to a `proptest-regressions` file that
is replayed on subsequent runs, so the failure becomes a permanent regression test. FsCheck does the same;
the difference is how routine it is in Rust codebases.

The properties worth reaching for are the classics: round-trips (`parse(render(x)) == x`), idempotence,
invariants that must hold for all inputs, and equivalence between a fast implementation and an obviously
correct slow one.

## Benchmarking with `criterion`

`cargo bench` exists but the built-in `#[bench]` attribute is still unstable, so the ecosystem uses
**criterion**, which is BenchmarkDotNet's counterpart: statistical analysis, outlier detection, warmup,
and HTML reports with regression detection against the previous run.

```toml
[dev-dependencies]
criterion = "0.8.0"

[[bench]]
name = "scan"
harness = false          # criterion supplies its own main
```

```rust,ignore
// benches/scan.rs
use std::hint::black_box;
use criterion::{criterion_group, criterion_main, Criterion};

fn normalize_tag(key: &str) -> String {
    key.trim().to_ascii_lowercase()
}

fn bench_normalize(c: &mut Criterion) {
    c.bench_function("normalize_tag/short", |b| {
        b.iter(|| normalize_tag(black_box("  Owner  ")))
    });

    let mut group = c.benchmark_group("normalize_tag/sizes");
    for len in [8usize, 64, 512] {
        let input = " ".repeat(2) + &"A".repeat(len);
        group.bench_with_input(format!("len-{len}"), &input, |b, s| {
            b.iter(|| normalize_tag(black_box(s)))
        });
    }
    group.finish();
}

criterion_group!(benches, bench_normalize);
criterion_main!(benches);
```

Two details that matter. **`harness = false`** tells Cargo not to link libtest, because criterion provides
its own `main` via the `criterion_main!` macro. And **`black_box`** — now `std::hint::black_box`, since
`criterion::black_box` is deprecated — is the optimisation barrier that stops LLVM from noticing your
benchmark's result is unused and deleting the whole computation. BenchmarkDotNet's `Consumer` and
`[Benchmark]` return values do the same job; the failure mode when you forget is identical and equally
baffling (impossibly fast results).

Run with `cargo bench`; criterion writes `target/criterion/` with HTML reports and compares each run
against the last, reporting "Performance has improved" or "regressed" with a confidence interval.

## The rest of the toolbox

A few crates round out the testing story, all with clear .NET counterparts:

| Need | Crate | .NET analogue |
|---|---|---|
| temp files/dirs, auto-deleted | `tempfile` | `Path.GetTempFileName` + finally |
| run your CLI end to end | `assert_cmd` + `predicates` | `Process.Start` + asserts |
| snapshot/approval testing | `insta` | Verify, ApprovalTests |
| HTTP mocking | `wiremock` | WireMock.Net |
| better assertion output | `pretty_assertions` | FluentAssertions |
| parameterised tests | `rstest` | `[Theory]` / `[TestCase]` |
| test doubles | `mockall` | Moq, NSubstitute |

`assert_cmd` is the one to know for a CLI, because it turns end-to-end testing into something readable:

```rust,ignore
// tests/cli.rs
use assert_cmd::Command;
use predicates::prelude::*;

#[test]
fn reports_missing_tags() {
    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "--rules", "tests/data/rules.json", "tests/data/resources.json"])
        .assert()
        .failure()                                   // non-zero exit for non-compliance
        .stdout(predicate::str::contains("missing tag 'owner'"));
}
```

A word on **mocking**, because the instinct transfers badly. In C#, mocking is pervasive because everything
is an interface behind a DI container. In Rust the more common approach is to make the code generic over a
trait and instantiate it with a test implementation — static dispatch, no proxy generation, no runtime
reflection:

```rust
trait Clock {
    fn now_secs(&self) -> u64;
}

struct SystemClock;
impl Clock for SystemClock {
    fn now_secs(&self) -> u64 {
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0)
    }
}

struct FixedClock(u64);
impl Clock for FixedClock {
    fn now_secs(&self) -> u64 { self.0 }
}

fn is_stale<C: Clock>(clock: &C, created_secs: u64, max_age: u64) -> bool {
    clock.now_secs().saturating_sub(created_secs) > max_age
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_stale_records() {
        let clock = FixedClock(1_000);
        assert!(is_stale(&clock, 500, 100));
        assert!(!is_stale(&clock, 950, 100));
    }
}

fn main() {
    assert!(!is_stale(&FixedClock(10), 5, 100));
}
```

`mockall` exists and generates mocks from a trait definition when you genuinely need call verification, but
reach for a hand-written fake first. It is usually five lines and always clearer.

## Coverage, CI, and lints

`cargo llvm-cov` (a cargo subcommand you install once) is the coverage tool, producing lcov or HTML output;
it replaces the older `tarpaulin` for most purposes and is the closest thing to Coverlet.

The CI shape that has become standard is worth copying wholesale:

```powershell
cargo fmt --check                        # formatting is not a matter of opinion
cargo clippy --all-targets -- -D warnings  # lints as errors
cargo test --workspace --all-features
cargo doc --no-deps                      # catches broken intra-doc links
```

`cargo fmt --check` is the piece with no real .NET counterpart in practice. `rustfmt` has essentially no
configuration knobs that anyone uses, so all Rust code looks the same and formatting arguments do not
happen. `dotnet format` exists but the ecosystem never converged on it.

Clippy is the analyzer, and `-D warnings` in CI with a curated allow-list is the norm. Crate-level lint
configuration lives at the top of `lib.rs` or, better, in `Cargo.toml`:

```toml
[lints.rust]
missing_docs = "warn"
unsafe_code = "forbid"

[lints.clippy]
unwrap_used = "warn"
expect_used = "warn"
pedantic = { level = "warn", priority = -1 }
```

`unsafe_code = "forbid"` is stronger than `deny` — it cannot be overridden by an inner `#[allow]` — and it
is a genuinely useful thing to put in an application crate.

## `polcheck`: the full testing shape

Bringing it together, here is the engine with its unit tests, doc examples, and the invariant a property
test would check.

```rust
use std::collections::HashMap;

/// A resource under evaluation.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Resource {
    pub id: String,
    pub tags: HashMap<String, String>,
}

/// A rule that a resource either satisfies or violates.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Rule {
    RequireTag(String),
    Not(Box<Rule>),
}

/// Evaluates `rule` against `resource`.
///
/// Returns `true` when the resource is compliant.
///
/// # Examples
///
/// ```
/// # use std::collections::HashMap;
/// # use doccheck::m_14_testing_and_docs::{Resource, Rule, evaluate};
/// let r = Resource {
///     id: "res-1".into(),
///     tags: HashMap::from([("owner".into(), "platform".into())]),
/// };
/// assert!(evaluate(&Rule::RequireTag("owner".into()), &r));
/// assert!(!evaluate(&Rule::RequireTag("env".into()), &r));
/// ```
pub fn evaluate(rule: &Rule, resource: &Resource) -> bool {
    match rule {
        Rule::RequireTag(key) => resource.tags.contains_key(key),
        Rule::Not(inner) => !evaluate(inner, resource),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn resource_with(tags: &[(&str, &str)]) -> Resource {
        Resource {
            id: "res-1".to_owned(),
            tags: tags.iter().map(|(k, v)| (k.to_string(), v.to_string())).collect(),
        }
    }

    #[test]
    fn require_tag_matches_present_key() {
        let r = resource_with(&[("owner", "platform")]);
        assert!(evaluate(&Rule::RequireTag("owner".into()), &r));
    }

    #[test]
    fn require_tag_rejects_absent_key() {
        let r = resource_with(&[]);
        assert!(!evaluate(&Rule::RequireTag("owner".into()), &r));
    }

    /// The property a proptest would check over generated rules:
    /// double negation is the identity.
    #[test]
    fn double_negation_is_identity() {
        let r = resource_with(&[("owner", "platform")]);
        for key in ["owner", "env", ""] {
            let rule = Rule::RequireTag(key.into());
            let double = Rule::Not(Box::new(Rule::Not(Box::new(rule.clone()))));
            assert_eq!(evaluate(&rule, &r), evaluate(&double, &r), "key={key}");
        }
    }
}

fn main() {
    let r = Resource { id: "x".into(), tags: HashMap::new() };
    assert!(!evaluate(&Rule::RequireTag("owner".into()), &r));
}
```

Note the doc example's hidden `# use` lines: they make the example compile without cluttering the rendered
page with imports the reader does not need to see.

## Before you move on

Rust's testing story is built in rather than chosen. `#[test]` is a language attribute, `cargo test` is a
first-party runner, and `#[cfg(test)] mod tests` in the same file gives you private-function access with no
`InternalsVisibleTo`. Integration tests in `tests/` compile as separate crates against your public API,
which makes them an honest check that the surface is usable. Tests run in parallel with output captured,
and a test returning `Result` lets you use `?` — adopt that immediately.

The genuinely novel feature is doc tests. Every code block in a `///` comment is compiled and run, so
documentation examples cannot rot, `# ` hides boilerplate from the rendered page, and `compile_fail` lets
you assert that misuse is rejected. There is nothing like it in .NET, and combined with docs.rs building
your documentation automatically on publish, it changes the economics of writing good docs.

The gaps relative to xUnit are real but small: no parameterised-test attribute (use a loop or `rstest`) and
no lifecycle hooks (use a helper function and let `Drop` clean up). Property testing via `proptest` and
benchmarking via `criterion` map onto FsCheck and BenchmarkDotNet closely, with `harness = false` and
`std::hint::black_box` being the two setup details that bite if you miss them. And mocking is usually a
hand-written trait implementation rather than a generated proxy, because static dispatch makes fakes free.

If you can explain why a unit test can see a private function but an integration test cannot, what
`# ` at the start of a doc-example line does, and why `black_box` exists, you are ready for concurrency.

Next: [15 — Concurrency: threads, channels, and data parallelism](15-concurrency.md).

### Sources

- *The Book*, ch. 11 "Writing Automated Tests". <https://doc.rust-lang.org/book/ch11-00-testing.html> — `#[test]`, `should_panic`, integration tests, and the `tests/common` convention.
- *The rustdoc Book*, "Documentation tests". <https://doc.rust-lang.org/rustdoc/write-documentation/documentation-tests.html> — hidden lines, attribute fences, and how examples are wrapped.
- *The rustdoc Book*, "Linking to items by name". <https://doc.rust-lang.org/rustdoc/write-documentation/linking-to-items-by-name.html> — intra-doc links and their build-time checking.
- *The Cargo Book*, `cargo test`. <https://doc.rust-lang.org/cargo/commands/cargo-test.html> — target selection and harness arguments.
- *proptest* documentation. <https://docs.rs/proptest/> and the proptest book <https://proptest-rs.github.io/proptest/> — strategies, shrinking, and the regressions file.
- *Criterion.rs* user guide. <https://bheisler.github.io/criterion.rs/book/> — `harness = false`, benchmark groups, and regression reporting.
- `std::hint::black_box`. <https://doc.rust-lang.org/std/hint/fn.black_box.html> — the optimisation barrier, stable since Rust 1.66.
- *Rust API Guidelines*, "Documentation". <https://rust-lang.github.io/api-guidelines/documentation.html> — the `# Examples` / `# Panics` / `# Errors` / `# Safety` conventions.
- *The Cargo Book*, "The lints section". <https://doc.rust-lang.org/cargo/reference/manifest.html#the-lints-section> — configuring lints in `Cargo.toml`.
