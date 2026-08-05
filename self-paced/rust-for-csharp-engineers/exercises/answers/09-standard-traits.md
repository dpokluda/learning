# Answers 09 — The standard traits

> Exercises: [09-standard-traits.md](../09-standard-traits.md)

## Part A

**A1. `Debug` and `Display` both format a value. Why are they separate traits?**

`Debug` is for programmers — it is derivable, is allowed to be ugly, is expected to show structure, and is what `{:?}`, `assert_eq!` failures, and error backtraces use. `Display` is for users: it is never derivable, because how a value should read to a human is a design decision no macro can make. Keeping them separate means `derive(Debug)` is free and universal while `Display` stays deliberate, and it means a type can be printable in a log without committing to a user-facing rendering. C# collapses both into `ToString()` plus a debugger-display attribute, which is why so many `ToString` implementations are a compromise between the two audiences.

**A2. Why should you implement `From` rather than `Into`, and what does that buy you?**

The standard library has a blanket `impl<T, U: From<T>> Into<U> for T`, so implementing `From` gives you `Into` automatically, while implementing `Into` directly gives you nothing in the other direction. It also composes with the `?` operator, which converts error types using `From`, so a single `impl From<io::Error> for MyError` makes every `?` on an I/O call work inside functions returning `MyError`. The rule of thumb is: implement `From` on the *target* type, and use `Into` only as a *bound* on a generic parameter (`fn new(name: impl Into<String>)`), where it lets callers pass either a `&str` or a `String`.

**A3. What is the contract between `PartialEq`/`Eq` and `PartialOrd`/`Ord`, and why is `f64` only `PartialEq`?**

`Eq` and `Ord` are marker refinements asserting *total* equality and *total* ordering: every value equals itself, and every pair of values is comparable. `f64` is only `PartialEq`/`PartialOrd` because `NaN != NaN` and `NaN` is unordered against everything, so neither total law holds. The consequence is real and practical: `f64` cannot be a `HashMap` key or a `BTreeMap` key, and `vec_of_f64.sort()` does not compile — you must use `sort_by(f64::total_cmp)` and make the tie-break explicit. C# lets you do all of these and simply gives you surprising results, which is the same trade-off it makes with `Nullable` equality.

**A4. If you implement `Ord` by hand, what must you also do, and what breaks if you get it wrong?**

`PartialOrd::partial_cmp` must agree with `Ord::cmp`, which in practice means writing `fn partial_cmp(&self, other: &Self) -> Option<Ordering> { Some(self.cmp(other)) }` and nothing else. You must also keep both consistent with `PartialEq`: `a.cmp(&b) == Ordering::Equal` if and only if `a == b`. If they disagree, `sort`, `BTreeMap`, and `binary_search` produce silently wrong answers rather than panicking, because they trust the contract and use it to prune comparisons — this is the same class of bug as a C# `IComparer` that is inconsistent with `Equals`, and it is just as hard to find.

**A5. What is the relationship between `Hash` and `Eq`, and what is the Rust equivalent of C#'s 'always override GetHashCode with Equals'?**

The contract is that `a == b` implies `hash(a) == hash(b)`. Rust enforces it structurally rather than by convention: `HashMap<K, V>` requires `K: Hash + Eq`, so you cannot use a key that has one without the other, and `#[derive(Hash, PartialEq, Eq)]` derives both from the same field set so they cannot drift. The place to be careful is a hand-written `PartialEq` that ignores a field — the derived `Hash` will still include it, and the map will fail to find entries that compare equal. That is the same bug as C#'s, but you have to work harder to write it.

**A6. `Deref` looks like an implicit conversion. Why is implementing it on your own types usually a mistake?**

`Deref` exists so that smart pointers can be transparent: `Box<T>`, `Rc<T>`, `String`, and `Vec<T>` implement it so that `&String` coerces to `&str` and every `&str` method is reachable. Implementing it on a domain type to get "inheritance" abuses that mechanism: the coercion is invisible at the call site, method resolution silently walks through it, and the resulting API is hard to read and hard to document. The API guidelines are explicit that `Deref` is for smart pointers only. If what you want is to expose a wrapped type's methods, write them explicitly or implement `AsRef`, which is the same idea with an explicit call.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 09 — The standard traits that make a type feel native.
//!
//! The exercise is to take one plain struct from "a bag of data" to "a type the
//! rest of the language already knows how to use", purely by implementing the
//! std traits. In .NET this is `ToString`/`IEquatable`/`IComparable`/
//! `GetHashCode`/`IParsable`; the Rust set is finer-grained and each piece is
//! separately opt-in.

use std::cmp::Ordering;
use std::fmt;
use std::str::FromStr;

/// A newtype over `u8` with meaning attached. `Copy` because it is one byte.
/// `PartialOrd`/`Ord` are derived, which orders by the inner value.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Default)]
pub struct Severity(u8);

