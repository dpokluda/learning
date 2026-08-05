# Answers 24 — Configuration with figment

> Exercises: [24-config.md](../24-config.md)

## Part A

**A1. `IConfiguration` and figment both layer providers. What is the fundamental difference in what you get out the far end?**

`IConfiguration` produces a stringly-typed tree that you index by colon-delimited path, and binding to a POCO is a separate, best-effort step that silently leaves unmatched properties at their defaults. figment produces a typed value through serde: you call `extract::<Settings>()` and either receive a fully-formed `Settings` or an error naming the field that was missing or ill-typed. The difference matters most at the failure boundary. A misspelled key in .NET means `configuration["Retry:Attempts"]` returns null and the bound property keeps its default — a bug that surfaces as wrong behaviour much later. In figment, with `deny_unknown_fields`, it is an error at startup that names the key and the provider it came from. The layering model itself is genuinely similar, and deliberately so: providers merge in order, later wins, and the environment goes last because that is what an orchestrator can set.

**A2. Describe the precedence order you would build for a service, and justify each layer's position.**

Serialized defaults first, so every field has a value and the type can always be constructed; this is also where the defaults live in code rather than in a file nobody deploys. The configuration file next, because it is the deployable unit that expresses this environment's intent. Environment variables last, because they are what a container orchestrator, a CI system or an operator debugging at three in the morning can actually change without a rebuild — and last means they win. If you support a command line, it goes after the environment, on the same reasoning taken one step further: the most immediate, most deliberate input wins. The rule generalizes as 'the harder something is to change, the earlier it merges', which is precisely the ordering ASP.NET Core's default host builder uses too.

**A3. figment's `Env::prefixed("POLCHECK_")` and its `split("__")` both look like small details. What goes wrong if you get either one wrong?**

`prefixed` takes the *entire* prefix including the trailing underscore, so `Env::prefixed("POLCHECK")` looks for variables named `POLCHECKENDPOINT` and finds nothing — a silent no-op that reads as 'environment overrides do not work'. `split` sets the string that separates nested keys, and it must not collide with anything appearing in a key name: choosing `_` would make `BACKOFF_MS` mean `backoff.ms`, which does not exist, so a perfectly reasonable-looking variable would fail to bind or bind to the wrong place. Double underscore is the conventional choice for exactly that reason, and it is the same convention ASP.NET Core uses for its own environment provider. The failure mode both share is silence, which is why a test that sets an environment variable and asserts it landed is worth writing once per project.

**A4. What are figment profiles, how do they differ from `appsettings.Development.json`, and what two calls do they require?**

A profile is a named section within the same source, so `[default]` and `[dev]` tables live in one TOML file rather than in separate files discriminated by filename. Selecting `dev` layers the `dev` section on top of `default`, so unspecified keys still fall through to the base — the same inheritance `appsettings.Development.json` gives you over `appsettings.json`, achieved without file proliferation. It requires two calls that are easy to forget: `.nested()` on the provider, telling it the top-level tables are profile names rather than configuration keys, and `.select(profile)` on the figment, telling it which one is live. Omit `nested()` and figment treats `default` and `dev` as ordinary keys that fail to deserialize; omit `select` and you get the default profile regardless of what you passed.

**A5. Why does figment report an error like ``missing field `retry` `` rather than pointing at a line number, and how do you make the message more useful?**

The error comes from serde's deserializer, which is describing the shape of the data it was handed after all providers merged — at that point there is no single file and no single line, because the value may have been assembled from three sources. figment compensates by attaching provider metadata, which is why a bad environment value renders as `invalid type: found string "not-a-number", expected u32 for key "RETRY.ATTEMPTS" in `POLCHECK_` environment variable(s)` — naming both the key and where it came from. To make messages more useful you keep the configuration type shallow and its field names close to what a user would write, turn on `deny_unknown_fields` so typos are named rather than ignored, and extract early, in `main`, so a misconfiguration fails at startup instead of on the first request.

**A6. Configuration errors like figment's are large enough that clippy complains. What is the lint, why does it fire, and what would you do in your own code?**

