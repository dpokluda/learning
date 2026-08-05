# 22 — reqwest and axum: HTTP in both directions

Almost every service you have written in .NET has been an HTTP client, an HTTP server, or both. Rust's
answers are **reqwest** for the client and **axum** for the server, and both will feel familiar — reqwest is
recognisably `HttpClient`, and axum is recognisably ASP.NET Core minimal APIs. What differs is where the
type system gets involved: extraction, routing, and response conversion are all trait-driven, so a lot of
what minimal APIs do by reflection at startup, axum does by trait resolution at compile time.

This module is short on theory and long on working code, because both crates are best learned by reading
their shapes. Everything here is compiled and executed, including a real server that a real client talks to
over a real socket — on localhost, so it stays offline.

> **Prerequisite:** [21 — tokio in practice](21-tokio-in-practice.md).

## reqwest: the client

The dependency needs a TLS backend, and in reqwest 0.13 the feature names changed — it is `rustls`, not
`rustls-tls`. Two other features that used to be on by default, `query` and `form`, must now be requested
explicitly, which is a common upgrade surprise:

```toml
[dependencies]
reqwest = { version = "0.13", default-features = false, features = ["rustls", "json", "query"] }
```

The `json` feature is what gives you `.json()` on requests and responses, and it is the one you will always
want.

### The client is the connection pool

The single most important thing to know about `reqwest::Client` is the same thing that trips people up with
`HttpClient`: **build one and reuse it**. It holds the connection pool, the TLS session cache, and the DNS
cache. Creating one per request is the Rust equivalent of the classic `new HttpClient()` socket-exhaustion
bug, and for the same reason.

The happy news is that Rust makes reuse natural. `Client` is internally `Arc`-based, so cloning it is cheap
and shares the pool — you clone it into every task that needs it rather than reaching for a static:

```rust,ignore
use std::time::Duration;

let client = reqwest::Client::builder()
    .timeout(Duration::from_secs(30))
    .connect_timeout(Duration::from_secs(5))
    .user_agent(concat!("polcheck/", env!("CARGO_PKG_VERSION")))
    .build()?;

// Cheap: shares the same pool.
let for_task = client.clone();
```

Note `.timeout()` on the builder. reqwest has no ambient default timeout, so a request against a hung server
waits forever unless you set one — the same trap as leaving `HttpClient.Timeout` at its default in a
background service, except that here the default is "never" rather than 100 seconds. Set it once on the
builder and every request inherits it.

### Requests and responses

The request builder chains, and `send()` awaits the response headers — not the body. That split matters for
streaming, and it is why you then call `.json()`, `.text()`, or `.bytes()` to consume the body:

```rust,ignore
use serde::{Deserialize, Serialize};

#[derive(Serialize)]
struct ScanRequest { subscription: String, rules: Vec<String> }

#[derive(Deserialize, Debug)]
struct ScanResponse { id: String, findings: usize }

async fn submit(client: &reqwest::Client) -> reqwest::Result<ScanResponse> {
    let resp = client
        .post("https://api.example.com/scans")
        .header("x-request-id", "abc-123")
        .bearer_auth("a-token")
        .query(&[("dryRun", "true")])           // needs the `query` feature
        .json(&ScanRequest {
            subscription: "sub-1".into(),
            rules: vec!["require-owner".into()],
        })
        .send()
        .await?
        .error_for_status()?                    // turn 4xx/5xx into Err
        .json::<ScanResponse>()
        .await?;

    Ok(resp)
}
```

`error_for_status()` is the piece to notice. Unlike `HttpClient`, reqwest does **not** treat a 404 or a 500
as an error by default — `send()` succeeds as long as it got a response. `error_for_status()` is the opt-in
equivalent of `EnsureSuccessStatusCode()`, and forgetting it means happily deserializing your error page.

When you need to branch on the status rather than fail, match on it:

