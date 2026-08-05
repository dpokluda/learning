# Answers 16 — Async and tokio

> Exercises: [16-async.md](../16-async.md)

## Part A

**A1. A .NET `Task` returned from an `async` method is already running. A Rust future returned from an `async fn` is not. Trace the consequences of that one difference.**

In .NET the state machine starts executing synchronously on the calling thread the moment you invoke the method, and only suspends at the first genuinely incomplete await; the `Task` you receive is a handle to work already in flight. In Rust an `async fn` is a *constructor*: calling it allocates a state machine in the `Init` state and returns it, having executed none of the body. Nothing happens until something calls `poll`, which in practice means until you `.await` it or hand it to an executor. Three consequences follow. First, `let f = do_work(); drop(f);` is a complete no-op in Rust and a fire-and-forget in .NET. Second, there is no such thing as an unobserved-task exception, because an un-awaited future never ran to produce one. Third, and most usefully, cancellation is free: dropping a future stops it dead at whatever await point it reached, with no cooperation from the code inside — which is why Rust needs no `CancellationToken` for the common case, whereas .NET does, because a running `Task` cannot be un-run.

**A2. `tokio::join!` and `tokio::spawn` both give you concurrency. What is the actual difference, and when does it matter?**

`join!` polls several futures from *one* task on *one* thread, interleaving them at await points. It is concurrency without parallelism, and it is what you want when the work is I/O-bound and the futures naturally cooperate. Because everything stays on one task, the futures may borrow from the enclosing scope and need not be `Send` — there is no `'static` requirement at all. `spawn` hands a future to the runtime as an independent task that the scheduler may run on any worker thread, so you get real parallelism, but you pay for it with `Send + 'static` bounds, exactly as `Task.Run` forces you to capture rather than borrow. The practical rule: `join!` for a fixed, small set of related awaits inside one logical operation; `spawn` when the work should proceed even if the caller stops awaiting, or when it is CPU-adjacent enough to want another core. `JoinSet` is `spawn` plus a tidy way to await a dynamic number of them, roughly `Task.WhenAll` over a list you built at runtime.

**A3. What happens to a panic inside a spawned tokio task, and how does that compare to an unhandled exception on a .NET thread-pool thread?**

It is caught at the task boundary and stored, so `handle.await` yields `Err(JoinError)` with `is_panic()` true. The process does not die and no other task is disturbed — the same shape as a faulted `Task` whose exception you observe by awaiting it. The trap is also the same: if you drop the `JoinHandle` and never await it, the panic vanishes silently, which is Rust's version of `async void`. The one difference worth knowing is that .NET's unobserved-task-exception machinery will at least raise `TaskScheduler.UnobservedTaskException` on finalization, whereas tokio gives you nothing; if you care, you await the handle or use a `JoinSet`, which surfaces every result. Note also that this only holds when panics unwind — under `panic = "abort"`, or inside an `extern "C"` function, the process really does die.

**A4. Explain why `tokio::select!` needs no `CancellationToken`, and what invariant that puts on the futures you write.**

`select!` polls its branches until one completes, then *drops* the rest. Dropping a future runs the destructors of everything it had live across its current await point and abandons the state machine; there is no cooperation required and nothing to poll again. That is why cancellation is instantaneous and universal in Rust while .NET needs a token threaded through every layer. The invariant it puts on you is cancel-safety: a future may be destroyed at any await point, so it must never hold a half-applied mutation across one. A future that pops an item off a queue, awaits a write, and then acknowledges will lose that item if the write is cancelled. The standard fix is to make the await-crossing step idempotent or to do the state change after the await rather than before — and tokio's own documentation labels which of its methods are cancel-safe precisely because this is easy to get wrong.

**A5. `std::sync::Mutex` and `tokio::sync::Mutex` both provide mutual exclusion. Give the rule for choosing, and the mechanical reason behind it.**

Use `std::sync::Mutex` unless you must hold the lock across an `.await`, in which case use tokio's. The mechanical reason is that `std::sync::MutexGuard` is not `Send`, so a future holding one across an await point is itself not `Send` and cannot be given to `tokio::spawn` — the compiler stops you, which is a rare case of the borrow checker enforcing an operational rule. Beyond compilability, the std mutex blocks the OS thread when contended, which on a runtime worker stalls every other task that worker was scheduled to poll; the async mutex instead parks the *task* and lets the worker move on. The cost is that the async mutex is substantially slower — it is a full async primitive with a wait queue, not a thin wrapper over a futex — so for the overwhelmingly common case of a short, non-awaiting critical section the std mutex is the right and faster answer. In .NET this distinction is `lock` versus `SemaphoreSlim.WaitAsync`, and the same reasoning applies.

**A6. Why must CPU-bound work go through `spawn_blocking`, and what is the .NET rule it corresponds to?**

