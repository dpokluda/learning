# 18 — clap: command-line interfaces

Part 1 taught you the language. Part 2 is about the ecosystem — the crates that a real Rust program is built
from — and it starts with the one you reach for first, because almost every Rust binary begins by working out
what the user asked it to do.

In .NET you have had three eras of this: hand-rolled `args[]` parsing, third-party libraries like
`CommandLineParser`, and more recently `System.CommandLine`. Rust's answer is **clap**, and it has won so
decisively that it is effectively the standard library for argument parsing — `cargo` itself uses it. What
makes clap pleasant is its derive API, which lets you describe your CLI as a struct and get parsing,
validation, help text, error messages, and shell completions generated from it.

This is where `polcheck` starts becoming a real program.

> **Prerequisite:** [09 — The standard traits](09-standard-traits.md), for derive macros and `From`.

## The shape of the thing

Here is a complete, working CLI in under twenty lines. Read it before the explanation.

```rust
use clap::Parser;

/// Validate cloud resources against governance policy.
#[derive(Parser, Debug)]
#[command(name = "polcheck", version, about)]
struct Cli {
    /// Path to the resource inventory file
    input: String,

    /// Emit machine-readable JSON instead of a human summary
    #[arg(long)]
    json: bool,

    /// Fail the run if more than this many violations are found
    #[arg(long, default_value_t = 0)]
    max_violations: usize,
}

fn main() {
    // In a real binary this is `Cli::parse()`, which reads std::env::args_os().
    // `parse_from` takes an explicit argv, which is what makes CLIs testable.
    let cli = Cli::parse_from(["polcheck", "inventory.json", "--json"]);

    assert_eq!(cli.input, "inventory.json");
    assert!(cli.json);
    assert_eq!(cli.max_violations, 0);
}
```

Everything about the interface is inferred from the struct. A field without an `#[arg]` attribute becomes a
**positional** argument. A `bool` field becomes a **flag** that is `true` when present. Field names become
long options with underscores converted to hyphens, so `max_violations` is `--max-violations`. The doc
comment above each field becomes its help text, and the doc comment on the struct becomes the program
description — which is the detail that makes clap genuinely delightful, because it means your help output
cannot drift away from your documentation.

Compare that to `System.CommandLine`, where you construct `Option<T>` and `Argument<T>` objects, add them to
a `RootCommand`, and then pull values back out of a `ParseResult` or bind them to a handler. clap's derive
approach front-loads all of that into a type declaration, and — importantly — the result is a plain struct
you can pass around, pattern-match on, and construct in tests.

The `#[command(...)]` attribute configures the command itself. `version` wires up `--version` from your
`Cargo.toml` version, and `about` pulls the description from the struct's doc comment. You get `--help` for
free, always.

### The two APIs

clap has a **derive API** (what you just saw) and a **builder API**, where you construct `Command` and `Arg`
values programmatically. They are the same engine — the derive macro expands into builder calls — and you
can mix them. Use derive for essentially everything; reach for the builder only when your arguments are not
known at compile time, such as a plugin system that contributes flags dynamically. The derive API requires
the `derive` feature, which is not on by default:

```toml
[dependencies]
clap = { version = "4.6", features = ["derive"] }
```

## Arguments in depth

Real CLIs need more than positionals and flags. The `#[arg(...)]` attribute is where the vocabulary lives,
and it is worth walking through the pieces you will actually use.

**Short and long forms.** `short` and `long` without a value derive the name from the field; give them a
value to override.

**Optional versus required.** This is where Rust's type system does the work that attributes do in other
libraries. An `Option<T>` field is optional. A bare `T` field is required. A `Vec<T>` field accepts the
option multiple times and collects every occurrence. There is no `[Required]` attribute because the type
already said it.