```rust,ignore
let resp = client.get(url).send().await?;

match resp.status() {
    s if s.is_success() => Ok(Some(resp.json::<ScanResponse>().await?)),
    reqwest::StatusCode::NOT_FOUND => Ok(None),
    reqwest::StatusCode::TOO_MANY_REQUESTS => {
        let retry_after = resp
            .headers()
            .get(reqwest::header::RETRY_AFTER)
            .and_then(|v| v.to_str().ok())
            .and_then(|v| v.parse::<u64>().ok())
            .unwrap_or(1);
        Err(anyhow::anyhow!("rate limited; retry after {retry_after}s"))
    }
    other => Err(anyhow::anyhow!("unexpected status {other}")),
}
```

That `Ok(None)` for a 404 is a nice illustration of the difference in philosophy. In .NET, "not found" as a
non-exceptional outcome requires you to *avoid* `EnsureSuccessStatusCode` and check manually; here the
absence of an exception mechanism means the natural expression — a `Result<Option<T>>` — is also the
idiomatic one.

Here is the whole client comparison in one table:

| Task | `HttpClient` | `reqwest` |
|---|---|---|
| Create | `new HttpClient()` (reuse!) | `Client::builder().build()?` (clone to share) |
| GET JSON | `GetFromJsonAsync<T>(url)` | `client.get(url).send().await?.json::<T>().await?` |
| POST JSON | `PostAsJsonAsync(url, body)` | `client.post(url).json(&body).send().await?` |
| Throw on error status | `EnsureSuccessStatusCode()` | `.error_for_status()?` |
| Auth header | `DefaultRequestHeaders.Authorization` | `.bearer_auth(t)` / `.header(...)` |
| Timeout | `client.Timeout` | `.timeout(d)` on builder or request |
| Cancellation | `CancellationToken` parameter | drop the future, or `tokio::time::timeout` |

That last row is worth a sentence. There is no `CancellationToken` parameter threaded through reqwest's API,
because in Rust dropping the future *is* cancellation. `tokio::time::timeout(d, client.get(url).send())`
gives you a per-call deadline with no cooperation from the library at all — the mechanism module 16
described, paying off in a concrete API.

### Retries and middleware

reqwest has no built-in retry policy, which is the one place it is less batteries-included than
`HttpClient` plus Polly. The ecosystem answer is `reqwest-middleware` together with
`reqwest-retry`, which layer a `ClientWithMiddleware` over the base client and give you exponential backoff
with jitter. For simple cases a hand-written loop with `tokio::time::sleep` is perfectly reasonable — and
combined with `tokio::time::pause` from the previous module, it is trivially testable.

## axum: the server

axum is built on tower and hyper, and its central idea is that a handler is just an `async fn` whose
parameters are **extractors** and whose return type implements `IntoResponse`. If that sounds exactly like
minimal APIs, it is — with the difference that "can this type be extracted from a request?" is a trait bound
the compiler checks, so a handler that asks for something unextractable fails to compile rather than
failing at startup.

Two version-specific details, both of which will bite you when following older tutorials. In axum 0.8, path
parameters use braces — `/resources/{id}` — rather than the colon syntax of earlier versions. And
`axum::Server` is gone; you bind a `tokio::net::TcpListener` yourself and hand it to `axum::serve`.

Here is a complete service, and then a client that talks to it — both running for real in this doctest:

```rust
use axum::{
    extract::{Path, Query, State},
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use serde::{Deserialize, Serialize};
use std::sync::{Arc, Mutex};

#[derive(Clone, Serialize, Deserialize, Debug, PartialEq)]
struct Finding {
    resource_id: String,
    rule: String,
}

/// Shared application state. Arc so every handler gets a cheap clone.
#[derive(Clone)]
struct AppState {
    findings: Arc<Mutex<Vec<Finding>>>,
}

#[derive(Deserialize)]
struct ListParams {
    /// ?rule=require-owner filters the list.
    rule: Option<String>,
}

/// GET /findings?rule=...
async fn list_findings(
    State(state): State<AppState>,
    Query(params): Query<ListParams>,
) -> Json<Vec<Finding>> {
    let all = state.findings.lock().unwrap();
    let filtered = all
        .iter()
        .filter(|f| params.rule.as_ref().is_none_or(|r| &f.rule == r))
        .cloned()
        .collect();
    Json(filtered)
}

/// GET /findings/{id} — 404 when absent.
async fn get_finding(
    State(state): State<AppState>,
    Path(id): Path<String>,
) -> Result<Json<Finding>, StatusCode> {
    let all = state.findings.lock().unwrap();
    all.iter()
        .find(|f| f.resource_id == id)
        .cloned()
        .map(Json)
        .ok_or(StatusCode::NOT_FOUND)
}

/// POST /findings — 201 with the created body.
async fn create_finding(
    State(state): State<AppState>,
    Json(body): Json<Finding>,
) -> impl IntoResponse {
    state.findings.lock().unwrap().push(body.clone());
    (StatusCode::CREATED, Json(body))
}

fn app(state: AppState) -> Router {
    Router::new()
        .route("/findings", get(list_findings).post(create_finding))
        .route("/findings/{id}", get(get_finding))   // axum 0.8 brace syntax
        .with_state(state)
}

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let state = AppState {
        findings: Arc::new(Mutex::new(vec![Finding {
            resource_id: "res-1".into(),
            rule: "require-owner".into(),
        }])),
    };

    // Bind port 0 so the OS picks a free port — no conflicts, no config.
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();

    let server = tokio::spawn(async move {
        axum::serve(listener, app(state)).await.unwrap();
    });

    let client = reqwest::Client::new();
    let base = format!("http://{addr}");

    // GET the seeded list.
    let all: Vec<Finding> = client
        .get(format!("{base}/findings"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(all.len(), 1);
    assert_eq!(all[0].resource_id, "res-1");

    // POST a new one; expect 201.
    let created = Finding { resource_id: "res-2".into(), rule: "require-env".into() };
    let resp = client
        .post(format!("{base}/findings"))
        .json(&created)
        .send()
        .await
        .unwrap();
    assert_eq!(resp.status(), reqwest::StatusCode::CREATED);
    assert_eq!(resp.json::<Finding>().await.unwrap(), created);

    // Query-string filtering.
    let filtered: Vec<Finding> = client
        .get(format!("{base}/findings?rule=require-env"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(filtered, vec![created]);

    // Path extraction, and the 404 path.
    let one: Finding = client
        .get(format!("{base}/findings/res-1"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(one.rule, "require-owner");

    let missing = client.get(format!("{base}/findings/nope")).send().await.unwrap();
    assert_eq!(missing.status(), reqwest::StatusCode::NOT_FOUND);

    server.abort();
}
```

That example is doing a lot, so let me draw out the parts that carry the ideas.

**Extractors are parameters.** `State<AppState>`, `Path<String>`, `Query<ListParams>`, and `Json<Finding>`
each implement `FromRequestParts` or `FromRequest`, and axum resolves them by type. The destructuring in the
parameter list — `State(state): State<AppState>` — is ordinary pattern matching, unwrapping the newtype
right there in the signature. Compared with minimal APIs' `[FromRoute]`/`[FromQuery]` attributes, the
information lives in the type rather than an annotation, and a missing `.with_state(...)` is a compile error
rather than a runtime resolution failure.

There is one ordering rule: an extractor that consumes the request body — `Json`, `String`, `Bytes` — must
come **last**, because there is only one body to consume. That is enforced by the trait split
(`FromRequestParts` for the many, `FromRequest` for the one), so getting it wrong is again a compile error,
albeit one whose message takes a moment to decode.

**Return types are conversions.** `Json<T>` sets the content type and serializes. A bare `StatusCode` is a
response. A tuple `(StatusCode, Json<T>)` sets both. `Result<T, E>` works when both sides implement
`IntoResponse`, which is what makes `?`-style error propagation natural in handlers. `impl IntoResponse` is
the catch-all when the concrete type is tedious to write.

