# 19 — anyhow and thiserror

Module 11 taught you the language's error machinery: `Result<T, E>`, the `?` operator, and the discipline of
making failure a value rather than a control-flow surprise. What it left open was the practical question of
what to put in the `E` slot. Writing a bespoke enum with a hand-written `Display` impl and a `From` for every
convertible error type is correct, tedious, and — after the third one — clearly a job for a macro.

Two crates own this space, and the division of labour between them is one of the sharpest conventions in the
Rust ecosystem: **thiserror** for libraries, **anyhow** for applications. Understanding *why* that split
exists is more valuable than memorising either API, because the same reasoning applies to how you design
exception hierarchies in .NET — you have simply never been forced to confront it, since .NET gives you one
mechanism for both cases and lets you find out at runtime whether the caller could handle what you threw.

> **Prerequisite:** [11 — Error handling](11-error-handling.md).

## Why there are two crates

Think about what a caller can *do* with an error.

When you write a **library**, your caller may want to branch on what went wrong. A config parser's caller
might reasonably want to treat "file not found" as "use defaults" while propagating "malformed on line 12".
For that to be possible, the error must be a type with distinguishable cases — an enum the caller can
`match` on, with the variants forming part of your public API and therefore part of your semver contract.

When you write an **application**, there is usually exactly one caller — `main` — and exactly one behaviour:
report the failure with enough context that a human can act on it, then exit non-zero. Enumerating every
distinct failure mode as a variant is pure overhead, because nothing is ever going to match on it.

thiserror serves the first case and anyhow the second. thiserror is a derive macro that writes the
boilerplate for *your* concrete error type; it disappears at compile time and adds nothing to your public
API. anyhow supplies a single opaque type, `anyhow::Error`, that can hold any error and accumulate
human-readable context as it propagates.

The .NET analogy is closer than it first appears. A carefully designed exception hierarchy —
`ArgumentNullException`, `FileNotFoundException`, `HttpRequestException` — exists so callers can write
targeted `catch` blocks. That is thiserror's job. But most application code catches `Exception`, logs it, and
gives up, which is anyhow's job. The difference is that Rust makes you declare which situation you are in,
and the type signature tells your caller what to expect.

## thiserror: typed errors for libraries

`thiserror::Error` derives the `std::error::Error` and `Display` impls from attributes on your enum. Here is
the error type for `polcheck`'s rule engine, which is a library module and therefore wants distinguishable
cases:

```rust
use thiserror::Error;

#[derive(Debug, Error)]
pub enum RuleError {
    /// The `#[error("...")]` string becomes the Display impl.
    #[error("rule file `{path}` could not be read")]
    Unreadable {
        path: String,
        /// `source` is recognised by name and becomes Error::source().
        #[source]
        source: std::io::Error,
    },

    /// `{0}` interpolates a tuple field.
    #[error("unknown operator `{0}`")]
    UnknownOperator(String),

    /// Struct-variant fields interpolate by name.
    #[error("rule `{name}` nests {depth} levels, exceeding the limit of {max}")]
    TooDeep { name: String, depth: usize, max: usize },

    /// `#[from]` generates a From impl *and* marks the field as the source.
    #[error("rule file is not valid JSON")]
    Json(#[from] serde_json::Error),
}

fn parse_operator(op: &str) -> Result<&'static str, RuleError> {
    match op {
        "equals" => Ok("=="),
        "notEquals" => Ok("!="),
        other => Err(RuleError::UnknownOperator(other.to_string())),
    }
}

fn main() {
    let err = parse_operator("startsWith").unwrap_err();
    assert_eq!(err.to_string(), "unknown operator `startsWith`");

    let err = RuleError::TooDeep { name: "no-public-ip".into(), depth: 12, max: 8 };
    assert_eq!(
        err.to_string(),
        "rule `no-public-ip` nests 12 levels, exceeding the limit of 8"
    );
}
```

Four attributes do all the work, and they are worth knowing precisely.

`#[error("...")]` is the `Display` impl, written as a format string with access to the variant's fields.
Positional fields interpolate as `{0}`, `{1}`; named fields as `{name}`. Note that the message is
lowercase and does not end in a period — that is the API guideline, because errors get embedded in larger
sentences like `failed to load config: rule file is not valid JSON`.

`#[source]` marks the underlying cause, which `Error::source()` returns. This builds the error **chain** —
the direct equivalent of `InnerException`. A field literally named `source` is picked up automatically
without the attribute.

