# 23 — tracing and logging

A program you cannot observe is a program you cannot operate, and this is an area where the .NET habits
transfer well in spirit and poorly in detail. You know `ILogger<T>`, structured logging with message
templates, log levels, scopes, and — if you have done distributed work — OpenTelemetry spans and
correlation ids. Rust has all of that, but arranged differently, and the arrangement is worth understanding
before you start sprinkling `println!` through your code.

There are two layers. The **log** crate is a minimal logging façade, and **tracing** is a
superset that adds spans and structured fields. The short version of the advice: use `log` if you are
writing a small library and want the lightest possible dependency, and use `tracing` for everything else,
especially anything async — because in an async program the notion of "where am I" that a stack trace
provides has been shredded into a thousand interleaved state machines, and spans are how you get it back.

> **Prerequisite:** [21 — tokio in practice](21-tokio-in-practice.md).

## The façade pattern

Both crates split *emitting* from *handling*, and this is the design decision that explains everything else.

A library calls `tracing::info!(...)`. That macro does not know or care where the data goes. It hands the
event to whatever global **subscriber** the application installed at startup, and if none was installed the
call compiles down to almost nothing. The application — and only the application — chooses the destination,
the format, and the filtering.

The parallel to .NET is exact and worth stating: `ILogger<T>` is the façade, the `ILoggerFactory` and its
registered providers are the implementation, and a library takes `ILogger<T>` without knowing whether the
host writes to console, Serilog, or Application Insights. Rust's version is the same separation implemented
with a global rather than dependency injection, which is a real difference — there is no logger to inject,
you just call the macro.

The practical consequence is a rule: **libraries depend on `tracing` (or `log`) and never on a subscriber;
binaries choose and install exactly one subscriber, once, at startup.** A library that installs a subscriber
is a library that has stolen a decision from its user.

```toml
# In a library
[dependencies]
tracing = "0.1"

# In a binary
[dependencies]
tracing = "0.1"
tracing-subscriber = { version = "0.3", features = ["env-filter"] }
```

That `env-filter` feature is not enabled by default, and leaving it out produces a confusing
"no `EnvFilter` in the root" error. It is the single most common setup mistake with this crate.

## Events

An event is a moment in time with a level, a message, and — the important part — structured fields:

```rust
fn main() {
    let resource_id = "res-1";
    let rule = "require-owner";
    let elapsed_ms = 42;

    // Levels, from most to least severe.
    tracing::error!("scan aborted");
    tracing::warn!("rule file is deprecated");
    tracing::info!("scan complete");
    tracing::debug!("cache miss");
    tracing::trace!("entering evaluate");

    // Structured fields come before the message. Shorthand captures the
    // variable under its own name.
    tracing::info!(resource_id, rule, elapsed_ms, "rule evaluated");

    // Explicit names, and sigils that control how a value is recorded.
    let findings = vec!["a", "b"];
    tracing::info!(
        target: "polcheck::scan",
        count = findings.len(),
        ?findings,                    // ? => record via Debug
        %resource_id,                 // % => record via Display
        "scan finished"
    );
}
```

The two sigils are the piece with no direct .NET equivalent, and they are genuinely useful once learned.
By default a field value must implement `tracing`'s `Value` trait, which covers primitives and strings.
Prefixing with `?` records it via `Debug` and with `%` via `Display`, which is how you log a type that is
neither. In .NET the closest thing is `@` in a Serilog message template asking for destructuring, and the
correspondence is close enough to be a useful mnemonic: `?` is Rust's `@`.

The structured-versus-interpolated distinction matters just as much here as in .NET. Writing
`tracing::info!("evaluated rule {rule} in {elapsed_ms}ms")` produces one opaque string; writing
`tracing::info!(rule, elapsed_ms, "rule evaluated")` produces a message plus two queryable fields. The
second is what makes a log aggregator useful, and it is exactly the reason you were taught to write
`_logger.LogInformation("Evaluated {Rule}", rule)` rather than string interpolation.

`target:` sets the module path the event is attributed to, which defaults to the current module and is what
filtering matches against — the analogue of the category string in `ILogger<T>`.

