//! The `polcheck` binary: argument parsing, configuration, tracing, and the
//! translation of library errors into a human-facing report.

use std::path::PathBuf;

use anyhow::{Context, Result};
use clap::{CommandFactory, Parser, Subcommand};
use tracing::{info, instrument};

use polcheck::config::{Overrides, Settings};
use polcheck::report::{self, Format};
use polcheck::rules::Severity;
use polcheck::source;

/// Evaluate governance rules against JSON resource records.
#[derive(Debug, Parser)]
#[command(name = "polcheck", version, about, long_about = None)]
struct Cli {
    /// Path to a TOML configuration file.
    #[arg(long, global = true, value_name = "FILE")]
    config: Option<PathBuf>,

    /// Increase logging verbosity (-v, -vv, -vvv).
    #[arg(short, long, global = true, action = clap::ArgAction::Count)]
    verbose: u8,

    #[command(subcommand)]
    command: Command,
}

#[derive(Debug, Subcommand)]
enum Command {
    /// Evaluate rules against resources.
    Scan(ScanArgs),
    /// Check a rule file for structural problems without evaluating it.
    Validate(ValidateArgs),
    /// Print a shell completion script to stdout.
    Completions {
        /// The shell to generate for.
        #[arg(value_enum)]
        shell: clap_complete::Shell,
    },
}

#[derive(Debug, clap::Args)]
struct ScanArgs {
    /// Rule file to evaluate.
    #[arg(short, long, value_name = "FILE")]
    rules: PathBuf,

    /// Read resources from this JSON file instead of the network.
    #[arg(short = 'R', long, value_name = "FILE", conflicts_with = "endpoint")]
    resources: Option<PathBuf>,

    /// Fetch resources from this URL.
    #[arg(long, value_name = "URL", env = "POLCHECK_ENDPOINT")]
    endpoint: Option<String>,

    /// Output format.
    #[arg(long, value_enum, default_value_t = Format::Text)]
    format: Format,

    /// Treat a reference to an absent field as an error.
    #[arg(long)]
    strict: bool,

    /// Exit non-zero when a finding at this severity or above is present.
    #[arg(long, value_enum)]
    fail_on: Option<SeverityArg>,
}

#[derive(Debug, clap::Args)]
struct ValidateArgs {
    /// Rule file to check.
    #[arg(short, long, value_name = "FILE")]
    rules: PathBuf,

    /// Maximum permitted nesting depth.
    #[arg(long)]
    max_depth: Option<usize>,
}

/// A clap-facing mirror of `Severity`, so the library type needs no clap dependency.
#[derive(Debug, Clone, Copy, clap::ValueEnum)]
enum SeverityArg {
    Info,
    Warning,
    Error,
}

impl From<SeverityArg> for Severity {
    fn from(v: SeverityArg) -> Self {
        match v {
            SeverityArg::Info => Severity::Info,
            SeverityArg::Warning => Severity::Warning,
            SeverityArg::Error => Severity::Error,
        }
    }
}

fn init_tracing(verbosity: u8) {
    use tracing_subscriber::{EnvFilter, fmt, prelude::*};

    let fallback = match verbosity {
        0 => "warn",
        1 => "info",
        2 => "debug",
        _ => "trace",
    };
    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new(fallback));

    tracing_subscriber::registry()
        .with(filter)
        .with(fmt::layer().with_writer(std::io::stderr))
        .init();
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    init_tracing(cli.verbose);

    let code = run(cli).await?;
    std::process::exit(code);
}

#[instrument(skip_all)]
async fn run(cli: Cli) -> Result<i32> {
    let settings = Settings::load(cli.config.as_deref()).context("failed to load configuration")?;

    match cli.command {
        Command::Completions { shell } => {
            let mut cmd = Cli::command();
            let name = cmd.get_name().to_string();
            clap_complete::aot::generate(shell, &mut cmd, name, &mut std::io::stdout());
            Ok(0)
        }

        Command::Validate(args) => {
            let settings = settings.apply(Overrides {
                max_depth: args.max_depth,
                ..Overrides::default()
            });

            let set = source::load_rules(&args.rules)
                .await
                .with_context(|| format!("loading rules from {}", args.rules.display()))?;

            set.validate(settings.max_depth)
                .context("rule file failed validation")?;

            println!("{} rule(s) OK", set.rules.len());
            Ok(0)
        }

        Command::Scan(args) => {
            let settings = settings.apply(Overrides {
                endpoint: args.endpoint.clone(),
                strict: args.strict.then_some(true),
                fail_on: args.fail_on.map(Severity::from),
                ..Overrides::default()
            });

            let set = source::load_rules(&args.rules)
                .await
                .with_context(|| format!("loading rules from {}", args.rules.display()))?;
            set.validate(settings.max_depth)
                .context("rule file failed validation")?;

            let resources = match (&args.resources, &settings.endpoint) {
                (Some(path), _) => source::load_resources(path)
                    .await
                    .with_context(|| format!("loading resources from {}", path.display()))?,
                (None, Some(url)) => {
                    let client = reqwest::Client::builder()
                        .user_agent(concat!("polcheck/", env!("CARGO_PKG_VERSION")))
                        .timeout(std::time::Duration::from_secs(30))
                        .build()
                        .context("building HTTP client")?;
                    source::fetch_resources(&client, url)
                        .await
                        .with_context(|| format!("fetching resources from {url}"))?
                }
                (None, None) => anyhow::bail!(
                    "no resource source: pass --resources <FILE> or set --endpoint / POLCHECK_ENDPOINT"
                ),
            };

            info!(
                rules = set.rules.len(),
                resources = resources.len(),
                "evaluating"
            );

            let findings = set
                .evaluate(&resources, settings.strict)
                .context("evaluation failed")?;

            let rendered =
                report::render(&findings, args.format).context("rendering the report")?;
            print!("{rendered}");

            Ok(report::exit_code(&findings, settings.fail_on))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cli_definition_is_internally_consistent() {
        Cli::command().debug_assert();
    }

    #[test]
    fn scan_rejects_both_resource_sources_at_once() {
        let err = Cli::try_parse_from([
            "polcheck",
            "scan",
            "-r",
            "rules.json",
            "-R",
            "res.json",
            "--endpoint",
            "https://x",
        ])
        .unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::ArgumentConflict);
    }

    #[test]
    fn verbosity_counts_repeated_flags() {
        let cli = Cli::try_parse_from(["polcheck", "-vv", "validate", "-r", "rules.json"]).unwrap();
        assert_eq!(cli.verbose, 2);
    }

    #[test]
    fn format_defaults_to_text() {
        let cli =
            Cli::try_parse_from(["polcheck", "scan", "-r", "r.json", "-R", "x.json"]).unwrap();
        match cli.command {
            Command::Scan(a) => assert_eq!(a.format, Format::Text),
            _ => panic!("expected scan"),
        }
    }
}
