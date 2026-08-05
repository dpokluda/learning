# Exercises 15 — Concurrency

> **Covers:** [15 — Concurrency](../15-concurrency.md). **Code:** `drills/src/ch15.rs`. **Answers:** [answers/15-concurrency.md](answers/15-concurrency.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** What do `Send` and `Sync` mean, and who implements them?

**A2.** Why is `Rc` not `Send` but `Arc` is, and what would go wrong?

**A3.** What does `thread::scope` give you that `thread::spawn` does not?

**A4.** Describe the `mpsc` channel's shutdown protocol and the classic bug.

**A5.** When would you reach for `rayon` instead of writing threads yourself?

**A6.** What is lock poisoning, and how should you handle it?

## Part B — Exercise

Open `drills/src/ch15.rs`. The goal is to write four shapes of concurrent code
with nothing but the standard library, and to notice what the compiler refuses
along the way.

`parallel_sum` uses `thread::scope` so the workers can borrow the caller's slice
directly — no `Arc`, no clone, no `'static`. `tally` needs shared mutable state,
so it uses `Arc<Mutex<_>>`, and you should notice that the lock *contains* the
map, so there is no way to touch it without holding the lock. `count_matching`
needs no lock at all. And `pipeline` is the one that will hang if you get it
wrong: the receiver's loop ends only when every sender has been dropped, and you
are holding one.

The final test uses two `const fn` assertions to prove `Send` and `Sync` are
checked statically. Try adding `assert_send::<std::rc::Rc<i64>>()` to it and
read the error — that is the compiler telling you at build time what .NET leaves
to a code review.

Run it with `cargo test ch15` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 15 — Threads, `Send`/`Sync`, shared state, and channels.
//!
//! `std` only: no rayon, no tokio. The point is to feel the compiler refuse to
//! let you share what cannot be shared.

// `Arc` and `Mutex` look unused until you write the bodies below.
#![allow(unused_imports)]

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

/// Split the slice in half and sum the halves on two threads. Use
/// `thread::scope` so you can borrow `values` directly — no `Arc`, no
/// `'static` bound, no clone. Handle the empty and single-element cases.
pub fn parallel_sum(_values: &[i64]) -> i64 {
    todo!("std::thread::scope")
}

/// Count word occurrences across `threads` workers. Note the type you will
/// need: in Rust the lock *contains* the data, so there is no way to reach it
/// without holding the lock — `lock(obj)` protects nothing by comparison.
/// Count per chunk locally, then merge under the lock; hold it briefly.
pub fn tally(_words: &[String], _threads: usize) -> HashMap<String, usize> {
    todo!("Arc<Mutex<HashMap<..>>> plus chunks()")
}

/// Count values matching `predicate` across threads with no lock at all.
pub fn count_matching(_values: &[i64], _predicate: fn(i64) -> bool) -> usize {
    todo!("AtomicUsize + fetch_add(.., Ordering::Relaxed)")
}

/// Square every input on a worker pool, collect through an `mpsc` channel, and
/// return the results sorted.
///
/// The classic hang lives here: the receiver's loop ends only when *every*
/// sender has been dropped, and you are holding one of them.
pub fn pipeline(_inputs: Vec<i64>, _workers: usize) -> Vec<i64> {
    todo!("clone tx per worker, then drop the original")
}

/// Compile-time assertions. Leave these alone — the last test uses them to
/// prove the marker traits are checked statically rather than at run time.
pub const fn assert_send<T: Send>() {}
pub const fn assert_sync<T: Sync>() {}
```

The test module that follows this in the file is the specification — read it before you write anything.
