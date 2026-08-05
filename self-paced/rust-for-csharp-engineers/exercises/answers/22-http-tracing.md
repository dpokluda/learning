# Answers 22 — axum, reqwest, and tracing

> Exercises: [22-http-tracing.md](../22-http-tracing.md)

## Part A

**A1. An axum handler declares what it needs in its parameter list. Explain the mechanism, and contrast it with ASP.NET Core's model binding.**

Every parameter type implements `FromRequestParts` or `FromRequest`, and axum's `Handler` trait is implemented generically for functions whose parameters all do. Asking for `Path<String>` means 'extract a path segment'; asking for `Json<T>` means 'read and deserialize the body'; asking for `State<S>` means 'give me the state I attached to the router'. The return type works the same way through `IntoResponse`. So binding is resolved at compile time by trait selection, and a handler that asks for something the route cannot supply is a type error — including the classic mistake of putting a body extractor anywhere but last, which is caught because `FromRequest` consumes the request. ASP.NET Core does the equivalent at runtime by convention and attributes, resolving services from a DI container. The tradeoff is real: axum gives you compile-time certainty and famously baroque error messages when a bound is unmet, while ASP.NET Core gives you a container and clearer diagnostics at the cost of discovering mistakes at startup or later.

**A2. `reqwest::Client` and `HttpClient` share a failure mode that has burned both ecosystems. What is it, and what is the correct usage?**

Constructing one per request. Both types own a connection pool, and creating a new one per call means a fresh TCP handshake and TLS negotiation every time, plus — in .NET's case — socket exhaustion, because disposed handlers leave sockets in `TIME_WAIT`. The correct usage in both is one long-lived instance shared across the application; `reqwest::Client` is internally an `Arc`, so cloning it is cheap and shares the pool, and passing `&Client` or a clone into every call site is idiomatic. The .NET answer is `IHttpClientFactory` or a static instance. The difference worth noting is that Rust's ownership rules make the sharing explicit — you must either clone or borrow, and the compiler will not let you accidentally keep a client alive past its owner — whereas in .NET the lifetime question is invisible until you profile.

**A3. reqwest does not treat a 404 as an error. Defend that decision, and describe the resulting code shape.**

A 404 is a completely successful HTTP exchange: the request was sent, the server understood it, and it answered. Treating it as an error conflates 'the network failed' with 'the resource does not exist', which are different problems with different responses — one is retryable, the other is not. So `send().await` returns `Ok` for any response the server produced, and only transport-level failures produce `Err`. The resulting shape is that you inspect `response.status()` explicitly, handling the statuses that carry meaning for your domain, and call `error_for_status()` to collapse the remaining non-success codes into an error. `HttpClient` behaves the same way — `EnsureSuccessStatusCode()` is opt-in for exactly this reason — but the .NET default of `GetFromJsonAsync` throwing on 404 has trained a lot of people to expect otherwise.

**A4. `tracing` calls itself structured and span-aware rather than a logging framework. What is the distinction, and what does `#[instrument]` do?**

A log record is a formatted string with a level. A tracing event is a set of typed key-value fields plus a message, recorded inside a *span* that has its own fields and a duration — so the output can be consumed as structured data, correlated by span, and exported as distributed traces rather than grepped. `#[instrument]` on a function opens a span for the duration of the call, records the arguments as fields by default, and closes it on return; `skip` omits arguments that are large or secret, and `fields(...)` adds computed ones. Every event emitted inside inherits the span's context, which is what `ILogger.BeginScope` gives you in .NET — except the fields are typed rather than an object bag, and the attribute writes the boilerplate. The closest whole-ecosystem comparison is Serilog plus OpenTelemetry, with `tracing-subscriber` playing the role of the sink configuration and `tracing-opentelemetry` the exporter.

**A5. Why must a library never call `tracing_subscriber::init()`, and what is the equivalent rule in .NET?**

The subscriber is a process-global singleton, installed once. If a library installs one, it silently dictates the output format and filtering for the entire application, and a second install fails — so two libraries that both do it produce a program that depends on link order. Libraries emit events and spans and say nothing about where they go; the binary chooses the subscriber, exactly once, in `main`. The .NET equivalent is the rule that a library takes an injected `ILogger<T>` and never constructs a `LoggerFactory`: configuration belongs to the composition root. `try_init` returning a `Result` rather than panicking is how you detect a double install, which is worth using in test helpers, where several tests may each want a subscriber and only the first can have one.

**A6. `EnvFilter` is not in `tracing-subscriber`'s default features. Why does that trip people up, and what does the filter actually do?**

It trips people up because every example on the internet uses `EnvFilter`, and adding `tracing-subscriber = "0.3"` without `features = ["env-filter"]` produces an unresolved-import error that looks like a version problem. What the filter does is decide, per callsite, whether an event or span is enabled — from a directive string like `warn,polcheck=debug,hyper=off` that scopes levels by module path, read from `RUST_LOG` by convention. The important property is that the decision is cached per callsite and checked before the event's fields are evaluated, so a `debug!` in a hot loop costs an atomic load and a branch when disabled, not a formatted string. That is the same design goal as `ILogger.IsEnabled`, achieved without asking the caller to write the guard.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` and `cargo clippy -- -D warnings` on the pinned toolchain.

