//! Drill 03 — Syntax orientation: expressions, shadowing, integer overflow.
//!
//! Read the test names first: each one states the idea it proves.

/// Sum a slice, returning `None` on overflow rather than wrapping or panicking.
/// Look at the `checked_*` family on the integer types.
pub fn checked_sum(_values: &[i32]) -> Option<i32> {
    todo!("return None instead of overflowing")
}

/// Classify a compliance score as a single `match` *expression* whose value is
/// the return value — no `return`, no mutable accumulator.
/// 0 => "empty", 1..=49 => "failing", 50..=89 => "partial",
/// 90..=99 => "healthy", 100 => "perfect", anything else => "invalid".
pub fn classify(_score: u32) -> &'static str {
    todo!("one match expression, no early returns")
}

/// Count the distinct, non-empty, trimmed comma-separated ids in `raw`.
/// Use shadowing — repeated `let raw = ...` — rather than inventing `raw2`.
pub fn distinct_ids(_raw: &str) -> usize {
    todo!("shadow the binding as you refine it")
}

/// Increment, wrapping 255 back to 0 — explicitly.
pub fn wrapping_tick(_counter: u8) -> u8 {
    todo!("wrapping is opt-in, never implicit")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn checked_sum_adds_normally() {
        assert_eq!(checked_sum(&[1, 2, 3]), Some(6));
        assert_eq!(checked_sum(&[]), Some(0));
    }

    #[test]
    fn checked_sum_reports_overflow_instead_of_wrapping() {
        assert_eq!(checked_sum(&[i32::MAX, 1]), None);
        assert_eq!(checked_sum(&[i32::MIN, -1]), None);
    }

    #[test]
    fn classify_is_exhaustive_over_the_range() {
        assert_eq!(classify(0), "empty");
        assert_eq!(classify(49), "failing");
        assert_eq!(classify(50), "partial");
        assert_eq!(classify(90), "healthy");
        assert_eq!(classify(100), "perfect");
        assert_eq!(classify(101), "invalid");
    }

    #[test]
    fn shadowing_changes_the_type_of_a_binding() {
        assert_eq!(distinct_ids(" vm-1, vm-2 ,vm-1 , "), 2);
        assert_eq!(distinct_ids(""), 0);
    }

    #[test]
    fn wrapping_is_explicit() {
        assert_eq!(wrapping_tick(254), 255);
        assert_eq!(wrapping_tick(255), 0);
    }
}
