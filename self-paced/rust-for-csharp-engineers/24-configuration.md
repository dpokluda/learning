# 24 — Configuration

Every non-trivial program eventually needs to answer the same question: where does a setting come from when
it could come from four places at once? You know the .NET answer intimately. `IConfiguration` is a layered
key/value store; `ConfigurationBuilder` stacks providers in order — `appsettings.json`, then
`appsettings.{Environment}.json`, then user secrets, then environment variables, then command-line
arguments — and later providers win. `IOptions<T>` binds a section of that store onto a POCO, and
`IOptionsSnapshot<T>` re-binds when the file changes.

Rust has no built-in equivalent, because Rust has no built-in host. What it has is two good crates that
implement the same idea, and the layering model transfers almost perfectly. What does *not* transfer is
change notification and dependency injection, and being clear about that up front saves you looking for
machinery that isn't there.

> **Prerequisite:** [20 — serde](20-serde.md), since both crates deserialize into your types via `Deserialize`.

## The shape of the problem

`polcheck` needs an API endpoint, a concurrency limit, retry parameters, and a verbosity level. Those should
be settable from a config file for the common case, from environment variables for containers and CI, and
from command-line flags for one-off overrides — with flags beating environment beating file beating built-in
defaults. That precedence order is so standard it is worth naming as the target:

```text
defaults  <  config file  <  environment  <  command-line flags
```

The type you are aiming at is an ordinary serde struct, and that is the first thing that will feel familiar:

```rust
use serde::Deserialize;

#[derive(Debug, Deserialize, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Settings {
    pub endpoint: String,
    pub concurrency: usize,
    #[serde(default)]
    pub verbose: bool,
    pub retry: Retry,
}

#[derive(Debug, Deserialize, PartialEq)]
pub struct Retry {
    pub attempts: u32,
    pub backoff_ms: u64,
}

fn main() {
    let toml_text = r#"
        endpoint = "https://management.azure.com"
        concurrency = 8
        [retry]
        attempts = 3
        backoff_ms = 250
    "#;
    let s: Settings = toml::from_str(toml_text).unwrap();
    assert_eq!(s.concurrency, 8);
    assert_eq!(s.retry.backoff_ms, 250);
    assert!(!s.verbose);
}
```

`deny_unknown_fields` deserves a moment. .NET's binder silently ignores keys it does not recognise, which
means a typo in `appsettings.json` fails silently and you discover it in production. Adding this one
attribute turns a misspelled key into a startup error, and I would put it on every configuration struct you
write. It is the single highest-value line in this module.

## The `config` crate

`config` is the closest structural match to `ConfigurationBuilder`: a builder, a stack of sources, and a
final bind onto your type.

```rust,ignore
use config::{Config, Environment, File};

pub fn load(path: &str) -> Result<Settings, config::ConfigError> {
    let cfg = Config::builder()
        // 1. defaults
        .set_default("concurrency", 4i64)?
        .set_default("retry.attempts", 3i64)?
        .set_default("retry.backoff_ms", 100i64)?
        // 2. file — extension inferred; optional so a missing file is fine
        .add_source(File::with_name(path).required(false))
        // 3. environment
        .add_source(
            Environment::with_prefix("POLCHECK")
                .prefix_separator("_")
                .separator("__")
                .try_parsing(true),
        )
        // 4. explicit override (e.g. from a CLI flag), applied only if Some
        .set_override_option("endpoint", std::env::var("FORCE_ENDPOINT").ok())?
        .build()?;

    cfg.try_deserialize()
}
```

Read that top to bottom and it is the `ConfigurationBuilder` you have written a hundred times.
`set_default` is `AddInMemoryCollection`, `File::with_name` is `AddJsonFile(optional: true)` — and note
that `with_name` omits the extension deliberately, so `config` will find `polcheck.toml`, `polcheck.json`,
`polcheck.yaml`, or `polcheck.ini` depending on which exists — `Environment` is
`AddEnvironmentVariables("POLCHECK_")`, and `try_deserialize` is `Bind`. Later sources win, exactly as in
.NET.

Two details in the environment source are worth slowing down for, and one of them cost me a debugging
session while writing this chapter.

The `separator("__")` call maps a double underscore to nesting, so `POLCHECK_RETRY__ATTEMPTS` sets
`retry.attempts`. This is the same convention .NET uses — `Logging__LogLevel__Default` — and for the same
reason: a colon is not portable in environment variable names on Unix.

The trap is that **`separator` also changes the separator used after the prefix**. Calling only
`.separator("__")` makes the crate look for `POLCHECK__ENDPOINT`, not `POLCHECK_ENDPOINT`, and your
single-underscore variables are silently ignored. I confirmed this empirically against config 0.15.25:

| Environment variable set | `.separator("__")` alone | `.prefix_separator("_").separator("__")` |
|---|---|---|
| `POLCHECK_ENDPOINT` | **ignored** | applied |
| `POLCHECK_RETRY__ATTEMPTS` | **ignored** | applied |
| `POLCHECK__ENDPOINT` | applied | ignored |

So always set `prefix_separator` explicitly when you set `separator`. Silence is the failure mode here, and
silence is the worst kind.

The other detail is `try_parsing(true)`. Environment variables are strings, and without this flag a
`concurrency` of `"8"` will not deserialize into a `usize`. With it, `config` attempts to parse values into
the target's shape. .NET's binder does this conversion unconditionally; `config` makes it opt-in.

## figment

`figment` — from the Rocket web framework's author — solves the same problem with a different emphasis. It
merges typed *providers* rather than a flat string map, tracks where every value came from for error
reporting, and has first-class support for profiles.

```rust,ignore
use figment::{
    providers::{Env, Format, Json, Serialized, Toml},
    Figment,
};

pub fn load(path: &str) -> Result<Settings, figment::Error> {
    Figment::from(Serialized::defaults(Settings::default()))
        .merge(Toml::file(path))
        .merge(Json::file("polcheck.json"))
        .merge(Env::prefixed("POLCHECK_").split("__"))
        .extract()
}
```

`Serialized::defaults` is the nicest idea in the crate: instead of writing `set_default` for every key as
strings, you write a `Default` impl for your settings type and serialize it as the base layer. Your defaults
are typed, checked by the compiler, and live next to the struct they belong to. In .NET terms it is as if
you could hand `ConfigurationBuilder` an instance of your options POCO as the lowest provider.

Note that `Env::prefixed("POLCHECK_")` takes the whole prefix *including* the trailing underscore, which
sidesteps the separator confusion the `config` crate has. `.split("__")` handles nesting.

`extract_inner` pulls out a sub-tree, which is the analogue of `GetSection("Retry").Get<Retry>()`:

```rust,ignore
let retry: Retry = figment.extract_inner("retry")?;
```

### Profiles

This is figment's genuinely distinctive feature and it maps directly onto something you use daily —
`ASPNETCORE_ENVIRONMENT` and the `appsettings.{Environment}.json` convention. A figment profile lets one
file hold several environments, with a `default` section merged underneath the selected one:

```toml
[default]
endpoint = "https://localhost:5001"
concurrency = 4
[default.retry]
attempts = 3
backoff_ms = 100

[release]
endpoint = "https://management.azure.com"
concurrency = 64
```

```rust,ignore
let settings: Settings = Figment::from(Toml::file("polcheck.toml").nested())
    .select("release")
    .extract()?;
```

I verified this merge behaviour: selecting `release` yields the release endpoint and concurrency while
inheriting `retry` from `default`. The `.nested()` call is what tells figment the file's top-level tables are
profiles rather than settings — forget it and your keys end up named `default.endpoint`.

The other thing figment gives you is provenance. Its errors carry the profile, the key path, and the source
metadata, so a failure tells you which file or variable was responsible. .NET's binder errors are markedly
less helpful, and if you have ever chased down which of six providers set a value, you will appreciate this.

### Choosing between them

| | `config` | `figment` |
|---|---|---|
| Mental model | flat key/value store, like `IConfiguration` | merged typed providers |
| Defaults | `set_default("a.b", v)` with stringly keys | `Serialized::defaults(T::default())` — typed |
| Profiles | manual (layer a second file) | built in, with `default` inheritance |
| Error detail | key and message | key, profile, provider, and source |
| Formats | TOML, JSON, YAML, INI, RON, JSON5 | TOML, JSON, YAML, env (via providers) |
| Watch/reload | `Config` is a snapshot; re-`build()` | snapshot; re-`extract()` |

Neither is wrong. Pick `config` if the `ConfigurationBuilder` shape is what your team expects; pick `figment`
if you want typed defaults and profiles, which for a CLI like `polcheck` I find the better fit. For a small
program, honestly consider neither — `toml::from_str` on one file plus a handful of `std::env::var` calls is
about fifteen lines and has no dependencies to justify.

## Wiring flags in on top

Layering config-file values *under* CLI flags is the part every real program needs and neither crate does for
you, because neither knows about clap. The pattern that works is to make the flag fields `Option<T>` in the
clap struct, load the file-and-environment layers into the settings struct, and then apply the `Some` values
over the top:

```rust
#[derive(Debug, PartialEq)]
pub struct Settings {
    pub endpoint: String,
    pub concurrency: usize,
    pub verbose: bool,
}

/// Only the fields the user actually passed are `Some`.
#[derive(Debug, Default)]
pub struct Overrides {
    pub endpoint: Option<String>,
    pub concurrency: Option<usize>,
    pub verbose: Option<bool>,
}

impl Settings {
    pub fn apply(mut self, o: Overrides) -> Self {
        if let Some(v) = o.endpoint { self.endpoint = v; }
        if let Some(v) = o.concurrency { self.concurrency = v; }
        if let Some(v) = o.verbose { self.verbose = v; }
        self
    }
}

fn main() {
    let from_file = Settings {
        endpoint: "https://from-file".into(),
        concurrency: 4,
        verbose: false,
    };

    let cli = Overrides { concurrency: Some(32), ..Default::default() };
    let final_settings = from_file.apply(cli);

    assert_eq!(final_settings.concurrency, 32);          // flag won
    assert_eq!(final_settings.endpoint, "https://from-file"); // file survived
}
```

