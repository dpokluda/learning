//! Crate drill 22 — axum, reqwest and tracing: a service and a client.
//!
//! Everything here runs against a server the test starts on `127.0.0.1:0`, so
//! the whole chapter is offline. That is also good practice: an integration
//! test that reaches the public internet is a test that fails for reasons you
//! do not control.

// Silenced until the implementations below actually use these.
#![allow(unused_imports)]
// The handlers look unused until `router` wires them up.
#![allow(dead_code)]

use std::net::SocketAddr;
use std::sync::{Arc, Mutex};

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use tokio::task::JoinHandle;

/// Shared state. axum has no DI container, so you pass state explicitly and the
/// `State` extractor pulls it out — less magic, and the type checker enforces
/// that every handler asking for state actually gets it.
#[derive(Clone, Default)]
pub struct AppState {
    pub hits: Arc<Mutex<Vec<String>>>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Resource {
    pub id: String,
    pub kind: String,
    pub compliant: bool,
}

#[derive(Debug, Deserialize)]
pub struct ListParams {
    #[serde(default)]
    pub kind: Option<String>,
}

#[derive(Debug, Serialize, Deserialize, PartialEq)]
pub struct ApiError {
    pub message: String,
}

/// Record `format!("get:{id}")` into the shared state, then:
/// * for the id `"missing"`, return `404` with
///   ``ApiError { message: "no resource `missing`" }``
/// * otherwise return the resource with `kind: "vm"` and `compliant: true`
async fn get_resource(
    State(_state): State<AppState>,
    Path(_id): Path<String>,
) -> Result<Json<Resource>, (StatusCode, Json<ApiError>)> {
    todo!("record the hit, then answer or 404")
}

/// Return two fixed resources — `("a", "vm", true)` and `("b", "disk", false)` —
/// filtered by `params.kind` when it is present.
async fn list_resources(Query(_params): Query<ListParams>) -> Json<Vec<Resource>> {
    todo!("filter the fixed list by the optional kind")
}

/// Echo the posted resource back with `compliant` recomputed as
/// `kind == "vm"`, under status `201 Created`.
async fn evaluate(Json(_resource): Json<Resource>) -> (StatusCode, Json<Resource>) {
    todo!("recompute the verdict and return it as 201")
}

/// Wire up the routes:
/// * `GET /resources/{id}` → `get_resource`  (note axum 0.8's brace syntax;
///   material written for 0.7 uses `:id` and will not compile)
/// * `GET /resources` → `list_resources`
/// * `POST /evaluate` → `evaluate`
///
/// and attach `state`.
pub fn router(_state: AppState) -> Router {
    todo!("build the Router and attach the state")
}

/// Bind a `TcpListener` to `127.0.0.1:0`, let the OS pick the port, and serve
/// the router on a spawned task. Returning the *real* address is what makes the
/// tests hermetic and parallel-safe.
pub async fn start_server(_state: AppState) -> (SocketAddr, JoinHandle<()>) {
    todo!("bind to port zero, then axum::serve on a spawned task")
}

/// `GET {base}/resources/{id}` and decode the body.
///
/// * a transport failure becomes `Err(format!("transport: {e}"))`
/// * a `404` is **not** a transport failure: decode the `ApiError` body and
///   return its `message` as the error string
/// * any other non-success status becomes `Err(format!("status: {e}"))` — use
///   `error_for_status`
/// * a decode failure becomes `Err(format!("decode: {e}"))`
pub async fn fetch_resource(
    _client: &reqwest::Client,
    _base: &str,
    _id: &str,
) -> Result<Resource, String> {
    todo!("send the request and classify the outcome")
}

/// A minimal `Layer` that records events into a vector, so the tests can assert
/// on what was logged. In .NET you would inject a fake `ILogger<T>`; the shape
/// of the idea is identical.
pub mod capture {
    use std::sync::{Arc, Mutex};

    use tracing::field::{Field, Visit};
    use tracing_subscriber::Layer;
    use tracing_subscriber::layer::Context;

    #[derive(Clone, Default)]
    pub struct Recorder(pub Arc<Mutex<Vec<String>>>);

    struct MessageVisitor(String);

    impl Visit for MessageVisitor {
        /// Put the `message` field's `Debug` rendering into `self.0`; append
        /// every other field as ` name=value`.
        fn record_debug(&mut self, _field: &Field, _value: &dyn std::fmt::Debug) {
            todo!("special-case the `message` field, append the rest")
        }
    }

    impl<S: tracing::Subscriber> Layer<S> for Recorder {
        /// Visit the event's fields and push `format!("{level}: {message}")`
        /// onto the shared vector.
        fn on_event(&self, _event: &tracing::Event<'_>, _ctx: Context<'_, S>) {
            todo!("record the event and push a formatted line")
        }
    }
}

/// Instrumented work. Add `#[tracing::instrument]`, skipping `resources` and
/// recording a `count` field holding its length. Inside, emit:
/// * a `debug!` event `"starting evaluation"`
/// * when there are failures, a `warn!` carrying the `failures` field
/// * an `info!` event `"evaluation complete"`
///
/// Return the number of non-compliant resources.
pub fn evaluate_all(_rule: &str, _resources: &[Resource]) -> usize {
    todo!("emit the three events and count the failures")
}

#[cfg(test)]
mod tests {
    use super::*;
    use tracing_subscriber::layer::SubscriberExt;
    use tracing_subscriber::util::SubscriberInitExt;
    use tracing_subscriber::{EnvFilter, Registry};