A tokio worker thread runs a loop that polls ready tasks. A task that computes for fifty milliseconds without awaiting occupies that worker for fifty milliseconds, during which every other task assigned to it makes no progress — tail latency across the whole service degrades even though nothing is deadlocked. `spawn_blocking` moves the closure onto a separate, much larger pool sized for blocking work, and hands you back a future that resolves when it finishes. The .NET rule it corresponds to is the standing advice never to block a thread-pool thread — `Task.Result`, `Thread.Sleep`, synchronous I/O — because the pool grows only slowly and a starved pool manifests as mysterious latency. The difference is one of degree: .NET's pool injects new threads under starvation, so blocking is a performance bug; tokio's runtime has a fixed worker count by default, so blocking is closer to a correctness bug.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` and `cargo clippy -- -D warnings` on the pinned toolchain.

```rust
//! Crate drill 16 — async Rust and tokio.
//!
//! The mental shift from .NET: a Rust `Future` is inert. Calling an `async fn`
//! allocates a state machine and does *nothing*; the work starts when something
//! polls it. A .NET `Task` is hot — it is already running by the time you hold
//! it. Everything below follows from that one difference.

use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use tokio::sync::{Mutex, mpsc, oneshot};
use tokio::task::JoinSet;

/// Proof that futures are lazy. Building the future must not run the body; only
/// awaiting it may. In .NET the equivalent method would already have started.
///
/// Written the long way on purpose: `async fn` is sugar for exactly this, and
/// seeing the `impl Future` return type makes it obvious that the function
/// *returns a value* rather than starting work. clippy would rather you used
/// the sugar, which is normally right — but not when the desugaring is the
/// lesson.
#[allow(clippy::manual_async_fn)]
pub fn make_counter_future(counter: Arc<AtomicUsize>) -> impl Future<Output = usize> {
    async move { counter.fetch_add(1, Ordering::SeqCst) + 1 }
}

/// Concurrency without parallelism: `join!` polls both futures on *one* task,
/// interleaving them at await points. The .NET analogue is `Task.WhenAll`, but
/// note the difference — `WhenAll` waits on work already running on the thread
/// pool, whereas `join!` is what *causes* these two futures to make progress.
pub async fn fetch_both(a: Duration, b: Duration) -> (String, String) {
    let first = async {
        tokio::time::sleep(a).await;
        format!("a after {}ms", a.as_millis())
    };
    let second = async {
        tokio::time::sleep(b).await;
        format!("b after {}ms", b.as_millis())
    };
    tokio::join!(first, second)
}

/// Real parallelism needs `spawn`, which hands the future to the runtime as an
/// independent task. This is the closest thing to `Task.Run`, and it carries
/// the same `'static` requirement for the same reason: the task may outlive the
/// caller's stack frame.
pub async fn spawn_all(inputs: Vec<u64>) -> Vec<u64> {
    let mut set = JoinSet::new();
    for v in inputs {
        set.spawn(async move {
            tokio::time::sleep(Duration::from_millis(v)).await;
            v * 2
        });
    }
    let mut out = Vec::new();
    while let Some(res) = set.join_next().await {
        out.push(res.expect("task panicked"));
    }
    out.sort_unstable();
    out
}

/// A panic in a spawned task does not abort the process and does not surface
/// where it happened — it is captured and delivered through the `JoinHandle`,
/// exactly as a faulted `Task` carries its exception. Ignoring the handle
/// swallows the failure, which is the async equivalent of a fire-and-forget
/// `async void`.
pub async fn panic_is_captured_in_the_handle() -> bool {
    let handle = tokio::spawn(async { panic!("boom") });
    let joined = handle.await;
    joined.is_err()
}

/// `select!` races futures and *cancels the losers by dropping them*. That is
/// the deepest difference from .NET, where `Task.WhenAny` leaves the loser
/// running and you need a `CancellationToken` to stop it. Here cancellation is
/// simply "stop polling", which is why it is instant and needs no cooperation —
/// and also why a future must never hold an invariant across an await point
/// that a sudden drop would break.
pub async fn first_response(fast: Duration, slow: Duration) -> &'static str {
    tokio::select! {
        _ = tokio::time::sleep(fast) => "fast",
        _ = tokio::time::sleep(slow) => "slow",
    }
}

/// A timeout is `select!` against a sleep, packaged. On expiry the wrapped
/// future is dropped mid-flight.
pub async fn with_timeout(work: Duration, limit: Duration) -> Result<&'static str, ()> {
    tokio::time::timeout(limit, async move {
        tokio::time::sleep(work).await;
        "done"
    })
    .await
    .map_err(|_| ())
}

