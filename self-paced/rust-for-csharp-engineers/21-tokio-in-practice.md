# 21 — tokio in practice

Module 16 taught you what a `Future` is, why Rust ships no runtime, and how `async`/`.await` compiles into a
state machine. This module is about living with tokio in a real program: configuring the runtime,
doing I/O, coordinating tasks, shutting down cleanly, and avoiding the handful of mistakes that turn an async
service into a mysteriously stalled one.

The framing that helps most is this. In .NET, the runtime is ambient — the thread pool exists, `Task.Run`
works from anywhere, and you have probably never thought about who owns it. In Rust the runtime is a value
you construct, configure, and drop. That is more work and considerably more control, and it means the
questions .NET answers implicitly (how many threads? what happens to in-flight work at shutdown?) become
things you decide.

> **Prerequisite:** [16 — Async and tokio](16-async-and-tokio.md).

## Configuring the runtime

`#[tokio::main]` is a macro that builds a runtime, blocks on your `async fn main`, and shuts down. It is
convenient and it hides every decision, so it is worth seeing what it expands to:

```rust,ignore
#[tokio::main]
async fn main() {
    println!("hello");
}

// expands to roughly:
fn main() {
    tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .unwrap()
        .block_on(async {
            println!("hello");
        })
}
```

`new_multi_thread` gives you a work-stealing scheduler with one worker thread per CPU — the closest
analogue to .NET's thread pool. `enable_all` turns on the I/O and timer drivers, and forgetting it produces
one of tokio's most confusing errors: a panic saying "there is no reactor running" the first time you await
a socket or a `sleep`.

You can configure all of it. The `flavor` and `worker_threads` arguments on the macro cover the common
cases, and the builder covers the rest:

```rust
fn main() {
    // A current-thread runtime: one thread, no work stealing, no Send requirement
    // on spawned futures. Ideal for CLIs and tests.
    let rt = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .unwrap();

    let answer = rt.block_on(async {
        tokio::time::sleep(std::time::Duration::from_millis(1)).await;
        40 + 2
    });
    assert_eq!(answer, 42);
}
```

The choice between flavours matters more than it looks. **Multi-thread** is right for a server handling
concurrent independent requests. **Current-thread** is right for a CLI, for tests, and for anything where
the work is I/O-bound and sequential — it removes the `Send` bound on spawned futures, which eliminates a
whole class of compile errors, and it removes cross-thread scheduling overhead. For `polcheck`, a CLI that
fetches some resources and evaluates rules, current-thread is very likely the right default, and
`#[tokio::main(flavor = "current_thread")]` is how you ask for it.

Two builder options are worth knowing. `worker_threads(n)` caps the pool, which matters in a container with
a CPU limit, because tokio sizes the pool from the *host's* core count and will happily create 64 workers in
a cgroup allowed one core. And `thread_name` makes your stack dumps and `tracing` output legible.

Because a runtime is a value, you can also have more than one — a common pattern for isolating a
latency-sensitive workload from a bulk one, which in .NET would require custom `TaskScheduler` work most
people never attempt. Dropping the runtime blocks until its threads finish, which is why a runtime dropped
inside async code will deadlock; keep it at the edge of your program.

## Tasks

`tokio::spawn` submits a future to the runtime and returns a `JoinHandle<T>`, which is a future resolving to
the task's output. The mental model to carry over from .NET is close to `Task.Run`, with three differences
that matter.

A tokio task is **not** started by `spawn` in the sense of running immediately on this thread — it is queued.
A `JoinHandle` that you drop does **not** cancel the task; it detaches it, unlike dropping a plain future.
And joining returns a `Result` whose error tells you the task panicked or was aborted, which is how a
panicking task is surfaced rather than crashing the process.

```rust
#[tokio::main(flavor = "current_thread")]
async fn main() {
    let handle = tokio::spawn(async {
        tokio::time::sleep(std::time::Duration::from_millis(1)).await;
        "done"
    });

    // Awaiting a JoinHandle yields Result<T, JoinError>.
    assert_eq!(handle.await.unwrap(), "done");

    // A panicking task does not take the process down.
    let handle = tokio::spawn(async { panic!("boom") });
    let err = handle.await.unwrap_err();
    assert!(err.is_panic());

    // Abort is cooperative-at-await-points cancellation.
    let handle = tokio::spawn(async {
        tokio::time::sleep(std::time::Duration::from_secs(60)).await;
    });
    handle.abort();
    assert!(handle.await.unwrap_err().is_cancelled());
}
```

