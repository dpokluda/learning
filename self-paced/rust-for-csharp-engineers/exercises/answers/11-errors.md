# Answers 11 — Error handling

> Exercises: [11-errors.md](../11-errors.md)

## Part A

**A1. Rust has no exceptions. Explain what `Result<T, E>` gives you that `try/catch` does not, and what it costs.**

It puts failure in the type, so a function's signature tells you whether it can fail and with what — information a C# signature simply does not carry, since any method can throw anything. That means the compiler can force you to handle or propagate every failure, and callers can pattern-match on specific failure modes without string-matching on exception types. The cost is syntactic weight on the happy path, which `?` largely absorbs, and the loss of automatic unwinding across many frames: with exceptions, twenty intermediate functions need no error-handling code at all, whereas in Rust each of them must at least write `?` and have a compatible error type. That is the trade — explicitness for ceremony — and it is why `anyhow` exists to make the ceremony cheap in applications.

**A2. What does `?` actually expand to, and what role does `From` play?**

`expr?` evaluates `expr`; on `Ok(v)` it becomes `v`, and on `Err(e)` it returns early with `Err(From::from(e))` from the enclosing function. The `From` conversion is the important half: it is what lets a function returning `MyError` call a function returning `io::Error` and write `?` with no explicit mapping, provided `impl From<io::Error> for MyError` exists. That is why `thiserror`'s `#[from]` attribute is so load-bearing — it generates exactly those impls, and with them a whole call graph's worth of error plumbing disappears. `?` also works on `Option`, returning `None` early, though the two cannot be mixed without an explicit conversion.

**A3. When is `panic!` the right answer, and how does that map onto C# practice?**

Panic is for bugs — violated invariants, unreachable states, indices out of range, an `unwrap` on something the surrounding logic guarantees is present. It is not for expected conditions like a missing file or malformed input, which are `Result`. The C# mapping is close to the distinction between a `Debug.Assert`/`InvalidOperationException` for programmer error and a caught, handled exception for environmental failure — except Rust makes it a type-level distinction rather than a naming convention. The practical consequence is that a library returning `Result` for everything recoverable and panicking only on bugs gives its callers a genuinely useful contract, whereas a library that panics on bad input is unusable in a server.

**A4. `unwrap`, `expect`, and `unwrap_or_default` all get a value out. Give a defensible rule for each.**

Use `expect("message")` when you are asserting an invariant, and write the message as the *reason it cannot fail* — `expect("config was validated at startup")` — so the panic tells a maintainer what assumption broke. Use `unwrap` only in tests, examples, and prototypes, where the extra message adds nothing. Use `unwrap_or_default` (or `unwrap_or`, `unwrap_or_else`) when there genuinely is a sensible fallback, and be suspicious of it when there is not, because it converts a failure into a silently wrong answer. The one to avoid entirely in library code is bare `unwrap` on anything derived from input.

**A5. What is the 'anyhow for binaries, thiserror for libraries' rule, and why does it hold?**

A library's errors are part of its API: callers need to distinguish failure modes programmatically, so the library should expose a typed enum — which `thiserror` generates from a derive, complete with `Display`, `Error`, and `From` impls. An application is the end of the line: nothing downstream will match on its errors, it just needs to add context and print a good message, so a single boxed dynamic error type with a context chain — `anyhow::Error` — is both easier and better. The rule holds because it follows from who the consumer is. Using `anyhow` in a library denies callers the ability to react; using `thiserror` everywhere in an application produces a large enum whose variants nothing ever matches on.

**A6. What is the difference between `Box<dyn Error>` and `anyhow::Error`, given both erase the concrete type?**

`Box<dyn Error>` is std's type-erased error; it works with `?`, it is what `fn main() -> Result<(), Box<dyn Error>>` uses, and it needs no dependency. `anyhow::Error` adds three things on top: it is a single word wide rather than a fat pointer, it captures a backtrace when one is enabled, and it carries a *context chain* so `.context("loading rules from {path}")` attaches human-readable framing at each level as the error propagates. It also downcasts back to concrete types when you need to react to a specific cause. For an application the context chain alone justifies the dependency — it is the difference between `No such file or directory` and a message that names the file and the operation.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 11 — `Option`, `Result`, `?`, and custom error types.
//!
//! The shape to internalise: a library defines a *closed* error enum, `From`
//! impls make `?` do the conversion silently, and panics are reserved for bugs.

use std::error::Error;
use std::fmt;
use std::num::ParseIntError;

/// The library's error type. One variant per way this module can fail, which is
/// the thing a C# exception hierarchy never tells the caller.
#[derive(Debug)]
pub enum ConfigError {
    MissingKey { key: String },
    BadNumber { key: String, source: ParseIntError },
    OutOfRange { key: String, value: i64, max: i64 },
}

impl fmt::Display for ConfigError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ConfigError::MissingKey { key } => write!(f, "missing required key `{key}`"),
            ConfigError::BadNumber { key, .. } => write!(f, "key `{key}` is not a number"),
            ConfigError::OutOfRange { key, value, max } => {
                write!(f, "key `{key}` is {value}, which exceeds the maximum of {max}")
            }
        }
    }
}

