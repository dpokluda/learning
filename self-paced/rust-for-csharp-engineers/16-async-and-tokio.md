# 16 — Async Rust and tokio

You know async/await better than most, so this module can move fast on syntax and spend its pages on the
differences — and the differences are larger than the identical keywords suggest. Both languages compile
`async fn` into a state machine. After that they diverge: .NET's `Task` is hot, self-driving, and backed by
a thread pool that is part of the runtime; Rust's `Future` is cold, inert, and does nothing at all until
something polls it — and that something is not in the standard library. You choose it, add it to
`Cargo.toml`, and start it yourself.

That one architectural decision explains almost every surprise you will hit.

> **Prerequisite:** [15 — Concurrency: threads, channels, and data parallelism](15-concurrency.md).

## Futures are cold

Here is the whole difference in six lines.

```csharp
// C#: calling the method starts the work immediately.
Task<int> t = ComputeAsync();   // already running on the thread pool
int result = await t;           // wait for something already in flight
```

```rust
async fn compute() -> i32 { 42 }

fn main() {
    // Calling it does NOTHING. `fut` is an inert value describing work.
    let fut = compute();
    // Without an executor, `fut` is simply dropped and the body never runs.
    drop(fut);
}
```

A .NET `Task` is *hot*: by the time you hold it, the work has been scheduled. A Rust `Future` is *cold*: it
is a value describing work that has not begun. It advances only when someone calls `poll` on it, which
happens when you `.await` it inside another future, or when a runtime drives it as a task.

The compiler will warn you about this — futures are `#[must_use]` — but the mental model is what matters.
A dropped future is cancelled work that never started. A dropped `Task` is work that runs to completion
regardless, possibly with an unobserved exception.

The consequence you will feel most often is that **`.await` is not "wait for a running thing", it is "drive
this thing to completion"**. Sequential awaits are therefore genuinely sequential:

```rust
use std::time::Duration;

async fn fetch(name: &str) -> String {
    tokio::time::sleep(Duration::from_millis(10)).await;
    format!("data:{name}")
}

#[tokio::main]
async fn main() {
    // Sequential: 20ms total. In C#, starting both then awaiting would be 10ms.
    let a = fetch("a").await;
    let b = fetch("b").await;
    assert_eq!(a, "data:a");
    assert_eq!(b, "data:b");

    // Concurrent: 10ms. `join!` polls both futures on this one task.
    let (a, b) = tokio::join!(fetch("a"), fetch("b"));
    assert_eq!((a.as_str(), b.as_str()), ("data:a", "data:b"));
}
```

In C#, `var ta = FetchAsync("a"); var tb = FetchAsync("b"); await ta; await tb;` is concurrent, because both
tasks started when you called the methods. Writing the equivalent in Rust gives you sequential execution,
because neither future does anything until awaited. **You must explicitly ask for concurrency** with
`join!`, `select!`, `spawn`, or `FuturesUnordered`. This is the single most common source of
"why is my Rust async code not faster?".

## The runtime is not included

`std` defines the `Future` trait, the `Waker` machinery, and the `async`/`.await` syntax — and nothing
else. There is no executor, no timer, no async file or socket API. Those live in a runtime crate.

That looks like an omission and it is a deliberate design decision. Rust targets embedded systems with no
heap, kernels with no threads, and WebAssembly with no sockets alongside cloud servers with 64 cores. A
single built-in runtime would either be wrong for most of those or so configurable as to be a runtime
zoo anyway. So `std` ships the *contract* and the ecosystem ships implementations:

**tokio** is the de facto standard, with by far the largest ecosystem — axum, hyper, reqwest, tonic, sqlx,
and most async crates depend on it. Unless you have a specific reason, use tokio. **smol** and
**async-std** are the notable alternatives; async-std is in maintenance mode and its docs now point users
towards smol, so treat it as legacy. **embassy** targets embedded. **glommio** and **monoio** are
thread-per-core designs for very high-throughput I/O.

The practical fallout is that async crates are usually tied to a runtime, and mixing runtimes in one binary
is a good way to get "there is no reactor running" panics at runtime. Check what your dependencies expect.

## Getting started with tokio

```toml
[dependencies]
tokio = { version = "1.53.1", features = ["full"] }
```