## Spans: the reason to prefer tracing

An event says something happened. A **span** says something was happening *for a period*, and every event
emitted during that period inherits the span's fields. That inheritance is the whole point.

Consider `polcheck` scanning many resources concurrently. Without spans, a hundred interleaved "rule
evaluated" lines tell you nothing about which resource each belonged to. With a span carrying
`resource_id`, every nested event is automatically tagged:

```rust
use tracing::{info, info_span, instrument};

fn evaluate(resource_id: &str, rules: &[&str]) -> usize {
    // Entering the span attaches its fields to everything inside.
    let span = info_span!("evaluate", resource_id, rule_count = rules.len());
    let _guard = span.enter();

    let mut findings = 0;
    for rule in rules {
        // This event carries resource_id and rule_count without repeating them.
        info!(rule, "checking");
        if *rule == "require-owner" {
            findings += 1;
        }
    }

    info!(findings, "evaluation complete");
    findings
}

/// The attribute does the same thing with less ceremony: it creates a span
/// named after the function, with the arguments as fields.
#[instrument(skip(rules), fields(rule_count = rules.len()))]
fn evaluate_instrumented(resource_id: &str, rules: &[&str]) -> usize {
    info!("starting");
    rules.iter().filter(|r| **r == "require-owner").count()
}

fn main() {
    assert_eq!(evaluate("res-1", &["require-owner", "require-env"]), 1);
    assert_eq!(evaluate_instrumented("res-2", &["require-owner"]), 1);
}
```

`#[instrument]` is the attribute you will use most. It wraps the function in a span named after it, records
every argument as a field, and — critically — handles async correctly. Its options matter: `skip` or
`skip_all` excludes arguments that are large, sensitive, or not `Debug`; `fields(...)` adds computed values;
`err` records the error when the function returns `Err`; and `level` overrides the default.

Two habits to bring from .NET logging hygiene: `skip` anything containing credentials or personal data,
since `#[instrument]` records arguments by default and will happily log a password; and prefer `skip_all`
plus explicit `fields(...)` on functions with many parameters, which is both faster and more deliberate.

### Why this matters for async

Here is the part that justifies the crate. In .NET, `ILogger.BeginScope` returns an `IDisposable` and the
scope flows across `await` via `AsyncLocal`. Rust has no ambient async context, so a span guard held across
an `.await` would be wrong — the task can be suspended and another task resumed on the same thread while
your guard is still "entered", attributing the other task's events to your span.

tracing solves this with `Instrument`, which attaches a span to a *future* so it is entered and exited
exactly when that future is polled:

```rust
use tracing::{info, info_span, Instrument};

async fn scan_one(id: &str) -> usize {
    info!("scanning");                 // tagged with the span attached below
    tokio::time::sleep(std::time::Duration::from_millis(1)).await;
    info!("done");
    id.len()
}

#[tokio::main(flavor = "current_thread")]
async fn main() {
    // Correct: attach the span to the future.
    let a = scan_one("res-1").instrument(info_span!("scan", resource_id = "res-1"));
    let b = scan_one("res-2").instrument(info_span!("scan", resource_id = "res-2"));

    let (x, y) = tokio::join!(a, b);
    assert_eq!((x, y), (5, 5));
}
```

Even though the two futures interleave on one thread, each event lands in the right span. `#[instrument]` on
an `async fn` does this for you automatically, which is why it is the recommended default. The rule to
remember is blunt: **never hold a `span.enter()` guard across an `.await`** — use `#[instrument]` or
`.instrument(...)` instead. Clippy will not catch this for you, and the resulting mis-attributed logs are
maddening to debug.

## Installing a subscriber

The binary picks the destination and the filter. The minimal setup is one line, and the realistic setup is
about five:

```rust,ignore
use tracing_subscriber::{fmt, prelude::*, EnvFilter};

fn init_tracing(verbosity: u8) {
    // Precedence: RUST_LOG wins; otherwise derive from -v flags.
    let default = match verbosity {
        0 => "warn",
        1 => "info",
        2 => "debug",
        _ => "trace",
    };
    let filter = EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| EnvFilter::new(default));

    tracing_subscriber::registry()
        .with(filter)
        .with(
            fmt::layer()
                .with_target(true)
                .with_timer(fmt::time::uptime())
                .with_writer(std::io::stderr),   // logs to stderr, data to stdout
        )
        .init();
}
```

