# Exercises 24 — Configuration with figment

> **Covers:** [24 — Configuration with figment](../24-configuration.md). **Code:** `crate-drills/src/ch24.rs`. **Answers:** [answers/24-config.md](answers/24-config.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** `IConfiguration` and figment both layer providers. What is the fundamental difference in what you get out the far end?

**A2.** Describe the precedence order you would build for a service, and justify each layer's position.

**A3.** figment's `Env::prefixed("POLCHECK_")` and its `split("__")` both look like small details. What goes wrong if you get either one wrong?

**A4.** What are figment profiles, how do they differ from `appsettings.Development.json`, and what two calls do they require?

**A5.** Why does figment report an error like ``missing field `retry` `` rather than pointing at a line number, and how do you make the message more useful?

**A6.** Configuration errors like figment's are large enough that clippy complains. What is the lint, why does it fire, and what would you do in your own code?

## Part B — Exercise

Open `crate-drills/src/ch24.rs`. This drill builds a layered configuration stack
of the kind every service needs, and then spends most of its tests on the ways
such a stack silently does nothing.

You compose three providers in precedence order — serialized defaults, then a
TOML file, then prefixed environment variables — and extract a typed `Settings`
from the result. Then you add a profile-aware variant, which needs two calls that
are easy to omit: `.nested()` on the file provider and `.select()` on the figment.

The tests are the interesting part. A missing file must contribute nothing rather
than fail. The file must override defaults *per key*, leaving untouched fields
alone. The environment must win because it merges last. A nested key must be
reachable through the `__` separator, and figment's `Env::prefixed` must be given
the whole prefix *including* its trailing underscore — get that wrong and
overrides silently stop working, with no error anywhere.

The failure-mode tests matter as much: a bad value must produce a message naming
the key and the provider, a misspelled key must be rejected rather than ignored,
and a genuinely missing required field must name itself. Compare each of those to
what `configuration["Retry:Attempts"]` gives you when the key is absent, which is
`null` and a bound property quietly holding its default.

Every test runs inside `figment::Jail`, which gives it a private working directory
and a private view of the environment. That is not a nicety: environment variables
are process-global, tests run in parallel, and in edition 2024 `std::env::set_var`
is `unsafe` for precisely that reason.

Run it with `cargo test ch24` from the `exercises/crate-drills` directory.

### Starter stub

```rust,ignore
//! Crate drill 24 — configuration with figment.
//!
//! `IConfiguration` layers providers and hands you a stringly-typed tree that
//! you bind at the end. figment layers providers too, but the result is a typed
//! value produced by serde, so a missing or mistyped key is a deserialization
//! error naming the field rather than a null you discover in production.
//!
//! One clippy lint is silenced for the whole module: `result_large_err` fires
//! because `figment::Error` is over 200 bytes. In your own library you would box
//! it; here the type is fixed by figment's API.
#![allow(clippy::result_large_err)]
// The provider imports are here for you; the nagging is silenced until you use
// them.
#![allow(unused_imports)]

use std::time::Duration;

use figment::providers::{Env, Format, Serialized, Toml};
use figment::{Figment, Profile};
use serde::{Deserialize, Serialize};

/// The whole configuration as one type. Unknown keys must be rejected rather
/// than ignored, so a typo in the file is an error instead of silence, and
/// `tags` must default to empty when absent.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Settings {
    pub endpoint: String,
    pub retry: RetrySettings,
    pub tags: Vec<String>,
}

/// Unknown keys are rejected here too.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RetrySettings {
    pub attempts: u32,
    pub backoff_ms: u64,
}

impl RetrySettings {
    pub fn backoff(&self) -> Duration {
        Duration::from_millis(self.backoff_ms)
    }
}

impl Default for Settings {
    /// `endpoint` is `"https://management.azure.com"`, three attempts, 250ms
    /// backoff, no tags.
    fn default() -> Self {
        todo!("supply the baseline configuration")
    }
}

/// Layer the providers so that later ones win:
/// 1. the serialized `Settings::default()`
/// 2. the TOML file at `path` — a missing file must contribute nothing rather
///    than fail
/// 3. environment variables prefixed `POLCHECK_`, where `__` separates nested
///    keys, so `POLCHECK_RETRY__ATTEMPTS` reaches `retry.attempts`
///
/// Note that figment's `Env::prefixed` takes the **whole** prefix including its
/// trailing underscore.
pub fn build(_path: &str) -> Figment {
    todo!("compose the three providers in precedence order")
}

/// The same stack, but reading the file as *profile-nested* sections and
/// selecting `profile`. This is figment's answer to
/// `appsettings.Development.json`: the sections live in one file under their
/// profile name, so the TOML provider needs `.nested()` and the figment needs
/// `.select(...)`.
pub fn build_with_profile(_path: &str, _profile: &str) -> Figment {
    todo!("read the file as nested profiles and select one")
}

pub fn extract(_figment: &Figment) -> Result<Settings, figment::Error> {
    todo!("extract the typed Settings")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