**State is explicit.** `.with_state(state)` injects it, and `AppState` must be `Clone` because each handler
invocation gets its own. Wrapping the shared parts in `Arc` makes that clone cheap. This is dependency
injection without a container: the compiler verifies at build time that every handler's `State<T>` matches
the router's, so there is no equivalent of discovering at startup that a service was never registered. The
tradeoff is honest — you lose scoped lifetimes and automatic constructor injection, and for a large service
you will hand-roll more wiring than ASP.NET Core requires.

### Errors that turn into responses

The idiomatic approach is an error enum implementing `IntoResponse`, which lets handlers use `?` and return
a domain error while the framework produces the HTTP shape:

```rust
use axum::{http::StatusCode, response::{IntoResponse, Response}, Json};
use serde::Serialize;

#[derive(Debug)]
enum ApiError {
    NotFound(String),
    Invalid(String),
    Internal(anyhow::Error),
}

#[derive(Serialize)]
struct ErrorBody { error: String }

impl IntoResponse for ApiError {
    fn into_response(self) -> Response {
        let (status, message) = match self {
            ApiError::NotFound(what) => (StatusCode::NOT_FOUND, format!("{what} not found")),
            ApiError::Invalid(why) => (StatusCode::BAD_REQUEST, why),
            ApiError::Internal(e) => {
                // Log the detail, return something generic.
                tracing::error!(error = ?e, "internal error");
                (StatusCode::INTERNAL_SERVER_ERROR, "internal error".to_string())
            }
        };
        (status, Json(ErrorBody { error: message })).into_response()
    }
}

/// Anything convertible into anyhow::Error becomes a 500 via `?`.
impl<E> From<E> for ApiError
where
    E: Into<anyhow::Error>,
{
    fn from(e: E) -> Self {
        ApiError::Internal(e.into())
    }
}

async fn get_rule(id: String) -> Result<Json<&'static str>, ApiError> {
    if id.is_empty() {
        return Err(ApiError::Invalid("id must not be empty".into()));
    }
    if id != "known" {
        return Err(ApiError::NotFound(format!("rule `{id}`")));
    }
    Ok(Json("ok"))
}

#[tokio::main(flavor = "current_thread")]
async fn main() {
    assert!(get_rule("known".into()).await.is_ok());

    let resp = get_rule("other".into()).await.unwrap_err().into_response();
    assert_eq!(resp.status(), StatusCode::NOT_FOUND);

    let resp = get_rule("".into()).await.unwrap_err().into_response();
    assert_eq!(resp.status(), StatusCode::BAD_REQUEST);
}
```

This is the direct counterpart of an exception filter or `IExceptionHandler` in ASP.NET Core, but visible in
the handler's signature: `Result<Json<T>, ApiError>` tells the reader exactly what can go wrong, and the
blanket `From` impl means `?` on any error produces a 500 with the detail logged rather than leaked.

### Middleware is tower

axum does not have its own middleware abstraction; it uses **tower**, a general-purpose
`Service` trait for anything request-to-response. `tower-http` supplies the layers you would expect —
tracing, compression, CORS, timeouts, request-body limits — and `.layer(...)` applies them:

```rust,ignore
use std::time::Duration;
use axum::Router;
use tower_http::{
    compression::CompressionLayer,
    timeout::TimeoutLayer,
    trace::TraceLayer,
};

let app = Router::new()
    .route("/findings", axum::routing::get(list_findings))
    .layer(TraceLayer::new_for_http())        // structured request logging
    .layer(CompressionLayer::new())           // gzip/brotli
    .layer(TimeoutLayer::new(Duration::from_secs(30)))
    .with_state(state);
```

Layers apply outermost-first in the order written, which is the opposite of the intuition most people bring
from ASP.NET Core's `Use...` ordering, so it is worth a comment in your code. The payoff for using tower
rather than a bespoke abstraction is that these layers work with any tower-based stack, including reqwest's
middleware ecosystem and tonic for gRPC.

