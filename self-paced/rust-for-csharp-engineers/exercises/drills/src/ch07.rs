//! Drill 07 — Algebraic data types and exhaustive pattern matching.
//!
//! A miniature of `polcheck`'s rule engine, and the most important drill in
//! Part 1. The enum is given; the recursive evaluation is yours.
//!
//! When the tests pass, do the follow-up: add a `Regex { field, pattern }`
//! variant and *do not* touch anything else. The compiler will list every site
//! that now needs updating. That list is the thing a C# type switch over a
//! record hierarchy can never give you.

use std::collections::HashMap;

pub type Resource = HashMap<String, String>;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Condition {
    /// Field exists and equals `value`.
    Equals { field: String, value: String },
    /// Field exists at all.
    Exists { field: String },
    /// Field exists and its value is one of `values`.
    In { field: String, values: Vec<String> },
    All(Vec<Condition>),
    Any(Vec<Condition>),
    /// `Box` is what gives this recursive enum a finite size.
    Not(Box<Condition>),
}

impl Condition {
    pub fn equals(_field: &str, _value: &str) -> Self {
        todo!()
    }

    pub fn exists(_field: &str) -> Self {
        todo!()
    }

    pub fn any_of(_field: &str, _values: &[&str]) -> Self {
        todo!()
    }

    /// Named `negate`, not `not`: clippy rejects an inherent `not` because a
    /// reader would expect `std::ops::Not`.
    pub fn negate(_inner: Condition) -> Self {
        todo!()
    }

    /// The evaluator. Handle every variant — no catch-all `_` arm, which is
    /// what makes the exhaustiveness check worth anything.
    ///
    /// Two tests pin down the edge cases: a missing field is `false` rather
    /// than an error, and empty `All`/`Any` are *not* symmetric.
    pub fn evaluate(&self, _resource: &Resource) -> bool {
        todo!()
    }

    /// How many leaf tests does this tree contain?
    pub fn leaf_count(&self) -> usize {
        todo!()
    }
}

/// Return the uppercased value of `field`, or `"<missing>"`. Use `let ... else`
/// so the happy path stays unindented.
pub fn required_tag(_resource: &Resource, _field: &str) -> String {
    todo!("use let-else so the happy path stays unindented")
}

pub fn resource(pairs: &[(&str, &str)]) -> Resource {
    pairs.iter().map(|(k, v)| (k.to_string(), v.to_string())).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn vm() -> Resource {
        resource(&[("type", "Microsoft.Compute/virtualMachines"), ("env", "prod"), ("owner", "ops")])
    }

    #[test]
    fn leaf_conditions_test_single_fields() {
        assert!(Condition::equals("env", "prod").evaluate(&vm()));
        assert!(!Condition::equals("env", "dev").evaluate(&vm()));
        assert!(Condition::exists("owner").evaluate(&vm()));
        assert!(!Condition::exists("costCenter").evaluate(&vm()));
    }

    #[test]
    fn missing_fields_are_false_not_an_error() {
        assert!(!Condition::equals("absent", "x").evaluate(&vm()));
        assert!(!Condition::any_of("absent", &["x"]).evaluate(&vm()));
    }

    #[test]
    fn combinators_compose_recursively() {
        let rule = Condition::All(vec![
            Condition::any_of("type", &["Microsoft.Compute/virtualMachines"]),
            Condition::Any(vec![
                Condition::equals("env", "prod"),
                Condition::equals("env", "staging"),
            ]),
            Condition::negate(Condition::exists("deletedAt")),
        ]);
        assert!(rule.evaluate(&vm()));
        assert_eq!(rule.leaf_count(), 4);
    }

    #[test]
    fn empty_all_is_true_and_empty_any_is_false() {
        // Vacuous truth, and the reason `All` and `Any` are not symmetric.
        assert!(Condition::All(vec![]).evaluate(&vm()));
        assert!(!Condition::Any(vec![]).evaluate(&vm()));
    }

    #[test]
    fn let_else_handles_the_missing_case_without_nesting() {
        assert_eq!(required_tag(&vm(), "owner"), "OPS");
        assert_eq!(required_tag(&vm(), "costCenter"), "<missing>");
    }
}


