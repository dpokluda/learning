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
