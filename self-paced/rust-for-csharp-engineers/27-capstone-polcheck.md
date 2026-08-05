# 27 — Capstone: building polcheck

Every module so far has shown you one piece in isolation. This chapter assembles them into a program you
could actually ship: a CLI that loads governance rules from JSON, evaluates them against resource records
read from disk or fetched over HTTP, and reports violations in human or machine-readable form with a
meaningful exit code.

The complete source lives in [`code/polcheck/`](code/polcheck/) next to this book. It compiles on stable
Rust 1.95, passes `cargo clippy --all-targets -- -D warnings`, is `cargo fmt`-clean, and its 29 tests pass.
Every output quoted below is real output from running it. I would encourage you to read this chapter with the
project open in your editor.

> **Prerequisite:** Part 2 in its entirety — this chapter assumes clap, anyhow, thiserror, serde, tokio,
> reqwest, tracing, and figment are all familiar.

## What we are building

`polcheck` answers one question: does this set of cloud resources satisfy this set of governance rules? A
rule names a condition over a resource's fields, a severity, and the resource types it applies to. Running
the tool produces findings and an exit code that CI can act on.

```text
$ polcheck scan -r examples/rules.json -R examples/resources.json
error   /subscriptions/s1/sa-logs     prod-needs-cost-centre
error   /subscriptions/s1/vm-test-02  require-owner
warning /subscriptions/s1/vm-test-02  env-must-be-known

3 finding(s)
$ echo $?
1
```

That is the whole product. What makes it worth studying is the structure underneath.

## The shape of the project

The first decision is one you make in .NET too, and it matters more here. `polcheck` is a **library crate
plus a binary crate in the same package**:

```text
polcheck/
├── Cargo.toml
├── examples/
│   ├── rules.json
│   └── resources.json
├── src/
│   ├── lib.rs        ← the public library: re-exports and a doc test
│   ├── error.rs      ← the typed error enum (thiserror)
│   ├── rules.rs      ← the domain model and evaluator
│   ├── config.rs     ← layered settings (figment)
│   ├── report.rs     ← rendering and exit codes
│   ├── source.rs     ← loading from disk and HTTP
│   └── main.rs       ← the binary: clap, tracing, anyhow
└── tests/
    └── cli.rs        ← integration tests that run the real binary
```

Cargo infers this from convention: `src/lib.rs` becomes the library target and `src/main.rs` becomes a binary
target that depends on it. The binary refers to the library by its package name — `use polcheck::rules::...`
— exactly as an external consumer would, which means the binary is continuously proving the library's public
API is usable. In .NET you would get the same effect with a class library project and a console project that
references it, and the discipline is the same: **domain logic in the library, process concerns in the
binary.**

That split drives the error strategy the book has been building towards. The library uses `thiserror` to
expose a typed enum that callers can match on; the binary uses `anyhow` to add human context and print a
chain. This is the "thiserror for libraries, anyhow for binaries" rule made concrete, and you can see the
seam clearly: `error.rs` never mentions `anyhow`, and `main.rs` never constructs a `polcheck::Error`.

## The domain model

The heart of the program is one enum, and it is the best possible advertisement for algebraic data types:

```rust,ignore
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "op", rename_all = "camelCase")]
pub enum Condition {
    Exists { field: String },
    Equals { field: String, value: String },
    OneOf  { field: String, values: Vec<String> },
    All    { of: Vec<Condition> },
    Any    { of: Vec<Condition> },
    Not    { of: Box<Condition> },
}
```

Six lines that would be an abstract base class and six subclasses in C#, plus a visitor if you wanted
exhaustive dispatch, plus a `JsonConverter` with a type discriminator to make it serialize. Here the
`#[serde(tag = "op")]` attribute *is* the discriminator configuration, and the JSON it produces is exactly
what you would design by hand:

```json
{
  "op": "any",
  "of": [
    { "op": "not", "of": { "op": "equals", "field": "env", "value": "prod" } },
    { "op": "exists", "field": "cost_centre" }
  ]
}
```

Note the `Box<Condition>` in the `Not` variant. `All` and `Any` hold a `Vec`, which is already a heap
pointer, so recursion through them is fine; `Not` holds a single `Condition` directly, and without the `Box`
the type would be infinitely sized. This is module 12's lesson arriving in real code rather than a toy
example: `Box` is how you give a recursive type a finite size, and it is the one place a C# developer — for
whom every class is already a reference — has to think about something new.

The evaluator is a `match`, and the compiler guarantees it is complete:

```rust,ignore
pub fn eval(&self, resource: &Resource, rule: &str, strict: bool) -> Result<bool> {
    match self {
        Condition::Exists { field } => Ok(resource
            .fields
            .get(field)
            .is_some_and(|v| !v.is_empty())),

        Condition::Equals { field, value } => match resource.fields.get(field) {
            Some(actual) => Ok(actual == value),
            None if strict => Err(Error::UnknownField {
                rule: rule.to_string(),
                field: field.clone(),
            }),
            None => Ok(false),
        },
        // ... OneOf, All, Any, Not
    }
}
```

Add a seventh variant tomorrow and every `match` in the codebase fails to compile until you handle it. That
is the property that makes this design worth the unfamiliarity: in C#, adding a subclass compiles fine and
fails at runtime in whichever switch you forgot.

The `None if strict` guard is worth a second look because it encodes a genuine product decision in the type
system. A rule that references `owner` on a resource with no `owner` field is ambiguous: is the resource
non-compliant, or did the rule author make a typo? `polcheck` lets the operator choose, and the two paths
have *different types* — one returns `Ok(false)` and one returns `Err(UnknownField)` — so the distinction
cannot be accidentally collapsed.

One implementation detail deserves comment because it will bite you. The `All` and `Any` arms use explicit
loops rather than `.all()` and `.any()`:

```rust,ignore
Condition::All { of } => {
    for c in of {
        if !c.eval(resource, rule, strict)? {
            return Ok(false);
        }
    }
    Ok(true)
}
```

The reason is that `?` inside a closure returns from *the closure*, not from the enclosing function, so
`of.iter().all(|c| c.eval(...)?)` does not typecheck. This is the most common friction point when combining
iterators with fallible operations. The alternatives are collecting into
`Result<Vec<bool>, _>` — which loses short-circuiting — or using `itertools`' `fold_ok`. For a short-circuiting
boolean fold, the plain loop is clearest, and choosing clarity over cleverness here is the right call.

## Layered configuration

`config.rs` implements the precedence chain from module 24 — defaults, then a TOML file, then `POLCHECK_*`
environment variables, then command-line flags:

```rust,ignore
pub fn load(config_path: Option<&Path>) -> Result<Self, Box<figment::Error>> {
    let mut fig = Figment::from(Serialized::defaults(Settings::default()));
    if let Some(p) = config_path {
        fig = fig.merge(Toml::file(p));
    } else {
        fig = fig.merge(Toml::file(PathBuf::from("polcheck.toml")));
    }
    fig.merge(Env::prefixed("POLCHECK_").split("__"))
        .extract()
        .map_err(Box::new)
}
```

The `Box<figment::Error>` is not decoration. Clippy's `result_large_err` lint flagged the unboxed version
while I was writing this chapter, because `figment::Error` is at least 208 bytes, and an `Err` variant that
large inflates every `Result<Settings, _>` — and every stack frame that holds one — even on the success path.
`Result<T, E>` is sized to fit its largest variant, so a fat error type is a cost you pay always and benefit
from never. Boxing moves it to the heap. This is a Rust-specific concern with no .NET analogue, because a
C# exception is always a reference and costs one pointer regardless of size.

The flag-override layer is the piece worth copying into your own tools:

```rust,ignore
#[derive(Debug, Default, Clone)]
pub struct Overrides {
    pub endpoint: Option<String>,
    pub max_depth: Option<usize>,
    pub strict: Option<bool>,
    pub fail_on: Option<Severity>,
}

impl Settings {
    #[must_use]
    pub fn apply(mut self, o: Overrides) -> Self {
        if let Some(v) = o.max_depth { self.max_depth = v; }
        // ...
        self
    }
}
```

Every field is `Option<T>` so that "the user did not pass this flag" is representable. A non-optional
`max_depth: usize` in the clap struct would carry clap's default value and silently overwrite whatever the
config file said. There is a test asserting exactly this — `flags_beat_everything_but_only_when_passed`
checks that an empty `Overrides` leaves the settings byte-for-byte identical — because it is the kind of bug
that is invisible until a user complains their config file is ignored.

And `deny_unknown_fields` on `Settings` means a typo is a startup error rather than a silent default. The
test proves it:

```rust,ignore
#[test]
fn unknown_keys_are_rejected_rather_than_ignored() {
    // polcheck.toml contains `max_dpeth = 3`
    let err = Settings::load(Some(&path)).unwrap_err();
    assert!(err.to_string().contains("max_dpeth"));
}
```

## The command line

`main.rs` defines the interface with clap's derive API. The structure is a global-options struct with a
subcommand enum, which is the shape almost every real CLI converges on:

```rust,ignore
#[derive(Debug, Parser)]
#[command(name = "polcheck", version, about, long_about = None)]
struct Cli {
    #[arg(long, global = true, value_name = "FILE")]
    config: Option<PathBuf>,

    #[arg(short, long, global = true, action = clap::ArgAction::Count)]
    verbose: u8,

    #[command(subcommand)]
    command: Command,
}

#[derive(Debug, Subcommand)]
enum Command {
    Scan(ScanArgs),
    Validate(ValidateArgs),
    Completions { #[arg(value_enum)] shell: clap_complete::Shell },
}
```

`global = true` makes `--config` and `-v` valid before or after the subcommand, which is what users expect
and what `System.CommandLine` calls a global option. `ArgAction::Count` turns repeated `-v` into a `u8`, the
idiom every Unix tool uses.

Two details in `ScanArgs` are the ones I would point out in a code review:

```rust,ignore
#[arg(short = 'R', long, value_name = "FILE", conflicts_with = "endpoint")]
resources: Option<PathBuf>,

#[arg(long, value_name = "URL", env = "POLCHECK_ENDPOINT")]
endpoint: Option<String>,
```

`conflicts_with` makes "read from a file *or* fetch from a URL, never both" a parse error rather than a
runtime check, and there is a test asserting the error kind is `ArgumentConflict`. And `env` gives the flag
an environment-variable fallback for free, which is the single most useful clap attribute for
containerised tools.

There is also a test you should copy into every clap project you write:

```rust,ignore
#[test]
fn cli_definition_is_internally_consistent() {
    Cli::command().debug_assert();
}
```

`debug_assert()` validates the whole command tree — duplicate argument ids, a `conflicts_with` naming a
non-existent argument, an invalid default — and turns what would be a runtime panic on first use into a test
failure. There is no `System.CommandLine` equivalent.

Notice too that `SeverityArg` in `main.rs` mirrors the library's `Severity` and converts via `From`. That
duplication is deliberate: it keeps `clap` out of the library's dependency list, so someone consuming
`polcheck` as a library does not drag in an argument parser. Making that boundary explicit is the Rust
equivalent of not referencing `Microsoft.Extensions.Hosting` from your domain assembly.

## Errors end to end

This is where the two-crate error strategy pays off, and the clearest way to see it is to break something:

```text
$ polcheck scan -r nope.json -R examples/resources.json
Error: loading rules from nope.json

Caused by:
    0: could not read rule file `nope.json`
    1: The system cannot find the file specified. (os error 2)
```

Three layers, each added by a different part of the program. The bottom is `std::io::Error` from the
operating system. The middle is the library's typed error, which named the file and the operation:

```rust,ignore
#[error("could not read rule file `{path}`")]
ReadRules {
    path: PathBuf,
    #[source]
    source: std::io::Error,
},
```

The top is the binary's `anyhow` context:

```rust,ignore
let set = source::load_rules(&args.rules)
    .await
    .with_context(|| format!("loading rules from {}", args.rules.display()))?;
```

Compare that with `InnerException` chains. The information content is the same, but the mechanics differ in
one important way: an `anyhow` context is added at the call site by the code that *knows why it was calling*,
whereas a .NET exception carries whatever the thrower decided plus a stack trace. The stack trace tells you
*where*; the context chain tells you *what the program was trying to do*, which is usually the more useful
question when the answer is going to a user rather than a debugger.

Note the closure in `with_context`. Using `.context(format!(...))` would format the string on every call
including the successful ones; `with_context` defers it until the error path. On a hot loop that difference
is real.

Returning `anyhow::Result<()>` from `main` gets you the formatted chain and a non-zero exit for free. But
`polcheck` needs a *specific* exit code, so `main` delegates to `run` and calls `std::process::exit`
explicitly:

```rust,ignore
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    init_tracing(cli.verbose);
    let code = run(cli).await?;
    std::process::exit(code);
}
```

The exit code carries meaning — 0 for clean, 1 for findings at or above the threshold — which is what lets
`polcheck` be a CI gate. The threshold itself is configurable, and because `Severity` derives `Ord` with its
variants in ascending order, the check is a one-liner: `findings.iter().any(|f| f.severity >= threshold)`.
Deriving `Ord` on an enum and getting declaration-order comparison for free is a small pleasure C# denies you
unless you cast to `int`.

One caveat worth knowing: `std::process::exit` terminates immediately without running destructors. Here
nothing needs flushing, but if you had a buffered writer or a tracing subscriber with a background worker,
you would flush before exiting.