`features = ["full"]` is fine while learning; production crates trim it (`rt-multi-thread`, `macros`,
`net`, `time`, `fs`, `sync`) to cut compile time.

```rust
use std::time::Duration;

#[tokio::main]
async fn main() {
    tokio::time::sleep(Duration::from_millis(1)).await;
    println!("done");
}
```

`#[tokio::main]` is a macro that rewrites your `async fn main` into a synchronous `main` that builds a
runtime and blocks on the future. It expands to roughly this, which is worth seeing once because it
demystifies the whole thing:

```rust
fn main() {
    tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .unwrap()
        .block_on(async {
            tokio::time::sleep(std::time::Duration::from_millis(1)).await;
        });
}
```

`block_on` is the bridge from sync to async — the `.GetAwaiter().GetResult()` of Rust, except here it is
the normal, correct way to start, not a deadlock-prone last resort. Building the runtime explicitly is what
you do when you want to configure it:

```rust
fn main() {
    let rt = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(4)
        .thread_name("polcheck-worker")
        .enable_all()                       // timers and I/O drivers
        .build()
        .expect("runtime");

    let result: u32 = rt.block_on(async { 1 + 1 });
    assert_eq!(result, 2);

    // A single-threaded runtime: no work stealing, futures need not be Send.
    let local = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .unwrap();
    assert_eq!(local.block_on(async { 40 + 2 }), 42);
}
```

`new_multi_thread` is a work-stealing pool much like the .NET thread pool. `new_current_thread` runs
everything on the calling thread, which is useful for tests, for CLI tools, and for futures that are not
`Send`.

## Tasks

`tokio::spawn` takes a future and hands it to the runtime as an independent task. **This is the closest
thing to `Task.Run`**, and it is where futures become hot:

```rust
#[tokio::main]
async fn main() {
    let handle = tokio::spawn(async {
        // This starts running as soon as the scheduler gets to it.
        (1..=10).sum::<u32>()
    });

    // JoinHandle is a future; awaiting it gives Result<T, JoinError>.
    let total = handle.await.expect("task did not panic");
    assert_eq!(total, 55);

    // Many tasks at once.
    let handles: Vec<_> = (0..4u32).map(|i| tokio::spawn(async move { i * i })).collect();
    let mut results = Vec::new();
    for h in handles {
        results.push(h.await.unwrap());
    }
    assert_eq!(results, vec![0, 1, 4, 9]);
}
```

Three differences from `Task.Run` that will catch you.

**`JoinHandle` returns `Result`.** A panicking task produces `Err(JoinError)` rather than propagating an
exception. Unlike .NET there is no "unobserved task exception" event — a spawned task whose handle you drop
panics silently into the void, so log it or keep the handle.

**The future must be `Send + 'static`.** Same rule as `thread::spawn`, same reason: a task can migrate
between worker threads. This is where the `Send` bound errors in async Rust come from, discussed below.

**Dropping the `JoinHandle` detaches the task** — it keeps running. To *cancel* it you call
`handle.abort()`, which is the next topic.

`JoinSet` is the ergonomic way to manage a dynamic group, replacing the `List<Task>` + `Task.WhenAll`
pattern with something that yields results as they finish:

```rust
use tokio::task::JoinSet;

#[tokio::main]
async fn main() {
    let mut set = JoinSet::new();
    for i in 0..5u32 {
        set.spawn(async move { i * 2 });
    }

    let mut out = Vec::new();
    while let Some(res) = set.join_next().await {
        out.push(res.expect("no panic"));
    }
    out.sort();
    assert_eq!(out, vec![0, 2, 4, 6, 8]);
}
```

`JoinSet` also aborts every remaining task when dropped, which makes structured shutdown much easier than
the .NET equivalent of tracking tasks and hoping.

## Cancellation is dropping

This is the deepest difference and the one most worth internalising.

In .NET, cancellation is cooperative and explicit: you pass a `CancellationToken` down the call chain, and
each layer checks it. Nothing stops a running task; you ask politely and it complies if it was written to.

In Rust, **dropping a future cancels it**. At the next `.await` point the future simply stops being polled,
its stack of local state unwinds through `Drop`, and the work is gone. No token, no cooperation, no check.