The lint is `result_large_err`, and it fires because `figment::Error` is over two hundred bytes — it carries the provider metadata, the key path and the serde message — so every `Result<T, figment::Error>` in the module is as large as its error variant, even on the overwhelmingly common success path. Since a `Result` is returned by value, that is real stack traffic and real memcpy for functions that almost never fail. In your own code the fix is to box the error, either as `Box<figment::Error>` in your own variant or by making the whole error type a newtype around a box, which shrinks the `Result` back to something pointer-sized. It is a small thing, but it is a nice example of the compiler and its lints surfacing a cost that in a garbage-collected language is invisible because everything was already behind a reference.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` and `cargo clippy -- -D warnings` on the pinned toolchain.

```rust
//! Crate drill 24 — configuration with figment.
//!
//! `IConfiguration` layers providers and hands you a stringly-typed tree that
//! you bind at the end. figment layers providers too, but the result is a
//! typed value produced by serde, so a missing or mistyped key is a
//! deserialization error naming the field rather than a null you discover in
//! production.
//!
//! One clippy lint is silenced for the whole module: `result_large_err` fires
//! because `figment::Error` is over 200 bytes, so every `Result` in this file
//! is dominated by its error variant. In a real library you box it; here the
//! error type is fixed by figment's own API, and the noise would drown the
//! lesson. That the lint fires at all is worth noticing — it is the same
//! finding that shaped `polcheck`'s configuration module.
#![allow(clippy::result_large_err)]

use std::time::Duration;

use figment::providers::{Env, Format, Serialized, Toml};
use figment::{Figment, Profile};
use serde::{Deserialize, Serialize};

/// The whole configuration as one type. Nesting is expressed by nesting, and
/// `Default` supplies the base layer — the equivalent of `AddInMemoryCollection`
/// with your defaults, except the compiler checks it.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Settings {
    pub endpoint: String,
    pub retry: RetrySettings,
    #[serde(default)]
    pub tags: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct RetrySettings {
    pub attempts: u32,
    /// Durations need a representation choice. Storing milliseconds as a plain
    /// integer keeps the file readable and the type honest.
    pub backoff_ms: u64,
}

impl RetrySettings {
    pub fn backoff(&self) -> Duration {
        Duration::from_millis(self.backoff_ms)
    }
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            endpoint: "https://management.azure.com".to_string(),
            retry: RetrySettings {
                attempts: 3,
                backoff_ms: 250,
            },
            tags: Vec::new(),
        }
    }
}

/// The layering. Later providers win, exactly as in `IConfiguration`, and the
/// order encodes the policy: built-in defaults, then the file, then the
/// environment, which is what a container orchestrator can actually set.
pub fn build(path: &str) -> Figment {
    Figment::from(Serialized::defaults(Settings::default()))
        .merge(Toml::file(path))
        // The prefix includes its trailing underscore. `POLCHECK_ENDPOINT`
        // maps to `endpoint`, and the nested `__` separator reaches
        // `retry.attempts` via `POLCHECK_RETRY__ATTEMPTS`.
        .merge(Env::prefixed("POLCHECK_").split("__"))
}

/// Profiles are figment's answer to `appsettings.Development.json`. A profile
/// section in the file is *nested* under its name, so the provider must be
/// told to read it that way with `.nested()`, and the figment must then
/// `.select()` which profile is live.
pub fn build_with_profile(path: &str, profile: &str) -> Figment {
    Figment::from(Serialized::defaults(Settings::default()))
        .merge(Toml::file(path).nested())
        .merge(Env::prefixed("POLCHECK_").split("__"))
        .select(Profile::new(profile))
}

pub fn extract(figment: &Figment) -> Result<Settings, figment::Error> {
    figment.extract()
}

#[cfg(test)]
mod tests {
    use super::*;
    use figment::Jail;

    /// `Jail` gives each test a private working directory and a private view of
    /// the environment, then restores both. Without it, environment-variable
    /// tests race each other — and in edition 2024 `set_var` is `unsafe`
    /// precisely because of that hazard.
    #[test]
    fn defaults_apply_when_nothing_else_is_present() {
        Jail::expect_with(|_jail| {
            let settings = extract(&build("polcheck.toml")).unwrap();
            assert_eq!(settings, Settings::default());
            assert_eq!(settings.retry.backoff(), Duration::from_millis(250));
            Ok(())
        });
    }

    #[test]
    fn a_missing_file_is_not_an_error() {
        Jail::expect_with(|_jail| {
            // `Toml::file` on a nonexistent path contributes nothing rather
            // than failing, which is what makes optional config files work.
            assert!(extract(&build("absolutely-not-here.toml")).is_ok());
            Ok(())
        });
    }

