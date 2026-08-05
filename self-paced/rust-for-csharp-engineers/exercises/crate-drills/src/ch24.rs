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
