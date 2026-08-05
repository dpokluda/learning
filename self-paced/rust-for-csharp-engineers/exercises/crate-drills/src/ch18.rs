//! Crate drill 18 — clap: turning `args[]` into a typed API.
//!
//! clap's derive API is the closest Rust gets to `System.CommandLine`, but the
//! direction of travel is reversed. In .NET you *build* a command tree and then
//! bind it to a handler; in Rust you declare the struct you want and clap
//! derives the parser from it. The struct is the specification.
//!
//! Most of this chapter is attribute work rather than function bodies: the
//! tests describe the command-line surface, and your job is to annotate the
//! types until clap produces it.

use std::path::PathBuf;

use anyhow::Result;
use clap::{Parser, Subcommand, ValueEnum};

/// The root command.
///
/// Required behaviour (see the tests):
/// * program name `polcheck`, version `0.1.0`, about "Evaluate governance rules"
/// * `-v` / `--verbose` **counts** repetitions into `u8` and is `global`
/// * `--config` is global, optional, and falls back to `POLCHECK_CONFIG`
#[derive(Debug, Parser)]
pub struct Cli {
    pub verbose: u8,

    pub config: Option<PathBuf>,

    #[command(subcommand)]
    pub command: Command,
}

/// Subcommands are an enum, so the compiler guarantees you handled every one —
/// which no `switch` over a string ever did.
///
/// Required behaviour:
/// * `eval <RULES> <RESOURCES>...` with at least one resource
/// * `--format` is a `ValueEnum` defaulting to `Format::Text`
/// * `--strict` is a plain boolean flag
/// * `--max-failures` parses a `u32` **constrained to 1 or greater**
/// * `config --show-secrets` *requires* `--i-understand`
#[derive(Debug, Subcommand)]
pub enum Command {
    Eval {
        rules: PathBuf,
        resources: Vec<PathBuf>,
        format: Format,
        strict: bool,
        max_failures: Option<u32>,
    },
    Config {
        show_secrets: bool,
        i_understand: bool,
    },
}

/// A closed set of string values, plus the generated help text listing them.
#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum Format {
    Text,
    Json,
}

/// Map the repeated `-v` count onto a tracing filter directive:
/// 0 → `"warn"`, 1 → `"info"`, 2 → `"debug"`, anything more → `"trace"`.
pub fn verbosity_filter(_verbose: u8) -> &'static str {
    todo!("match on the verbosity count")
}

/// Read the file at `path`, returning its contents.
///
/// * An I/O failure must be wrapped with the context
///   ``reading rule file `{path}` `` so the message says what was attempted.
/// * A file whose contents are blank after trimming must fail with
///   ``rule file `{path}` is empty`` and **no** further cause — reach for
///   `anyhow::bail!`.
pub fn load_rules(_path: &std::path::Path) -> Result<String> {
    todo!("read the file with context, and bail on empty contents")
}

/// Render an error the way a CLI should:
///
/// ```text
/// error: <headline>
///   caused by: <first cause>
///   caused by: <second cause>
/// ```
///
/// `anyhow::Error::chain()` walks the causes; skip the first, which is the
/// headline you already printed.
pub fn render_error(_err: &anyhow::Error) -> String {
    todo!("format the headline then each cause on its own indented line")
}

#[cfg(test)]
mod tests {
    use super::*;
    use clap::CommandFactory;

    /// clap's own test: it validates the derived command tree for conflicts,
    /// duplicate ids and impossible arities. Run it and configuration mistakes
    /// become test failures rather than runtime surprises.
    #[test]
    fn the_derived_command_is_internally_consistent() {
        Cli::command().debug_assert();
    }

    #[test]
    fn parsing_produces_a_typed_value_not_a_string_bag() {
        let cli = Cli::try_parse_from([
            "polcheck", "-vv", "eval", "rules.json", "a.json", "b.json", "--format", "json",
        ])
        .expect("valid invocation");

        assert_eq!(cli.verbose, 2);
        match cli.command {
            Command::Eval {
                rules,
                resources,
                format,
                strict,
                max_failures,
            } => {
                assert_eq!(rules, PathBuf::from("rules.json"));
                assert_eq!(resources.len(), 2);
                assert_eq!(format, Format::Json);
                assert!(!strict);
                assert_eq!(max_failures, None);
            }
            other => panic!("unexpected command: {other:?}"),
        }
    }

    #[test]
    fn a_repeated_flag_counts_rather_than_toggles() {
        let cli = Cli::try_parse_from(["polcheck", "-vvv", "eval", "r.json", "a.json"]).unwrap();
        assert_eq!(cli.verbose, 3);
        assert_eq!(verbosity_filter(cli.verbose), "trace");
        assert_eq!(verbosity_filter(0), "warn");
    }

    #[test]
    fn a_missing_required_positional_is_a_usage_error() {
        let err = Cli::try_parse_from(["polcheck", "eval", "rules.json"]).unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::MissingRequiredArgument);
    }

    #[test]
    fn value_enums_reject_anything_outside_the_set() {
        let err =
            Cli::try_parse_from(["polcheck", "eval", "r.json", "a.json", "--format", "xml"])
                .unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::InvalidValue);
    }

    #[test]
    fn a_range_value_parser_rejects_out_of_range_numbers() {
        let ok = Cli::try_parse_from([
            "polcheck",
            "eval",
            "r.json",
            "a.json",
            "--max-failures",
            "3",
        ])
        .unwrap();
        match ok.command {
            Command::Eval { max_failures, .. } => assert_eq!(max_failures, Some(3)),
            _ => unreachable!(),
        }

        let err = Cli::try_parse_from([
            "polcheck",
            "eval",
            "r.json",
            "a.json",
            "--max-failures",
            "0",
        ])
        .unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::ValueValidation);
    }

    #[test]
    fn requires_expresses_a_dependency_between_flags() {
        let err = Cli::try_parse_from(["polcheck", "config", "--show-secrets"]).unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::MissingRequiredArgument);

        let ok =
            Cli::try_parse_from(["polcheck", "config", "--show-secrets", "--i-understand"])
                .unwrap();
        match ok.command {
            Command::Config { show_secrets, .. } => assert!(show_secrets),
            _ => unreachable!(),
        }
    }

    #[test]
    fn help_and_version_are_generated_not_written() {
        let err = Cli::try_parse_from(["polcheck", "--help"]).unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::DisplayHelp);
        let text = err.to_string();
        assert!(text.contains("Evaluate governance rules"));
        assert!(text.contains("eval"));

        let err = Cli::try_parse_from(["polcheck", "--version"]).unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::DisplayVersion);
        assert!(err.to_string().contains("0.1.0"));
    }

    #[test]
    fn context_builds_a_readable_causal_chain() {
        let missing = std::path::Path::new("definitely-not-here.json");
        let err = load_rules(missing).unwrap_err();

        let rendered = render_error(&err);
        assert!(rendered.starts_with("error: reading rule file `definitely-not-here.json`"));
        assert!(rendered.contains("caused by:"));

        // The original `io::Error` is still in there, typed, and can be
        // recovered — the thing a stringified exception message loses.
        let io = err
            .downcast_ref::<std::io::Error>()
            .expect("io error preserved in the chain");
        assert_eq!(io.kind(), std::io::ErrorKind::NotFound);
    }

    #[test]
    fn bail_produces_an_error_with_no_further_cause() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("empty.json");
        std::fs::write(&path, "   \n").unwrap();

        let err = load_rules(&path).unwrap_err();
        assert!(err.to_string().contains("is empty"));
        assert_eq!(err.chain().count(), 1);
    }
}