Several deliberate choices there. Logging to **stderr** keeps stdout clean for the program's actual output,
which is what makes a CLI composable in a pipeline — the same discipline as writing diagnostics to
`Console.Error`. `EnvFilter` reads the `RUST_LOG` environment variable, which is the ecosystem convention
and roughly equivalent to the `Logging:LogLevel` configuration section, but with a compact directive syntax:

```text
RUST_LOG=info                          # everything at info and above
RUST_LOG=polcheck=debug,reqwest=warn   # per-module levels
RUST_LOG=warn,polcheck::scan=trace     # a global default plus an override
```

The `registry().with(...).with(...)` shape is tracing's **layer** system, and it is the feature that makes
the crate scale. A layer is a composable piece of subscriber behaviour, so you can send the same events to a
console formatter, a JSON file, and an OpenTelemetry exporter simultaneously by adding layers — the direct
analogue of registering multiple `ILoggerProvider`s, but with the composition visible in one expression.

For machine-readable output, `.json()` on the fmt layer emits one JSON object per event with all fields
promoted to top-level keys, which is what you want when a log aggregator is on the other end:

```rust,ignore
tracing_subscriber::fmt()
    .json()
    .with_current_span(true)
    .with_span_list(true)
    .init();
```

### Bridging `log`

Many crates — including some you depend on transitively — use `log` rather than `tracing`. The
`tracing-log` bridge captures those records so they appear alongside your spans. `tracing-subscriber` enables
it by default, so in practice this works without thought, but it is worth knowing why `log`-based crates
show up in your output.

The reverse direction exists too: if your application uses `log` and `env_logger`, that is a perfectly
reasonable lightweight choice. `env_logger` reads the same `RUST_LOG` variable, and the setup is a single
`env_logger::init()`. The comparison is straightforward:

| | `log` + `env_logger` | `tracing` + `tracing-subscriber` |
|---|---|---|
| Events | yes | yes |
| Structured fields | no (message only) | yes |
| Spans / context | no | yes |
| Async-aware | n/a | yes (`Instrument`) |
| Output formats | text | text, JSON, custom layers |
| OpenTelemetry | no | yes, via `tracing-opentelemetry` |
| .NET analogue | `Console.WriteLine` with levels | `ILogger` + Serilog + OTel |

My recommendation matches the ecosystem's: `tracing` unless you have a specific reason not to. The cost over
`log` is small and the span machinery is the whole value.

## Distributed tracing

`tracing-opentelemetry` turns spans into OpenTelemetry spans, exportable to Jaeger, Zipkin, or an OTLP
collector. Because spans already exist in your code, adding distributed tracing is a subscriber change
rather than an instrumentation project — which is the payoff for having used spans rather than log lines all
along.

```rust,ignore
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

fn init_telemetry(tracer: impl opentelemetry::trace::Tracer + Send + Sync + 'static) {
    let otel = tracing_opentelemetry::layer().with_tracer(tracer);

    tracing_subscriber::registry()
        .with(EnvFilter::from_default_env())
        .with(tracing_subscriber::fmt::layer())   // still log locally
        .with(otel)                               // and export spans
        .init();
}
```

The conceptual mapping to .NET is one-to-one: a tracing `Span` is an `Activity`, `#[instrument]` is
`ActivitySource.StartActivity`, span fields are activity tags, and the OTLP exporter is the same on both
sides. If you have wired up OpenTelemetry in ASP.NET Core, you already know what this does.

For HTTP services, `tower-http`'s `TraceLayer` creates a span per request automatically, and
`tracing-opentelemetry` propagates the W3C `traceparent` header, so a request crossing from a .NET service
into a Rust one keeps its trace id.

## Practical guidance

A few opinions, offered as opinions.

