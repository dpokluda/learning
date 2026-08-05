# Exercises 11 — Error handling

> **Covers:** [11 — Error handling](../11-error-handling.md). **Code:** `drills/src/ch11.rs`. **Answers:** [answers/11-errors.md](answers/11-errors.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** Rust has no exceptions. Explain what `Result<T, E>` gives you that `try/catch` does not, and what it costs.

**A2.** What does `?` actually expand to, and what role does `From` play?

**A3.** When is `panic!` the right answer, and how does that map onto C# practice?

**A4.** `unwrap`, `expect`, and `unwrap_or_default` all get a value out. Give a defensible rule for each.

**A5.** What is the 'anyhow for binaries, thiserror for libraries' rule, and why does it hold?

**A6.** What is the difference between `Box<dyn Error>` and `anyhow::Error`, given both erase the concrete type?

## Part B — Exercise

Open `drills/src/ch11.rs`. The goal is to build a library-quality error type by
hand, so that when `thiserror` generates one for you later you know exactly what
it generated.

You will write a three-variant error enum, its `Display` impl with exact
wording, and — the part people skip — a `source()` implementation that returns
the underlying `ParseIntError` for the one variant that has a cause. A test then
walks the chain and asserts it has two links, which is what makes the difference
between an error message and a diagnosis.

The rest of the drill is about choosing between `Option` and `Result` per
function and meaning it: `get` returns `Option` because absence is an ordinary
answer, `require` returns `Result` because the caller asked for something
mandatory, and `retry_budget` swallows both failure modes into a default using
combinators rather than a `match`.

Run it with `cargo test ch11` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 11 — `Option`, `Result`, `?`, and custom error types.
//!
//! The shape to internalise: a closed error enum, `source()` for the chain, and
//! panics reserved for bugs.

// Fields look unread while the bodies are still `todo!()`.
#![allow(dead_code)]

use std::error::Error;
use std::fmt;
use std::num::ParseIntError;

/// One variant per way this module can fail — the thing a C# exception
/// hierarchy never tells the caller.
#[derive(Debug)]
pub enum ConfigError {
    MissingKey { key: String },
    BadNumber { key: String, source: ParseIntError },
    OutOfRange { key: String, value: i64, max: i64 },
}

/// The exact strings the tests expect:
///   "missing required key `{key}`"
///   "key `{key}` is not a number"
///   "key `{key}` is {value}, which exceeds the maximum of {max}"
impl fmt::Display for ConfigError {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}

impl Error for ConfigError {
    /// This is what builds the chain — the `InnerException` analogue, except
    /// it is a method, so a variant with no cause simply returns `None`.
    /// Only `BadNumber` has one.
    fn source(&self) -> Option<&(dyn Error + 'static)> {
        todo!()
    }
}

pub struct Settings {
    pub(crate) pairs: Vec<(String, String)>,
}

impl Settings {
    /// Parse `key = value` lines, ignoring any line without an `=`.
    pub fn parse(_text: &str) -> Self {
        todo!("split_once('=') is the tool")
    }

    /// Absence is an ordinary answer here, not a failure.
    pub fn get(&self, _key: &str) -> Option<&str> {
        todo!()
    }

    /// The caller asked for something required, so absence *is* a failure.
    pub fn require(&self, _key: &str) -> Result<&str, ConfigError> {
        todo!("Option::ok_or_else")
    }

    /// Three fallible steps chained with `?`. Read the happy path top to
    /// bottom; each `?` is an early return that already converted the error.
    pub fn require_bounded_int(&self, _key: &str, _max: i64) -> Result<i64, ConfigError> {
        todo!()
    }
}

/// Parse `retries`, falling back to 3 if it is absent *or* unparseable.
/// Combinators, not `match`.
pub fn retry_budget(_settings: &Settings) -> u32 {
    todo!("and_then + ok + unwrap_or")
}

/// Walk the whole `source` chain, outermost error first.
pub fn error_chain(_err: &dyn Error) -> Vec<String> {
    todo!("while let Some(e) = current")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
