# Exercises 07 — Structs, enums, and matching

> **Covers:** [07 — Structs, enums, and matching](../07-structs-enums-matching.md). **Code:** `drills/src/ch07.rs`. **Answers:** [answers/07-structs-enums.md](answers/07-structs-enums.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** Rust's `enum` and C#'s `enum` share a keyword and almost nothing else. Explain the difference, and name the C# construct that comes closest.

**A2.** What does 'exhaustiveness' buy you, and how is it different from a C# switch expression that also warns on missing cases?

**A3.** When should you write `if let` instead of `match`, and what does `let ... else` add?

**A4.** What is the point of `#[non_exhaustive]` on an enum, and what does it do to downstream `match`?

**A5.** Rust structs come in three shapes. Name them and say when each is right.

**A6.** Match guards, bindings with `@`, and or-patterns all exist. Give a one-line use for each.

## Part B — Exercise

Open `drills/src/ch07.rs`. This is the most important drill in Part 1, and it
builds a miniature of the rule engine the capstone uses.

You are given a recursive `Condition` enum with six variants and asked to write
the evaluator: a `match` over every variant, recursing through the combinators,
with no catch-all arm. Two tests pin down edge cases worth thinking about before
you write the code — a missing field is `false` rather than an error, and empty
`All` and empty `Any` are deliberately not symmetric.

When the tests pass, do the follow-up, because it is the actual point: add a
seventh variant and change nothing else. The compiler will hand you the complete
list of places that must be updated. Compare that to what happens in C# when you
add a case to a record hierarchy and the type switch has a `_` fallback.

Run it with `cargo test ch07` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
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
```

The test module that follows this in the file is the specification — read it before you write anything.