/// Implementing `source` is what builds the chain. It is the `InnerException`
/// analogue, except it is a method rather than a field, so a variant that has no
/// cause simply returns `None`.
impl Error for ConfigError {
    fn source(&self) -> Option<&(dyn Error + 'static)> {
        match self {
            ConfigError::BadNumber { source, .. } => Some(source),
            _ => None,
        }
    }
}

pub struct Settings {
    pairs: Vec<(String, String)>,
}

impl Settings {
    pub fn parse(text: &str) -> Self {
        let pairs = text
            .lines()
            .filter_map(|line| line.split_once('='))
            .map(|(k, v)| (k.trim().to_string(), v.trim().to_string()))
            .collect();
        Self { pairs }
    }

    /// Returns `Option`, because "absent" is not an error here — it is a
    /// perfectly ordinary answer the caller may want to default.
    pub fn get(&self, key: &str) -> Option<&str> {
        self.pairs.iter().find(|(k, _)| k == key).map(|(_, v)| v.as_str())
    }

    /// Returns `Result`, because the caller asked for something required.
    pub fn require(&self, key: &str) -> Result<&str, ConfigError> {
        self.get(key).ok_or_else(|| ConfigError::MissingKey { key: key.to_string() })
    }

    /// Three fallible steps chained with `?`. Read it as the happy path; every
    /// `?` is an early return that has already converted the error type.
    pub fn require_bounded_int(&self, key: &str, max: i64) -> Result<i64, ConfigError> {
        let raw = self.require(key)?;
        let value: i64 = raw
            .parse()
            .map_err(|source| ConfigError::BadNumber { key: key.to_string(), source })?;
        if value > max {
            return Err(ConfigError::OutOfRange { key: key.to_string(), value, max });
        }
        Ok(value)
    }
}

/// Combinators on `Option` replace most null-checking. `and_then` is the
/// flat-mapping one; `unwrap_or` supplies the fallback.
pub fn retry_budget(settings: &Settings) -> u32 {
    settings.get("retries").and_then(|raw| raw.parse::<u32>().ok()).unwrap_or(3)
}

/// Walk the whole `source` chain — the loop every top-level error reporter
/// needs, and roughly what `ToString` on an AggregateException gives you.
pub fn error_chain(err: &dyn Error) -> Vec<String> {
    let mut chain = vec![err.to_string()];
    let mut current = err.source();
    while let Some(e) = current {
        chain.push(e.to_string());
        current = e.source();
    }
    chain
}

#[cfg(test)]
mod tests {
    use super::*;

    fn settings() -> Settings {
        Settings::parse("retries = 5\ntimeout = 30\nbad = xyz\nhuge = 9999")
    }

    #[test]
    fn option_models_absence_without_ceremony() {
        assert_eq!(settings().get("retries"), Some("5"));
        assert_eq!(settings().get("nope"), None);
    }

    #[test]
    fn result_models_a_requirement_that_was_not_met() {
        assert!(settings().require("retries").is_ok());
        let err = settings().require("nope").unwrap_err();
        assert_eq!(err.to_string(), "missing required key `nope`");
    }

    #[test]
    fn the_question_mark_returns_early_with_conversion() {
        assert_eq!(settings().require_bounded_int("timeout", 60).unwrap(), 30);
    }

    #[test]
    fn each_failure_mode_gets_its_own_variant() {
        let s = settings();
        assert!(matches!(
            s.require_bounded_int("missing", 60),
            Err(ConfigError::MissingKey { .. })
        ));
        assert!(matches!(
            s.require_bounded_int("bad", 60),
            Err(ConfigError::BadNumber { .. })
        ));
        assert!(matches!(
            s.require_bounded_int("huge", 60),
            Err(ConfigError::OutOfRange { value: 9999, max: 60, .. })
        ));
    }

    #[test]
    fn source_builds_a_chain_the_caller_can_walk() {
        let err = settings().require_bounded_int("bad", 60).unwrap_err();
        let chain = error_chain(&err);
        assert_eq!(chain.len(), 2, "expected the ParseIntError to be the source");
        assert_eq!(chain[0], "key `bad` is not a number");
        assert_eq!(chain[1], "invalid digit found in string");
    }

    #[test]
    fn combinators_supply_a_default_instead_of_panicking() {
        assert_eq!(retry_budget(&settings()), 5);
        assert_eq!(retry_budget(&Settings::parse("")), 3);
        assert_eq!(retry_budget(&Settings::parse("retries = nonsense")), 3);
    }

    #[test]
    #[should_panic(expected = "missing required key")]
    fn expect_is_for_bugs_not_for_control_flow() {
        // `expect` states an invariant. If it fires, the program is wrong —
        // which is exactly when a panic is the correct response.
        Settings::parse("").require("nope").expect("missing required key");
    }
}
```
