# Answers 07 — Structs, enums, and matching

> Exercises: [07-structs-enums.md](../07-structs-enums.md)

## Part A

**A1. Rust's `enum` and C#'s `enum` share a keyword and almost nothing else. Explain the difference, and name the C# construct that comes closest.**

A C# `enum` is a named integer constant — it carries no payload, and a value outside the declared set is representable and legal. A Rust `enum` is a tagged union: each variant may carry its own differently-typed data, and the type is *closed*, so a value is always exactly one of the declared variants. The closest C# construct is an abstract base class with a fixed set of sealed subclasses, or the record-hierarchy pattern people use to fake discriminated unions. The differences that matter are that Rust's version is a value type with no allocation, the compiler knows the variant set is complete, and `match` is checked for exhaustiveness against it.

**A2. What does 'exhaustiveness' buy you, and how is it different from a C# switch expression that also warns on missing cases?**

Exhaustiveness means the compiler proves you handled every variant, and it is an error rather than a warning. The difference from C# is not the check but what it is checking *against*: C#'s exhaustiveness over a type hierarchy is best-effort, because the compiler cannot prove no other subclass exists unless every type is sealed and in the same assembly, so it emits a warning and inserts a runtime throw. Rust's enum is closed by construction, so the check is total. The practical payoff is refactoring: add a variant and the compiler hands you the complete list of sites to update, which turns "find every switch on this type" from a grep exercise into a build.

**A3. When should you write `if let` instead of `match`, and what does `let ... else` add?**

`if let` is for when exactly one pattern is interesting and the rest are uniformly uninteresting — it is `match` with one arm and an implicit `_ => ()`. `let ... else` is for the case where the *uninteresting* branch diverges: it binds the pattern's variables into the enclosing scope and requires the `else` block to `return`, `break`, `continue`, or panic. That inversion is what keeps the happy path unindented: instead of nesting the entire remainder of the function inside `if let Some(x) = ...`, you write `let Some(x) = ... else { return Err(...); };` and carry on at the top level. It is the guard-clause pattern, and it is the single biggest readability win in the language for functions that validate several things in a row.

**A4. What is the point of `#[non_exhaustive]` on an enum, and what does it do to downstream `match`?**

It tells other crates that you reserve the right to add variants, so their `match` on your enum must include a wildcard arm and will not break when you do. Without it, adding a variant to a public enum is a semver-major change, because every downstream exhaustive `match` stops compiling. It is the enum-level acknowledgement that exhaustiveness is a contract, not just a convenience. Within your own crate the attribute has no effect, so you keep the refactoring benefit internally while giving consumers stability — the same trade C# makes implicitly by never being able to prove exhaustiveness in the first place.

**A5. Rust structs come in three shapes. Name them and say when each is right.**

Named-field structs (`struct Point { x: f64, y: f64 }`) are the default for anything with more than one meaningful component. Tuple structs (`struct Meters(f64)`) are for newtypes and for wrappers where the field name would add nothing — the one-field form is the workhorse of the newtype pattern, giving you a distinct type over an existing representation for free. Unit structs (`struct Marker;`) carry no data and exist to implement traits or act as type-level tags; they are what you use for a strategy object with no state, where C# would make you allocate a stateless class instance. All three are plain values with no header, no vtable, and no identity.

**A6. Match guards, bindings with `@`, and or-patterns all exist. Give a one-line use for each.**

A guard (`Some(n) if n > 10 =>`) refines a pattern with a boolean condition the pattern language cannot express — note it does not participate in exhaustiveness checking, so a guarded arm never counts as covering its variant. An `@` binding (`n @ 1..=9 =>`) matches a pattern *and* binds the whole matched value to a name, which saves re-extracting it in the arm body. An or-pattern (`Equals { .. } | Exists { .. } =>`) lets several variants share one arm, and inside a sub-pattern (`Some(1 | 2)`) it keeps the nesting flat. Together they cover most of what you would otherwise write as a chain of `if`s inside a single catch-all arm.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 07 — Algebraic data types and exhaustive pattern matching.
//!
//! This is a miniature of `polcheck`'s rule engine: a recursive condition tree
//! evaluated against a flat bag of resource fields. In C# you would reach for an
//! abstract `Condition` base class plus a visitor, or a `record` hierarchy with
//! a type switch that the compiler cannot prove exhaustive. Here the enum *is*
//! the closed set, and `match` proves you handled it.

use std::collections::HashMap;

/// A resource is just a set of string-valued fields for this drill.
pub type Resource = HashMap<String, String>;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Condition {
    /// Field exists and equals the given value.
    Equals { field: String, value: String },
    /// Field exists at all.
    Exists { field: String },
    /// Field exists and its value is one of these.
    In { field: String, values: Vec<String> },
    All(Vec<Condition>),
    Any(Vec<Condition>),
    Not(Box<Condition>),
}

impl Condition {
    pub fn equals(field: &str, value: &str) -> Self {
        Condition::Equals { field: field.into(), value: value.into() }
    }

    pub fn exists(field: &str) -> Self {
        Condition::Exists { field: field.into() }
    }

    pub fn any_of(field: &str, values: &[&str]) -> Self {
        Condition::In {
            field: field.into(),
            values: values.iter().map(|s| s.to_string()).collect(),
        }
    }

    /// Named `negate` rather than `not`: clippy's `should_implement_trait` lint
    /// flags an inherent method called `not`, because a reader will expect
    /// `std::ops::Not`. Naming conventions are lint-enforced here.
    pub fn negate(inner: Condition) -> Self {
        Condition::Not(Box::new(inner))
    }

    /// The evaluator. Every arm is required: add a variant above and this stops
    /// compiling until you handle it. That is the guarantee C# switch
    /// expressions only approximate.
    pub fn evaluate(&self, resource: &Resource) -> bool {
        match self {
            Condition::Equals { field, value } => resource.get(field) == Some(value),
            Condition::Exists { field } => resource.contains_key(field),
            Condition::In { field, values } => match resource.get(field) {
                Some(actual) => values.contains(actual),
                None => false,
            },
            // Note the vacuous-truth semantics, made explicit by `all`/`any`.
            Condition::All(children) => children.iter().all(|c| c.evaluate(resource)),
            Condition::Any(children) => children.iter().any(|c| c.evaluate(resource)),
            Condition::Not(inner) => !inner.evaluate(resource),
        }
    }

    /// How many leaf tests does this tree contain? Recursion over the ADT again.
    pub fn leaf_count(&self) -> usize {
        match self {
            Condition::Equals { .. } | Condition::Exists { .. } | Condition::In { .. } => 1,
            Condition::All(children) | Condition::Any(children) => {
                children.iter().map(Condition::leaf_count).sum()
            }
            Condition::Not(inner) => inner.leaf_count(),
        }
    }
}

/// `if let` with an `else` branch that must diverge — the `let ... else` form.
/// Reads top-to-bottom instead of nesting the happy path inside a block.
pub fn required_tag(resource: &Resource, field: &str) -> String {
    let Some(value) = resource.get(field) else {
        return "<missing>".to_string();
    };
    value.to_ascii_uppercase()
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
```
