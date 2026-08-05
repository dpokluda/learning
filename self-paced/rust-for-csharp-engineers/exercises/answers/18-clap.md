# Answers 18 — clap and anyhow

> Exercises: [18-clap.md](../18-clap.md)

## Part A

**A1. clap's derive API and `System.CommandLine` solve the same problem from opposite directions. Describe both, and say what Rust's approach buys.**

`System.CommandLine` is builder-first: you construct `Option<T>` and `Argument<T>` objects, add them to `Command`s, compose a tree, and then bind the parsed result to a handler — the model is a runtime object graph, and the mapping from it to your parameters is resolved by convention or by hand. clap's derive is type-first: you declare the struct or enum you want to end up with, annotate it, and the proc macro generates the parser that produces exactly that type. What Rust's approach buys is that the parsed representation is a normal, exhaustively-matchable value with no `Option`s you forgot to check and no string keys anywhere. Subcommands become an enum, so adding one is a compile error at every `match` that handles them. Help text is generated from the same declaration, so it cannot drift. And clap validates the derived command tree itself — `Cli::command().debug_assert()` in a test catches duplicate ids, impossible arities and conflicting requirements before a user does.

**A2. What does `ArgAction::Count` do that a `bool` cannot, and what is the idiomatic thing to do with the result?**

It records how many times the flag appeared rather than merely whether it did, giving you `-v`, `-vv`, `-vvv` as a single argument yielding a `u8`. A `bool` collapses all of those to `true`. The idiomatic thing is to map the count onto a logging level — zero meaning warn, one info, two debug, three or more trace — and hand that to your subscriber's filter. It matters because it keeps a very common CLI convention out of your parsing code entirely: by the time your code runs, verbosity is an ordinary integer and everything downstream is plain Rust. Marking it `global = true` additionally lets the flag appear before or after the subcommand, which is what users expect and what a hand-rolled parser almost always gets wrong.

**A3. Explain the difference between a `value_parser` and a `ValueEnum`, and when each is the right tool.**

A `ValueEnum` is for a closed set of names: it derives the string-to-variant mapping, renders the possible values into the help text, and produces a real enum you can match on. Use it whenever the set is fixed at compile time — output formats, log levels, modes. A `value_parser` is for values that need parsing or validating rather than choosing: `clap::value_parser!(u32).range(1..)` accepts any integer but rejects zero, and a custom function can accept anything you can parse from a string. The dividing line is whether the domain is enumerable. Both share the important property that failure happens during parsing, so by the time your code sees the value it is already valid — the same discipline as parsing rather than validating, applied to the command line. In `System.CommandLine` these correspond to `FromAmong` and a custom parser delegate respectively.

**A4. What is `.context()` actually doing in `anyhow`, and how does the result differ from wrapping an exception in another exception?**

`.context("msg")` on a `Result` converts the error into an `anyhow::Error` whose displayed message is `msg` and whose `source` is the original error, preserved intact with its concrete type. Repeated up a call stack it builds a chain that reads as a narrative of what the program was attempting, from the outermost intent down to the mechanical failure — `reading rule file \`x.json\`` then `The system cannot find the file specified`. That is structurally the same as nesting exceptions in `InnerException`, with two practical differences. First, it is nearly free to write, so people actually do it, whereas `throw new Exception("...", ex)` is verbose enough that most .NET code just lets the original bubble. Second, `with_context` takes a closure, so formatting the message costs nothing on the success path — the string is only built when there is actually an error.

**A5. An `anyhow::Error` has erased the concrete type. In what sense is that not true, and why does it matter?**

It is erased from the *signature*, not from the value. `anyhow::Error` stores the original boxed error and `downcast_ref::<T>()` gets it back, so a caller can still react to a specific failure — checking whether the underlying `io::Error` has kind `NotFound`, for instance, to distinguish 'no config file, use defaults' from 'config file exists but is unreadable'. It matters because it is what makes `anyhow` acceptable in application code: you get the ergonomics of a single error type everywhere without permanently losing the information you would need for the rare case where you must branch. The comparison with .NET is close — catching `Exception` and testing `is IOException` — but the Rust version is explicit at the point of use rather than hidden in a catch filter, and the compiler still tells you the function can fail.

**A6. Give the rule for when to use `anyhow::bail!` rather than propagating an existing error with `?`.**

