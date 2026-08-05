//! `polcheck` — evaluate governance rules against JSON resource records.
//!
//! This is the capstone project for *Rust for C# Engineers*. It is deliberately
//! small but structurally complete: a library crate holding the domain logic
//! with typed errors, and a thin binary that parses arguments, loads layered
//! configuration, sets up tracing, and converts library errors into a report
//! for the user.
//!
//! ```
//! use polcheck::rules::{Condition, Resource, Rule, RuleSet, Severity};
//! use std::collections::BTreeMap;
//!
//! let set = RuleSet {
//!     rules: vec![Rule {
//!         name: "require-owner".into(),
//!         severity: Severity::Error,
//!         applies_to: vec![],
//!         condition: Condition::Exists { field: "owner".into() },
//!     }],
//! };
//!
//! let resource = Resource {
//!     id: "res-1".into(),
//!     kind: "vm".into(),
//!     fields: BTreeMap::new(),
//! };
//!
//! let findings = set.evaluate(&[resource], false).unwrap();
//! assert_eq!(findings.len(), 1);
//! assert_eq!(findings[0].rule, "require-owner");
//! ```

pub mod config;
pub mod error;
pub mod report;
pub mod rules;
pub mod source;

pub use error::{Error, Result};