That panic behaviour deserves emphasis because it differs from what you might expect. An unobserved faulted
`Task` in .NET historically risked crashing the process at finalization; here the panic is captured in the
`JoinError` and the runtime keeps going. The risk is the mirror image: if you spawn and never join, a
panicking task fails silently. Either join your handles or log the result.

When you have many tasks, `JoinSet` is better than a `Vec<JoinHandle<_>>`. It lets you await completions in
the order they finish, and dropping it aborts everything still running — which is exactly the structured
concurrency guarantee you want:

```rust
use tokio::task::JoinSet;

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let mut set = JoinSet::new();

    for i in 0..5u32 {
        set.spawn(async move {
            tokio::time::sleep(std::time::Duration::from_millis((5 - i) as u64)).await;
            i * 10
        });
    }

    let mut results = Vec::new();
    while let Some(res) = set.join_next().await {
        results.push(res.unwrap());
    }

    results.sort_unstable();
    assert_eq!(results, vec![0, 10, 20, 30, 40]);
}
```

`JoinSet` is roughly `Task.WhenAll` when you drain it fully, and roughly a repeated `Task.WhenAny` when you
process results as they arrive — but with the added property that it owns its tasks, so an early return
cancels them instead of leaking work.

### Blocking work

This is the single most important operational rule in async Rust, and it has no real .NET equivalent because
.NET's thread pool grows when you block it. Tokio's does not.

A tokio worker thread runs many tasks cooperatively. If one task blocks the thread — a synchronous file
read, a `std::thread::sleep`, a CPU-bound loop, a blocking database driver — every other task assigned to
that worker stops. With one worker (current-thread flavour) the whole program stops.

`spawn_blocking` moves that work to a separate, elastic pool reserved for it:

```rust
fn expensive_hash(input: &str) -> u64 {
    // Stand-in for genuinely CPU-bound work.
    let mut h: u64 = 1469598103934665603;
    for b in input.bytes() {
        h ^= b as u64;
        h = h.wrapping_mul(1099511628211);
    }
    h
}

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let input = "res-1".to_string();

    // Runs on the blocking pool; the async worker stays free.
    let digest = tokio::task::spawn_blocking(move || expensive_hash(&input))
        .await
        .unwrap();

    assert_ne!(digest, 0);
}
```

The rule of thumb: anything that will occupy a thread for more than about 100 microseconds without awaiting
belongs in `spawn_blocking`. For genuinely parallel CPU work, combine it with rayon (module 15) —
`spawn_blocking` a closure that runs a rayon parallel iterator, so the async runtime stays responsive while
rayon saturates the cores.

## Time

`tokio::time` replaces `Task.Delay`, `CancellationTokenSource(TimeSpan)`, and `System.Threading.Timer` with
three primitives that compose better than any of them.

`sleep` yields for a duration. `timeout` wraps any future and cancels it at a deadline. `interval` ticks
repeatedly, and unlike a naive `loop { sleep }` it accounts for the time your work took, so ticks do not
drift:

```rust
use std::time::Duration;
use tokio::time::{interval, sleep, timeout, Instant};

#[tokio::main(flavor = "current_thread")]
async fn main() {
    // timeout returns Err(Elapsed) and drops the inner future.
    let slow = sleep(Duration::from_secs(60));
    assert!(timeout(Duration::from_millis(10), slow).await.is_err());

    // A fast future completes normally.
    let fast = async { 7 };
    assert_eq!(timeout(Duration::from_millis(50), fast).await.unwrap(), 7);

    // interval fires immediately, then on a fixed schedule.
    let start = Instant::now();
    let mut ticker = interval(Duration::from_millis(5));
    for _ in 0..3 {
        ticker.tick().await;
    }
    assert!(start.elapsed() >= Duration::from_millis(10));
}
```

The detail that catches people: the first `tick()` completes immediately, so a three-iteration loop waits
only two intervals. If you want to wait before the first iteration, call `tick()` once before the loop or
use `interval_at`.

There is also a genuinely excellent testing feature with no .NET counterpart short of injecting a clock
abstraction everywhere. `tokio::time::pause` freezes the clock and auto-advances it whenever every task is
idle, so a test of an hour-long backoff runs instantly and deterministically. It lives behind tokio's
`test-util` feature, which — despite the name — is **not** included in `full`, so you must ask for it:

```toml
[dev-dependencies]
tokio = { version = "1", features = ["macros", "rt", "test-util"] }
```