`bail!` creates a brand-new error out of a formatted message and returns it, so it is for failures that *originate here* — a validation that failed, an invariant your code checked, a state the program refuses to proceed from. There is no underlying cause to preserve, and the resulting error has a chain of length one. `?` is for failures that came from somewhere else, and you should nearly always attach `.context()` to it so the chain records why you were making that call. The anti-pattern to avoid is `bail!("failed to read file: {e}")`, which flattens a real cause into a string and throws away the typed error and everything downcasting could have done with it — the exact equivalent of `throw new Exception(ex.Message)` in C#, and just as bad.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` and `cargo clippy -- -D warnings` on the pinned toolchain.

```rust
//! Crate drill 18 — clap: turning `args[]` into a typed API.
//!
//! clap's derive API is the closest Rust gets to `System.CommandLine`, but the
//! direction of travel is reversed. In .NET you *build* a command tree and then
//! bind it to a handler; in Rust you declare the struct you want and clap
//! derives the parser from it. The struct is the specification.

use std::path::PathBuf;

use anyhow::{Context, Result, bail};
use clap::{ArgAction, Parser, Subcommand, ValueEnum};

/// The root command. `#[command(...)]` on the struct configures the program;
/// doc comments become the help text, which is why they are written as prose
/// rather than as terse labels.
#[derive(Debug, Parser)]
#[command(name = "polcheck", version = "0.1.0", about = "Evaluate governance rules")]
pub struct Cli {
    /// Increase logging verbosity; repeat for more detail (-v, -vv, -vvv).
    #[arg(short, long, action = ArgAction::Count, global = true)]
    pub verbose: u8,

    /// Path to the configuration file. Falls back to `POLCHECK_CONFIG`.
    #[arg(long, env = "POLCHECK_CONFIG", global = true)]
    pub config: Option<PathBuf>,

    #[command(subcommand)]
    pub command: Command,
}

/// Subcommands are an enum, and that is the whole trick: the compiler now
/// guarantees you handled every command, which no `switch` over a string ever
/// did.
#[derive(Debug, Subcommand)]
pub enum Command {
    /// Evaluate a rule set against a resource file.
    Eval {
        /// The rule file to evaluate.
        rules: PathBuf,

        /// One or more resource files. At least one is required.
        #[arg(required = true, num_args = 1..)]
        resources: Vec<PathBuf>,

        /// Output format.
        #[arg(long, value_enum, default_value_t = Format::Text)]
        format: Format,

        /// Fail the process when any resource is non-compliant.
        #[arg(long)]
        strict: bool,

        /// Stop after this many failures. Must be at least 1.
        #[arg(long, value_parser = clap::value_parser!(u32).range(1..))]
        max_failures: Option<u32>,
    },

    /// Print the effective configuration and exit.
    Config {
        /// Show secrets in plain text. Requires an explicit acknowledgement.
        #[arg(long, requires = "i_understand")]
        show_secrets: bool,

        /// Acknowledge that secrets will be printed.
        #[arg(long = "i-understand", id = "i_understand")]
        i_understand: bool,
    },
}

/// `ValueEnum` gives you a closed set of string values *and* the help text
/// listing them, which is the part hand-rolled parsing always forgets.
#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum Format {
    Text,
    Json,
}

/// Map the repeated `-v` count onto a filter directive. Note that this is
/// ordinary code operating on an ordinary `u8` — once clap has parsed, nothing
/// is stringly typed any more.
pub fn verbosity_filter(verbose: u8) -> &'static str {
    match verbose {
        0 => "warn",
        1 => "info",
        2 => "debug",
        _ => "trace",
    }
}

/// Where clap stops and `anyhow` starts. clap reports *usage* errors; anyhow
/// carries *runtime* failures, and `.context()` is how you leave a trail
/// explaining what the program was attempting. The result reads like a stack of
/// `InnerException` messages, except you wrote each layer deliberately.
pub fn load_rules(path: &std::path::Path) -> Result<String> {
    let text = std::fs::read_to_string(path)
        .with_context(|| format!("reading rule file `{}`", path.display()))?;

    if text.trim().is_empty() {
        bail!("rule file `{}` is empty", path.display());
    }
    Ok(text)
}

/// Format an anyhow error the way a CLI should: the headline, then each cause
/// indented beneath it. This is `iter_chain`, the equivalent of walking
/// `InnerException` until it is null.
pub fn render_error(err: &anyhow::Error) -> String {
    let mut out = format!("error: {err}");
    for cause in err.chain().skip(1) {
        out.push_str(&format!("\n  caused by: {cause}"));
    }
    out
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
```