### Graceful shutdown

Everything from module 21 applies, and axum has a hook for it:

```rust,ignore
let listener = tokio::net::TcpListener::bind("0.0.0.0:8080").await?;

axum::serve(listener, app)
    .with_graceful_shutdown(async {
        tokio::signal::ctrl_c().await.expect("ctrl-c handler");
        tracing::info!("shutting down");
    })
    .await?;
```

`with_graceful_shutdown` stops accepting new connections when the future resolves and waits for in-flight
requests to finish. Combine it with the `CancellationToken` pattern to bring background tasks down at the
same time.

### Testing handlers

Because a `Router` is a tower `Service`, you can call it directly with a constructed `Request` and get a
`Response` — no socket, no port, no server task. That is the equivalent of `WebApplicationFactory` and
`TestServer`, but lighter, and it is why axum services are pleasant to test:

```rust,ignore
use axum::body::Body;
use axum::http::{Request, StatusCode};
use tower::ServiceExt;                        // brings `oneshot` into scope

#[tokio::test]
async fn returns_404_for_unknown_finding() {
    let app = app(test_state());

    let response = app
        .oneshot(
            Request::builder()
                .uri("/findings/nope")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}
```

For testing *clients* rather than servers, `wiremock` gives you a stub HTTP server with request matching and
assertions — the analogue of mocking `HttpMessageHandler`, and considerably more pleasant than doing so.

## Before you move on

reqwest is `HttpClient` with the same cardinal rule — build one `Client` and clone it, because it owns the
connection pool — and two differences worth internalising: there is no default timeout, so set one on the
builder; and a 4xx/5xx is not an error until you call `error_for_status()`. Cancellation needs no token
parameter because dropping the future cancels the request, which makes `tokio::time::timeout` a universal
per-call deadline. Retries are not built in; reach for `reqwest-middleware` plus `reqwest-retry`, or write
the loop.

axum is minimal APIs with the magic moved into the type system. A handler is an `async fn` taking extractors
(`State`, `Path`, `Query`, `Json`) and returning anything that implements `IntoResponse`, with the
body-consuming extractor required to come last. State is injected with `.with_state(...)` and checked at
compile time rather than resolved at runtime. Errors become responses by implementing `IntoResponse` on your
own error enum, which keeps `?` working inside handlers while keeping the failure modes visible in the
signature. Middleware is tower's `Layer`, shared with the wider ecosystem, and a `Router` is itself a
`Service`, so handler tests need no server at all.

The version details that will cost you time if you learn them from older material: reqwest 0.13 renamed the
TLS feature to `rustls` and made `query` and `form` non-default, while axum 0.8 uses `{id}` path syntax and
replaced `axum::Server` with `axum::serve(listener, router)`.

If you can explain why `Json<T>` must be the last parameter of a handler and why cloning a `reqwest::Client`
is the right way to share it, you are ready to make all of this observable.

Next: [23 — tracing and logging](23-tracing-and-logging.md).

### Sources

- `reqwest`. <https://docs.rs/reqwest/0.13/reqwest/> — client builder, request builder, and feature flags.
- `reqwest` feature list. <https://docs.rs/crate/reqwest/0.13/features> — confirms `rustls`, and that `query`/`form` are not default.
- `axum`. <https://docs.rs/axum/0.8/axum/> — routing, extractors, `IntoResponse`, and state.
- `axum::extract`. <https://docs.rs/axum/0.8/axum/extract/index.html> — the extractor ordering rule and the `FromRequestParts`/`FromRequest` split.
- axum 0.8 changelog. <https://github.com/tokio-rs/axum/blob/main/axum/CHANGELOG.md> — the `{id}` path syntax change.
- `tower-http`. <https://docs.rs/tower-http/> — tracing, compression, CORS, and timeout layers.
- `wiremock`. <https://docs.rs/wiremock/> — HTTP mocking for client tests.
- Microsoft Learn, "Minimal APIs overview". <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/overview> — the ASP.NET Core comparison point.