```rust
//! Crate drill 22 — axum, reqwest and tracing: a service and a client.
//!
//! Everything here runs against a server the test starts on `127.0.0.1:0`, so
//! the whole chapter is offline. That is also good practice: an integration
//! test that reaches the public internet is a test that fails for reasons you
//! do not control.

use std::net::SocketAddr;
use std::sync::{Arc, Mutex};

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use tokio::net::TcpListener;
use tokio::task::JoinHandle;

/// Shared state. In ASP.NET Core this would be a singleton resolved from the DI
/// container; axum has no container, so you pass the state explicitly and the
/// `State` extractor pulls it out. Less magic, and the type checker enforces
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

/// A handler is just an async function. Its *parameters* declare what it needs
/// from the request, and the return type declares the response — the analogue
/// of ASP.NET Core minimal API handlers, with the binding resolved at compile
/// time rather than by convention.
async fn get_resource(
    State(state): State<AppState>,
    Path(id): Path<String>,
) -> Result<Json<Resource>, (StatusCode, Json<ApiError>)> {
    state.hits.lock().unwrap().push(format!("get:{id}"));

    if id == "missing" {
        return Err((
            StatusCode::NOT_FOUND,
            Json(ApiError {
                message: format!("no resource `{id}`"),
            }),
        ));
    }
    Ok(Json(Resource {
        id,
        kind: "vm".to_string(),
        compliant: true,
    }))
}

async fn list_resources(Query(params): Query<ListParams>) -> Json<Vec<Resource>> {
    let all = vec![
        Resource { id: "a".into(), kind: "vm".into(), compliant: true },
        Resource { id: "b".into(), kind: "disk".into(), compliant: false },
    ];
    let filtered = match params.kind {
        Some(kind) => all.into_iter().filter(|r| r.kind == kind).collect(),
        None => all,
    };
    Json(filtered)
}

/// `Json<T>` in argument position deserializes the body; the same type in
/// return position serializes it and sets the content type.
async fn evaluate(Json(resource): Json<Resource>) -> (StatusCode, Json<Resource>) {
    let verdict = Resource {
        compliant: resource.kind == "vm",
        ..resource
    };
    (StatusCode::CREATED, Json(verdict))
}

pub fn router(state: AppState) -> Router {
    Router::new()
        // Note axum 0.8's path syntax: braces, not the `:id` of earlier
        // versions. Material written against 0.7 will not compile.
        .route("/resources/{id}", get(get_resource))
        .route("/resources", get(list_resources))
        .route("/evaluate", post(evaluate))
        .with_state(state)
}

/// Bind to port zero and let the OS choose. Returning the real address is what
/// makes the test hermetic — no fixed port, so tests can run in parallel.
pub async fn start_server(state: AppState) -> (SocketAddr, JoinHandle<()>) {
    let listener = TcpListener::bind("127.0.0.1:0").await.expect("bind");
    let addr = listener.local_addr().expect("local_addr");

    let handle = tokio::spawn(async move {
        axum::serve(listener, router(state)).await.expect("serve");
    });
    (addr, handle)
}

/// The client side. `reqwest::Client` is `HttpClient`: expensive to build,
/// cheap to clone, holds the connection pool, and must be reused. Constructing
/// one per request is the same mistake in both ecosystems.
pub async fn fetch_resource(
    client: &reqwest::Client,
    base: &str,
    id: &str,
) -> Result<Resource, String> {
    let response = client
        .get(format!("{base}/resources/{id}"))
        .send()
        .await
        .map_err(|e| format!("transport: {e}"))?;

    // A 404 is a perfectly successful HTTP exchange, so it is *not* an error
    // here — unlike `EnsureSuccessStatusCode`, reqwest makes you ask.
    if response.status() == StatusCode::NOT_FOUND {
        let body: ApiError = response.json().await.map_err(|e| e.to_string())?;
        return Err(body.message);
    }

    response
        .error_for_status()
        .map_err(|e| format!("status: {e}"))?
        .json::<Resource>()
        .await
        .map_err(|e| format!("decode: {e}"))
}

/// A minimal `Layer`/`Subscriber` that records events into a vector, so the
/// tests can assert on what was logged without any output plumbing. In .NET you
/// would inject a fake `ILogger<T>`; the shape of the idea is identical.
pub mod capture {
    use std::sync::{Arc, Mutex};

    use tracing::field::{Field, Visit};
    use tracing_subscriber::Layer;
    use tracing_subscriber::layer::Context;

    #[derive(Clone, Default)]
    pub struct Recorder(pub Arc<Mutex<Vec<String>>>);

    struct MessageVisitor(String);

    impl Visit for MessageVisitor {
        fn record_debug(&mut self, field: &Field, value: &dyn std::fmt::Debug) {
            if field.name() == "message" {
                self.0 = format!("{value:?}");
            } else {
                self.0.push_str(&format!(" {}={:?}", field.name(), value));
            }
        }
    }

    impl<S: tracing::Subscriber> Layer<S> for Recorder {
        fn on_event(&self, event: &tracing::Event<'_>, _ctx: Context<'_, S>) {
            let mut visitor = MessageVisitor(String::new());
            event.record(&mut visitor);
            let level = *event.metadata().level();
            self.0
                .lock()
                .unwrap()
                .push(format!("{level}: {}", visitor.0));
        }
    }
}

/// Instrumented work. `#[instrument]` opens a span for the call, and every
/// event inside inherits its fields — the structured-logging scope that
/// `ILogger.BeginScope` gives you, except the fields are typed and the
/// attribute writes the boilerplate.
#[tracing::instrument(skip(resources), fields(count = resources.len()))]
pub fn evaluate_all(rule: &str, resources: &[Resource]) -> usize {
    tracing::debug!("starting evaluation");
    let failures = resources.iter().filter(|r| !r.compliant).count();
    if failures > 0 {
        tracing::warn!(failures, "non-compliant resources found");
    }
    tracing::info!("evaluation complete");
    failures
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
```
