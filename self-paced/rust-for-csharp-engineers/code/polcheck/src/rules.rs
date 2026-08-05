//! The rule model: an algebraic data type describing a governance rule,
//! and the evaluator that applies it to a resource.

use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

use crate::error::{Error, Result};

/// How serious a violation is.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Severity {
    Info,
    Warning,
    Error,
}

impl std::fmt::Display for Severity {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let s = match self {
            Severity::Info => "info",
            Severity::Warning => "warning",
            Severity::Error => "error",
        };
        f.write_str(s)
    }
}

/// A resource to be evaluated: an id, a type, and a flat bag of string fields.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Resource {
    pub id: String,
    #[serde(rename = "type")]
    pub kind: String,
    #[serde(default)]
    pub fields: BTreeMap<String, String>,
}

/// The condition tree. This is the algebraic data type the whole book has been
/// building towards: a closed set of variants, recursive, exhaustively matched.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "op", rename_all = "camelCase")]
pub enum Condition {
    /// The field exists and is non-empty.
    Exists { field: String },
    /// The field equals a literal value.
    Equals { field: String, value: String },
    /// The field's value is one of a set.
    OneOf { field: String, values: Vec<String> },
    /// Every nested condition holds.
    All { of: Vec<Condition> },
    /// At least one nested condition holds.
    Any { of: Vec<Condition> },
    /// The nested condition does not hold.
    Not { of: Box<Condition> },
}

/// A named rule: a condition, a severity, and the resource types it applies to.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Rule {
    pub name: String,
    pub severity: Severity,
    #[serde(default)]
    pub applies_to: Vec<String>,
    pub condition: Condition,
}

/// A rule set, as loaded from a JSON file.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RuleSet {
    pub rules: Vec<Rule>,
}

/// A single violation.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct Finding {
    pub resource_id: String,
    pub rule: String,
    pub severity: Severity,
    pub detail: String,
}

impl Condition {
    /// Structural depth, used to reject pathological rule files.
    pub fn depth(&self) -> usize {
        match self {
            Condition::Exists { .. } | Condition::Equals { .. } | Condition::OneOf { .. } => 1,
            Condition::All { of } | Condition::Any { of } => {
                1 + of.iter().map(Condition::depth).max().unwrap_or(0)
            }
            Condition::Not { of } => 1 + of.depth(),
        }
    }

    /// Evaluate against a resource. `Ok(true)` means the condition holds.
    ///
    /// `strict` turns a reference to an absent field into an error rather than
    /// treating it as "does not hold" — the difference between a rule author's
    /// typo and a resource that genuinely lacks the field.
    pub fn eval(&self, resource: &Resource, rule: &str, strict: bool) -> Result<bool> {
        match self {
            Condition::Exists { field } => {
                Ok(resource.fields.get(field).is_some_and(|v| !v.is_empty()))
            }

            Condition::Equals { field, value } => match resource.fields.get(field) {
                Some(actual) => Ok(actual == value),
                None if strict => Err(Error::UnknownField {
                    rule: rule.to_string(),
                    field: field.clone(),
                }),
                None => Ok(false),
            },

            Condition::OneOf { field, values } => match resource.fields.get(field) {
                Some(actual) => Ok(values.iter().any(|v| v == actual)),
                None if strict => Err(Error::UnknownField {
                    rule: rule.to_string(),
                    field: field.clone(),
                }),
                None => Ok(false),
            },

            // `?` inside a closure would return from the closure, so these use
            // an explicit loop rather than `.all()` / `.any()`.
            Condition::All { of } => {
                for c in of {
                    if !c.eval(resource, rule, strict)? {
                        return Ok(false);
                    }
                }
                Ok(true)
            }

            Condition::Any { of } => {
                for c in of {
                    if c.eval(resource, rule, strict)? {
                        return Ok(true);
                    }
                }
                Ok(false)
            }

            Condition::Not { of } => Ok(!of.eval(resource, rule, strict)?),
        }
    }
}

impl Rule {
    /// Whether this rule applies to the given resource.
    pub fn applies(&self, resource: &Resource) -> bool {
        self.applies_to.is_empty() || self.applies_to.iter().any(|t| t == &resource.kind)
    }

    /// Evaluate, returning `Some(finding)` when the rule is violated.
    pub fn check(&self, resource: &Resource, strict: bool) -> Result<Option<Finding>> {
        if !self.applies(resource) {
            return Ok(None);
        }
        if self.condition.eval(resource, &self.name, strict)? {
            return Ok(None);
        }
        Ok(Some(Finding {
            resource_id: resource.id.clone(),
            rule: self.name.clone(),
            severity: self.severity,
            detail: format!("resource does not satisfy `{}`", self.name),
        }))
    }
}

impl RuleSet {
    /// Reject rule files that nest beyond `limit`.
    pub fn validate(&self, limit: usize) -> Result<()> {
        for rule in &self.rules {
            if rule.condition.depth() > limit {
                return Err(Error::TooDeep {
                    rule: rule.name.clone(),
                    limit,
                });
            }
        }
        Ok(())
    }

