//! Typed errors for the `polcheck` library.
//!
//! Every fallible operation in the library returns one of these. The binary
//! converts them into `anyhow::Error` at the boundary and adds context.

use std::path::PathBuf;

/// Everything that can go wrong inside the `polcheck` library.
#[derive(Debug, thiserror::Error)]
pub enum Error {
    /// A rule file could not be read from disk.
    #[error("could not read rule file `{path}`")]
    ReadRules {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },

    /// A rule file was read but is not valid JSON, or does not match the schema.
    #[error("rule file `{path}` is not valid")]
    ParseRules {
        path: PathBuf,
        #[source]
        source: serde_json::Error,
    },

    /// A resource document could not be parsed.
    #[error("resource document is not valid JSON")]
    ParseResources(#[from] serde_json::Error),

    /// A rule referred to a field that no resource carries.
    #[error("rule `{rule}` references unknown field `{field}`")]
    UnknownField { rule: String, field: String },

    /// The rule tree nested deeper than `max_depth` allows.
    #[error("rule `{rule}` nests deeper than the limit of {limit}")]
    TooDeep { rule: String, limit: usize },

    /// Fetching resources over HTTP failed.
    #[error("could not fetch resources from `{url}`")]
    Fetch {
        url: String,
        #[source]
        source: reqwest::Error,
    },
}

/// The library's result alias.
pub type Result<T> = std::result::Result<T, Error>;
