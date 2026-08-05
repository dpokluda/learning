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
