//! Loading rules and resources, from disk or over HTTP.

use std::path::Path;

use crate::error::{Error, Result};
use crate::rules::{Resource, RuleSet};

/// Read and parse a rule file.
pub async fn load_rules(path: &Path) -> Result<RuleSet> {
    let text = tokio::fs::read_to_string(path)
        .await
        .map_err(|source| Error::ReadRules {
            path: path.to_path_buf(),
            source,
        })?;

    serde_json::from_str(&text).map_err(|source| Error::ParseRules {
        path: path.to_path_buf(),
        source,
    })
}

/// Read and parse a resource file.
pub async fn load_resources(path: &Path) -> Result<Vec<Resource>> {
    let text = tokio::fs::read_to_string(path)
        .await
        .map_err(|source| Error::ReadRules {
            path: path.to_path_buf(),
            source,
        })?;

    Ok(serde_json::from_str(&text)?)
}

/// Fetch resources from an HTTP endpoint.
pub async fn fetch_resources(client: &reqwest::Client, url: &str) -> Result<Vec<Resource>> {
    let make_err = |source| Error::Fetch {
        url: url.to_string(),
        source,
    };

    client
        .get(url)
        .send()
        .await
        .map_err(make_err)?
        .error_for_status()
        .map_err(make_err)?
        .json::<Vec<Resource>>()
        .await
        .map_err(make_err)
}