    async fn harness() -> (String, AppState, reqwest::Client) {
        let state = AppState::default();
        let (addr, _handle) = start_server(state.clone()).await;
        (format!("http://{addr}"), state, reqwest::Client::new())
    }

    #[tokio::test]
    async fn a_handler_binds_its_arguments_from_the_request() {
        let (base, state, client) = harness().await;

        let got = fetch_resource(&client, &base, "abc").await.unwrap();
        assert_eq!(
            got,
            Resource { id: "abc".into(), kind: "vm".into(), compliant: true }
        );

        // The `State` extractor really did hand the handler the shared value.
        assert_eq!(state.hits.lock().unwrap().as_slice(), ["get:abc"]);
    }

    #[tokio::test]
    async fn a_404_is_a_successful_exchange_that_you_must_check_for() {
        let (base, _state, client) = harness().await;

        let err = fetch_resource(&client, &base, "missing").await.unwrap_err();
        assert_eq!(err, "no resource `missing`");
    }

    #[tokio::test]
    async fn query_parameters_deserialize_through_serde() {
        let (base, _state, client) = harness().await;

        let all: Vec<Resource> = client
            .get(format!("{base}/resources"))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(all.len(), 2);

        let vms: Vec<Resource> = client
            .get(format!("{base}/resources"))
            .query(&[("kind", "vm")])
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(vms.len(), 1);
        assert_eq!(vms[0].id, "a");
    }

    #[tokio::test]
    async fn json_bodies_round_trip_through_the_derive() {
        let (base, _state, client) = harness().await;

        let response = client
            .post(format!("{base}/evaluate"))
            .json(&Resource { id: "z".into(), kind: "disk".into(), compliant: true })
            .send()
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::CREATED);
        let verdict: Resource = response.json().await.unwrap();
        assert!(!verdict.compliant, "a disk is not a vm, so the verdict flips");
        assert_eq!(verdict.id, "z");
    }

    #[tokio::test]
    async fn a_client_is_cheap_to_clone_and_shares_the_pool() {
        let (base, _state, client) = harness().await;

        let mut set = tokio::task::JoinSet::new();
        for i in 0..8 {
            let client = client.clone();
            let base = base.clone();
            set.spawn(async move { fetch_resource(&client, &base, &format!("r{i}")).await });
        }
        let mut ok = 0;
        while let Some(res) = set.join_next().await {
            if res.unwrap().is_ok() {
                ok += 1;
            }
        }
        assert_eq!(ok, 8);
    }

    #[test]
    fn instrument_records_events_at_their_declared_levels() {
        let recorder = capture::Recorder::default();
        let sink = Arc::clone(&recorder.0);

        // `with_default` scopes the subscriber to this closure, which is how
        // you keep logging assertions from leaking between tests.
        let subscriber = Registry::default()
            .with(recorder)
            .with(EnvFilter::new("debug"));

        tracing::subscriber::with_default(subscriber, || {
            let resources = vec![
                Resource { id: "a".into(), kind: "vm".into(), compliant: true },
                Resource { id: "b".into(), kind: "disk".into(), compliant: false },
            ];
            assert_eq!(evaluate_all("no-public-ip", &resources), 1);
        });

        let lines = sink.lock().unwrap().clone();
        assert!(lines.iter().any(|l| l.starts_with("DEBUG") && l.contains("starting evaluation")));
        assert!(lines.iter().any(|l| l.starts_with("WARN") && l.contains("failures=1")));
        assert!(lines.iter().any(|l| l.starts_with("INFO")));
    }

    #[test]
    fn the_env_filter_decides_what_survives() {
        let recorder = capture::Recorder::default();
        let sink = Arc::clone(&recorder.0);

        // Raise the floor to `warn`: the debug and info events vanish before
        // they cost anything, which is the point of level checks being
        // evaluated per callsite rather than per message.
        let subscriber = Registry::default()
            .with(recorder)
            .with(EnvFilter::new("warn"));

        tracing::subscriber::with_default(subscriber, || {
            let resources = vec![Resource {
                id: "b".into(),
                kind: "disk".into(),
                compliant: false,
            }];
            evaluate_all("no-public-ip", &resources);
        });

        let lines = sink.lock().unwrap().clone();
        assert_eq!(lines.len(), 1);
        assert!(lines[0].starts_with("WARN"));
    }

    #[test]
    fn init_can_only_succeed_once_per_process() {
        // The global subscriber is process-wide, so libraries must never call
        // `init`. Only the binary gets to choose. `try_init` returning an
        // error is how you detect a double install.
        let first = Registry::default().with(EnvFilter::new("error")).try_init();
        let second = Registry::default().with(EnvFilter::new("error")).try_init();
        assert!(first.is_ok() ^ second.is_ok() || second.is_err());
    }
}