`#[from]` does two jobs: it generates `impl From<TheFieldType> for YourError`, and it implies `#[source]`.
That `From` impl is what makes `?` work, so a function returning `Result<T, RuleError>` can use `?` on
anything returning `serde_json::Error` and get automatic conversion. This is the single highest-value
attribute in the crate.

`#[error(transparent)]` forwards both `Display` and `source` straight through to the inner error, adding no
message of its own. Use it for a variant that is a pure pass-through wrapper.

### Error chains

Because `source()` links errors together, you can walk the chain — and this is where the `InnerException`
comparison becomes concrete:

```rust
use std::error::Error as StdError;
use thiserror::Error;

#[derive(Debug, Error)]
#[error("could not load rule set `{name}`")]
struct LoadError {
    name: String,
    #[source]
    source: ParseError,
}

#[derive(Debug, Error)]
#[error("bad syntax on line {line}")]
struct ParseError {
    line: usize,
}

fn main() {
    let err = LoadError {
        name: "prod".into(),
        source: ParseError { line: 12 },
    };

    // Display shows only the outermost message — the whole point of a chain.
    assert_eq!(err.to_string(), "could not load rule set `prod`");

    // Walk it the way a reporter would.
    let mut messages = vec![err.to_string()];
    let mut current: Option<&dyn StdError> = err.source();
    while let Some(e) = current {
        messages.push(e.to_string());
        current = e.source();
    }

    assert_eq!(
        messages,
        vec!["could not load rule set `prod`", "bad syntax on line 12"]
    );
}
```

Each layer's message describes *its own* failure, and the chain assembles the full story. This is why the
guideline says not to include the inner error's text in your own message: doing so produces the duplicated
"error: X: X: X" output you have seen from badly-behaved tools.

### Getting the granularity right

The temptation with thiserror is to add a variant for every conceivable failure. Resist it. Every variant is
public API — adding one is a minor version bump, and removing or renaming one is a breaking change, because
callers may be matching on it.

The right question is always: *would a caller plausibly behave differently for this case?* If two failures
would be handled identically, they should be one variant, possibly with a field distinguishing them for
display purposes. Rust's `#[non_exhaustive]` attribute is the escape hatch for future growth — it forces
callers to include a wildcard arm, so adding variants later is not breaking:

```rust
use thiserror::Error;

#[derive(Debug, Error)]
#[non_exhaustive]
pub enum StoreError {
    #[error("resource `{0}` was not found")]
    NotFound(String),
    #[error("access to `{0}` was denied")]
    Denied(String),
}

fn describe(e: &StoreError) -> &'static str {
    match e {
        StoreError::NotFound(_) => "retry with a different id",
        StoreError::Denied(_) => "check your credentials",
        // Required because of #[non_exhaustive], even inside the defining crate's
        // downstream users. Adding a variant later will not break them.
        _ => "unexpected failure",
    }
}

fn main() {
    assert_eq!(describe(&StoreError::NotFound("vm-1".into())), "retry with a different id");
}
```

## anyhow: contextual errors for applications

anyhow goes the other way. `anyhow::Error` is a single type that wraps any `E: std::error::Error + Send +
Sync + 'static`, stores it in one pointer-width word, and preserves the full chain. `anyhow::Result<T>` is
just `Result<T, anyhow::Error>`.

The payoff is that `?` works on *everything* without a single `From` impl, because anyhow has a blanket
conversion:

```rust
use anyhow::Result;

fn parse_port(s: &str) -> Result<u16> {
    let n: u16 = s.parse()?;          // ParseIntError -> anyhow::Error
    Ok(n)
}

fn read_config(text: &str) -> Result<serde_json::Value> {
    let v: serde_json::Value = serde_json::from_str(text)?;   // serde_json::Error too
    Ok(v)
}

fn main() {
    assert_eq!(parse_port("8080").unwrap(), 8080);
    assert!(parse_port("not-a-port").is_err());
    assert!(read_config(r#"{"a":1}"#).is_ok());
    assert!(read_config("{oops").is_err());
}
```

Two functions, two completely unrelated error types, zero conversion boilerplate. That convenience is the
whole pitch — and it is also exactly why libraries should not do this, because the caller now has no idea
what can go wrong.

### Context is the real feature

The convenience is nice; **context** is the reason to use anyhow. A bare `std::io::Error` says
`The system cannot find the file specified.` and nothing else — not which file, not why you wanted it. The
`Context` trait attaches that information as you propagate:

```rust
use anyhow::{Context, Result};

fn load_rules(path: &str) -> Result<String> {
    std::fs::read_to_string(path)
        .with_context(|| format!("failed to read rule file `{path}`"))
}

fn start(path: &str) -> Result<()> {
    let _text = load_rules(path).context("policy engine could not start")?;
    Ok(())
}

fn main() {
    let err = start("does-not-exist.toml").unwrap_err();

    // The outermost context is what Display shows.
    assert_eq!(err.to_string(), "policy engine could not start");

    // The chain holds the whole story, outermost first.
    let chain: Vec<String> = err.chain().map(|e| e.to_string()).collect();
    assert_eq!(chain.len(), 3);
    assert_eq!(chain[0], "policy engine could not start");
    assert!(chain[1].contains("does-not-exist.toml"));
    // chain[2] is the underlying OS error.
}
```

`context` takes a value eagerly; `with_context` takes a closure evaluated only on the error path, which
matters when building the message allocates. Use `with_context` whenever the message is formatted, `context`
for a static string.

The habit to build is attaching context at every layer where you know something the layer below does not.
The result, when printed with `{:#}` or by returning it from `main`, is a report that reads like a stack of
increasingly specific explanations:

```text
Error: policy engine could not start

Caused by:
    0: failed to read rule file `does-not-exist.toml`
    1: The system cannot find the file specified. (os error 2)
```

That is dramatically more useful than a .NET stack trace for this class of problem, because each line was
written by a human who knew what that layer was trying to accomplish. A stack trace tells you *where* you
were; a context chain tells you *what you were trying to do*.

### Creating errors ad hoc

anyhow provides three shorthands for errors that do not wrap anything. `anyhow!` builds an error from a
format string, `bail!` is `return Err(anyhow!(...))`, and `ensure!` is a checked assertion that returns an
error instead of panicking:

```rust
use anyhow::{anyhow, bail, ensure, Result};

fn check_depth(name: &str, depth: usize) -> Result<()> {
    ensure!(depth <= 8, "rule `{name}` nests {depth} levels, limit is 8");
    Ok(())
}

fn pick_backend(kind: &str) -> Result<&'static str> {
    match kind {
        "json" => Ok("json"),
        "toml" => Ok("toml"),
        "" => bail!("no backend specified"),
        other => Err(anyhow!("unsupported backend `{other}`")),
    }
}

fn main() {
    assert!(check_depth("ok-rule", 3).is_ok());
    assert_eq!(
        check_depth("deep", 12).unwrap_err().to_string(),
        "rule `deep` nests 12 levels, limit is 8"
    );
    assert_eq!(pick_backend("json").unwrap(), "json");
    assert_eq!(pick_backend("xml").unwrap_err().to_string(), "unsupported backend `xml`");
    assert_eq!(pick_backend("").unwrap_err().to_string(), "no backend specified");
}
```

`ensure!` deserves a moment. It looks like `Debug.Assert` but it is not: assertions abort, `ensure!` returns
a recoverable error. Reach for it wherever you would have thrown `ArgumentException` — it is the
precondition-checking idiom that stays inside the `Result` world.

### Recovering a typed error

Occasionally application code does need to branch on a specific failure — retrying on a timeout, say. anyhow
supports this through **downcasting**, which is the direct analogue of `catch (HttpRequestException ex)`:

```rust
use anyhow::{Context, Result};
use thiserror::Error;

#[derive(Debug, Error)]
enum FetchError {
    #[error("request timed out after {0}ms")]
    Timeout(u64),
    #[error("server returned {0}")]
    Status(u16),
}

fn fetch(url: &str) -> Result<String> {
    if url.contains("slow") {
        return Err(FetchError::Timeout(5000)).context("fetching policy definitions");
    }
    Ok("{}".to_string())
}

fn main() {
    let err = fetch("https://slow.example.com").unwrap_err();

    // Context is preserved...
    assert_eq!(err.to_string(), "fetching policy definitions");

    // ...and the typed error is still recoverable from underneath it.
    match err.downcast_ref::<FetchError>() {
        Some(FetchError::Timeout(ms)) => assert_eq!(*ms, 5000),
        other => panic!("expected a timeout, got {other:?}"),
    }

    // `is` checks membership anywhere in the chain.
    assert!(err.is::<FetchError>());
    assert!(!err.is::<std::io::Error>());
}
```

Note that adding context did not destroy the typed error — it is still there, retrievable by type. This is
the pattern that makes "anyhow in the application, thiserror in the library" work: the library defines
precise types, the application wraps them in context for reporting, and on the rare occasion it needs to
branch, it downcasts back to the precise type.

Do not overuse it. If you find yourself downcasting routinely, that is a signal the function should have
returned a typed error in the first place.

### `main` and backtraces

The final convenience: `main` can return `anyhow::Result<()>`, and on `Err` the runtime prints the full
chain and exits with status 1.