```rust
use std::time::Duration;
use tokio::time::{advance, pause, sleep, Instant};

#[tokio::main(flavor = "current_thread")]
async fn main() {
    pause();                                  // time is now virtual

    let start = Instant::now();
    sleep(Duration::from_secs(3600)).await;   // returns immediately
    assert!(start.elapsed() >= Duration::from_secs(3600));

    // You can also step the clock explicitly.
    let t = Instant::now();
    advance(Duration::from_secs(30)).await;
    assert!(t.elapsed() >= Duration::from_secs(30));
}
```

In a test you get this with `#[tokio::test(start_paused = true)]`. Retry logic, cache expiry, and rate
limiters become testable in milliseconds, and I would consider this reason enough on its own to prefer
`tokio::time::sleep` over any hand-rolled delay.

## Files and I/O

`tokio::fs` mirrors `std::fs` with async functions, and `tokio::io` provides `AsyncRead`/`AsyncWrite` along
with `AsyncReadExt`/`AsyncWriteExt` extension traits that supply the actual methods. Forgetting to import
those extension traits is a common early stumble — the methods simply do not exist until you do.

```rust
use tokio::io::AsyncWriteExt;

#[tokio::main(flavor = "current_thread")]
async fn main() -> std::io::Result<()> {
    let dir = std::env::temp_dir().join("polcheck-doc-demo");
    tokio::fs::create_dir_all(&dir).await?;
    let path = dir.join("rules.json");

    // One-shot helpers, same shape as std::fs.
    tokio::fs::write(&path, br#"{"rules":[]}"#).await?;
    let bytes = tokio::fs::read(&path).await?;
    assert_eq!(bytes, br#"{"rules":[]}"#);

    // Or a handle, with the AsyncWriteExt methods.
    let mut f = tokio::fs::File::create(&path).await?;
    f.write_all(b"{}").await?;
    f.flush().await?;
    drop(f);

    assert_eq!(tokio::fs::read_to_string(&path).await?, "{}");

    tokio::fs::remove_dir_all(&dir).await?;
    Ok(())
}
```

An honest caveat that tokio's own documentation makes: `tokio::fs` is not truly asynchronous. Operating
systems offer no portable async file API, so tokio implements these by dispatching to the blocking pool.
The benefit is that they do not stall a worker; the cost is a thread hop per call. For a program that reads
a config file once at startup, `std::fs` inside `spawn_blocking` — or even directly, before the runtime
starts — is simpler and no slower. Reach for `tokio::fs` when file work is interleaved with real async I/O.

Sockets are a different story: `tokio::net` is genuinely async, built on epoll/kqueue/IOCP, and is where the
runtime earns its keep.

## Coordinating tasks

tokio provides async-aware synchronisation primitives in `tokio::sync`. They differ from `std`'s in that
waiting on them yields to the runtime rather than blocking the thread.

The channels are the most useful, and the four flavours map onto distinct problems:

| Channel | Shape | .NET analogue |
|---|---|---|
| `mpsc` | many producers, one consumer, bounded or unbounded | `Channel<T>` |
| `oneshot` | one value, once | `TaskCompletionSource<T>` |
| `broadcast` | many producers, many consumers, each sees every message | event / `IObservable` |
| `watch` | many consumers see only the latest value | `BehaviorSubject` |

`mpsc` is the workhorse, and the bounded variant gives you backpressure for free — `send` awaits when the
buffer is full, which throttles producers instead of growing memory without limit:

```rust
use tokio::sync::mpsc;

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let (tx, mut rx) = mpsc::channel::<u32>(4);   // bounded: backpressure at 4

    let producer = tokio::spawn(async move {
        for i in 0..10 {
            // Awaits when the buffer is full.
            if tx.send(i).await.is_err() {
                break;                             // receiver dropped
            }
        }
        // tx dropped here, which closes the channel
    });

    let mut total = 0;
    // recv() returns None once all senders are dropped.
    while let Some(v) = rx.recv().await {
        total += v;
    }

    producer.await.unwrap();
    assert_eq!(total, 45);
}
```

The `while let Some(v) = rx.recv().await` loop terminating when all senders drop is the idiom to internalise;
it is how an async pipeline shuts down without an explicit sentinel message. If the loop hangs forever, the
cause is almost always a `tx` clone you forgot to drop.

`oneshot` handles request/response, which is how you build the actor pattern — a task owning some state,
receiving commands over an `mpsc`, and replying over a `oneshot` embedded in each command:

```rust
use tokio::sync::{mpsc, oneshot};

enum Command {
    Count { reply: oneshot::Sender<usize> },
    Add(String),
}

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let (tx, mut rx) = mpsc::channel::<Command>(8);

    // The actor owns the state; no locks anywhere.
    let actor = tokio::spawn(async move {
        let mut findings: Vec<String> = Vec::new();
        while let Some(cmd) = rx.recv().await {
            match cmd {
                Command::Add(f) => findings.push(f),
                Command::Count { reply } => {
                    let _ = reply.send(findings.len());
                }
            }
        }
    });

    tx.send(Command::Add("missing owner tag".into())).await.unwrap();
    tx.send(Command::Add("public ip attached".into())).await.unwrap();

    let (reply_tx, reply_rx) = oneshot::channel();
    tx.send(Command::Count { reply: reply_tx }).await.unwrap();
    assert_eq!(reply_rx.await.unwrap(), 2);

    drop(tx);
    actor.await.unwrap();
}
```

This is worth dwelling on, because it is the pattern that most often replaces `Arc<Mutex<T>>` in async Rust.
The state lives in one task, which owns it exclusively; concurrency is handled by the channel; there is no
lock to hold across an await and therefore no deadlock to design around. When you feel the urge to reach for
a shared mutex in async code, ask first whether an actor would be simpler.

When you do need shared state, `tokio::sync::Mutex` exists — but prefer `std::sync::Mutex` unless you must
hold the guard across an `.await`. The std mutex is faster and, crucially, its guard is not `Send`, so the
compiler stops you from holding it across an await point. That "limitation" is a feature: holding a lock
across an await is how async deadlocks are born.

## Graceful shutdown

Shutdown is where async services most often disappoint, and tokio gives you good tools if you plan for it.
The requirements are usually: notice a signal, stop accepting new work, let in-flight work finish within a
deadline, then exit.

Two primitives do most of the job. `CancellationToken` (from `tokio-util`, not tokio itself — a detail that
costs everyone one failed build) broadcasts "stop" to any number of tasks. A `watch` channel does the same
if you prefer to avoid the dependency.

```rust
use std::time::Duration;
use tokio::time::{sleep, timeout};
use tokio_util::sync::CancellationToken;

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let token = CancellationToken::new();
    let mut workers = tokio::task::JoinSet::new();

    for id in 0..3u32 {
        let token = token.clone();
        workers.spawn(async move {
            let mut processed = 0u32;
            loop {
                tokio::select! {
                    // Bias is not needed here, but note the cancellation arm
                    // comes first for readability.
                    _ = token.cancelled() => break,
                    _ = sleep(Duration::from_millis(1)) => processed += 1,
                }
            }
            (id, processed)
        });
    }

    // Let them work, then ask everyone to stop.
    sleep(Duration::from_millis(10)).await;
    token.cancel();

    // Wait for drain, but never forever.
    let drained = timeout(Duration::from_secs(5), async {
        let mut done = Vec::new();
        while let Some(r) = workers.join_next().await {
            done.push(r.unwrap());
        }
        done
    })
    .await
    .expect("workers should drain within the deadline");

    assert_eq!(drained.len(), 3);
}
```

The structure generalises: a token every task selects on, a `JoinSet` to await the drain, and a `timeout`
around the drain so a stuck task cannot hang your shutdown. That last part is the one people omit and then
regret at three in the morning.

In a real binary the trigger comes from the OS. `tokio::signal::ctrl_c()` is a future that resolves on
SIGINT and works on Windows too; on Unix, `signal::unix::signal(SignalKind::terminate())` gets you SIGTERM,
which is what container orchestrators actually send:

```rust,ignore
use tokio_util::sync::CancellationToken;

async fn shutdown_signal(token: CancellationToken) {
    let ctrl_c = async {
        tokio::signal::ctrl_c().await.expect("failed to install Ctrl-C handler");
    };

    #[cfg(unix)]
    let terminate = async {
        tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())
            .expect("failed to install SIGTERM handler")
            .recv()
            .await;
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => {},
        _ = terminate => {},
    }

    tracing::info!("shutdown signal received");
    token.cancel();
}
```

`std::future::pending()` as the non-Unix branch is a neat trick worth stealing: a future that never
completes, used to make a `select!` arm inert on platforms where it does not apply.

## Pitfalls

Four mistakes account for most of the async debugging I have seen, and all four are avoidable once named.

**Blocking a worker.** Already covered, and worth repeating because the symptom — everything mysteriously
stalls, sometimes only under load — is so unhelpful. If a task will not yield promptly, `spawn_blocking` it.
Tokio's `console` subscriber can find these for you when the cause is not obvious.

