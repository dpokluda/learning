# Answers 20 — serde

> Exercises: [20-serde.md](../20-serde.md)

## Part A

**A1. `System.Text.Json` has a runtime type model and a source generator. serde has neither a runtime model nor, in the usual sense, reflection. How does it work?**

serde splits the problem into a data model in the middle and two derives on the edges. `Serialize` is a trait whose implementation walks *your* type and calls methods on a generic `Serializer` — `serialize_struct`, `serialize_field`, and so on — describing the value in terms of serde's twenty-nine-element data model. `Deserialize` is the mirror image, driving a `Visitor` that the format calls back into. `#[derive(Serialize, Deserialize)]` generates those implementations at compile time from the struct definition, fully monomorphized against whichever format you use. The consequence is that your type knows nothing about JSON and JSON knows nothing about your type, which is why the identical derive drives TOML, YAML, MessagePack and a binary codec, and why there is no reflection cost at runtime. It is closer in spirit to `System.Text.Json`'s source generator than to its reflection path, but it has been the only mode since the beginning rather than an opt-in for trimming.

**A2. `rename_all`, `rename`, `default` and `skip_serializing_if` all shape the wire format. Explain how they interact and what each one costs.**

`rename_all` is a container attribute setting a naming convention for every field, and `rename` on an individual field overrides it — so the common shape is one `rename_all = "camelCase"` plus a handful of exceptions where the wire name is genuinely irregular. `default` affects deserialization only: an absent key gets `Default::default()` (or a named function's result) instead of being an error, which is how you add a field without breaking older payloads. `skip_serializing_if` affects serialization only: it takes a predicate path and omits the key when it returns true, keeping output tidy without making the field `Option`. The two are usually paired, because a field you omit on the way out you must tolerate missing on the way in. The cost of `default` is that a typo in the key becomes silence rather than an error, which is exactly why `deny_unknown_fields` earns its place next to it.

**A3. Describe serde's four enum representations and say what each is good for.**

*Externally tagged* is the default: the variant name is the key and the payload is its value, `{"eq":{"field":"tag"}}`. It round-trips every variant shape and is unambiguous, but it nests awkwardly and few REST APIs use it. *Internally tagged* puts a discriminant field alongside the data, `{"kind":"eq","field":"tag"}` — the shape most hand-designed JSON APIs actually use, and what .NET 8's `JsonPolymorphic` emits, but it cannot represent a newtype variant wrapping a non-map value. *Adjacently tagged* gives the discriminant and the payload separate keys, `{"kind":"count","value":3}`; it works for every variant shape at the cost of an extra level. *Untagged* has no discriminant and decides by trying each variant in declaration order, which is convenient for genuinely polymorphic input and is a trap: the first variant that parses wins, error messages degrade to 'data did not match any variant', and adding a permissive variant can silently capture input the previous one handled.

**A4. What is borrowed deserialization, what does it buy, and what does it cost?**

A field typed `&'a str` (or `&'a [u8]`, or `Cow<'a, str>`) deserializes by pointing into the input buffer rather than allocating a new `String`, so a struct of borrowed fields costs zero allocations to parse. For a hot path parsing many small records that is a substantial win, and it is something .NET can only approach by dropping to `Utf8JsonReader` and doing the work by hand. The cost is the lifetime: the deserialized value cannot outlive the text it borrows from, which propagates through every type that contains it and rules out storing it in a long-lived structure or sending it across a task boundary. It also only works when the input needs no transformation — a JSON string containing an escape sequence must be unescaped into fresh storage, which is why `Cow<'a, str>` with `#[serde(borrow)]` is the pragmatic choice: borrow when possible, allocate when not.

**A5. `try_from` lets you enforce an invariant during deserialization. Where does the attribute go, and why is that placement the one people get wrong?**

It is a *container* attribute — `#[serde(try_from = "String")]` on the struct or enum — and it means 'deserialize a `String`, then run `TryFrom<String>` to produce this type, surfacing any error as a deserialization error'. People reach for it on a field, because that is where the validation feels like it belongs, and it silently does not apply. The right decomposition is a newtype wrapping the raw representation, carrying the container attribute, used as the field's type — which is a good design anyway, because the invariant then holds everywhere the type appears rather than only at one deserialization site. It is the parse-don't-validate discipline applied to the wire boundary, and it is strictly stronger than a `JsonConverter` that validates, because the only way to construct the type is through the conversion.

**A6. `deny_unknown_fields` is not the default. Argue both sides, then say when you would turn it on.**

The default of ignoring unknown keys is what makes forward compatibility work: a client written against version one keeps functioning when the server starts sending a version-two field, which is essential for any evolving API and is why `System.Text.Json` behaves the same way. Turning it on trades that for catching typos: a config file with `endpont` instead of `endpoint` becomes an error naming the offending key rather than a default that silently takes effect and a bug you find in production. The rule that falls out is to deny unknown fields on things a human writes and types by hand — configuration files, rule definitions, manifests — and to permit them on anything arriving over a wire from another system you do not deploy in lockstep with. Note the interaction with `flatten`: serde cannot know which fields belong to the flattened type, so `deny_unknown_fields` and `flatten` do not compose.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` and `cargo clippy -- -D warnings` on the pinned toolchain.

```rust
//! Crate drill 20 — serde: derive-time serialization.
//!
//! `System.Text.Json` reads your type at runtime through reflection and a
//! source generator when you ask nicely. serde has no runtime model at all:
//! `#[derive(Serialize, Deserialize)]` emits the code at compile time, against
//! a data model that is format-agnostic. That is why the same derive drives
//! JSON, TOML and a binary codec without knowing about any of them.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};

/// The workhorse. `rename_all` fixes the C#-to-JSON casing mismatch once for
/// the whole type rather than attribute-by-attribute, `default` supplies a
/// value when the field is absent, and `skip_serializing_if` keeps the output
/// tidy without making the field optional on the way in.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Assignment {
    pub id: String,

    /// Renamed individually because the wire name does not follow the type's
    /// convention. Field attributes win over the container attribute.
    #[serde(rename = "policyDefinitionId")]
    pub definition_id: String,

    /// Absent in older payloads; `default` makes that a non-breaking change
    /// instead of a deserialization failure.
    #[serde(default)]
    pub enforcement_mode: EnforcementMode,

    /// Present in the struct, omitted from the output when empty.
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    pub parameters: BTreeMap<String, String>,

    /// `flatten` splices the inner type's fields into this level, which is how
    /// you model shared metadata without nesting it on the wire.
    #[serde(flatten)]
    pub audit: Audit,
}

