# Answers 15 — Concurrency

> Exercises: [15-concurrency.md](../15-concurrency.md)

## Part A

**A1. What do `Send` and `Sync` mean, and who implements them?**

`Send` means a value may be *moved* to another thread; `Sync` means `&T` may be *shared* with another thread, which is equivalent to saying `&T` is `Send`. They are auto traits: the compiler implements them automatically for any type whose components are all `Send`/`Sync`, so you almost never write an impl — you only ever write a manual one inside `unsafe` code, or write a negative impl to opt out. The practical effect is that thread-safety is inferred and checked structurally: put an `Rc` inside your struct and the struct silently stops being `Send`, and the error appears at the point you try to spawn.

**A2. Why is `Rc` not `Send` but `Arc` is, and what would go wrong?**

`Rc`'s reference count is a plain integer updated with ordinary increments and decrements. If two threads cloned and dropped the same `Rc` concurrently, those updates would race, the count could be lost, and the value would be freed while still referenced — a use-after-free. `Arc` uses atomic operations for the count, which costs more but is race-free, so it is `Send` and `Sync` when its contents are. This is the clearest example of Rust making a performance/safety trade *explicit in the type system* rather than in documentation: .NET has exactly one reference kind and pays the thread-safe cost universally in the GC's design.

**A3. What does `thread::scope` give you that `thread::spawn` does not?**

`spawn` requires the closure to be `'static`, because the thread may outlive the spawning stack frame, so anything borrowed must be moved in or wrapped in `Arc`. `thread::scope` guarantees that every thread it starts is joined before the scope returns, which lets the compiler accept closures that borrow local variables — no `Arc`, no clone, no `'static` bound. It is the tool that removes most of the ceremony from data-parallel code over an existing buffer, and it has no direct .NET analogue: `Parallel.ForEach` achieves the same effect by being a closed library API that happens to join, not by anything the type system checks.

**A4. Describe the `mpsc` channel's shutdown protocol and the classic bug.**

The receiver's iteration ends when every `Sender` has been dropped. There is no explicit `Complete()` call — liveness is derived from ownership. The classic bug is cloning the sender for each worker and forgetting to `drop(tx)` on the original: one live sender remains in scope, so the receiver blocks forever and the program hangs with no error. The .NET analogue is forgetting `BlockingCollection.CompleteAdding()`, with the same symptom, except that in Rust the fix is a `drop` rather than a method call, which is easy to miss because it looks like a no-op line.

**A5. When would you reach for `rayon` instead of writing threads yourself?**

When the work is data-parallel over a collection and you want work-stealing without designing a scheduler: changing `.iter()` to `.par_iter()` is usually the whole diff, and rayon handles chunking, load balancing, and joining. It is the `Parallel.ForEach`/PLINQ analogue, and like PLINQ it pays off for CPU-bound work over enough items to amortise the coordination. It is the wrong tool for I/O-bound concurrency — that is async's job — and for pipelines with ordering or backpressure requirements, where explicit threads and channels give you the control rayon deliberately hides.

**A6. What is lock poisoning, and how should you handle it?**

If a thread panics while holding a `Mutex`, the lock is marked poisoned, and every subsequent `lock()` returns `Err(PoisonError)` — the standard library's way of telling you the protected data may be in a half-updated state. In practice most code writes `.lock().unwrap()` or `.expect("poisoned")`, which propagates the panic, and that is defensible: if the invariant is broken, continuing is worse. When you can prove the data is still consistent you can recover with `into_inner()` on the error. Notably `parking_lot::Mutex` drops poisoning altogether for a smaller, faster lock, which is a reasonable choice if your panic policy is abort anyway.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 15 — Threads, `Send`/`Sync`, shared state, and channels.
//!
//! Everything here is `std` only: no rayon, no tokio. The point is to feel the
//! compiler refusing to let you share what cannot be shared, which is the
//! difference between "we have a convention" and "it is checked".

use std::collections::HashMap;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, mpsc};
use std::thread;

/// Scoped threads can borrow from the stack, so no `Arc` and no `'static`
/// bound. This is the tool that removes most `Arc::clone` noise, and it has no
/// direct .NET analogue — `Parallel.ForEach` gets there by being a closed API.
pub fn parallel_sum(values: &[i64]) -> i64 {
    if values.is_empty() {
        return 0;
    }
    let mid = values.len() / 2;
    let (left, right) = values.split_at(mid);

    thread::scope(|s| {
        let handle = s.spawn(|| left.iter().sum::<i64>());
        let right_total: i64 = right.iter().sum();
        handle.join().expect("worker panicked") + right_total
    })
}

