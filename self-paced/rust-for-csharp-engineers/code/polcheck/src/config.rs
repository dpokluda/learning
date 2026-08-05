//! Layered configuration: built-in defaults, then a TOML file, then
//! `POLCHECK_*` environment variables, then command-line flags.

use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

use figment::{
    Figment,
    providers::{Env, Format, Serialized, Toml},
};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Settings {
    /// Where to fetch resources from when `--resources` is not given.
    pub endpoint: Option<String>,
    /// Maximum rule nesting depth accepted by `validate`.
    pub max_depth: usize,
    /// Treat a reference to an absent field as an error.
    pub strict: bool,
    /// Exit non-zero when findings at this severity or above are present.
    pub fail_on: crate::rules::Severity,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            endpoint: None,
            max_depth: 8,
            strict: false,
            fail_on: crate::rules::Severity::Error,
        }
    }
}

/// Fields the user may override on the command line. `None` means "not passed",
/// which is what keeps a clap default from silently clobbering the config file.
#[derive(Debug, Default, Clone)]
pub struct Overrides {
    pub endpoint: Option<String>,
    pub max_depth: Option<usize>,
    pub strict: Option<bool>,
    pub fail_on: Option<crate::rules::Severity>,
}

impl Settings {
    /// Load defaults, then the file if it exists, then the environment.
    ///
    /// The error is boxed because `figment::Error` is over 200 bytes, and an
    /// unboxed `Err` variant that large makes every `Result<Settings, _>` — and
    /// every caller's stack frame — pay for it. Clippy's `result_large_err`
    /// lint catches exactly this.
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

    /// Apply command-line overrides on top.
    #[must_use]
    pub fn apply(mut self, o: Overrides) -> Self {
        if let Some(v) = o.endpoint {
            self.endpoint = Some(v);
        }
        if let Some(v) = o.max_depth {
            self.max_depth = v;
        }
        if let Some(v) = o.strict {
            self.strict = v;
        }
        if let Some(v) = o.fail_on {
            self.fail_on = v;
        }
        self
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::rules::Severity;

    #[test]
    fn defaults_are_used_when_nothing_else_is_present() {
        let s = Settings::default();
        assert_eq!(s.max_depth, 8);
        assert!(!s.strict);
        assert_eq!(s.fail_on, Severity::Error);
        assert_eq!(s.endpoint, None);
    }

    #[test]
    fn file_values_beat_defaults() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("polcheck.toml");
        std::fs::write(&path, "max_depth = 3\nstrict = true\n").unwrap();

        let s = Settings::load(Some(&path)).unwrap();
        assert_eq!(s.max_depth, 3);
        assert!(s.strict);
        // Not mentioned in the file, so the default survives.
        assert_eq!(s.fail_on, Severity::Error);
    }

    #[test]
    fn flags_beat_everything_but_only_when_passed() {
        let base = Settings {
            max_depth: 3,
            strict: true,
            ..Settings::default()
        };

        // An empty override set changes nothing — this is the bug that a
        // non-Option flag field would cause.
        let untouched = base.clone().apply(Overrides::default());
        assert_eq!(untouched, base);

        let overridden = base.apply(Overrides {
            max_depth: Some(99),
            ..Overrides::default()
        });
        assert_eq!(overridden.max_depth, 99);
        assert!(overridden.strict, "unrelated fields must survive");
    }

    #[test]
    fn unknown_keys_are_rejected_rather_than_ignored() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("polcheck.toml");
        std::fs::write(&path, "max_dpeth = 3\n").unwrap();

        let err = Settings::load(Some(&path)).unwrap_err();
        assert!(
            err.to_string().contains("max_dpeth"),
            "expected the typo to be named, got: {err}"
        );
    }
}
