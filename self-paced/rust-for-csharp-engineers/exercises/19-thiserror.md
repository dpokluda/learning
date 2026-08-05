# Exercises 19 — thiserror

> **Covers:** [19 — thiserror](../19-anyhow-and-thiserror.md). **Code:** `crate-drills/src/ch19.rs`. **Answers:** [answers/19-thiserror.md](answers/19-thiserror.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** State the 'anyhow for binaries, thiserror for libraries' rule and derive it from first principles rather than quoting it.

**A2.** What exactly does `#[from]` generate, and why is it more than a convenience?

**A3.** When can you not use `#[from]`, and what do you do instead?

**A4.** What does `#[error(transparent)]` do, and when is it the right choice?

**A5.** Compare a thiserror enum with a C# exception hierarchy. What is genuinely different, not just differently spelled?

**A6.** A typed error is more precise than `anyhow::Error`, yet applications routinely convert one into the other. Is anything lost?

## Part B — Exercise

Open `crate-drills/src/ch19.rs`. This is the library half of the error story: you
are building a typed, matchable error API of the kind you would publish, rather
than the context-chained blob an application prints and exits on.

The chapter ships with placeholder `#[error("TODO: ...")]` attributes so the
crate compiles before you start. Replace them with the exact messages the tests
require, then wire up the relationships: `#[from]` on the variant that wraps a
`ParseError`, so `?` converts with no ceremony and the cause chain works;
`#[error(transparent)]` on the variant that should widen a type without adding a
message layer; and a hand-written `map_err` for the variant that carries a path,
because `From<io::Error>` cannot invent one.

There is a genuine parser underneath — count maximum parenthesis nesting, ignore
parentheses inside string literals, reject an unbalanced `)` with its byte offset,
reject text that ends mid-literal. It is small, but it gives the error type
something real to describe.

Watch for one thiserror behaviour the tests quietly rely on: a field *named*
`source` is treated as the cause automatically, with or without the `#[source]`
attribute. Finally, `describe` walks `std::error::Error::source` to the bottom,
which is `InnerException` traversal spelled as a loop.

Run it with `cargo test ch19` from the `exercises/crate-drills` directory.

### Starter stub

```rust,ignore
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
```

The test module that follows this in the file is the specification — read it before you write anything.