impl Severity {
    pub const INFO: Severity = Severity(0);
    pub const WARNING: Severity = Severity(1);
    pub const ERROR: Severity = Severity(2);

    pub fn level(self) -> u8 {
        self.0
    }
}

/// `Display` is the user-facing rendering — the `ToString` analogue. Note there
/// is no `impl ToString`: you get it free via the blanket impl.
impl fmt::Display for Severity {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let text = match self.0 {
            0 => "info",
            1 => "warning",
            _ => "error",
        };
        f.write_str(text)
    }
}

#[derive(Debug, PartialEq, Eq)]
pub struct ParseSeverityError(pub String);

impl fmt::Display for ParseSeverityError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "unknown severity: {}", self.0)
    }
}

impl std::error::Error for ParseSeverityError {}

/// `FromStr` is what powers `"warning".parse::<Severity>()` — the `TryParse`
/// analogue, except it returns a `Result` rather than using an `out` parameter.
impl FromStr for Severity {
    type Err = ParseSeverityError;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s.trim().to_ascii_lowercase().as_str() {
            "info" => Ok(Severity::INFO),
            "warning" | "warn" => Ok(Severity::WARNING),
            "error" => Ok(Severity::ERROR),
            other => Err(ParseSeverityError(other.to_string())),
        }
    }
}

/// `From` is the infallible conversion. Implementing it also gives you `Into`
/// for free through the blanket impl — always implement `From`, never `Into`.
impl From<Severity> for u8 {
    fn from(value: Severity) -> Self {
        value.0
    }
}

/// `TryFrom` is the fallible direction, and it also wires up `try_into()`.
impl TryFrom<u8> for Severity {
    type Error = ParseSeverityError;

    fn try_from(value: u8) -> Result<Self, Self::Error> {
        match value {
            0..=2 => Ok(Severity(value)),
            other => Err(ParseSeverityError(other.to_string())),
        }
    }
}

/// A finding, ordered by severity descending and then by id ascending — the
/// classic "sort by several keys with mixed direction" problem. Deriving `Ord`
/// would give the wrong order, so this one is hand-written.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub id: String,
    pub severity: Severity,
}

impl Ord for Finding {
    fn cmp(&self, other: &Self) -> Ordering {
        other.severity.cmp(&self.severity).then_with(|| self.id.cmp(&other.id))
    }
}

/// The contract: whenever `Ord` is implemented by hand, `PartialOrd` must agree
/// with it. Delegating is the only correct implementation.
impl PartialOrd for Finding {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl fmt::Display for Finding {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "[{}] {}", self.severity, self.id)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeSet;

    #[test]
    fn display_is_the_tostring_analogue() {
        assert_eq!(Severity::WARNING.to_string(), "warning");
        assert_eq!(format!("{}", Severity::ERROR), "error");
    }

    #[test]
    fn debug_and_display_are_different_traits_on_purpose() {
        assert_eq!(format!("{}", Severity::INFO), "info");
        assert_eq!(format!("{:?}", Severity::INFO), "Severity(0)");
    }

    #[test]
    fn fromstr_powers_the_parse_method() {
        assert_eq!("warning".parse::<Severity>(), Ok(Severity::WARNING));
        assert_eq!("  WARN ".parse::<Severity>(), Ok(Severity::WARNING));
        assert_eq!(
            "nope".parse::<Severity>().unwrap_err(),
            ParseSeverityError("nope".to_string())
        );
    }

    #[test]
    fn from_gives_into_for_free() {
        let raw: u8 = Severity::ERROR.into();
        assert_eq!(raw, 2);

        let back: Severity = 1u8.try_into().unwrap();
        assert_eq!(back, Severity::WARNING);
        assert!(Severity::try_from(9u8).is_err());
    }

    #[test]
    fn default_is_the_zero_value() {
        assert_eq!(Severity::default(), Severity::INFO);
    }

    #[test]
    fn hash_and_eq_let_the_type_be_a_key() {
        let set: BTreeSet<Severity> = [Severity::ERROR, Severity::INFO, Severity::ERROR].into();
        assert_eq!(set.len(), 2);
        // BTreeSet iterates in `Ord` order, which the derive made numeric.
        assert_eq!(set.iter().next(), Some(&Severity::INFO));
    }

    #[test]
    fn hand_written_ord_drives_sort() {
        let mut findings = [
            Finding { id: "b".into(), severity: Severity::INFO },
            Finding { id: "a".into(), severity: Severity::ERROR },
            Finding { id: "c".into(), severity: Severity::ERROR },
        ];
        findings.sort();
        let rendered: Vec<String> = findings.iter().map(Finding::to_string).collect();
        assert_eq!(rendered, ["[error] a", "[error] c", "[info] b"]);
    }
}
```