#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Audit {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub created_by: Option<String>,
    #[serde(default)]
    pub revision: u32,
}

/// A plain unit-variant enum serializes as a bare string, which is what a
/// `JsonStringEnumConverter` gives you in .NET — except here it is the default
/// rather than an opt-in.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum EnforcementMode {
    #[default]
    Default,
    DoNotEnforce,
}

/// Enum representations are where serde leaves `System.Text.Json` behind, since
/// .NET has no built-in encoding for a discriminated union at all. This is the
/// *externally tagged* default: `{"eq": {...}}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum ExternallyTagged {
    Eq { field: String, value: String },
    Always,
}

/// *Internally tagged*: the discriminant becomes a field alongside the data,
/// `{"kind": "eq", "field": ..., "value": ...}`. This is the shape most REST
/// APIs actually use, and the one `JsonPolymorphic` produces in .NET 8+.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum InternallyTagged {
    Eq { field: String, value: String },
    Always,
}

/// *Adjacently tagged*: discriminant and payload get their own keys. It is the
/// only representation that works for every variant shape, including newtype
/// variants wrapping a primitive.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kind", content = "value", rename_all = "camelCase")]
pub enum AdjacentlyTagged {
    Count(u32),
    Name(String),
}

/// *Untagged*: no discriminant, decided by trying each variant in order. It is
/// convenient and it is a trap — the first variant that parses wins, so put the
/// most specific one first and never rely on it for round-tripping ambiguity.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum Untagged {
    Structured { field: String },
    Shorthand(String),
}

/// Zero-copy deserialization. `&'a str` borrows directly out of the input
/// buffer, so this struct costs no allocations at all — something a .NET
/// deserializer can only approach with `Utf8JsonReader` and manual work.
/// The lifetime is the price: the value cannot outlive the JSON text.
#[derive(Debug, PartialEq, Deserialize)]
pub struct BorrowedRecord<'a> {
    pub id: &'a str,
    pub kind: &'a str,
}

/// A container-level `try_from` routes deserialization through a fallible
/// conversion, which is how you enforce an invariant at the boundary. Note this
/// is a *container* attribute — a common mistake is reaching for it on a field.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(try_from = "String", into = "String")]
pub struct RuleName(String);

impl TryFrom<String> for RuleName {
    type Error = String;

    fn try_from(value: String) -> Result<Self, Self::Error> {
        if value.is_empty() || value.len() > 32 {
            return Err(format!("rule name must be 1..=32 chars, got {}", value.len()));
        }
        Ok(RuleName(value))
    }
}