```rust
use std::time::Duration;
use tokio::time::{sleep, timeout};

#[tokio::main]
async fn main() {
    // timeout drops the inner future when the deadline passes.
    let slow = async {
        sleep(Duration::from_secs(60)).await;
        "never"
    };
    let result = timeout(Duration::from_millis(20), slow).await;
    assert!(result.is_err());               // Elapsed: the future was dropped mid-flight

    // select! runs futures concurrently and drops the losers.
    let winner = tokio::select! {
        _ = sleep(Duration::from_millis(50)) => "slow",
        _ = sleep(Duration::from_millis(5))  => "fast",
    };
    assert_eq!(winner, "fast");
}
```

`timeout` is `Task.WaitAsync(TimeSpan)` except that the inner work genuinely stops rather than continuing
to run unobserved. `select!` is `Task.WhenAny` except that the losers are cancelled rather than left
running. Both are strictly better defaults.

The price is **cancellation safety**, a concept with no .NET counterpart. Because a future can be dropped
at *any* `.await` point, a future that holds partial state across an await may lose it. The classic hazard
is a `select!` branch that reads from a stream:

```rust,ignore
// DANGEROUS: if the other branch wins, the partially-read buffer is lost.
loop {
    tokio::select! {
        result = socket.read_partial_into(&mut buf) => { /* ... */ }
        _ = shutdown.recv() => break,
    }
}
```

The rule is that only *cancel-safe* operations belong directly in a `select!` branch, and every tokio API
documents whether it is. `tokio::sync::mpsc::Receiver::recv` is cancel safe; `AsyncReadExt::read` into a
caller-owned buffer is not, in the sense that data already read is not lost but the operation may be
mid-flight. When in doubt, `spawn` the work as a task and `select!` on the `JoinHandle`, because a task is
not dropped by a losing `select!` branch — only aborted deliberately.

For explicit, propagating cancellation there is `CancellationToken`, which lives in **`tokio-util`** (not
`tokio`) behind the `rt` feature and is a near-exact port of the .NET type:

```rust
use std::time::Duration;
use tokio_util::sync::CancellationToken;

#[tokio::main]
async fn main() {
    let token = CancellationToken::new();
    let child = token.child_token();          // linked, like CreateLinkedTokenSource

    let worker = tokio::spawn(async move {
        let mut ticks = 0u32;
        loop {
            tokio::select! {
                _ = child.cancelled() => break,
                _ = tokio::time::sleep(Duration::from_millis(1)) => ticks += 1,
            }
        }
        ticks
    });

    tokio::time::sleep(Duration::from_millis(15)).await;
    token.cancel();                            // cancels the whole tree

    let ticks = worker.await.unwrap();
    assert!(ticks > 0);
}
```

## `Send` bounds and the `!Send` trap

The error that stops every newcomer at least once:

```rust,ignore
use std::rc::Rc;

#[tokio::main]
async fn main() {
    tokio::spawn(async {
        let data = Rc::new(5);
        tokio::task::yield_now().await;      // Rc is held across an await point
        println!("{data}");
    });
}
// error: future cannot be sent between threads safely
// note: `Rc<i32>` is not `Send`
```

Because a task may migrate between worker threads at any `.await`, everything alive **across** an await
point must be `Send`. Note the emphasis: a non-`Send` value created and dropped between two awaits is fine;
it is holding it across one that fails.

The same applies to lock guards, and this one is a genuine bug rather than a mere inconvenience:

```rust,ignore
use std::sync::Mutex;
let m = Mutex::new(0);
let guard = m.lock().unwrap();
some_async_op().await;                 // holding a std MutexGuard across await: bad
*guard += 1;
```

That will not compile inside `tokio::spawn` (`MutexGuard` is not `Send`), and even in a single-threaded
runtime it is a deadlock waiting to happen, because another task on the same thread may try to take the
lock while this one is suspended. The fixes, in order of preference: restructure so the guard's scope ends
before the await; or use `tokio::sync::Mutex`, whose `lock().await` is designed to be held across awaits at
the cost of being slower.