    /// Evaluate every rule against every resource.
    pub fn evaluate(&self, resources: &[Resource], strict: bool) -> Result<Vec<Finding>> {
        let mut findings = Vec::new();
        for resource in resources {
            for rule in &self.rules {
                if let Some(f) = rule.check(resource, strict)? {
                    findings.push(f);
                }
            }
        }
        findings.sort_by(|a, b| {
            b.severity
                .cmp(&a.severity)
                .then_with(|| a.resource_id.cmp(&b.resource_id))
                .then_with(|| a.rule.cmp(&b.rule))
        });
        Ok(findings)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn resource(id: &str, kind: &str, pairs: &[(&str, &str)]) -> Resource {
        Resource {
            id: id.to_string(),
            kind: kind.to_string(),
            fields: pairs
                .iter()
                .map(|(k, v)| (k.to_string(), v.to_string()))
                .collect(),
        }
    }

    #[test]
    fn exists_requires_a_non_empty_value() {
        let r = resource("a", "vm", &[("owner", "dave"), ("env", "")]);
        let owner = Condition::Exists {
            field: "owner".into(),
        };
        let env = Condition::Exists {
            field: "env".into(),
        };
        let missing = Condition::Exists {
            field: "cost".into(),
        };

        assert!(owner.eval(&r, "t", false).unwrap());
        assert!(!env.eval(&r, "t", false).unwrap());
        assert!(!missing.eval(&r, "t", false).unwrap());
    }

    #[test]
    fn strict_mode_turns_a_missing_field_into_an_error() {
        let r = resource("a", "vm", &[]);
        let c = Condition::Equals {
            field: "owner".into(),
            value: "dave".into(),
        };
        assert!(!c.eval(&r, "t", false).unwrap());
        assert!(matches!(
            c.eval(&r, "require-owner", true),
            Err(Error::UnknownField { .. })
        ));
    }

    #[test]
    fn boolean_combinators_short_circuit() {
        let r = resource("a", "vm", &[("env", "prod")]);
        let all = Condition::All {
            of: vec![
                Condition::Exists {
                    field: "env".into(),
                },
                Condition::OneOf {
                    field: "env".into(),
                    values: vec!["prod".into(), "staging".into()],
                },
            ],
        };
        assert!(all.eval(&r, "t", false).unwrap());

        let any = Condition::Any {
            of: vec![
                Condition::Equals {
                    field: "env".into(),
                    value: "dev".into(),
                },
                Condition::Equals {
                    field: "env".into(),
                    value: "prod".into(),
                },
            ],
        };
        assert!(any.eval(&r, "t", false).unwrap());

        let not = Condition::Not { of: Box::new(any) };
        assert!(!not.eval(&r, "t", false).unwrap());
    }

    #[test]
    fn applies_to_filters_by_resource_type() {
        let rule = Rule {
            name: "require-owner".into(),
            severity: Severity::Error,
            applies_to: vec!["vm".into()],
            condition: Condition::Exists {
                field: "owner".into(),
            },
        };
        assert!(rule.applies(&resource("a", "vm", &[])));
        assert!(!rule.applies(&resource("b", "storage", &[])));
        // A non-applying rule yields no finding even though the field is absent.
        assert_eq!(
            rule.check(&resource("b", "storage", &[]), false).unwrap(),
            None
        );
    }

    #[test]
    fn depth_is_measured_through_nesting() {
        let flat = Condition::Exists { field: "a".into() };
        assert_eq!(flat.depth(), 1);

        let nested = Condition::All {
            of: vec![Condition::Not {
                of: Box::new(Condition::Exists { field: "a".into() }),
            }],
        };
        assert_eq!(nested.depth(), 3);

        let set = RuleSet {
            rules: vec![Rule {
                name: "deep".into(),
                severity: Severity::Info,
                applies_to: vec![],
                condition: nested,
            }],
        };
        assert!(set.validate(3).is_ok());
        assert!(matches!(set.validate(2), Err(Error::TooDeep { .. })));
    }

    #[test]
    fn findings_are_sorted_by_severity_then_resource() {
        let set = RuleSet {
            rules: vec![
                Rule {
                    name: "info-rule".into(),
                    severity: Severity::Info,
                    applies_to: vec![],
                    condition: Condition::Exists {
                        field: "nope".into(),
                    },
                },
                Rule {
                    name: "error-rule".into(),
                    severity: Severity::Error,
                    applies_to: vec![],
                    condition: Condition::Exists {
                        field: "nope".into(),
                    },
                },
            ],
        };
        let resources = vec![resource("z", "vm", &[]), resource("a", "vm", &[])];
        let findings = set.evaluate(&resources, false).unwrap();

        assert_eq!(findings.len(), 4);
        assert_eq!(findings[0].severity, Severity::Error);
        assert_eq!(findings[0].resource_id, "a");
        assert_eq!(findings[3].severity, Severity::Info);
    }

    #[test]
    fn ruleset_round_trips_through_json() {
        let json = r#"{
          "rules": [{
            "name": "require-owner",
            "severity": "error",
            "applies_to": ["vm"],
            "condition": { "op": "exists", "field": "owner" }
          }]
        }"#;
        let set: RuleSet = serde_json::from_str(json).unwrap();
        assert_eq!(set.rules.len(), 1);
        assert_eq!(set.rules[0].severity, Severity::Error);

        let back = serde_json::to_string(&set).unwrap();
        assert!(back.contains("\"op\":\"exists\""));
    }
}
