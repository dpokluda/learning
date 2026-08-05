//! Rendering findings for humans and for machines.

use crate::rules::{Finding, Severity};

/// How to present the results.
#[derive(Debug, Clone, Copy, PartialEq, Eq, clap::ValueEnum)]
pub enum Format {
    /// Aligned plain text for a terminal.
    Text,
    /// One JSON array, for piping into `jq`.
    Json,
}

/// Render findings to a string.
pub fn render(findings: &[Finding], format: Format) -> Result<String, serde_json::Error> {
    match format {
        Format::Json => serde_json::to_string_pretty(findings),
        Format::Text => Ok(render_text(findings)),
    }
}

fn render_text(findings: &[Finding]) -> String {
    if findings.is_empty() {
        return "no findings\n".to_string();
    }

    let widest = findings
        .iter()
        .map(|f| f.resource_id.len())
        .max()
        .unwrap_or(0);

    let mut out = String::new();
    for f in findings {
        out.push_str(&format!(
            "{:<7} {:<width$}  {}\n",
            f.severity.to_string(),
            f.resource_id,
            f.rule,
            width = widest
        ));
    }
    out.push_str(&format!("\n{} finding(s)\n", findings.len()));
    out
}

/// The process exit code: 1 when any finding is at or above `threshold`.
pub fn exit_code(findings: &[Finding], threshold: Severity) -> i32 {
    if findings.iter().any(|f| f.severity >= threshold) {
        1
    } else {
        0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn finding(id: &str, rule: &str, severity: Severity) -> Finding {
        Finding {
            resource_id: id.into(),
            rule: rule.into(),
            severity,
            detail: String::new(),
        }
    }

    #[test]
    fn empty_input_renders_a_friendly_line() {
        assert_eq!(render(&[], Format::Text).unwrap(), "no findings\n");
        assert_eq!(render(&[], Format::Json).unwrap(), "[]");
    }

    #[test]
    fn text_output_aligns_on_the_longest_id() {
        let f = vec![
            finding("short", "a", Severity::Error),
            finding("a-much-longer-id", "b", Severity::Info),
        ];
        let out = render(&f, Format::Text).unwrap();
        assert!(out.contains("error   short             a"));
        assert!(out.contains("2 finding(s)"));
    }

    #[test]
    fn json_output_is_an_array_of_objects() {
        let f = vec![finding("r", "rule", Severity::Warning)];
        let out = render(&f, Format::Json).unwrap();
        let parsed: serde_json::Value = serde_json::from_str(&out).unwrap();
        assert_eq!(parsed[0]["severity"], "warning");
        assert_eq!(parsed[0]["resource_id"], "r");
    }

    #[test]
    fn exit_code_respects_the_threshold() {
        let warn = vec![finding("r", "rule", Severity::Warning)];
        assert_eq!(exit_code(&warn, Severity::Error), 0);
        assert_eq!(exit_code(&warn, Severity::Warning), 1);
        assert_eq!(exit_code(&warn, Severity::Info), 1);
        assert_eq!(exit_code(&[], Severity::Info), 0);
    }
}