```rust
use std::sync::Arc;
use tokio::sync::Mutex as AsyncMutex;

#[tokio::main]
async fn main() {
    // Preferred: keep the sync lock's scope tight.
    let counter = Arc::new(std::sync::Mutex::new(0u32));
    {
        let mut g = counter.lock().unwrap();
        *g += 1;
    }                                        // guard dropped before any await
    tokio::task::yield_now().await;
    assert_eq!(*counter.lock().unwrap(), 1);

    // When you genuinely must hold it across an await:
    let shared = Arc::new(AsyncMutex::new(Vec::<u32>::new()));
    let mut g = shared.lock().await;
    tokio::task::yield_now().await;          // fine: tokio's guard is Send
    g.push(1);
    drop(g);
    assert_eq!(shared.lock().await.len(), 1);
}
```

The rule of thumb: **use `std::sync::Mutex` in async code by default**, and only reach for
`tokio::sync::Mutex` when the critical section must span an `.await`.

## No `SynchronizationContext`, no `ConfigureAwait`

A short section that will save you time: tokio has nothing corresponding to
`SynchronizationContext`/`TaskScheduler.Current`, so there is no context capture, no
`ConfigureAwait(false)`, and no `.Result` deadlock. Awaiting does not "return to the UI thread" because
there is no such concept. Everything behaves as if you had written `ConfigureAwait(false)` everywhere,
which is what library authors want anyway.

The related absence is `ValueTask`. Rust's futures are already zero-allocation by construction — an `async
fn` compiles to an anonymous state machine struct stored inline in its parent — so there is no allocation
to avoid. Boxing (`Pin<Box<dyn Future>>`) is opt-in, for trait objects and recursion, and is the thing you
occasionally add rather than the thing you optimise away.

## Async in traits

Async methods in traits (stable since Rust 1.75) work, with one caveat you will hit as soon as you try to
use them dynamically:

```rust
trait Fetcher {
    async fn fetch(&self, id: &str) -> String;
}

struct Fake;

impl Fetcher for Fake {
    async fn fetch(&self, id: &str) -> String {
        format!("fake:{id}")
    }
}

async fn use_it<F: Fetcher>(f: &F) -> String {
    f.fetch("res-1").await
}

#[tokio::main]
async fn main() {
    assert_eq!(use_it(&Fake).await, "fake:res-1");
}
```

That works for static dispatch. For `dyn Fetcher` it does not, because the returned future's type differs
per implementation and has no fixed size. The library-quality answer is to return a boxed future
explicitly, which is what the `async-trait` crate did before native support and what you still write by
hand when you need trait objects:

```rust
use std::future::Future;
use std::pin::Pin;

type BoxFuture<'a, T> = Pin<Box<dyn Future<Output = T> + Send + 'a>>;

trait Fetcher: Send + Sync {
    fn fetch<'a>(&'a self, id: &'a str) -> BoxFuture<'a, String>;
}

struct Fake;
impl Fetcher for Fake {
    fn fetch<'a>(&'a self, id: &'a str) -> BoxFuture<'a, String> {
        Box::pin(async move { format!("fake:{id}") })
    }
}

#[tokio::main]
async fn main() {
    let f: Box<dyn Fetcher> = Box::new(Fake);
    assert_eq!(f.fetch("res-1").await, "fake:res-1");
}
```

`Pin<Box<dyn Future>>` is the price of dynamic dispatch, and it is one allocation per call. C# pays a
similar cost invisibly for every non-`ValueTask` async method, so this is not a regression — it is just
visible.

## Pinning, briefly

`Pin` will appear in your error messages, so here is the minimum. An `async` block compiles to a state
machine struct that may contain references *into itself* — a local borrow held across an await becomes a
field pointing at another field. Such a **self-referential** struct becomes unsound if it is moved, because
the internal pointer would dangle.

`Pin<P>` is the type-level promise "this will not move again". `Future::poll` takes `Pin<&mut Self>`, which
is why you cannot poll a future you merely own. In practice you almost never touch this: `.await` handles
pinning, `Box::pin` pins to the heap, and `tokio::pin!` pins to the stack when you need to `select!` on the
same future repeatedly.

```rust
use std::time::Duration;

#[tokio::main]
async fn main() {
    let sleep = tokio::time::sleep(Duration::from_millis(50));
    tokio::pin!(sleep);                       // pin it so we can poll it in a loop

    let mut ticks = 0;
    loop {
        tokio::select! {
            _ = &mut sleep => break,          // needs Pin to be polled by reference
            _ = tokio::time::sleep(Duration::from_millis(5)) => ticks += 1,
        }
    }
    assert!(ticks > 0);
}
```

The other place it surfaces is recursion: an `async fn` that awaits itself has infinite size, and the fix is
`Box::pin` around the recursive call — exactly the `Box` trick from module 12 applied to futures.

```rust
use std::future::Future;
use std::pin::Pin;

