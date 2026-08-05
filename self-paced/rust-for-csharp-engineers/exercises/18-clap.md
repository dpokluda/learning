# Exercises 18 — clap and anyhow

> **Covers:** [18 — clap and anyhow](../18-clap.md). **Code:** `crate-drills/src/ch18.rs`. **Answers:** [answers/18-clap.md](answers/18-clap.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** clap's derive API and `System.CommandLine` solve the same problem from opposite directions. Describe both, and say what Rust's approach buys.

**A2.** What does `ArgAction::Count` do that a `bool` cannot, and what is the idiomatic thing to do with the result?

**A3.** Explain the difference between a `value_parser` and a `ValueEnum`, and when each is the right tool.

**A4.** What is `.context()` actually doing in `anyhow`, and how does the result differ from wrapping an exception in another exception?

**A5.** An `anyhow::Error` has erased the concrete type. In what sense is that not true, and why does it matter?

**A6.** Give the rule for when to use `anyhow::bail!` rather than propagating an existing error with `?`.

## Part B — Exercise

Open `crate-drills/src/ch18.rs`. Unlike most drills, the bulk of the work here is
attributes rather than function bodies: the tests describe a command-line surface
and your job is to annotate `Cli` and `Command` until clap produces exactly it.

The surface you are building has a repeated `-v` flag that counts rather than
toggles, a global `--config` with an environment-variable fallback, a subcommand
taking one positional rule file and one-or-more positional resources, a
`ValueEnum` output format with a default, a numeric option constrained to values
of one or more, and a dangerous flag that requires an explicit acknowledgement
flag alongside it. Every one of those is a single attribute once you know which,
and the tests tell you what each must do.

Start with `the_derived_command_is_internally_consistent`, which calls clap's own
`debug_assert`. It validates the command tree for duplicate ids, impossible
arities and unsatisfiable requirements, and it is worth having in every real CLI
project for the same reason.

The last two drills switch to `anyhow`: wrap an I/O failure with context so the
message names the file, `bail!` on empty input so the chain has exactly one link,
and render an error as a headline plus indented causes. The test then downcasts
back to `std::io::Error` and checks its `kind`, which is the proof that erasing
the type into `anyhow::Error` did not actually lose it.

Run it with `cargo test ch18` from the `exercises/crate-drills` directory.

### Starter stub

```rust,ignore
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
```

The test module that follows this in the file is the specification — read it before you write anything.
