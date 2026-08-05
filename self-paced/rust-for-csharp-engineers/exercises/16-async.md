# Exercises 16 — Async and tokio

> **Covers:** [16 — Async and tokio](../16-async-and-tokio.md). **Code:** `crate-drills/src/ch16.rs`. **Answers:** [answers/16-async.md](answers/16-async.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** A .NET `Task` returned from an `async` method is already running. A Rust future returned from an `async fn` is not. Trace the consequences of that one difference.

**A2.** `tokio::join!` and `tokio::spawn` both give you concurrency. What is the actual difference, and when does it matter?

**A3.** What happens to a panic inside a spawned tokio task, and how does that compare to an unhandled exception on a .NET thread-pool thread?

**A4.** Explain why `tokio::select!` needs no `CancellationToken`, and what invariant that puts on the futures you write.

**A5.** `std::sync::Mutex` and `tokio::sync::Mutex` both provide mutual exclusion. Give the rule for choosing, and the mechanical reason behind it.

**A6.** Why must CPU-bound work go through `spawn_blocking`, and what is the .NET rule it corresponds to?

## Part B — Exercise

Open `crate-drills/src/ch16.rs`. This drill is about the two facts that reorganise
everything you know about asynchrony when you come from .NET: futures are inert
until polled, and cancellation is a drop.

You start by writing a function that *returns* a future which increments a
counter — the test asserts the counter is still zero after you build it, so any
implementation that does the work eagerly fails immediately. From there you work
through the vocabulary: `join!` for concurrency on one task, `spawn` and
`JoinSet` for genuine parallelism, `select!` for racing, `timeout` for bounding,
`mpsc` for streaming with backpressure, and `oneshot` for the
`TaskCompletionSource` shape.

Three of the drills are about hazards rather than features. One proves that a
panic in a spawned task lands in the `JoinHandle` rather than killing the
process. One holds a `tokio::sync::Mutex` guard across an `.await`, which is the
only reason that type exists — try it with `std::sync::Mutex` afterwards and read
the error, because that error is the lesson. The last moves a CPU-bound product
onto the blocking pool.

Several tests use `#[tokio::test(start_paused = true)]`, which pauses the clock
and auto-advances it whenever every task is idle. That is how a test can assert
that two five-second sleeps ran concurrently without taking five seconds — and it
needs tokio's `test-util` feature, which is *not* included in `full`.

Run it with `cargo test ch16` from the `exercises/crate-drills` directory.

### Starter stub

```rust,ignore
//! Crate drill 16 — async Rust and tokio.
//!
//! The mental shift from .NET: a Rust `Future` is inert. Calling an `async fn`
//! allocates a state machine and does *nothing*; the work starts when something
//! polls it. A .NET `Task` is hot — it is already running by the time you hold
//! it. Every drill below follows from that one difference.

// Every import below is used by the finished implementations; until you write
// them the compiler would nag about each one, so the noise is silenced here.
#![allow(unused_imports)]

use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use tokio::sync::{Mutex, mpsc, oneshot};
use tokio::task::JoinSet;

/// Return a future that, **when awaited**, increments `counter` and yields the
/// new value. The test asserts the counter is still zero after you build the
/// future, so the increment must live inside the future's body.
///
/// Written as `-> impl Future` rather than `async fn` on purpose: seeing the
/// return type makes it obvious the function returns a *value*.
#[allow(clippy::manual_async_fn)]
pub fn make_counter_future(_counter: Arc<AtomicUsize>) -> impl Future<Output = usize> {
    async move { todo!("increment the counter and return the new value") }
}

/// Sleep for `a` and for `b` **concurrently** on a single task, returning
/// `("a after {millis}ms", "b after {millis}ms")`. Use `tokio::join!`. The test
/// runs with a paused clock and asserts the elapsed time is the maximum of the
/// two, not the sum — so sequential awaits will fail it.
pub async fn fetch_both(_a: Duration, _b: Duration) -> (String, String) {
    todo!("await both futures concurrently with tokio::join!")
}

/// Spawn one task per input, each sleeping `v` milliseconds and producing
/// `v * 2`. Collect the results and return them **sorted ascending**. A
/// `JoinSet` is the ergonomic way to do this; `res.expect("task panicked")`
/// unwraps the `JoinError`.
pub async fn spawn_all(_inputs: Vec<u64>) -> Vec<u64> {
    todo!("spawn a task per input and gather the doubled results")
}

/// Spawn a task that panics, await its handle, and return whether the join
/// result was an error. The point is that the panic does not take down the
/// process — it is captured and delivered through the handle, exactly as a
/// faulted `Task` carries its exception.
pub async fn panic_is_captured_in_the_handle() -> bool {
    todo!("spawn a panicking task and inspect the JoinHandle result")
}

/// Race two sleeps with `tokio::select!` and return `"fast"` or `"slow"`
/// depending on which completed first. The loser is *dropped*, which is how
/// cancellation works in Rust — no `CancellationToken` required.
pub async fn first_response(_fast: Duration, _slow: Duration) -> &'static str {
    todo!("race the two sleeps with tokio::select!")
}

/// Run a future that sleeps for `work`, bounded by `limit`. Return `Ok("done")`
/// if it finished in time and `Err(())` if it did not. `tokio::time::timeout`
/// does the work; map its error.
pub async fn with_timeout(_work: Duration, _limit: Duration) -> Result<&'static str, ()> {
    todo!("wrap the work in tokio::time::timeout")
}

/// Build a bounded `mpsc` channel with capacity `buffer`, spawn a producer that
/// sends `i * i` for `i` in `0..count`, and drain the receiver into a `Vec`.
/// The producer must drop its sender when done, or the consumer loop never
/// ends.
pub async fn producer_consumer(_count: usize, _buffer: usize) -> Vec<usize> {
    todo!("wire up a bounded channel and drain it")
}

/// Use a `oneshot` channel — the primitive behind `TaskCompletionSource` — to
/// send `"computed"` from a spawned task back to the caller.
pub async fn request_response() -> String {
    todo!("send a value through a oneshot channel and await it")
}

/// Share a counter across `tasks` spawned tasks, each of which locks it,
/// **awaits a 1ms sleep while holding the guard**, then increments. Return the
/// final value. The await-while-locked is the whole reason `tokio::sync::Mutex`
/// exists: a `std::sync::MutexGuard` is not `Send`, so the future would not be
/// spawnable.
pub async fn shared_counter(_tasks: usize) -> usize {
    todo!("share a tokio Mutex across tasks and increment across an await")
}

/// Compute `n!` on the blocking pool with `tokio::task::spawn_blocking`, so the
/// CPU work never occupies a runtime worker. This is the counterpart to the
/// .NET rule about never blocking a thread-pool thread.
pub async fn offload_cpu_work(_n: u64) -> u64 {
    todo!("move the product onto the blocking pool")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
