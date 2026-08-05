# Answers 19 — thiserror

> Exercises: [19-thiserror.md](../19-thiserror.md)

## Part A

**A1. State the 'anyhow for binaries, thiserror for libraries' rule and derive it from first principles rather than quoting it.**

Ask who consumes the error. A library's caller may need to react differently to different failures — retry on a transport error, fall back on a missing key, surface a validation failure to a user — and they can only do that if the failures are distinguishable programmatically. That means a typed enum with one variant per failure mode, which is exactly what `thiserror` generates from a derive. An application is the end of the chain: nothing downstream matches on its errors, it needs to add context and print something a human can act on, so a single dynamically-typed error carrying a context chain — `anyhow::Error` — is both less code and more useful. The rule is therefore not a convention but a consequence. Using `anyhow` in a library denies callers the ability to branch; using `thiserror` throughout an application produces a large enum whose variants nothing ever inspects, at the cost of writing them all.

**A2. What exactly does `#[from]` generate, and why is it more than a convenience?**

It generates `impl From<Inner> for YourError` mapping to that variant, and — because a `#[from]` field is implicitly also `#[source]` — it wires the wrapped error into the cause chain. The `From` impl is what makes `?` convert automatically, which is the difference between a codebase where every call site writes `.map_err(MyError::Io)` and one where the happy path reads cleanly. It is more than convenience because without it, typed errors are genuinely painful to use and people give up and reach for `Box<dyn Error>` or stringly-typed errors, losing the ability to match. The constraint to remember is that a variant can have at most one `#[from]` field and the `From` impl must be unambiguous, so two variants cannot both convert from the same type — which is a real design pressure pushing you toward carrying context in the variant instead.

**A3. When can you not use `#[from]`, and what do you do instead?**

When the variant carries information the source error does not have. A variant like `Io { path, source }` exists precisely so the message can name the file, and `From<io::Error>` cannot invent a path — the conversion has no access to it. So you drop the blanket conversion and construct the variant by hand at the call site that knows the missing piece, typically with `.map_err(|source| RuleError::Io { path: path.to_path_buf(), source })`. This is the same trade `anyhow`'s `.context()` makes, and it is worth making deliberately: `#[from]` buys terseness, an explicit `map_err` buys a message that says which file. A useful middle ground is to name the field `source` even without `#[from]`, because thiserror treats a field named `source` as the cause automatically, so the chain still works.

**A4. What does `#[error(transparent)]` do, and when is it the right choice?**

It forwards both `Display` and `source` to the single wrapped error, so the variant contributes no message layer of its own and does not appear in the rendered chain at all. It is right when you are widening a type rather than adding information — a crate-level error that unions several sub-module errors, or a variant that exists only so `?` can convert. Using a normal `#[error("...")]` there would insert a redundant line in the output, the pattern that makes some error messages read as four levels of `an error occurred`. The mechanical constraint is that a transparent variant must have exactly one field and no format string, since there is nothing to format.

**A5. Compare a thiserror enum with a C# exception hierarchy. What is genuinely different, not just differently spelled?**

Three things. First, the set is closed: a `match` on the enum is exhaustive, so adding a failure mode is a compile error at every site that handles them, whereas a new exception subclass silently falls through existing `catch` blocks to whatever handler is above. Second, it is in the signature: `fn parse(&self) -> Result<Rule, RuleError>` tells you the function can fail and with what, whereas any C# method can throw anything and only documentation says otherwise. Third, there is no unwinding: an error travels back through every intermediate frame explicitly, which is more code but means no frame is surprised by control flow it never wrote. The similarity is the cause chain — `source()` really is `InnerException`, and both are how a diagnosis differs from a message. What is *not* different is the design pressure to keep the hierarchy shallow and meaningful; a twelve-variant enum is as bad as a twelve-class hierarchy.

**A6. A typed error is more precise than `anyhow::Error`, yet applications routinely convert one into the other. Is anything lost?**