/// An async channel with backpressure: `send` on a bounded channel awaits when
/// the buffer is full, which is how you stop a fast producer from exhausting
/// memory. `Channel<T>` in .NET is the direct analogue and behaves the same way.
pub async fn producer_consumer(count: usize, buffer: usize) -> Vec<usize> {
    let (tx, mut rx) = mpsc::channel::<usize>(buffer);

    tokio::spawn(async move {
        for i in 0..count {
            // Awaits when the buffer is full; that await *is* the backpressure.
            if tx.send(i * i).await.is_err() {
                break;
            }
        }
        // Dropping `tx` here closes the channel and ends the loop below.
    });

    let mut out = Vec::new();
    while let Some(v) = rx.recv().await {
        out.push(v);
    }
    out
}

/// A `oneshot` is a single-value channel — the primitive behind
/// `TaskCompletionSource`.
pub async fn request_response() -> String {
    let (tx, rx) = oneshot::channel::<String>();
    tokio::spawn(async move {
        let _ = tx.send("computed".to_string());
    });
    rx.await.expect("sender dropped")
}

/// The async mutex is *not* a faster `std::sync::Mutex` — it is for holding a
/// lock across an `.await`, which the std guard cannot do because it is not
/// `Send`. If you are not awaiting while holding it, the std mutex is the right
/// choice and is considerably cheaper.
pub async fn shared_counter(tasks: usize) -> usize {
    let counter = Arc::new(Mutex::new(0usize));
    let mut set = JoinSet::new();

    for _ in 0..tasks {
        let counter = Arc::clone(&counter);
        set.spawn(async move {
            let mut guard = counter.lock().await;
            // Awaiting while holding the guard is the whole reason this type
            // exists. `std::sync::MutexGuard` would make the future non-Send.
            tokio::time::sleep(Duration::from_millis(1)).await;
            *guard += 1;
        });
    }
    while set.join_next().await.is_some() {}

    *counter.lock().await
}

/// CPU-bound work must not run on a runtime worker: while it spins, that worker
/// polls nothing else. `spawn_blocking` moves it to a dedicated pool, and is
/// the counterpart to the .NET advice never to block a thread-pool thread.
pub async fn offload_cpu_work(n: u64) -> u64 {
    tokio::task::spawn_blocking(move || (1..=n).product())
        .await
        .expect("blocking task panicked")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn a_future_does_nothing_until_awaited() {
        let counter = Arc::new(AtomicUsize::new(0));
        let fut = make_counter_future(Arc::clone(&counter));

        // Built but never polled: the body has not run.
        assert_eq!(counter.load(Ordering::SeqCst), 0);

        assert_eq!(fut.await, 1);
        assert_eq!(counter.load(Ordering::SeqCst), 1);
    }

    #[tokio::test(start_paused = true)]
    async fn join_runs_futures_concurrently_on_one_task() {
        // With time paused, tokio auto-advances the clock when every task is
        // idle, so this asserts the *logical* schedule, not the wall clock.
        let start = tokio::time::Instant::now();
        let (a, b) = fetch_both(Duration::from_secs(3), Duration::from_secs(5)).await;

        assert_eq!(a, "a after 3000ms");
        assert_eq!(b, "b after 5000ms");
        // Concurrent, so the total is the max rather than the sum.
        assert_eq!(start.elapsed(), Duration::from_secs(5));
    }

    #[tokio::test(start_paused = true)]
    async fn spawned_tasks_make_progress_independently() {
        assert_eq!(spawn_all(vec![30, 10, 20]).await, vec![20, 40, 60]);
    }

    #[tokio::test]
    async fn a_panicking_task_faults_its_handle_rather_than_the_process() {
        assert!(panic_is_captured_in_the_handle().await);
    }

    #[tokio::test(start_paused = true)]
    async fn select_returns_the_winner_and_drops_the_loser() {
        let winner =
            first_response(Duration::from_millis(10), Duration::from_secs(60)).await;
        assert_eq!(winner, "fast");
    }

    #[tokio::test(start_paused = true)]
    async fn timeout_is_select_against_a_sleep() {
        assert_eq!(
            with_timeout(Duration::from_millis(10), Duration::from_secs(1)).await,
            Ok("done")
        );
        assert_eq!(
            with_timeout(Duration::from_secs(10), Duration::from_millis(1)).await,
            Err(())
        );
    }

    #[tokio::test]
    async fn a_bounded_channel_applies_backpressure() {
        // A buffer of 1 with 5 items forces the producer to await repeatedly.
        assert_eq!(producer_consumer(5, 1).await, vec![0, 1, 4, 9, 16]);
    }

    #[tokio::test]
    async fn oneshot_is_a_task_completion_source() {
        assert_eq!(request_response().await, "computed");
    }

    #[tokio::test]
    async fn the_async_mutex_may_be_held_across_an_await() {
        assert_eq!(shared_counter(8).await, 8);
    }

    #[tokio::test]
    async fn blocking_work_belongs_on_the_blocking_pool() {
        assert_eq!(offload_cpu_work(10).await, 3_628_800);
    }
}
```
