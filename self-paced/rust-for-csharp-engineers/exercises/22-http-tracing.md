# Exercises 22 — axum, reqwest, and tracing

> **Covers:** [22 — axum, reqwest, and tracing](../22-reqwest-and-axum.md). **Code:** `crate-drills/src/ch22.rs`. **Answers:** [answers/22-http-tracing.md](answers/22-http-tracing.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** An axum handler declares what it needs in its parameter list. Explain the mechanism, and contrast it with ASP.NET Core's model binding.

**A2.** `reqwest::Client` and `HttpClient` share a failure mode that has burned both ecosystems. What is it, and what is the correct usage?

**A3.** reqwest does not treat a 404 as an error. Defend that decision, and describe the resulting code shape.

**A4.** `tracing` calls itself structured and span-aware rather than a logging framework. What is the distinction, and what does `#[instrument]` do?

**A5.** Why must a library never call `tracing_subscriber::init()`, and what is the equivalent rule in .NET?

**A6.** `EnvFilter` is not in `tracing-subscriber`'s default features. Why does that trip people up, and what does the filter actually do?

## Part B — Exercise

Open `crate-drills/src/ch22.rs`. This drill builds both halves of an HTTP
interaction and then instruments them, all against a server the tests start on
`127.0.0.1:0` — so nothing here touches the network, and the tests can run in
parallel because the OS assigns each one a free port.

On the server side you write three handlers and the router that wires them
together. The handlers show off axum's binding model: one takes `State` and a
`Path` segment and returns either a `Json` body or a status-plus-body error tuple;
one takes deserialized `Query` parameters; one takes a `Json` body and returns
`201`. Note the route syntax — axum 0.8 uses `/resources/{id}`, and any material
written for 0.7 will use `:id` and simply not compile.

On the client side you write `fetch_resource`, which must classify four different
outcomes: a transport failure, a `404` carrying a structured error body, any other
non-success status, and a decode failure. That a 404 is not automatically an error
is the point — it was a perfectly successful exchange, and only your domain knows
what it means.

The last third is `tracing`. You implement a tiny `Layer` that records events into
a vector, which is the moral equivalent of injecting a fake `ILogger<T>`, then
instrument a function so that its events carry span context. Two tests then prove
the things people most often get wrong about tracing: that `EnvFilter` really does
suppress events below its level before they cost anything, and that the global
subscriber can only be installed once per process — which is why libraries must
never install one.

Run it with `cargo test ch22` from the `exercises/crate-drills` directory.

### Starter stub

```rust,ignore
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
```

The test module that follows this in the file is the specification — read it before you write anything.