```rust
use clap::Parser;

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    /// Inventory files to scan (at least one required)
    #[arg(required = true)]
    inputs: Vec<String>,

    /// Optional path to write the report to; defaults to stdout
    #[arg(short, long)]
    output: Option<String>,

    /// Subscription to scope the scan to; repeatable
    #[arg(short = 's', long = "subscription")]
    subscriptions: Vec<String>,

    /// Severity threshold
    #[arg(long, default_value = "warning")]
    min_severity: String,
}

fn main() {
    let cli = Cli::parse_from([
        "polcheck", "a.json", "b.json",
        "-s", "sub-1", "-s", "sub-2",
        "--output", "report.json",
    ]);

    assert_eq!(cli.inputs, vec!["a.json", "b.json"]);
    assert_eq!(cli.subscriptions, vec!["sub-1", "sub-2"]);
    assert_eq!(cli.output.as_deref(), Some("report.json"));
    assert_eq!(cli.min_severity, "warning");     // the default applied
}
```

**Defaults.** There are two spellings and the difference trips people up. `default_value = "..."` takes a
string that is then parsed like any user input. `default_value_t = expr` takes a value of the field's actual
type, which is type-checked at compile time and requires the type to implement `Display`. Prefer
`default_value_t` — it catches typos in your defaults at build time.

**Counting.** `ArgAction::Count` turns repeated flags into a number, which is how `-vvv` becomes verbosity
level 3.

**Environment fallback.** `env = "VAR"` makes clap consult an environment variable when the flag is absent,
giving you the precedence chain that twelve-factor apps expect: explicit flag beats environment variable
beats default. Getting this right by hand is fiddly; here it is one attribute. It needs the `env` feature.

```rust
use clap::{ArgAction, Parser};

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    /// Increase logging verbosity (-v, -vv, -vvv)
    #[arg(short, long, action = ArgAction::Count)]
    verbose: u8,

    /// Suppress all non-error output
    #[arg(short, long)]
    quiet: bool,

    /// Number of parallel workers
    #[arg(long, default_value_t = 4)]
    workers: usize,
}

fn main() {
    let cli = Cli::parse_from(["polcheck", "-vvv", "--workers", "16"]);
    assert_eq!(cli.verbose, 3);
    assert_eq!(cli.workers, 16);
    assert!(!cli.quiet);

    let level = match cli.verbose {
        0 => "warn",
        1 => "info",
        2 => "debug",
        _ => "trace",
    };
    assert_eq!(level, "trace");
}
```

### Typed values and enums

A field's type is also its parser. Declare `workers: usize` and clap will reject `--workers abc` with a
clear message before your code ever runs — the same guarantee `System.CommandLine`'s typed options give you,
but derived from the field rather than declared separately.

For a fixed set of choices, `ValueEnum` is the tool. It generates the parser, the list of possible values in
the help output, and the completion candidates:

```rust
use clap::{Parser, ValueEnum};

#[derive(ValueEnum, Clone, Copy, Debug, PartialEq)]
#[value(rename_all = "kebab-case")]
enum OutputFormat {
    /// Human-readable summary
    Text,
    /// Newline-delimited JSON
    JsonLines,
    /// A single JSON document
    Json,
}

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    #[arg(long, value_enum, default_value_t = OutputFormat::Text)]
    format: OutputFormat,
}

fn main() {
    let cli = Cli::parse_from(["polcheck", "--format", "json-lines"]);
    assert_eq!(cli.format, OutputFormat::JsonLines);

    let cli = Cli::parse_from(["polcheck"]);
    assert_eq!(cli.format, OutputFormat::Text);

    // An invalid value is an error, not a panic — and the message lists the
    // valid choices automatically.
    assert!(Cli::try_parse_from(["polcheck", "--format", "xml"]).is_err());
}
```

Note `try_parse_from`, the fallible sibling of `parse_from`. The `parse` family prints a formatted error and
calls `std::process::exit` on failure, which is what you want in `main` and emphatically not what you want in
a test.

When the built-in parsing is not enough, `value_parser` accepts any function from `&str` to
`Result<T, E>`, which is where domain validation belongs:

```rust
use clap::Parser;

/// A validated Azure subscription id.
#[derive(Debug, Clone, PartialEq)]
struct SubscriptionId(String);

fn parse_subscription(s: &str) -> Result<SubscriptionId, String> {
    if s.len() == 36 && s.chars().all(|c| c.is_ascii_hexdigit() || c == '-') {
        Ok(SubscriptionId(s.to_string()))
    } else {
        Err(format!("`{s}` is not a valid subscription id (expected a GUID)"))
    }
}

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    #[arg(long, value_parser = parse_subscription)]
    subscription: SubscriptionId,
}

fn main() {
    let good = "00000000-0000-0000-0000-000000000001";
    let cli = Cli::parse_from(["polcheck", "--subscription", good]);
    assert_eq!(cli.subscription, SubscriptionId(good.to_string()));

    assert!(Cli::try_parse_from(["polcheck", "--subscription", "nope"]).is_err());
}
```

This is the newtype pattern from module 09 doing exactly what it is for: the moment parsing succeeds, the
rest of the program has a `SubscriptionId` that is guaranteed well-formed, and no downstream function needs
to re-validate. clap also ships range parsers for numeric bounds — `value_parser = clap::value_parser!(u16).range(1..=65535)`
is the idiomatic way to constrain a port.

## Subcommands

Most non-trivial tools are really several tools sharing a name — `git commit`, `dotnet build`,
`cargo test`. clap models this with an enum, and the modelling is unusually satisfying because Rust's enums
are algebraic: each variant carries exactly the arguments that variant needs, and the type system makes it
impossible to read a flag that belongs to a different subcommand.

```rust
use clap::{Parser, Subcommand};

#[derive(Parser, Debug)]
#[command(name = "polcheck", version, about = "Governance policy checker")]
struct Cli {
    /// Increase logging verbosity
    #[arg(short, long, global = true)]
    verbose: bool,

    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand, Debug)]
enum Command {
    /// Evaluate resources against a rule set
    Scan {
        /// Inventory file to read
        input: String,
        /// Rule set to apply
        #[arg(short, long, default_value = "rules.toml")]
        rules: String,
    },
    /// Check a rule file for syntax errors
    Validate {
        /// Rule file to check
        rules: String,
    },
    /// Print the effective configuration and exit
    Config,
}

fn main() {
    let cli = Cli::parse_from(["polcheck", "-v", "scan", "inv.json", "--rules", "prod.toml"]);
    assert!(cli.verbose);

    match &cli.command {
        Command::Scan { input, rules } => {
            assert_eq!(input, "inv.json");
            assert_eq!(rules, "prod.toml");
        }
        other => panic!("unexpected: {other:?}"),
    }

    // A subcommand with no arguments is a unit variant.
    let cli = Cli::parse_from(["polcheck", "config"]);
    assert!(matches!(cli.command, Command::Config));
}
```

Two things deserve attention. `global = true` makes `--verbose` accepted before *or* after the subcommand,
which is what users expect from top-level flags and is otherwise annoying to arrange. And the `match` at the
bottom is exhaustive — add a variant and the compiler tells you where to handle it, which is a real
maintenance benefit over a string-keyed dispatch table.

Nesting works by making a variant hold another `Subcommand` enum, giving you `polcheck rules list` style
hierarchies. Making the field `Option<Command>` makes the subcommand optional, so the bare program name is
valid and you can print help or run a default action.

### Sharing arguments between subcommands

When several subcommands need the same flags, factor them into a struct deriving `clap::Args` and
`#[command(flatten)]` it in. The flattened fields appear as if declared inline, but you get one definition
and one type to pass around:

```rust
use clap::{Args, Parser, Subcommand};

/// Options shared by every subcommand that talks to Azure.
#[derive(Args, Debug, Clone)]
struct ConnectionOpts {
    /// Subscription to operate on
    #[arg(long, env = "POLCHECK_SUBSCRIPTION")]
    subscription: Option<String>,

    /// Request timeout in seconds
    #[arg(long, default_value_t = 30)]
    timeout: u64,
}

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand, Debug)]
enum Command {
    Scan {
        input: String,
        #[command(flatten)]
        conn: ConnectionOpts,
    },
    Export {
        #[command(flatten)]
        conn: ConnectionOpts,
    },
}

fn main() {
    let cli = Cli::parse_from(["polcheck", "scan", "inv.json", "--timeout", "60"]);
    match cli.command {
        Command::Scan { input, conn } => {
            assert_eq!(input, "inv.json");
            assert_eq!(conn.timeout, 60);
            assert_eq!(conn.subscription, None);
        }
        _ => panic!("wrong variant"),
    }
}
```

This is the analogue of a shared options class in `System.CommandLine`, except that the grouping survives
into your program: `conn` is a real value you can hand to a client constructor, rather than a set of
individually-plucked parse results.

## Relationships between arguments

Some constraints are about how arguments interact, and expressing them declaratively means clap produces the
error message instead of you.

The main tools are `conflicts_with` for mutually exclusive options, `requires` for one flag implying
another, and `ArgGroup` for "exactly one of these" or "at least one of these". A group is declared on the
command and its members named:

```rust
use clap::Parser;

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
#[command(group(
    clap::ArgGroup::new("source")
        .required(true)
        .args(["input", "stdin"])
))]
struct Cli {
    /// Read the inventory from a file
    #[arg(long)]
    input: Option<String>,

    /// Read the inventory from standard input
    #[arg(long)]
    stdin: bool,

    /// Rewrite the input file in place; only meaningful with --input
    #[arg(long, requires = "input")]
    in_place: bool,

    /// Never write anything
    #[arg(long, conflicts_with = "in_place")]
    dry_run: bool,
}

fn main() {
    // The group is required: exactly one source must be given.
    assert!(Cli::try_parse_from(["polcheck"]).is_err());
    assert!(Cli::try_parse_from(["polcheck", "--input", "a.json", "--stdin"]).is_err());
    assert!(Cli::try_parse_from(["polcheck", "--stdin"]).is_ok());

    // conflicts_with is enforced as you would expect.
    assert!(Cli::try_parse_from([
        "polcheck", "--input", "a.json", "--in-place", "--dry-run"
    ]).is_err());

    // ... but see the surprise below: this one is accepted, not rejected.
    assert!(Cli::try_parse_from(["polcheck", "--stdin", "--in-place"]).is_ok());
}
```

That last assertion is not a typo, and it is worth dwelling on because it cost me a failing test to
discover. On its own, `requires = "input"` behaves exactly as you would expect: `--in-place` without
`--input` is rejected. But once `input` is a member of a **required** `ArgGroup`, clap treats the
requirement as satisfied when the *group* is satisfied — so `--stdin --in-place` sails through even though
`--input` was never supplied. The declarative constraints compose in ways that are not obvious from reading
any single attribute.

The lesson generalises. Declarative constraints buy you good error messages and self-documenting help, and
for simple cases — a pair of mutually exclusive flags, one option implying another — they are exactly right.
But they interact, and the interactions are not type-checked. When a rule matters, express it in ordinary
Rust after parsing, where you can read it, test it, and control the message:

```rust
use clap::Parser;

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    #[arg(long)]
    input: Option<String>,
    #[arg(long)]
    stdin: bool,
    #[arg(long)]
    in_place: bool,
}

/// The validated shape, where illegal states are unrepresentable.
#[derive(Debug, PartialEq)]
enum Source {
    File { path: String, in_place: bool },
    Stdin,
}

impl Cli {
    fn source(&self) -> Result<Source, String> {
        match (&self.input, self.stdin) {
            (Some(_), true) => Err("--input and --stdin are mutually exclusive".into()),
            (None, false) => Err("one of --input or --stdin is required".into()),
            (None, true) if self.in_place => {
                Err("--in-place needs a file; it cannot be used with --stdin".into())
            }
            (None, true) => Ok(Source::Stdin),
            (Some(path), false) => Ok(Source::File {
                path: path.clone(),
                in_place: self.in_place,
            }),
        }
    }
}

fn main() {
    let cli = Cli::parse_from(["polcheck", "--stdin", "--in-place"]);
    assert_eq!(cli.source(), Err("--in-place needs a file; it cannot be used with --stdin".into()));

    let cli = Cli::parse_from(["polcheck", "--input", "a.json", "--in-place"]);
    assert_eq!(cli.source(), Ok(Source::File { path: "a.json".into(), in_place: true }));

    let cli = Cli::parse_from(["polcheck", "--stdin"]);
    assert_eq!(cli.source(), Ok(Source::Stdin));
}
```

