//! End-to-end tests that run the real binary, the way a user would.
//!
//! These are the analogue of an xUnit test that shells out to the tool: they
//! exercise argument parsing, configuration layering, exit codes, and stdout
//! formatting together. No network is used.

use assert_cmd::Command;
use predicates::prelude::PredicateBooleanExt;
use predicates::str::contains;
use std::io::Write;

fn write(dir: &tempfile::TempDir, name: &str, body: &str) -> std::path::PathBuf {
    let path = dir.path().join(name);
    let mut f = std::fs::File::create(&path).unwrap();
    f.write_all(body.as_bytes()).unwrap();
    path
}

const RULES: &str = r#"{
  "rules": [
    {
      "name": "require-owner",
      "severity": "error",
      "applies_to": ["vm"],
      "condition": { "op": "exists", "field": "owner" }
    },
    {
      "name": "env-must-be-known",
      "severity": "warning",
      "applies_to": [],
      "condition": {
        "op": "oneOf",
        "field": "env",
        "values": ["dev", "staging", "prod"]
      }
    }
  ]
}"#;

const RESOURCES: &str = r#"[
  { "id": "res-good", "type": "vm", "fields": { "owner": "dave", "env": "prod" } },
  { "id": "res-bad",  "type": "vm", "fields": { "env": "wat" } }
]"#;

#[test]
fn scan_reports_findings_and_exits_nonzero() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(&dir, "rules.json", RULES);
    let resources = write(&dir, "resources.json", RESOURCES);

    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .assert()
        .code(1)
        .stdout(contains("error").and(contains("require-owner")))
        .stdout(contains("res-bad"))
        .stdout(contains("2 finding(s)"));
}

#[test]
fn a_clean_scan_exits_zero() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(&dir, "rules.json", RULES);
    let resources = write(
        &dir,
        "resources.json",
        r#"[{ "id": "ok", "type": "vm", "fields": { "owner": "dave", "env": "prod" } }]"#,
    );

    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .assert()
        .code(0)
        .stdout(contains("no findings"));
}

#[test]
fn json_output_parses_as_json() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(&dir, "rules.json", RULES);
    let resources = write(&dir, "resources.json", RESOURCES);

    let out = Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "--format", "json", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .output()
        .unwrap();

    let parsed: serde_json::Value = serde_json::from_slice(&out.stdout).unwrap();
    assert_eq!(parsed.as_array().unwrap().len(), 2);
    assert_eq!(parsed[0]["severity"], "error");
}

#[test]
fn fail_on_threshold_changes_the_exit_code() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(
        &dir,
        "rules.json",
        r#"{"rules":[{"name":"info-only","severity":"info","applies_to":[],
            "condition":{"op":"exists","field":"nope"}}]}"#,
    );
    let resources = write(
        &dir,
        "resources.json",
        r#"[{"id":"a","type":"vm","fields":{}}]"#,
    );

    // Default threshold is `error`, and the only finding is `info`.
    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .assert()
        .code(0);

    // Lower the threshold and the same run fails.
    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "--fail-on", "info", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .assert()
        .code(1);
}

#[test]
fn a_missing_rule_file_produces_a_readable_error_chain() {
    let dir = tempfile::tempdir().unwrap();
    let resources = write(&dir, "resources.json", RESOURCES);

    let out = Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "-r", "does-not-exist.json", "-R"])
        .arg(&resources)
        .output()
        .unwrap();

    assert!(!out.status.success());
    let stderr = String::from_utf8_lossy(&out.stderr);
    // anyhow prints the context chain: our message, then the library error,
    // then the underlying io::Error.
    assert!(stderr.contains("loading rules"), "stderr was:\n{stderr}");
    assert!(stderr.contains("Caused by"), "stderr was:\n{stderr}");
}

#[test]
fn validate_rejects_a_rule_file_that_nests_too_deeply() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(
        &dir,
        "rules.json",
        r#"{"rules":[{"name":"deep","severity":"info","applies_to":[],
            "condition":{"op":"all","of":[{"op":"not","of":{"op":"exists","field":"a"}}]}}]}"#,
    );

    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["validate", "--max-depth", "2", "-r"])
        .arg(&rules)
        .assert()
        .failure()
        .stderr(contains("nests deeper"));

    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["validate", "--max-depth", "3", "-r"])
        .arg(&rules)
        .assert()
        .success()
        .stdout(contains("1 rule(s) OK"));
}

#[test]
fn config_file_supplies_defaults_that_flags_can_override() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(
        &dir,
        "rules.json",
        r#"{"rules":[{"name":"deep","severity":"info","applies_to":[],
            "condition":{"op":"all","of":[{"op":"not","of":{"op":"exists","field":"a"}}]}}]}"#,
    );
    let config = write(&dir, "polcheck.toml", "max_depth = 2\n");

    // The config file's limit of 2 rejects the depth-3 rule.
    Command::cargo_bin("polcheck")
        .unwrap()
        .arg("--config")
        .arg(&config)
        .args(["validate", "-r"])
        .arg(&rules)
        .assert()
        .failure()
        .stderr(contains("nests deeper"));

    // An explicit flag wins over the file.
    Command::cargo_bin("polcheck")
        .unwrap()
        .arg("--config")
        .arg(&config)
        .args(["validate", "--max-depth", "5", "-r"])
        .arg(&rules)
        .assert()
        .success();
}

#[test]
fn strict_mode_turns_a_missing_field_into_a_hard_error() {
    let dir = tempfile::tempdir().unwrap();
    let rules = write(
        &dir,
        "rules.json",
        r#"{"rules":[{"name":"owner-is-dave","severity":"error","applies_to":[],
            "condition":{"op":"equals","field":"owner","value":"dave"}}]}"#,
    );
    let resources = write(
        &dir,
        "resources.json",
        r#"[{"id":"a","type":"vm","fields":{}}]"#,
    );

    // Lenient: a missing field is just a violation.
    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .assert()
        .code(1)
        .stdout(contains("owner-is-dave"));

    // Strict: it is a rule-authoring error instead.
    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["scan", "--strict", "-r"])
        .arg(&rules)
        .arg("-R")
        .arg(&resources)
        .assert()
        .failure()
        .stderr(contains("unknown field"));
}

#[test]
fn completions_are_generated_for_a_named_shell() {
    Command::cargo_bin("polcheck")
        .unwrap()
        .args(["completions", "bash"])
        .assert()
        .success()
        .stdout(contains("polcheck"));
}
