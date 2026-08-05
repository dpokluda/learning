//! Crate drill 19 — thiserror: typed errors for libraries.
//!
//! The rule this chapter drills is "anyhow for binaries, thiserror for
//! libraries". A library's error type is part of its public API, the way a
//! custom exception hierarchy is in .NET — but with one decisive difference:
//! the signature lists it, so callers cannot fail to know it exists.
//!
//! Like the clap chapter, most of the work here is in the attributes. Add the
//! `#[error(...)]`, `#[from]`, `#[source]` and `#[error(transparent)]`
//! annotations until the tests describing the messages and the cause chain
//! pass.

// `count_depth` looks unused until `parse_rule` calls it.
#![allow(dead_code)]

use std::path::PathBuf;

use thiserror::Error;

/// A leaf error from the layer below.
///
/// Required messages:
/// * `UnexpectedToken` → ``unexpected token `{token}` at position {position}``
/// * `Unterminated` → `unterminated string literal`
///
/// Placeholder `#[error(...)]` attributes are supplied so the crate compiles;
/// replace them.
#[derive(Debug, Error, PartialEq, Eq)]
pub enum ParseError {
    #[error("TODO: the message for UnexpectedToken")]
    UnexpectedToken { token: String, position: usize },
    #[error("TODO: the message for Unterminated")]
    Unterminated,
}

/// The crate-level error.
///
/// Required behaviour:
/// * `Io` displays as ``could not read `{path}` `` and exposes the inner
///   `io::Error` as its `source`
/// * `Syntax` wraps a `ParseError` such that `?` converts automatically **and**
///   the cause chain is wired up, and displays as `rule syntax is invalid`
/// * `UnknownField` displays as ``unknown field `{0}`; expected one of {1}``
/// * `TooDeep` displays as `rule nesting exceeded the limit of {limit}` and has
///   no source at all
#[derive(Debug, Error)]
pub enum RuleError {
    #[error("TODO: the message for Io")]
    Io {
        path: PathBuf,
        source: std::io::Error,
    },
    #[error("TODO: the message for Syntax")]
    Syntax(ParseError),
    #[error("TODO: the message for UnknownField")]
    UnknownField(String, String),
    #[error("TODO: the message for TooDeep")]
    TooDeep {
        limit: usize,
    },
}

