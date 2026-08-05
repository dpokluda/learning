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