**Holding a lock across an await.** With `std::sync::Mutex` the compiler prevents it. With
`tokio::sync::Mutex` it compiles and then deadlocks under contention. Scope the guard so it drops before the
await, and if you cannot, reconsider whether an actor fits better.

**`select!` and cancellation safety.** `select!` drops the losing futures, which is exactly what you want for
timeouts and exactly what you do not want when the dropped future had consumed input it cannot un-consume.
A future is *cancellation safe* if dropping it mid-poll loses no data; `mpsc::Receiver::recv` and
`sleep` are, while a general "read a whole line" future may not be. The rule: in a `select!` loop, only await
things documented as cancellation safe, and if you must await something else, spawn it and select on its
`JoinHandle` instead. Each crate's docs state this explicitly — tokio is unusually good about it.

**Fire-and-forget spawning.** A `JoinHandle` dropped on the floor means a panic in that task vanishes.
Collect handles in a `JoinSet`, or at minimum log the join result.

One more, less a pitfall than a surprise: `async` blocks and functions are lazy. Calling an async function
does nothing until the future is awaited or spawned. In .NET an `async` method starts executing eagerly up
to its first real await, so the habit of calling now and awaiting later — `var t = FooAsync(); ...; await t;`
— does not carry over. In Rust that runs nothing until the `.await`; use `tokio::spawn` to get eager
execution.

## Before you move on

The runtime is a value you own. `#[tokio::main]` hides a `Builder::new_multi_thread().enable_all()`, and
knowing that is what lets you choose the current-thread flavour for CLIs and tests, cap `worker_threads` in a
container, or run more than one runtime for workload isolation. Tasks are spawned onto it with
`tokio::spawn`, which returns a `JoinHandle` that captures panics rather than crashing the process and
detaches rather than cancelling when dropped; `JoinSet` is the better choice for groups because it aborts
what it still owns.

The operational rule that matters most is that blocking a worker thread stalls every task sharing it, since
tokio's pool does not grow the way .NET's does — so synchronous or CPU-bound work goes through
`spawn_blocking`. From `tokio::time`, `sleep`, `timeout`, and `interval` cover the ground `Task.Delay` and
`System.Threading.Timer` do, with `pause`/`start_paused` giving you deterministic tests of time-dependent
logic that .NET can only match by abstracting the clock. `tokio::fs` is blocking work behind an async
façade, while `tokio::net` is genuinely async.

For coordination, prefer message passing to shared state: `mpsc` for pipelines with backpressure, `oneshot`
for replies, `watch` for latest-value fan-out, and the actor pattern — state owned by one task, commands in,
replies out — as the default answer to "where should this mutable state live?". Shutdown wants a
`CancellationToken` from `tokio-util`, a `select!` arm in every loop, a `JoinSet` to drain, and a `timeout`
around the drain.

If you can explain why holding a `tokio::sync::Mutex` guard across an `.await` compiles while a
`std::sync::Mutex` guard does not, and what "cancellation safe" means for an arm of a `select!` loop, you are
ready to put a network on the other end of all this.

Next: [22 — reqwest and axum](22-reqwest-and-axum.md).

### Sources

- *Tokio Tutorial*. <https://tokio.rs/tokio/tutorial> — runtime, tasks, channels, and the actor pattern.
- `tokio`. <https://docs.rs/tokio/1/tokio/> — the API reference, including per-method cancellation-safety notes.
- `tokio::runtime`. <https://docs.rs/tokio/1/tokio/runtime/index.html> — flavours, `Builder` options, and shutdown behaviour.
- `tokio::task::spawn_blocking`. <https://docs.rs/tokio/1/tokio/task/fn.spawn_blocking.html> — the blocking pool and when to use it.
- `tokio::fs`. <https://docs.rs/tokio/1/tokio/fs/index.html> — the documented caveat that file operations run on the blocking pool.
- `tokio::time`. <https://docs.rs/tokio/1/tokio/time/index.html> — `sleep`, `timeout`, `interval`, and clock pausing.
- `tokio::select!`. <https://docs.rs/tokio/1/tokio/macro.select.html> — branch semantics and cancellation safety.
- `tokio_util::sync::CancellationToken`. <https://docs.rs/tokio-util/0.7/tokio_util/sync/struct.CancellationToken.html> — hierarchical cancellation.
- *Tokio topics: graceful shutdown*. <https://tokio.rs/tokio/topics/shutdown> — the signal, notify, drain pattern.