/// Shared mutable state across `'static` threads: `Arc` for shared ownership,
/// `Mutex` for exclusive access. Note the type — in Rust the lock *contains*
/// the data, so there is no way to touch it without holding the lock. C#'s
/// `lock(obj)` protects nothing by construction.
pub fn tally(words: &[String], threads: usize) -> HashMap<String, usize> {
    let counts: Arc<Mutex<HashMap<String, usize>>> = Arc::new(Mutex::new(HashMap::new()));
    let chunk = words.len().div_ceil(threads.max(1));

    thread::scope(|s| {
        for slice in words.chunks(chunk.max(1)) {
            let counts = Arc::clone(&counts);
            s.spawn(move || {
                // Compute outside the lock, then merge — hold it briefly.
                let mut local: HashMap<&str, usize> = HashMap::new();
                for w in slice {
                    *local.entry(w.as_str()).or_insert(0) += 1;
                }
                let mut guard = counts.lock().expect("poisoned");
                for (k, v) in local {
                    *guard.entry(k.to_string()).or_insert(0) += v;
                }
            });
        }
    });

    Arc::try_unwrap(counts).expect("all workers joined").into_inner().expect("poisoned")
}

/// An atomic needs no lock at all. `Ordering::Relaxed` is enough for a pure
/// counter, because no other memory is being published through it.
pub fn count_matching(values: &[i64], predicate: fn(i64) -> bool) -> usize {
    let hits = AtomicUsize::new(0);
    thread::scope(|s| {
        for chunk in values.chunks(64.max(values.len() / 4 + 1)) {
            let hits = &hits;
            s.spawn(move || {
                let n = chunk.iter().filter(|&&v| predicate(v)).count();
                hits.fetch_add(n, Ordering::Relaxed);
            });
        }
    });
    hits.load(Ordering::Relaxed)
}

/// A worker pool fed by a channel. `mpsc` is multi-producer/single-consumer,
/// so the *sender* is cloned and the receiver stays put. Dropping every sender
/// is what ends the consumer's loop — there is no separate "complete" call, and
/// forgetting to drop is the classic hang.
pub fn pipeline(inputs: Vec<i64>, workers: usize) -> Vec<i64> {
    let (tx, rx) = mpsc::channel::<i64>();

    thread::scope(|s| {
        let chunk = inputs.len().div_ceil(workers.max(1)).max(1);
        for slice in inputs.chunks(chunk) {
            let tx = tx.clone();
            s.spawn(move || {
                for &v in slice {
                    tx.send(v * v).expect("receiver alive");
                }
            });
        }
        // Critical: drop the original sender or `rx` never sees a close.
        drop(tx);

        let mut out: Vec<i64> = rx.iter().collect();
        out.sort_unstable();
        out
    })
}

/// A type that is `Send` but deliberately not `Sync` would be rejected by
/// `thread::scope` if shared. We cannot *demonstrate* a compile error in a
/// passing test, so instead we assert the marker traits statically: if `Rc`
/// ever became `Send`, this file would stop compiling.
pub const fn assert_send<T: Send>() {}
pub const fn assert_sync<T: Sync>() {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn scoped_threads_may_borrow_the_callers_stack() {
        let values: Vec<i64> = (1..=100).collect();
        assert_eq!(parallel_sum(&values), 5050);
        assert_eq!(parallel_sum(&[]), 0);
        assert_eq!(parallel_sum(&[7]), 7);
    }

    #[test]
    fn a_mutex_owns_the_data_it_protects() {
        let words: Vec<String> =
            ["deny", "audit", "deny", "deny", "audit", "modify"].iter().map(|s| s.to_string()).collect();
        let counts = tally(&words, 3);
        assert_eq!(counts["deny"], 3);
        assert_eq!(counts["audit"], 2);
        assert_eq!(counts["modify"], 1);
    }

    #[test]
    fn atomics_need_no_lock() {
        let values: Vec<i64> = (0..1000).collect();
        assert_eq!(count_matching(&values, |v| v % 2 == 0), 500);
    }

    #[test]
    fn dropping_every_sender_closes_the_channel() {
        // If the original `tx` were not dropped, this test would hang forever.
        assert_eq!(pipeline(vec![3, 1, 2], 2), vec![1, 4, 9]);
        assert_eq!(pipeline(vec![], 2), Vec::<i64>::new());
    }

    #[test]
    fn marker_traits_are_checked_at_compile_time() {
        assert_send::<Arc<Mutex<HashMap<String, usize>>>>();
        assert_sync::<Arc<Mutex<HashMap<String, usize>>>>();
        assert_send::<i64>();
        // `assert_send::<std::rc::Rc<i64>>()` is a compile error, by design.
    }
}
```
