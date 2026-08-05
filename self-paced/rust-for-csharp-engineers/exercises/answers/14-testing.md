# Answers 14 — Testing and documentation

> Exercises: [14-testing.md](../14-testing.md)

## Part A

**A1. Where do unit tests live in Rust, and why is that not considered a design smell?**

In a `#[cfg(test)] mod tests` block inside the file they test, compiled only under `cargo test` and stripped from release builds entirely. It is not a smell because the module is a child of the code under test, so it can reach that module's *private* items — which is precisely what unit testing usually needs, and what C# forces you to obtain through `InternalsVisibleTo`, `public` visibility you did not want, or reflection. Tests that should exercise only the public API go in `tests/`, which is compiled as a separate crate and therefore sees exactly what a real consumer sees. The two locations encode the distinction that xUnit projects usually leave to convention.

**A2. What is a doc test, and what does it change about documentation?**

A fenced code block in a `///` doc comment is compiled and executed by `cargo test`. That single fact changes documentation from prose that drifts into prose that cannot drift: a renamed method, a changed signature, or a wrong assertion breaks the build. It also changes what you write, because the example must be a complete, runnable program fragment — so it gets the imports right and shows real usage rather than an elided sketch. .NET's XML doc comments have no equivalent; `<example>` blocks are unchecked text, which is why so many of them reference APIs that no longer exist.

**A3. `should_panic`, `no_run`, `ignore`, `compile_fail` — what is each for?**

`#[should_panic(expected = "...")]` on a test asserts that it panics with a message containing that substring, which is the `Assert.Throws` analogue. On a doc test, ```` ```should_panic ```` does the same. ```` ```no_run ```` compiles the example but does not execute it — right for anything that opens a socket, writes a file, or blocks. ```` ```ignore ```` skips it entirely, including compilation, and should be rare because it is where wrong examples hide; prefer `no_run` or `text` when you can. ```` ```compile_fail ```` asserts the example does *not* compile, which is how you document a borrow-checker or type-level guarantee.

**A4. Rust's built-in test harness has no `[Theory]`/`[InlineData]`. What do you do instead, and is that a loss?**

You write a slice of cases and loop, putting the input in the assertion message so a failure names the offending row. It is only a mild loss: you give up per-case test names and per-case isolation, but you get to see all the data in one place and you avoid the attribute gymnastics that inline data requires for anything more complex than primitives. When per-case isolation genuinely matters, a declarative macro or the `rstest` crate provides parameterised tests properly. Note also that Rust tests run in parallel *threads* by default, which is stricter than xUnit's per-class parallelism, so shared mutable state and current-directory changes will bite you sooner.

**A5. How do benchmarks work, and what is the BenchmarkDotNet analogue?**

The built-in `#[bench]` attribute is still unstable, so the ecosystem standard is `criterion`, added as a dev-dependency with a `[[bench]]` target and `harness = false`. It does statistical analysis over many samples, detects regressions against a saved baseline, and produces plots — very much the role BenchmarkDotNet plays. The equivalent of `[Benchmark]`'s consume-the-result problem is `std::hint::black_box`, which prevents the optimiser from deleting the computation you are trying to measure. The important discipline is the same in both worlds: benchmark release builds, and never trust a single run.

**A6. What is property-based testing, and which crate provides it?**

Instead of asserting on fixed examples, you assert a *property* that must hold for all inputs — round-tripping, idempotence, an invariant — and the framework generates many random inputs to try to falsify it, then *shrinks* any failure down to a minimal counterexample. `proptest` is the mainstream crate, with `quickcheck` as the older alternative; the .NET analogue is FsCheck. It is worth the effort exactly where example-based tests are weakest: parsers, serialisers, comparators, and anything with an algebraic law, where the interesting inputs are the ones you did not think of.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 14 — Tests that are documentation, and documentation that is tested.
//!
//! The drill here is the one thing .NET has no equivalent of: examples in doc
//! comments are compiled and executed by `cargo test`, so they cannot rot.

/// Compute the compliance percentage, rounded down.
///
/// This example is a real test. Break the function and `cargo test` fails on
/// *this block*, not just on the unit tests below.
///
/// ```
/// use drills::ch14::compliance_percent;
///
/// assert_eq!(compliance_percent(9, 10), 90);
/// assert_eq!(compliance_percent(0, 0), 100);
/// ```
///
/// Failure is expressed in the type, so the doc example can show it too:
///
/// ```
/// # use drills::ch14::compliance_percent;
/// // More compliant than total is a caller bug, and it saturates rather than
/// // wrapping.
/// assert_eq!(compliance_percent(11, 10), 100);
/// ```
pub fn compliance_percent(compliant: u32, total: u32) -> u32 {
    if total == 0 {
        return 100;
    }
    let pct = (u64::from(compliant) * 100) / u64::from(total);
    pct.min(100) as u32
}

/// Panics when the budget is negative, because that is a programming error
/// rather than a runtime condition.
///
/// ```should_panic
/// use drills::ch14::assert_budget;
/// assert_budget(-1);
/// ```
pub fn assert_budget(budget: i32) -> u32 {
    assert!(budget >= 0, "budget must be non-negative, got {budget}");
    budget as u32
}

/// A snippet that must *compile* but should not run — the `no_run` tag. Useful
/// for examples that would open a socket or write a file.
///
/// ```no_run
/// use drills::ch14::compliance_percent;
/// let path = std::env::args().nth(1).unwrap();
/// println!("{path}: {}", compliance_percent(1, 2));
/// ```
pub fn documented_but_not_run() {}

pub fn severity_label(pct: u32) -> &'static str {
    match pct {
        100 => "compliant",
        70..=99 => "degraded",
        _ => "non-compliant",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The table-driven shape. Rust has no `[Theory]`/`[InlineData]` attribute
    /// in the standard harness, and it turns out not to need one: a slice of
    /// tuples plus a loop is clearer and gives a better failure message.
    #[test]
    fn severity_label_covers_every_band() {
        let cases: &[(u32, &str)] = &[
            (0, "non-compliant"),
            (69, "non-compliant"),
            (70, "degraded"),
            (99, "degraded"),
            (100, "compliant"),
        ];

        for (input, expected) in cases {
            assert_eq!(
                severity_label(*input),
                *expected,
                "severity_label({input}) should be {expected}"
            );
        }
    }

    #[test]
    fn division_never_panics_on_zero_total() {
        assert_eq!(compliance_percent(0, 0), 100);
        assert_eq!(compliance_percent(5, 0), 100);
    }

    #[test]
    fn rounding_is_toward_zero() {
        assert_eq!(compliance_percent(1, 3), 33);
        assert_eq!(compliance_percent(2, 3), 66);
    }

    #[test]
    fn large_inputs_do_not_overflow_the_multiplication() {
        // The naive `compliant * 100` in u32 overflows here; widening to u64
        // is the fix, and this test is what proves it.
        assert_eq!(compliance_percent(u32::MAX, u32::MAX), 100);
        assert_eq!(compliance_percent(u32::MAX / 2, u32::MAX), 49);
    }

    #[test]
    #[should_panic(expected = "budget must be non-negative")]
    fn should_panic_matches_on_the_message() {
        assert_budget(-5);
    }

    #[test]
    #[ignore = "demonstrates `cargo test -- --ignored`; nothing slow actually happens"]
    fn expensive_test_is_opt_in() {
        assert_eq!(compliance_percent(1, 1), 100);
    }
}
```