    #[test]
    fn the_file_overrides_the_defaults_field_by_field() {
        Jail::expect_with(|jail| {
            jail.create_file(
                "polcheck.toml",
                r#"
                endpoint = "https://example.test"
                [retry]
                attempts = 7
                backoff_ms = 250
                "#,
            )?;

            let settings = extract(&build("polcheck.toml")).unwrap();
            assert_eq!(settings.endpoint, "https://example.test");
            assert_eq!(settings.retry.attempts, 7);
            // Untouched fields keep the default; merging is per-key.
            assert!(settings.tags.is_empty());
            Ok(())
        });
    }

    #[test]
    fn the_environment_wins_because_it_is_merged_last() {
        Jail::expect_with(|jail| {
            jail.create_file("polcheck.toml", r#"endpoint = "https://from-file.test""#)?;
            jail.set_env("POLCHECK_ENDPOINT", "https://from-env.test");

            let settings = extract(&build("polcheck.toml")).unwrap();
            assert_eq!(settings.endpoint, "https://from-env.test");
            Ok(())
        });
    }

    #[test]
    fn a_nested_key_is_reached_through_the_separator() {
        Jail::expect_with(|jail| {
            jail.set_env("POLCHECK_RETRY__ATTEMPTS", "11");

            let settings = extract(&build("polcheck.toml")).unwrap();
            assert_eq!(settings.retry.attempts, 11);
            // The sibling under the same table is untouched.
            assert_eq!(settings.retry.backoff_ms, 250);
            Ok(())
        });
    }

    #[test]
    fn values_are_parsed_into_the_target_type_not_left_as_strings() {
        Jail::expect_with(|jail| {
            jail.set_env("POLCHECK_RETRY__BACKOFF_MS", "1500");

            let settings = extract(&build("polcheck.toml")).unwrap();
            assert_eq!(settings.retry.backoff(), Duration::from_millis(1500));
            Ok(())
        });
    }

    #[test]
    fn a_bad_value_fails_loudly_at_extraction_time() {
        Jail::expect_with(|jail| {
            jail.set_env("POLCHECK_RETRY__ATTEMPTS", "not-a-number");

            let err = extract(&build("polcheck.toml")).unwrap_err();
            let text = err.to_string();
            // The message names the offending path *and* the provider it came
            // from, which is the whole advantage over
            // `configuration["Retry:Attempts"]` quietly returning null. Note
            // the key is echoed as the environment spelled it.
            assert!(
                text.contains(r#"invalid type: found string "not-a-number", expected u32"#),
                "unhelpful message: {text}"
            );
            assert!(text.contains("RETRY.ATTEMPTS"), "message lost the key: {text}");
            assert!(text.contains("environment variable"), "message lost the source: {text}");
            Ok(())
        });
    }

    #[test]
    fn deny_unknown_fields_catches_a_misspelled_key() {
        Jail::expect_with(|jail| {
            jail.create_file(
                "polcheck.toml",
                r#"
                endpont = "https://typo.test"
                "#,
            )?;

            let err = extract(&build("polcheck.toml")).unwrap_err();
            assert!(err.to_string().contains("endpont"));
            Ok(())
        });
    }

    #[test]
    fn a_missing_required_field_names_itself() {
        // Drop the defaults layer so `retry` genuinely has no value anywhere.
        Jail::expect_with(|jail| {
            jail.create_file("polcheck.toml", r#"endpoint = "https://x.test""#)?;

            let figment = Figment::from(Toml::file("polcheck.toml"));
            let err = figment.extract::<Settings>().unwrap_err();
            assert_eq!(err.to_string(), "missing field `retry`");
            Ok(())
        });
    }

    #[test]
    fn profiles_select_a_nested_section_of_the_same_file() {
        Jail::expect_with(|jail| {
            jail.create_file(
                "polcheck.toml",
                r#"
                [default]
                endpoint = "https://prod.test"

                [dev]
                endpoint = "https://dev.test"
                "#,
            )?;

            let prod = extract(&build_with_profile("polcheck.toml", "default")).unwrap();
            assert_eq!(prod.endpoint, "https://prod.test");

            // Selecting `dev` layers it on top of `default`, so unspecified
            // keys still come from the base section.
            let dev = extract(&build_with_profile("polcheck.toml", "dev")).unwrap();
            assert_eq!(dev.endpoint, "https://dev.test");
            assert_eq!(dev.retry.attempts, 3);
            Ok(())
        });
    }
}
```