/// A wrapper that adds a variant without adding a message layer: `Rule` must
/// forward both `Display` and `source` to the error it wraps, and still support
/// `?` conversion from `RuleError`.
#[derive(Debug, Error)]
pub enum ConfigError {
    // `#[from]` is supplied so the tests compile; the message handling is yours.
    #[error("TODO: this variant should contribute no message of its own")]
    Rule(#[from] RuleError),
    #[error("no configuration file found in {searched:?}")]
    NotFound { searched: Vec<PathBuf> },
}

/// Count the maximum parenthesis nesting depth of `text` via `count_depth`,
/// converting its `ParseError` with `?`, and reject anything deeper than 8 with
/// `RuleError::TooDeep { limit: 8 }`.
pub fn parse_rule(_text: &str) -> Result<usize, RuleError> {
    todo!("delegate to count_depth, then enforce the depth limit")
}

/// Walk `text` character by character tracking nesting depth.
///
/// * A `"` toggles "inside a string literal"; parentheses inside a literal do
///   not count.
/// * A `)` with depth already zero is
///   `ParseError::UnexpectedToken { token: ")", position }` where `position` is
///   the byte offset from `char_indices`.
/// * Text that ends inside a string literal is `ParseError::Unterminated`.
/// * Otherwise return the maximum depth reached.
fn count_depth(_text: &str) -> Result<usize, ParseError> {
    todo!("scan the text, tracking depth and string state")
}

/// Read `path`, mapping any I/O failure into `RuleError::Io` **carrying the
/// path**. Because the variant needs data the `io::Error` never had, `#[from]`
/// cannot help here — construct it by hand.
pub fn read_rule(_path: &std::path::Path) -> Result<String, RuleError> {
    todo!("map_err into RuleError::Io, attaching the path")
}

/// Render an error and everything beneath it, joined with `" <- "`, by walking
/// `std::error::Error::source` — the same idea as following `InnerException`
/// until it is null.
pub fn describe(_err: &dyn std::error::Error) -> String {
    todo!("walk the source chain and join the messages")
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::error::Error;

    #[test]
    fn the_error_attribute_writes_the_display_impl() {
        let e = ParseError::UnexpectedToken {
            token: ")".into(),
            position: 7,
        };
        assert_eq!(e.to_string(), "unexpected token `)` at position 7");
        assert_eq!(ParseError::Unterminated.to_string(), "unterminated string literal");
    }

    #[test]
    fn positional_fields_interpolate_by_index() {
        let e = RuleError::UnknownField("wehn".into(), "when, then".into());
        assert_eq!(e.to_string(), "unknown field `wehn`; expected one of when, then");
    }

    #[test]
    fn from_makes_the_question_mark_operator_convert_for_you() {
        let err = parse_rule("(and (eq \"a\" \"b\"))) ").unwrap_err();
        assert!(matches!(err, RuleError::Syntax(ParseError::UnexpectedToken { .. })));
    }

    #[test]
    fn from_also_wires_up_the_source_link() {
        let err = parse_rule("(\"oops").unwrap_err();
        let source = err.source().expect("Syntax carries its cause");
        assert_eq!(source.to_string(), "unterminated string literal");
        assert_eq!(describe(&err), "rule syntax is invalid <- unterminated string literal");
    }

    #[test]
    fn a_variant_without_a_cause_terminates_the_chain() {
        let err = parse_rule("(((((((((x)))))))))").unwrap_err();
        assert!(matches!(err, RuleError::TooDeep { limit: 8 }));
        assert!(err.source().is_none());
    }

    #[test]
    fn explicit_source_carries_context_that_from_cannot() {
        let err = read_rule(std::path::Path::new("no-such-rule.json")).unwrap_err();

        // The message names the path — information the io::Error never had.
        assert_eq!(err.to_string(), "could not read `no-such-rule.json`");

        let io = err
            .source()
            .and_then(|s| s.downcast_ref::<std::io::Error>())
            .expect("io error is the source");
        assert_eq!(io.kind(), std::io::ErrorKind::NotFound);
    }

    #[test]
    fn transparent_forwards_display_and_source_unchanged() {
        let inner = RuleError::TooDeep { limit: 8 };
        let expected = inner.to_string();
        let outer: ConfigError = inner.into();

        assert_eq!(outer.to_string(), expected);
        // The wrapper contributes no message of its own, so the chain is one
        // link shorter than the nesting suggests.
        assert_eq!(describe(&outer), "rule nesting exceeded the limit of 8");
    }

    #[test]
    fn errors_stay_matchable_which_is_the_point_of_typing_them() {
        // A caller can react to *this* failure without string matching — the
        // thing a stringly-typed exception message makes impossible.
        let recovered = match parse_rule("(((((((((x)))))))))") {
            Err(RuleError::TooDeep { limit }) => limit,
            other => panic!("unexpected: {other:?}"),
        };
        assert_eq!(recovered, 8);
    }

    #[test]
    fn anyhow_can_still_swallow_a_typed_error_and_give_it_back() {
        // A binary erases the type into `anyhow::Error` for reporting, but the
        // concrete type survives and can be downcast when a caller cares.
        let typed = parse_rule("(((((((((x)))))))))").unwrap_err();
        let erased: anyhow::Error = typed.into();

        assert!(erased.downcast_ref::<RuleError>().is_some());
        assert_eq!(erased.to_string(), "rule nesting exceeded the limit of 8");
    }

    #[test]
    fn well_formed_input_needs_no_error_at_all() {
        // `(and` -> 1, `(eq` -> 2, `(not` -> 2, `(missing` -> 3.
        assert_eq!(parse_rule("(and (eq \"tag\" \"prod\") (not (missing)))").unwrap(), 3);
        assert_eq!(parse_rule("flat").unwrap(), 0);
    }
}
