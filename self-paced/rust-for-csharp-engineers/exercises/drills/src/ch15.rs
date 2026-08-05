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