fn countdown(n: u32) -> Pin<Box<dyn Future<Output = u32> + Send>> {
    Box::pin(async move {
        if n == 0 { return 0; }
        countdown(n - 1).await + 1
    })
}

#[tokio::main]
async fn main() {
    assert_eq!(countdown(5).await, 5);
}
```

## Blocking in async code

The cardinal sin, identical in both ecosystems and worth restating because the failure mode is worse in
Rust. A tokio worker thread runs many tasks; blocking it — `std::thread::sleep`, a synchronous file read, a
CPU-bound loop, `block_on` — stalls every task assigned to that thread. In .NET the thread-pool injection
heuristic eventually adds threads and papers over it; tokio has a fixed pool and will simply stall.

```rust
use std::time::Duration;

#[tokio::main]
async fn main() {
    // Wrong: std::thread::sleep blocks the worker.
    // std::thread::sleep(Duration::from_secs(1));

    // Right: the async sleep yields.
    tokio::time::sleep(Duration::from_millis(1)).await;

    // For genuinely blocking work, move it to the blocking pool.
    let result = tokio::task::spawn_blocking(|| {
        std::thread::sleep(Duration::from_millis(5));   // fine here
        (1..=1000u64).sum::<u64>()
    })
    .await
    .expect("blocking task");
    assert_eq!(result, 500_500);
}
```

`spawn_blocking` runs the closure on a separate, larger, dynamically-sized pool reserved for blocking work,
and gives you a `JoinHandle` to await. It is the tool for synchronous database drivers, CPU-heavy
computation, and any `std::fs` call in a hot path. There is no .NET equivalent because the .NET thread pool
serves both roles — which is exactly why blocking it is a subtler and more insidious problem there.

## `polcheck`: an async fetch-and-scan

The engine again, this time fetching resource inventories concurrently from several sources with a timeout,
a concurrency limit, and graceful shutdown. This is the shape module 21 and the capstone build on.

```rust
use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{mpsc, Semaphore};
use tokio::task::JoinSet;

#[derive(Debug, Clone)]
pub struct Resource {
    pub id: String,
    pub tags: HashMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub resource_id: String,
    pub reason: String,
}

/// Stands in for an HTTP call in module 22.
async fn fetch_page(page: u32) -> Vec<Resource> {
    tokio::time::sleep(Duration::from_millis(2)).await;
    (0..3)
        .map(|i| Resource {
            id: format!("res-{page}-{i}"),
            tags: if i == 0 {
                HashMap::from([("owner".to_owned(), "platform".to_owned())])
            } else {
                HashMap::new()
            },
        })
        .collect()
}

fn check(r: &Resource) -> Option<Finding> {
    (!r.tags.contains_key("owner")).then(|| Finding {
        resource_id: r.id.clone(),
        reason: "missing tag 'owner'".to_owned(),
    })
}