This is more code, and it is better code. The `match` is exhaustive, so the compiler enumerates the cases
for you; the messages are yours; and — the real prize — it returns a `Source` enum in which the invalid
combinations no longer exist. Downstream code matches on two clean variants instead of re-deriving intent
from three loosely-related fields. That is the same "parse, don't validate" instinct behind the
`SubscriptionId` newtype earlier, applied one level up.

## Help, versions, and completions

Help output is generated, and generated well: `-h` gives a terse summary, `--help` the long form with full
doc comments, and both are colourised and wrapped to the terminal width. `--version` comes from
`Cargo.toml`. The practical consequence is that the *only* way to keep help text current is to write good
doc comments, which is a pleasant incentive.

Shell completion scripts are generated by the companion crate `clap_complete`. The usual pattern is a hidden
subcommand that writes a script to stdout, which users pipe into their shell configuration:

```rust
use clap::{CommandFactory, Parser, Subcommand};
use clap_complete::aot::{generate, Shell};

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand, Debug)]
enum Command {
    Scan { input: String },
    /// Generate a shell completion script
    Completions {
        /// Shell to generate for
        shell: Shell,
    },
}

fn main() {
    let cli = Cli::parse_from(["polcheck", "completions", "bash"]);

    if let Command::Completions { shell } = cli.command {
        let mut cmd = Cli::command();          // requires CommandFactory in scope
        let name = cmd.get_name().to_string();
        let mut out: Vec<u8> = Vec::new();     // a real binary writes to io::stdout()
        generate(shell, &mut cmd, name, &mut out);

        let script = String::from_utf8(out).unwrap();
        assert!(script.contains("polcheck"));
    }
}
```

Two easily-missed details, both of which cost me a compile error to discover. `Cli::command()` comes from the
`CommandFactory` trait, which must be imported explicitly — deriving `Parser` implements it but does not
bring it into scope. And in `clap_complete` 4.6, `generate` lives under the `aot` module (ahead-of-time
generation), not at the crate root as older examples show. `Shell` derives `ValueEnum`, so it works directly
as an argument type.

There is nothing comparable in the .NET CLI world short of hand-writing completion scripts, and it is a
genuinely nice thing to be able to offer users for four lines of code.

## Testing a CLI

Because `Cli` is an ordinary struct and `try_parse_from` takes an explicit argv, argument parsing is
unit-testable without spawning a process — which is worth doing, because CLI surfaces are exactly the kind of
thing that breaks silently.

There is also a free sanity check worth wiring into every project. `Command::debug_assert()` validates your
entire CLI definition — duplicate names, references to non-existent arguments in `requires`, conflicting
short flags — and panics with a precise message. These are mistakes that would otherwise surface as a
runtime panic on a user's machine:

```rust
use clap::{CommandFactory, Parser};

#[derive(Parser, Debug)]
#[command(name = "polcheck")]
struct Cli {
    #[arg(long)]
    input: Option<String>,
    #[arg(long, requires = "input")]
    in_place: bool,
}

fn main() {
    // In a real project this lives in a #[test] fn.
    Cli::command().debug_assert();

    assert!(Cli::try_parse_from(["polcheck", "--in-place"]).is_err());
}
```