```rust,ignore
use anyhow::{Context, Result};

fn main() -> Result<()> {
    let text = std::fs::read_to_string("rules.toml")
        .context("failed to read rules.toml")?;
    println!("{} bytes", text.len());
    Ok(())
}
```

This works because `anyhow::Error` implements `Debug` to print the chain — and it is `Debug`, not `Display`,
that `Termination` uses. That is why you get the nicely formatted "Caused by:" block rather than a single
line.

Backtraces are captured automatically when `RUST_BACKTRACE=1` is set, using the std `Backtrace` support. The
cost is zero when the variable is unset, so there is no reason to avoid shipping with it available — the same
bargain as `DOTNET_gcServer`-style environment switches, and far cheaper than .NET's always-on stack traces.

## Choosing, in practice

The rule is easy to state and worth internalising: **a crate that others depend on returns typed errors
built with thiserror; a binary returns `anyhow::Result`.** In a workspace with a `polcheck-core` library
crate and a `polcheck` binary crate, the library defines `RuleError`, and the binary wraps everything in
context and lets `main` print it.

The dependency direction reinforces this. anyhow can absorb any thiserror type automatically. The reverse is
awkward — you would need a variant holding an `anyhow::Error`, which is legal (`#[error(transparent)] Other(#[from] anyhow::Error)`)
but throws away the type information that was the point of the exercise.

There is one grey area worth naming honestly. A large binary with substantial internal structure sometimes
benefits from typed errors in its core modules even though nothing outside will ever see them, purely for
testability — asserting `matches!(err, RuleError::TooDeep { .. })` is far better than asserting on a message
string. Start with anyhow, and promote a module to thiserror when its tests start matching on text.

Here is the whole comparison in one place:

| | thiserror | anyhow | .NET analogue |
|---|---|---|---|
| Use in | libraries | binaries | — |
| Error type | your own enum/struct | opaque `anyhow::Error` | custom exception vs `Exception` |
| Caller can match | yes, exhaustively | only by downcast | `catch (SpecificEx)` vs `catch (Exception)` |
| Adds to public API | yes — semver relevant | no | yes vs no |
| Context attachment | manual, via variants | `.context(...)` | message + `InnerException` |
| Cost of a new failure mode | new variant, maybe breaking | none | new exception type |
| Runtime size | as declared | one word | — |

## Before you move on

The split is the thing to remember: thiserror generates the `Display` and `Error` impls for a concrete error
type you own, and belongs in libraries where callers may need to distinguish failures; anyhow provides one
opaque error type that absorbs anything via `?` and accumulates human-readable context, and belongs in
binaries where the only consumer is a human reading stderr.

From thiserror, the four attributes carry everything: `#[error("...")]` for the message, `#[source]` for the
cause chain, `#[from]` to generate the `From` impl that makes `?` work, and `#[error(transparent)]` for
pass-through variants. Keep variants few and semantically meaningful, remembering that each one is public
API, and use `#[non_exhaustive]` to leave room to grow.

From anyhow, `context` and `with_context` are the features that matter — not the convenience of skipping
`From` impls. A chain of contexts explaining what each layer was *trying to do* beats a stack trace showing
where it was. `anyhow!`, `bail!`, and `ensure!` cover ad-hoc errors, `downcast_ref` recovers a typed error
when you genuinely need to branch, and returning `anyhow::Result<()>` from `main` prints the whole chain.

If you can explain why a library returning `anyhow::Result` is doing its callers a disservice, and why
adding `.context(...)` does not prevent a later `downcast_ref` from finding the original typed error, you
understand the division of labour.

Next: [20 — serde: serialization](20-serde.md).

### Sources

- `thiserror`. <https://docs.rs/thiserror/2.0/thiserror/> — the derive attributes and their exact semantics.
- `anyhow`. <https://docs.rs/anyhow/1.0/anyhow/> — `Error`, `Context`, `anyhow!`, `bail!`, `ensure!`, and downcasting.
- `std::error::Error`. <https://doc.rust-lang.org/std/error/trait.Error.html> — the `source()` chain both crates build on.
- *Rust API Guidelines*, "Necessities". <https://rust-lang.github.io/api-guidelines/interoperability.html#error-types-are-meaningful-and-well-behaved-c-good-err> — why error types should be meaningful, `Send + Sync + 'static`, and lowercase without trailing punctuation.
- *The Rust Reference*, "The `non_exhaustive` attribute". <https://doc.rust-lang.org/reference/attributes/type_system.html#the-non_exhaustive-attribute> — forward-compatible enums.
