# Exercises 20 — serde

> **Covers:** [20 — serde](../20-serde.md). **Code:** `crate-drills/src/ch20.rs`. **Answers:** [answers/20-serde.md](answers/20-serde.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** `System.Text.Json` has a runtime type model and a source generator. serde has neither a runtime model nor, in the usual sense, reflection. How does it work?

**A2.** `rename_all`, `rename`, `default` and `skip_serializing_if` all shape the wire format. Explain how they interact and what each one costs.

**A3.** Describe serde's four enum representations and say what each is good for.

**A4.** What is borrowed deserialization, what does it buy, and what does it cost?

**A5.** `try_from` lets you enforce an invariant during deserialization. Where does the attribute go, and why is that placement the one people get wrong?

**A6.** `deny_unknown_fields` is not the default. Argue both sides, then say when you would turn it on.

## Part B — Exercise

Open `crate-drills/src/ch20.rs`. Every type you need is already declared; this
drill is pure attribute work, and it is the fastest way to build a working mental
index of serde's field and container attributes.

The first group shapes one struct's wire format: camelCase everywhere except one
irregular field, unknown keys rejected, two fields defaulting when absent, an
empty map omitted from the output entirely, and a nested type whose fields appear
at the parent level rather than under their own key. Read the tests carefully —
each asserts on the exact JSON, so there is no ambiguity about what each attribute
must do.

The second group is the one C# has no equivalent for: four enum representations,
externally, internally and adjacently tagged plus untagged, each producing a
different shape from the same variants. Write them, then look at the four asserted
strings side by side, because that comparison is the thing worth remembering.

The last three are individually small and individually important. `BorrowedRecord`
must deserialize without allocating — the test proves it by checking the field
pointer lands inside the input buffer. `RuleName` must validate during
deserialization via a `TryFrom<String>`, using a *container* attribute, which is
the placement people get wrong. And the final drill just round-trips the struct,
which is the check you should write for any type that crosses a wire.

Run it with `cargo test ch20` from the `exercises/crate-drills` directory.

### Starter stub

```rust,ignore
//! Crate drill 20 — serde: derive-time serialization.
//!
//! `System.Text.Json` reads your type at runtime through reflection and a
//! source generator when you ask nicely. serde has no runtime model at all:
//! `#[derive(Serialize, Deserialize)]` emits the code at compile time, against
//! a data model that is format-agnostic. That is why the same derive drives
//! JSON, TOML and a binary codec without knowing about any of them.
//!
//! This chapter is entirely attribute work. The types are given; add the
//! `#[serde(...)]` annotations until the tests describing the wire format pass.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};

/// Required wire format:
/// * fields are `camelCase`, **except** `definition_id`, which is
///   `policyDefinitionId`
/// * unknown keys are rejected rather than ignored
/// * `enforcement_mode` and `parameters` default when absent
/// * `parameters` is omitted from the output entirely when empty
/// * `audit`'s fields appear at *this* level, not nested under `audit`
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Assignment {
    pub id: String,
    pub definition_id: String,
    pub enforcement_mode: EnforcementMode,
    pub parameters: BTreeMap<String, String>,
    pub audit: Audit,
}

/// `created_by` is `camelCase`, optional, and omitted when `None`; `revision`
/// defaults to zero.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct Audit {
    pub created_by: Option<String>,
    pub revision: u32,
}

/// Serializes as a bare string in `camelCase`: `"default"` / `"doNotEnforce"`.
/// `Default` must be the `Default` variant.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum EnforcementMode {
    #[default]
    Default,
    DoNotEnforce,
}

/// Externally tagged (serde's default), with `camelCase` variant names:
/// `{"eq":{"field":"tag","value":"prod"}}` and `"always"`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum ExternallyTagged {
    Eq { field: String, value: String },
    Always,
}

/// Internally tagged on a `kind` key, `camelCase`:
/// `{"kind":"eq","field":"tag","value":"prod"}` and `{"kind":"always"}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum InternallyTagged {
    Eq { field: String, value: String },
    Always,
}

/// Adjacently tagged on `kind` / `value`, `camelCase`:
/// `{"kind":"count","value":3}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum AdjacentlyTagged {
    Count(u32),
    Name(String),
}

/// Untagged: `{"field":"tag"}` deserializes to `Structured`, a bare string to
/// `Shorthand`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum Untagged {
    Structured { field: String },
    Shorthand(String),
}

/// Zero-copy deserialization: both fields must borrow directly out of the input
/// buffer rather than allocating. The test proves it by comparing pointers.
#[derive(Debug, PartialEq, Deserialize)]
pub struct BorrowedRecord<'a> {
    pub id: &'a str,
    pub kind: &'a str,
}

/// A newtype that enforces an invariant at the serialization boundary: route
/// deserialization through `TryFrom<String>` and serialization through
/// `Into<String>`. Note this is a *container* attribute — reaching for it on a
/// field is a common mistake.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RuleName(String);

impl TryFrom<String> for RuleName {
    type Error = String;

    /// Reject anything that is empty or longer than 32 characters with
    /// `format!("rule name must be 1..=32 chars, got {}", value.len())`.
    fn try_from(_value: String) -> Result<Self, Self::Error> {
        todo!("validate the length before constructing the newtype")
    }
}

impl From<RuleName> for String {
    fn from(value: RuleName) -> Self {
        value.0
    }
}
```

The test module that follows this in the file is the specification — read it before you write anything.