For end-to-end coverage — checking exit codes and actual stdout — `assert_cmd` builds and runs your binary,
and `predicates` expresses assertions about its output. That combination is the closest thing Rust has to
testing a console app with a captured `TextWriter`, and it belongs in `tests/cli.rs` as an integration test.

```rust,ignore
// tests/cli.rs
use assert_cmd::Command;
use predicates::prelude::*;

#[test]
fn rejects_missing_input() {
    Command::cargo_bin("polcheck")
        .unwrap()
        .arg("scan")
        .assert()
        .failure()
        .stderr(predicate::str::contains("required"));
}
```

## Wiring it into `main`

One last piece. `Cli::parse()` handles its own errors by printing and exiting, so `main` can stay clean.
The idiomatic `main` for a clap program returns a `Result` and delegates immediately:

```rust,ignore
use clap::Parser;

#[derive(Parser)]
#[command(name = "polcheck", version, about)]
struct Cli {
    input: String,
}

fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    run(cli)
}

fn run(cli: Cli) -> anyhow::Result<()> {
    println!("scanning {}", cli.input);
    Ok(())
}
```

Splitting `run` out of `main` is a small habit with a large payoff: `run` is testable, and `main` stays a
three-line adapter between the process world and your program. The `anyhow::Result` in the signature is the
subject of the next module.

## Before you move on

clap's derive API turns a struct into a complete command-line interface, and the mapping is worth having in
your head: doc comments become help text, field types become parsers and required-ness (`Option<T>` optional,
`T` required, `Vec<T>` repeatable), bare fields become positionals, `bool` fields become flags, and
underscores become hyphens. `#[command(...)]` configures the command, `#[arg(...)]` configures an argument,
`#[command(subcommand)]` points at an enum whose variants are subcommands, and `#[command(flatten)]` shares a
group of arguments across several of them.

The pieces you will use constantly are `default_value_t` for type-checked defaults, `env` for environment
fallback, `ArgAction::Count` for `-vvv`, `ValueEnum` for fixed choice sets, and `value_parser` for domain
validation that produces a newtype the rest of your program can trust. Remember that `parse`/`parse_from`
exit the process on error while `try_parse_from` returns a `Result` — the latter is what makes CLIs
unit-testable — and that `Command::debug_assert()` catches definition mistakes at test time rather than in
production.

Against `System.CommandLine`, the difference in feel comes down to clap deriving everything from types you
were going to declare anyway, and giving you back a plain struct rather than a parse result to query. Three
gotchas will bite you: `CommandFactory` needs an explicit import before `Cli::command()` resolves,
`clap_complete::aot::generate` moved under the `aot` module, and declarative constraints interact — a
`requires` pointing at a member of a required `ArgGroup` is satisfied by the group, not by the argument. When
a rule genuinely matters, parse the flags into an enum that makes the invalid states unrepresentable and
check it in ordinary Rust.

If you can explain why `Option<String>` and `String` produce different required-ness without any attribute,
and when you would reach for `try_parse_from` over `parse_from`, you are ready to deal with what happens when
the work those arguments describe goes wrong.

Next: [19 — anyhow and thiserror](19-anyhow-and-thiserror.md).

### Sources

- `clap` API documentation. <https://docs.rs/clap/4.6/clap/> — the reference for `Parser`, `Args`, `Subcommand`, `ValueEnum`, and every `#[arg]` attribute.
- *clap Derive Reference*. <https://docs.rs/clap/4.6/clap/_derive/index.html> — the authoritative list of derive attributes and their magic-method expansions.
- *clap Derive Tutorial*. <https://docs.rs/clap/4.6/clap/_derive/_tutorial/index.html> — a worked progression from a single flag to nested subcommands.
- `clap_complete`. <https://docs.rs/clap_complete/4.6/clap_complete/> — shell completion generation; note the `aot` module.
- `assert_cmd`. <https://docs.rs/assert_cmd/2.2/assert_cmd/> — running your binary in integration tests.
- Microsoft Learn, "System.CommandLine overview". <https://learn.microsoft.com/dotnet/standard/commandline/> — the .NET comparison point.