#[tokio::main]
async fn main() {
    // Bound concurrency: at most 4 fetches in flight, like SemaphoreSlim.
    let permits = Arc::new(Semaphore::new(4));
    let (tx, mut rx) = mpsc::channel::<Finding>(64);

    // A consumer task drains findings while producers are still running.
    let collector = tokio::spawn(async move {
        let mut found = Vec::new();
        while let Some(f) = rx.recv().await {
            found.push(f);
        }
        found
    });

    let mut set = JoinSet::new();
    for page in 0..10u32 {
        let permits = Arc::clone(&permits);
        let tx = tx.clone();
        set.spawn(async move {
            let _permit = permits.acquire_owned().await.expect("semaphore open");
            // Give up on a slow page rather than hanging the whole scan.
            let resources = tokio::time::timeout(Duration::from_secs(5), fetch_page(page))
                .await
                .unwrap_or_default();
            for r in &resources {
                if let Some(f) = check(r) {
                    if tx.send(f).await.is_err() {
                        break;                     // collector gone: stop early
                    }
                }
            }
            resources.len()
        });
    }
    drop(tx);                                       // close the channel when producers finish

    let mut scanned = 0usize;
    while let Some(res) = set.join_next().await {
        scanned += res.expect("task did not panic");
    }

    let findings = collector.await.expect("collector did not panic");
    assert_eq!(scanned, 30);
    assert_eq!(findings.len(), 20);                 // 2 of every 3 lack the owner tag
    assert!(findings.iter().all(|f| f.reason.contains("owner")));
}
```

Every mechanism in that program has a .NET counterpart — `Semaphore` is `SemaphoreSlim`, `mpsc::channel` is
`Channel.CreateBounded`, `JoinSet` is a `List<Task>` with `WhenAny`, `timeout` is `WaitAsync` — and each one
behaves slightly better: the timeout genuinely stops the work, the channel's capacity applies real
backpressure through `send().await`, and `JoinSet` aborts stragglers if it is dropped. Note the
`drop(tx)` again: the same "close the channel by dropping the last sender" discipline as module 15.

## Before you move on

The syntax is familiar, so spend your attention on the semantics. A Rust future is **cold** — calling an
`async fn` allocates nothing and starts nothing, and concurrency must be requested explicitly with `join!`,
`select!`, `spawn`, or `JoinSet`; two sequential awaits are sequential, unlike the C# habit of starting
tasks then awaiting them. The runtime is **not in std**, so you choose one (tokio, in practice), configure
it, and start it, with `#[tokio::main]` being a thin wrapper over `Builder` plus `block_on`.

Cancellation is **dropping**, which makes `timeout` and `select!` genuinely stop work rather than abandoning
it, and introduces cancellation safety as a property you must check for anything you put in a `select!`
branch. `tokio_util::sync::CancellationToken` is there when you want .NET-style propagating cancellation.

Everything held across an `.await` in a spawned task must be `Send`, which is where the `Rc` and
`MutexGuard` errors come from; keep sync lock scopes tight and reach for `tokio::sync::Mutex` only when the
critical section must span an await. There is no `SynchronizationContext`, no `ConfigureAwait`, and no
`ValueTask`, because none of the problems they solve exist. `Pin` is machinery you mostly do not touch,
appearing when you `select!` on a future by reference or write a recursive `async fn`.

Finally: **threads remain the simpler default.** Async in Rust costs you a runtime dependency, a
function-colouring split, `Send` bounds, and cancellation-safety reasoning. Take that on when you have
thousands of concurrent I/O operations, and use threads and rayon when you do not.

If you can explain why `let a = f(); let b = g(); a.await; b.await;` is not concurrent, what happens to the
inner future when a `timeout` elapses, and why holding a `std::sync::MutexGuard` across an await fails to
compile, you understand async Rust better than most people writing it.

Next: [17 — Unsafe Rust, FFI, and calling Rust from .NET](17-unsafe-ffi-interop.md).

### Sources

- *The Async Book* (Asynchronous Programming in Rust). <https://rust-lang.github.io/async-book/> — futures, the `Future` trait, executors, pinning, and cancellation.
- `std::future::Future`. <https://doc.rust-lang.org/std/future/trait.Future.html> — `poll`, `Pin<&mut Self>`, and the `Waker` contract.
- *Tokio tutorial*. <https://tokio.rs/tokio/tutorial> — runtime setup, tasks, channels, `select!`, and shared state.
- `tokio::select!`. <https://docs.rs/tokio/latest/tokio/macro.select.html> — the cancellation-safety discussion and the list of cancel-safe APIs.
- *Tokio docs*, `spawn_blocking` and "CPU-bound tasks and blocking code". <https://docs.rs/tokio/latest/tokio/task/fn.spawn_blocking.html> — why blocking a worker is fatal and what the blocking pool is for.
- `tokio::sync::Mutex`. <https://docs.rs/tokio/latest/tokio/sync/struct.Mutex.html> — explicit guidance that `std::sync::Mutex` is usually the right choice.
- Rust Blog, "Announcing `async fn` and return-position `impl Trait` in traits" (Rust 1.75). <https://blog.rust-lang.org/2023/12/21/async-fn-rpit-in-traits.html> — what native async traits do and do not support.
- `tokio_util::sync::CancellationToken`. <https://docs.rs/tokio-util/latest/tokio_util/sync/struct.CancellationToken.html> — child tokens and cancellation trees.
- *async-std* crate page. <https://crates.io/crates/async-std> — the maintenance notice directing users to smol.