**Levels.** `error` for something that needs a human, `warn` for something recoverable but notable, `info`
for the milestones an operator cares about, `debug` for developer detail, `trace` for firehose. The
discipline that pays off is keeping `info` genuinely sparse — a scan started, a scan finished, a
configuration loaded — so that turning on `info` in production stays affordable.

**Never log secrets.** `#[instrument]` records arguments by default, so a function taking a token or
connection string needs `skip`. Consider a newtype whose `Debug` impl redacts, which makes the safe thing
automatic:

```rust
use std::fmt;

#[derive(Clone)]
pub struct Secret(String);

impl fmt::Debug for Secret {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("Secret(***)")
    }
}

impl Secret {
    pub fn expose(&self) -> &str { &self.0 }
}

fn main() {
    let token = Secret("ghp_realtokenvalue".into());
    assert_eq!(format!("{token:?}"), "Secret(***)");
    assert!(!format!("{token:?}").contains("realtoken"));
    assert_eq!(token.expose(), "ghp_realtokenvalue");
}
```

That is the newtype pattern applied to a security problem: the only way to get the raw value is to call a
method named `expose`, which is grep-able in review.

**Cost.** Disabled events are cheap — a level check against an atomic — but the *arguments* are still
evaluated unless the macro short-circuits them, so avoid expensive formatting in hot paths. `tracing`'s
compile-time `max_level_*` features can strip levels entirely from release builds, which is the closest
analogue to a conditional-compilation logging guard.

**Testing.** `tracing_test` or a custom capturing layer lets you assert that a given event was emitted,
which is occasionally the cleanest way to test an error path with no other observable effect.

## Before you move on

The architecture is a façade: `tracing` (or `log`) is what your code calls, a **subscriber** is what the
binary installs, and libraries must never install one. `tracing-subscriber` needs its `env-filter` feature
turned on explicitly, and `EnvFilter` reads `RUST_LOG` with per-module directives — the convention every
Rust binary follows.

Events carry a level, a message, and structured fields, with `?` recording via `Debug` and `%` via
`Display`. Prefer fields over interpolation for exactly the reason you prefer message templates over string
interpolation in .NET: fields are queryable and strings are not. Spans are the reason to choose tracing over
log, because every event inside a span inherits its fields, and `#[instrument]` gives you one per function
with the arguments attached — remembering to `skip` anything sensitive.

The async rule is the one to write on your hand: never hold a `span.enter()` guard across an `.await`, since
Rust has no `AsyncLocal` to flow the scope and the guard will mis-attribute other tasks' events. Use
`#[instrument]` on the async fn, or `.instrument(span)` on the future. Layers compose destinations the way
multiple `ILoggerProvider`s do, and because your code already emits spans, exporting to OpenTelemetry is a
subscriber change rather than a rewrite.

If you can explain why a library should not call `tracing_subscriber::fmt::init()`, and why
`.instrument(span)` is correct where `let _g = span.enter()` is wrong in async code, you have the model.

Next: [24 — Configuration](24-configuration.md).

### Sources

- `tracing`. <https://docs.rs/tracing/0.1/tracing/> — events, spans, fields, and the `?`/`%` sigils.
- `tracing::instrument`. <https://docs.rs/tracing/0.1/tracing/attr.instrument.html> — attribute options including `skip`, `skip_all`, `fields`, `err`, and async handling.
- `tracing-subscriber`. <https://docs.rs/tracing-subscriber/0.3/tracing_subscriber/> — `registry`, layers, `fmt`, and `EnvFilter` (behind the `env-filter` feature).
- `EnvFilter` directive syntax. <https://docs.rs/tracing-subscriber/0.3/tracing_subscriber/filter/struct.EnvFilter.html> — the `RUST_LOG` grammar.
- `log`. <https://docs.rs/log/0.4/log/> — the minimal logging façade.
- `env_logger`. <https://docs.rs/env_logger/0.11/env_logger/> — the lightweight subscriber for `log`.
- `tracing-opentelemetry`. <https://docs.rs/tracing-opentelemetry/> — exporting spans to OpenTelemetry.
- Microsoft Learn, "Logging in .NET". <https://learn.microsoft.com/dotnet/core/extensions/logging> — the `ILogger`/provider comparison point.