The `Option` is doing essential work here. A plain `usize` field cannot distinguish "the user passed
`--concurrency 4`" from "clap filled in the default of 4", so a non-optional flag field would silently
clobber the config file with a default the user never asked for. This is the same bug people hit in .NET when
they bind a POCO with non-nullable value types over a partial configuration source, and the fix is the same:
make absence representable. Rust just makes you do it with a type the compiler enforces rather than a
nullable annotation you can ignore.

With figment there is a neater variant — implement `Serialize` on the overrides with
`skip_serializing_if = "Option::is_none"` and merge them as the topmost `Serialized` provider, which puts
flags into the same merge pipeline as everything else.

## What Rust does not give you

Two absences are worth stating plainly so you stop looking.

**There is no `IOptionsSnapshot<T>` or reload-on-change.** Both crates hand you a snapshot; if you want live
reload you watch the file yourself with `notify` and swap an `ArcSwap<Settings>` or
`Arc<RwLock<Settings>>`. That is maybe twenty lines, and most CLIs do not need it. Long-running services
sometimes do, and then you are building it deliberately rather than getting it for free.

**There is no dependency injection.** `IOptions<T>` exists mostly to get configuration into constructors
through a container. Rust programs pass `&Settings` (or `Arc<Settings>`) as an argument, which is more
typing at the call site and far less machinery overall — you can see exactly what each function depends on.
For `polcheck`, `main` loads settings once, wraps them in an `Arc`, and clones the `Arc` into each task.

**Secrets** are the one place to be careful. There is no user-secrets provider and no Key Vault
configuration provider wired up by convention; you read from the environment, or call the Azure SDK
yourself. Wrap anything sensitive in a redacting newtype, as shown in the previous module, so a stray
`{:?}` cannot leak it.

## Before you move on

The layering model is the part that transfers: defaults, then file, then environment, then flags, with later
layers winning — the same precedence `ConfigurationBuilder` gives you, expressed by the order of
`add_source` or `merge` calls. Both `config` and `figment` deserialize into an ordinary serde struct, and
putting `deny_unknown_fields` on that struct converts a silent typo into a startup error, which is a strictly
better outcome than .NET's silently-ignored key.

The two crates differ in emphasis. `config` mirrors `ConfigurationBuilder` most literally, at the cost of
stringly-typed defaults and one nasty trap: setting `separator("__")` also changes the prefix separator, so
you must set `prefix_separator("_")` explicitly or your `POLCHECK_*` variables are silently ignored, and you
need `try_parsing(true)` for anything that is not a string. `figment` lets you write defaults as a typed
`Default` impl, tracks provenance in its errors, and supports profiles that merge a `default` section under a
selected one — the direct analogue of `appsettings.{Environment}.json`.

Command-line flags must be `Option<T>` so that "not passed" is distinguishable from "passed the default
value"; otherwise a clap default silently overwrites a config file. And two pieces of .NET machinery simply
are not there: no reload-on-change, and no DI — you pass `&Settings` or an `Arc<Settings>` around explicitly.

If you can explain why `Option<T>` is required in the overrides struct, and what goes wrong when you call
`separator` without `prefix_separator`, you have the two things this module exists to teach.

Next: [25 — Database access with sqlx](25-sqlx.md).

### Sources

- `config` crate. <https://docs.rs/config/0.15/config/> — `ConfigBuilder`, `File`, `Environment`, `set_default`, `try_deserialize`. Separator behaviour verified empirically against 0.15.25.
- `config::Environment`. <https://docs.rs/config/0.15/config/struct.Environment.html> — `prefix_separator`, `separator`, `try_parsing`.
- `figment`. <https://docs.rs/figment/0.10/figment/> — providers, merging, `extract`, `extract_inner`, error metadata.
- `figment` profiles. <https://docs.rs/figment/0.10/figment/struct.Profile.html> — `nested()` and `select()`.
- serde container attributes. <https://serde.rs/container-attrs.html#deny_unknown_fields> — rejecting unrecognised keys.
- Microsoft Learn, "Configuration in .NET". <https://learn.microsoft.com/dotnet/core/extensions/configuration> — provider ordering and the `__` environment-variable convention.
- Microsoft Learn, "Options pattern in .NET". <https://learn.microsoft.com/dotnet/core/extensions/options> — `IOptions`/`IOptionsSnapshot`, the machinery Rust does not provide.
