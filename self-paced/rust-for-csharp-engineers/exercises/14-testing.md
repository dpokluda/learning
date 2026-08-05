# Exercises 14 — Testing and documentation

> **Covers:** [14 — Testing and documentation](../14-testing-and-docs.md). **Code:** `drills/src/ch14.rs`. **Answers:** [answers/14-testing.md](answers/14-testing.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** Where do unit tests live in Rust, and why is that not considered a design smell?

**A2.** What is a doc test, and what does it change about documentation?

**A3.** `should_panic`, `no_run`, `ignore`, `compile_fail` — what is each for?

**A4.** Rust's built-in test harness has no `[Theory]`/`[InlineData]`. What do you do instead, and is that a loss?

**A5.** How do benchmarks work, and what is the BenchmarkDotNet analogue?

**A6.** What is property-based testing, and which crate provides it?

## Part B — Exercise

Open `drills/src/ch14.rs`. Half of this drill is written *in the doc comments*,
which is the point.

Implement the four functions, then write the doc examples the TODOs ask for: a
plain one that will be compiled and executed, one that uses `# ` to hide a setup
line from the rendered page while still compiling it, a `should_panic` one, and
a `no_run` one. Run `cargo test --doc` and watch them execute. Then break one
deliberately — change an expected value — and confirm the build fails. That is
the property XML doc comments never had.

One implementation detail is a real bug worth finding on your own:
`compliance_percent` has a test that passes `u32::MAX` for both arguments, and
the obvious `compliant * 100 / total` overflows long before it gets there.

Run it with `cargo test ch14` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 14 — Tests that are documentation, and documentation that is tested.
//!
//! Half of this drill is written in doc comments. `cargo test` compiles and
//! runs them, so a wrong example is a failing build — which is what .NET XML
//! doc comments never gave you. `cargo test --doc` runs only those.

/// Compute the compliance percentage, rounded down. `total == 0` means 100.
///
/// TODO: add a doc example here. It must `use drills::ch14::compliance_percent;`
/// and assert that 9 of 10 is 90 and 0 of 0 is 100. It will actually run.
///
/// TODO: add a second example showing that more compliant than total saturates
/// at 100. Prefix the `use` line with `# ` to hide it from the rendered page
/// while still compiling it.
pub fn compliance_percent(_compliant: u32, _total: u32) -> u32 {
    todo!("watch for overflow in `compliant * 100` — widen to u64 first")
}

/// Panics when the budget is negative, because that is a programming error
/// rather than a runtime condition.
///
/// TODO: add a ```should_panic doc example.
pub fn assert_budget(_budget: i32) -> u32 {
    todo!("assert! with a message that names the offending value")
}

/// TODO: add a ```no_run doc example — it must compile but must not execute.
/// This is the tag for examples that would open a socket or write a file.
pub fn documented_but_not_run() {}

/// 100 => "compliant", 70..=99 => "degraded", anything else => "non-compliant".
pub fn severity_label(_pct: u32) -> &'static str {
    todo!()
}
```

The test module that follows this in the file is the specification — read it before you write anything.