## Async, but only where it earns its place

`polcheck` is a `#[tokio::main]` program, and it is worth being honest about why. Reading two files
sequentially gains nothing from async — this is not a program with thousands of concurrent connections. It is
async because `reqwest` is async, and once one dependency in your call graph is async the colour propagates
all the way to `main`. That is the "function colouring" problem from module 16 showing up in a real program.

The .NET contrast is instructive. There, `HttpClient` also pushes you towards async, but you can escape with
`.GetAwaiter().GetResult()` at the cost of a possible deadlock, and the thread pool is always there. In Rust
there is no ambient runtime, so the decision is explicit and visible in the code. I think the Rust version is
better — the cost is stated rather than hidden — but it is a real cost.

The runtime configuration is a place where being deliberate pays:

```toml
tokio = { version = "1", features = ["macros", "rt-multi-thread", "fs", "time", "signal"] }
```

Not `features = ["full"]`. Each feature is there because something uses it, which keeps compile times down
and makes the dependency's surface area legible. If `polcheck` only ever did one request, `rt` instead of
`rt-multi-thread` with `#[tokio::main(flavor = "current_thread")]` would spawn no worker threads at all —
worth considering for a CLI, where startup time is user-visible.

## Testing at two levels

The test suite has 29 tests in three groups, and the split maps onto the .NET distinction between unit tests
and tests that exercise a deployed artifact.

**Unit tests** live in `#[cfg(test)] mod tests` blocks inside each source file — the arrangement that has no
C# equivalent, since xUnit tests live in a separate assembly and can only see `public` and `InternalsVisibleTo`
members. Because a Rust child module can see its parent's private items, these tests reach internals without
any visibility hack, and `#[cfg(test)]` means they are compiled out of the shipped binary entirely.

**Integration tests** live in `tests/cli.rs`, which Cargo compiles as a *separate crate* that can only see
`polcheck`'s public API. Here they use `assert_cmd` to run the actual binary:

```rust,ignore
#[test]
fn fail_on_threshold_changes_the_exit_code() {
    // Default threshold is `error`, and the only finding is `info`.
    Command::cargo_bin("polcheck").unwrap()
        .args(["scan", "-r"]).arg(&rules).arg("-R").arg(&resources)
        .assert().code(0);

    // Lower the threshold and the same run fails.
    Command::cargo_bin("polcheck").unwrap()
        .args(["scan", "--fail-on", "info", "-r"]).arg(&rules).arg("-R").arg(&resources)
        .assert().code(1);
}
```

`Command::cargo_bin` finds the compiled binary for you, so the test exercises argument parsing, configuration
layering, evaluation, rendering, and the exit code in one shot. Every test writes its fixtures into a
`tempfile::tempdir()` whose `Drop` impl deletes the directory — the `IDisposable` pattern, but unforgettable.

There is one test I want to single out, because it tests a *guarantee* rather than a behaviour:

```rust,ignore
#[test]
fn a_missing_rule_file_produces_a_readable_error_chain() {
    // ...
    assert!(stderr.contains("loading rules"));
    assert!(stderr.contains("Caused by"));
}
```

Error messages are part of your interface. If someone refactors and drops a `.context(...)`, this test
notices. Asserting on diagnostics is under-practised in both ecosystems, and it costs four lines.

**Doc tests** are the third group. The example in `lib.rs` compiles and runs as part of `cargo test`, which
means the documentation on the front page of the crate cannot rot. This is the feature I miss most when I go
back to C#: an XML `<example>` block is a comment, and a Rust doc example is a test.

## Packaging and shipping

A release build turns on the optimisations that matter for a distributed binary:

```toml
[profile.release]
lto = true            # link-time optimisation across crate boundaries
codegen-units = 1     # slower build, better codegen
strip = true          # drop debug symbols
```

`cargo build --release` with those settings produces a **6.1 MB** self-contained executable on Windows. That
number deserves context for a .NET developer. It sounds large next to a 60 KB `.dll`, but the honest
comparison is to `dotnet publish -r win-x64 --self-contained`, which lands in the 60–70 MB range, or
NativeAOT at roughly 10–15 MB. Rust's binary needs no runtime installed, starts in single-digit
milliseconds, and links the C runtime dynamically by default.

Distribution has three routes. `cargo install --path .` builds and drops the binary in `~/.cargo/bin`,
which is the Rust developer's equivalent of `dotnet tool install --global` and works only for people who have
Rust. `cargo install polcheck` does the same from crates.io once published — note that this *builds from
source* on the user's machine, unlike NuGet which ships prebuilt assemblies. For everyone else you ship
binaries, which means cross-compilation:

```bash
rustup target add x86_64-unknown-linux-musl
cargo build --release --target x86_64-unknown-linux-musl
```

The `musl` target is worth knowing: it links libc statically, producing a binary that runs on any Linux
distribution regardless of glibc version — the thing that makes `FROM scratch` containers possible. Getting
there in .NET requires NativeAOT and still carries caveats. Cross-compiling to a *different* platform needs a
linker for that target, which is what the `cross` tool automates by running the build in a container.

The one genuine ergonomic loss versus .NET: there is no `dotnet publish`-style single command that produces
artifacts for every platform. Most projects use a GitHub Actions matrix, and `cargo-dist` generates one for
you.

## What to take from this

Step back and look at what the program actually is. Roughly 900 lines including tests, thirteen
dependencies, no runtime to install, no reflection, no configuration-by-convention magic, and a compiler that
refuses to build it if a `match` is incomplete, a borrow outlives its owner, or an error goes unhandled.

The parts that would have been hardest in C# — a serializable recursive discriminated union, exhaustive
dispatch over it, a layered configuration system that distinguishes "unset" from "default", and an error
chain that survives refactoring — are the parts Rust made routine. The parts that were harder than C# are
also real: `Box` around a recursive variant, `?` not working inside closures, async colouring the whole call
graph because one library needed it, and a clippy lint about the size of an error type.

That trade is the honest summary of the language. You do more work up front to describe what you mean, and in
exchange a category of runtime failure stops existing.

## Before you move on

The structural decision that shapes everything else is library-plus-binary in one package: domain logic and
typed `thiserror` errors in `lib.rs` and its modules, process concerns and `anyhow` context in `main.rs`,
with the binary consuming the library through its public API exactly as an external user would. Keep clap out
of the library — mirror its enums and convert with `From` — so a library consumer does not inherit an
argument parser.

The `Condition` enum is the argument for algebraic data types in one screen: six variants, serde-tagged JSON
for free, `Box` on the self-recursive variant to make it sized, and exhaustive `match` that turns tomorrow's
seventh variant into a compile error rather than a production surprise. Remember why the `All`/`Any` arms are
loops: `?` returns from the closure, not the function.

Configuration layers defaults under file under environment under flags, with every override field
`Option<T>` so an unpassed flag cannot clobber the file, `deny_unknown_fields` turning typos into startup
errors, and a boxed error because a 208-byte `Err` variant inflates every `Result` on the success path too.
Errors read as a chain because each layer adds what it knows, and `with_context` defers formatting to the
failure path.

Test at both levels: `#[cfg(test)]` modules that can see private items and vanish from the release build, and
a `tests/` crate that runs the real binary through `assert_cmd` and asserts on exit codes and stderr, plus doc
tests that keep the documentation honest. Ship with `lto`, `codegen-units = 1`, and `strip`, and reach for the
`musl` target when you want a binary that runs anywhere.

If you can explain why `Not` needs a `Box` when `Any` does not, and why the `Overrides` struct uses
`Option<T>` for fields that already have defaults, you have the two ideas that make this program work.

Next: [28 — Idioms, patterns, and anti-patterns](28-idioms-and-antipatterns.md).

### Sources

- The Cargo Book, "Package Layout". <https://doc.rust-lang.org/cargo/guide/project-layout.html> — the `src/lib.rs` + `src/main.rs` + `tests/` convention.
- The Cargo Book, "Profiles". <https://doc.rust-lang.org/cargo/reference/profiles.html> — `lto`, `codegen-units`, `strip`.
- The Cargo Book, `cargo install`. <https://doc.rust-lang.org/cargo/commands/cargo-install.html> — building from source into `~/.cargo/bin`.
- The rustup Book, "Cross-compilation". <https://rust-lang.github.io/rustup/cross-compilation.html> — `rustup target add` and linker requirements.
- `clap::Command::debug_assert`. <https://docs.rs/clap/4/clap/struct.Command.html#method.debug_assert> — validating the command tree in a test.
- `assert_cmd`. <https://docs.rs/assert_cmd/2/assert_cmd/> — running the built binary from an integration test.
- Clippy, `result_large_err`. <https://rust-lang.github.io/rust-clippy/master/index.html#result_large_err> — why a large `Err` variant is a cost on the success path.
- The Rust Programming Language, ch. 11, "Writing Automated Tests". <https://doc.rust-lang.org/book/ch11-00-testing.html> — unit, integration, and doc tests.
