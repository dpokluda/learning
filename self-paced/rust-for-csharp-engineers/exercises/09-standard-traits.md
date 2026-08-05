# Exercises 09 — The standard traits

> **Covers:** [09 — The standard traits](../09-standard-traits.md). **Code:** `drills/src/ch09.rs`. **Answers:** [answers/09-standard-traits.md](answers/09-standard-traits.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** `Debug` and `Display` both format a value. Why are they separate traits?

**A2.** Why should you implement `From` rather than `Into`, and what does that buy you?

**A3.** What is the contract between `PartialEq`/`Eq` and `PartialOrd`/`Ord`, and why is `f64` only `PartialEq`?

**A4.** If you implement `Ord` by hand, what must you also do, and what breaks if you get it wrong?

**A5.** What is the relationship between `Hash` and `Eq`, and what is the Rust equivalent of C#'s 'always override GetHashCode with Equals'?

**A6.** `Deref` looks like an implicit conversion. Why is implementing it on your own types usually a mistake?

## Part B — Exercise

Open `drills/src/ch09.rs`. The goal is to take a bare struct and make it feel
like a built-in type, purely by implementing standard traits.

The file will not compile until you work out which derives the tests need — do
that by reading the assertions rather than by deriving everything reflexively,
because each of `Copy`, `Ord`, `Hash`, and `Default` is load-bearing for exactly
one test. Then fill in `Display`, `FromStr`, `From`, and `TryFrom`, and note as
you go which methods you get for free: you never implement `ToString`, and you
never implement `Into`.

The last piece is `Ord` for `Finding`, which must sort by severity *descending*
and then by id *ascending*. Deriving it gives the wrong answer, so this one is
hand-written — and once you write `Ord` by hand you owe the language a
`PartialOrd` that agrees with it.

Run it with `cargo test ch09` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 09 — The standard traits that make a type feel native.
//!
//! Take a plain struct from "a bag of data" to "a type the rest of the language
//! already knows how to use". Every test below is unlocked by one std trait.

use std::cmp::Ordering;
use std::fmt;
use std::str::FromStr;

/// Add the derives the tests need. Work them out from the assertions rather
/// than deriving everything reflexively: `Copy`, ordering, hashing and
/// `Default` are each load-bearing for exactly one test.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Severity(u8);

impl Severity {
    pub const INFO: Severity = Severity(0);
    pub const WARNING: Severity = Severity(1);
    pub const ERROR: Severity = Severity(2);

    pub fn level(self) -> u8 {
        todo!()
    }
}

/// The user-facing rendering — `"info"` / `"warning"` / `"error"`. Note there
/// is no `impl ToString`: you get `to_string()` free from the blanket impl.
impl fmt::Display for Severity {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}

#[derive(Debug, PartialEq, Eq)]
pub struct ParseSeverityError(pub String);

/// Reads `unknown severity: {0}`.
impl fmt::Display for ParseSeverityError {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}

impl std::error::Error for ParseSeverityError {}

/// This is what powers `"warning".parse::<Severity>()` — the `TryParse`
/// analogue, except the failure is a value rather than an `out` parameter.
/// Accept `info`, `warning`, `warn`, `error`, case-insensitively, ignoring
/// surrounding whitespace.
impl FromStr for Severity {
    type Err = ParseSeverityError;

    fn from_str(_s: &str) -> Result<Self, Self::Err> {
        todo!()
    }
}

/// Implement `From` and you get `Into` free from the blanket impl. Always
/// implement this direction, never `Into` directly.
impl From<Severity> for u8 {
    fn from(_value: Severity) -> Self {
        todo!()
    }
}

/// The fallible direction, which also wires up `try_into()`. Accept 0..=2.
impl TryFrom<u8> for Severity {
    type Error = ParseSeverityError;

    fn try_from(_value: u8) -> Result<Self, Self::Error> {
        todo!()
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub id: String,
    pub severity: Severity,
}

/// Order by severity **descending**, then by id **ascending**. Deriving `Ord`
/// would give the wrong answer, so this one is hand-written. `Ordering::then_with`
/// is the tie-breaking combinator.
impl Ord for Finding {
    fn cmp(&self, _other: &Self) -> Ordering {
        todo!()
    }
}

/// The contract: when `Ord` is hand-written, `PartialOrd` must agree with it.
/// Delegating is the only correct implementation — write that one line.
impl PartialOrd for Finding {
    fn partial_cmp(&self, _other: &Self) -> Option<Ordering> {
        todo!()
    }
}

/// Renders as `"[{severity}] {id}"`.
impl fmt::Display for Finding {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}
```

The test module that follows this in the file is the specification — read it before you write anything.