impl From<RuleName> for String {
    fn from(value: RuleName) -> Self {
        value.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample() -> Assignment {
        Assignment {
            id: "a1".into(),
            definition_id: "d1".into(),
            enforcement_mode: EnforcementMode::DoNotEnforce,
            parameters: BTreeMap::new(),
            audit: Audit {
                created_by: Some("david".into()),
                revision: 2,
            },
        }
    }

    #[test]
    fn rename_all_and_rename_shape_the_wire_format() {
        let json = serde_json::to_value(sample()).unwrap();
        assert_eq!(json["id"], "a1");
        assert_eq!(json["policyDefinitionId"], "d1");
        assert_eq!(json["enforcementMode"], "doNotEnforce");
    }

    #[test]
    fn skip_serializing_if_omits_the_key_entirely() {
        let json = serde_json::to_value(sample()).unwrap();
        assert!(json.get("parameters").is_none(), "empty map must be omitted");

        let mut with_params = sample();
        with_params.parameters.insert("tag".into(), "prod".into());
        let json = serde_json::to_value(with_params).unwrap();
        assert_eq!(json["parameters"]["tag"], "prod");
    }

    #[test]
    fn flatten_splices_the_inner_fields_into_this_level() {
        let json = serde_json::to_value(sample()).unwrap();
        // Not `json["audit"]["createdBy"]` — flatten removed the nesting.
        assert_eq!(json["createdBy"], "david");
        assert_eq!(json["revision"], 2);
        assert!(json.get("audit").is_none());
    }

    #[test]
    fn default_makes_a_missing_field_a_non_event() {
        let text = r#"{"id":"a1","policyDefinitionId":"d1"}"#;
        let value: Assignment = serde_json::from_str(text).unwrap();

        assert_eq!(value.enforcement_mode, EnforcementMode::Default);
        assert_eq!(value.audit.revision, 0);
        assert!(value.parameters.is_empty());
    }

    #[test]
    fn deny_unknown_fields_turns_a_typo_into_an_error() {
        // The default is to ignore unknown keys, which is what
        // System.Text.Json does too — and why misspelled config silently
        // does nothing. Opting in catches it.
        let text = r#"{"id":"a1","policyDefinitionId":"d1","enforcmentMode":"default"}"#;
        let err = serde_json::from_str::<Assignment>(text).unwrap_err();
        assert!(err.to_string().contains("unknown field"));
    }

    #[test]
    fn the_four_enum_representations_produce_four_different_shapes() {
        let ext = ExternallyTagged::Eq {
            field: "tag".into(),
            value: "prod".into(),
        };
        assert_eq!(
            serde_json::to_string(&ext).unwrap(),
            r#"{"eq":{"field":"tag","value":"prod"}}"#
        );
        assert_eq!(serde_json::to_string(&ExternallyTagged::Always).unwrap(), r#""always""#);

        let int = InternallyTagged::Eq {
            field: "tag".into(),
            value: "prod".into(),
        };
        assert_eq!(
            serde_json::to_string(&int).unwrap(),
            r#"{"kind":"eq","field":"tag","value":"prod"}"#
        );
        assert_eq!(
            serde_json::to_string(&InternallyTagged::Always).unwrap(),
            r#"{"kind":"always"}"#
        );

        assert_eq!(
            serde_json::to_string(&AdjacentlyTagged::Count(3)).unwrap(),
            r#"{"kind":"count","value":3}"#
        );
    }

    #[test]
    fn untagged_picks_the_first_variant_that_parses() {
        let structured: Untagged = serde_json::from_str(r#"{"field":"tag"}"#).unwrap();
        assert_eq!(structured, Untagged::Structured { field: "tag".into() });

        let shorthand: Untagged = serde_json::from_str(r#""tag""#).unwrap();
        assert_eq!(shorthand, Untagged::Shorthand("tag".into()));
    }

    #[test]
    fn borrowed_deserialization_points_into_the_input_buffer() {
        let text = String::from(r#"{"id":"a1","kind":"eq"}"#);
        let record: BorrowedRecord<'_> = serde_json::from_str(&text).unwrap();
        assert_eq!(record.id, "a1");

        // Proof it really borrowed: the field points inside `text`.
        let base = text.as_ptr() as usize;
        let field = record.id.as_ptr() as usize;
        assert!(field > base && field < base + text.len());
    }

    #[test]
    fn try_from_enforces_an_invariant_at_the_boundary() {
        let ok: RuleName = serde_json::from_str(r#""deny-public-ip""#).unwrap();
        assert_eq!(serde_json::to_string(&ok).unwrap(), r#""deny-public-ip""#);

        let err = serde_json::from_str::<RuleName>(r#""""#).unwrap_err();
        assert!(err.to_string().contains("1..=32"));
    }

    #[test]
    fn a_full_round_trip_is_lossless() {
        let original = sample();
        let text = serde_json::to_string(&original).unwrap();
        let back: Assignment = serde_json::from_str(&text).unwrap();
        assert_eq!(original, back);
    }
}
```