Almost nothing, and that is the point. `anyhow::Error` boxes the original value, so the concrete type survives and `downcast_ref::<RuleError>()` recovers it whenever a caller genuinely needs to branch; `Display` still shows the typed error's message, and the `source` chain is preserved. What you lose is compile-time knowledge — the signature no longer says which failures are possible, so the compiler cannot tell you that you forgot a case, and a downcast can fail at runtime where a `match` could not. That is an acceptable trade at the top of an application, where the answer to every failure is 'report it and exit', and an unacceptable one at a library boundary, where it silently removes a capability from every future caller.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` and `cargo clippy -- -D warnings` on the pinned toolchain.

```rust
//! Crate drill 19 — thiserror: typed errors for libraries.
//!
//! The rule this chapter drills is "anyhow for binaries, thiserror for
//! libraries". A library's error type is part of its public API, the way a
//! custom exception hierarchy is in .NET — but with one decisive difference:
//! the signature lists it, so callers cannot fail to know it exists.

use std::path::PathBuf;

use thiserror::Error;

/// A leaf error from the layer below. `#[error(...)]` writes the `Display`
/// impl; the format string may interpolate the variant's own fields.
#[derive(Debug, Error, PartialEq, Eq)]
pub enum ParseError {
    #[error("unexpected token `{token}` at position {position}")]
    UnexpectedToken { token: String, position: usize },

    #[error("unterminated string literal")]
    Unterminated,
}

/// The crate-level error. Each variant answers "what kind of thing went wrong",
/// and the ones that wrap a lower-level cause use `#[from]`, which generates
/// both the `From` conversion (so `?` converts automatically) and the `source`
/// link (so callers can walk the chain).
#[derive(Debug, Error)]
pub enum RuleError {
    /// `#[from]` on a field implies `#[source]`; there is no need for both.
    #[error("could not read `{path}`")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },

    #[error("rule syntax is invalid")]
    Syntax(#[from] ParseError),

    #[error("unknown field `{0}`; expected one of {1}")]
    UnknownField(String, String),

    /// A variant with no cause at all — the error originates here.
    #[error("rule nesting exceeded the limit of {limit}")]
    TooDeep { limit: usize },
}

/// `transparent` forwards both `Display` and `source` to the inner error,
/// which is how you add a variant without adding a message layer. Use it when
/// the wrapped error already says everything worth saying.
#[derive(Debug, Error)]
pub enum ConfigError {
    #[error(transparent)]
    Rule(#[from] RuleError),

    #[error("no configuration file found in {searched:?}")]
    NotFound { searched: Vec<PathBuf> },
}

/// Because `RuleError::Syntax` carries `#[from] ParseError`, `?` converts the
/// leaf error into the crate error with no ceremony. This is the mechanism that
/// makes typed errors bearable to write.
pub fn parse_rule(text: &str) -> Result<usize, RuleError> {
    let depth = count_depth(text)?;
    if depth > 8 {
        return Err(RuleError::TooDeep { limit: 8 });
    }
    Ok(depth)
}

fn count_depth(text: &str) -> Result<usize, ParseError> {
    let mut depth = 0usize;
    let mut max = 0usize;
    let mut in_string = false;

    for (position, ch) in text.char_indices() {
        match ch {
            '"' => in_string = !in_string,
            '(' if !in_string => {
                depth += 1;
                max = max.max(depth);
            }
            ')' if !in_string => {
                depth = depth.checked_sub(1).ok_or(ParseError::UnexpectedToken {
                    token: ")".to_string(),
                    position,
                })?;
            }
            _ => {}
        }
    }
    if in_string {
        return Err(ParseError::Unterminated);
    }
    Ok(max)
}

/// Attaching context that `#[from]` alone cannot supply: the path. Note the
/// deliberate `map_err` — when a variant carries extra data, you give up the
/// blanket conversion and construct it by hand at the call site that knows the
/// missing piece.
pub fn read_rule(path: &std::path::Path) -> Result<String, RuleError> {
    std::fs::read_to_string(path).map_err(|source| RuleError::Io {
        path: path.to_path_buf(),
        source,
    })
}

/// Render an error and everything beneath it. `std::error::Error::source` is
/// the same idea as `InnerException`, and thiserror wired it up for you.
pub fn describe(err: &dyn std::error::Error) -> String {
    let mut parts = vec![err.to_string()];
    let mut current = err.source();
    while let Some(cause) = current {
        parts.push(cause.to_string());
        current = cause.source();
    }
    parts.join(" <- ")
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
```
