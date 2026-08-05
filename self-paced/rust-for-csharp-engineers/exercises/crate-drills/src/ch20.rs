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
