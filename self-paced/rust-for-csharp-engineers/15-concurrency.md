# 15 — Concurrency: threads, channels, and data parallelism

"Fearless concurrency" is Rust's most quoted marketing phrase and it is easy to misread. It does not mean
concurrency becomes simple — deadlocks, livelocks, and logic races are all still available to you. It means
one specific, enormous category of bug is gone: **data races cannot happen in safe Rust**, and the compiler
proves it rather than trusting you. Two unsynchronised threads touching the same memory with at least one
writing is a compile error, not a Heisenbug you chase for three days.

The mechanism is entirely made of things you have already learned. Ownership means data has one owner;
borrowing means a `&mut` is exclusive; and two marker traits, `Send` and `Sync`, extend those rules across
thread boundaries. There is no new concurrency subsystem — the same rules that stopped you invalidating a
`Vec` iterator stop you sharing a `HashMap` across threads.

> **Prerequisite:** [12 — Smart pointers and interior mutability](12-smart-pointers.md) and
> [14 — Testing, documentation, and benchmarks](14-testing-and-docs.md).

## Threads

`std::thread::spawn` takes a closure and runs it on a new OS thread. There is no thread pool in `std` —
`spawn` is a real 1:1 OS thread, closer to `new Thread(...)` than to `Task.Run`.

```rust
use std::thread;

fn main() {
    let handle = thread::spawn(|| {
        (1..=10).sum::<u32>()
    });

    // join() blocks and returns the closure's value, wrapped in a Result
    // that is Err if the thread panicked.
    let total = handle.join().expect("worker panicked");
    assert_eq!(total, 55);
}
```

`JoinHandle<T>` is `Task<T>`, and `join()` is `.Result` — a blocking wait. The difference from .NET is that
a panicking thread produces `Err` from `join` rather than an `AggregateException`, and — importantly — a
thread whose handle is dropped without joining is **detached**, running until it finishes or the process
exits. There is no `IsBackground` distinction; when `main` returns, the process ends and detached threads
are killed wherever they are.

### `move` and why closures need it

A spawned closure may outlive the function that created it, so the compiler requires it to own everything it
captures — the `'static` bound on `spawn`. That is what `move` does:

```rust,compile_fail
use std::thread;

fn main() {
    let data = vec![1, 2, 3];
    thread::spawn(|| {
        println!("{:?}", data);      // error: closure may outlive `data`
    });
}
```

```rust
use std::thread;

fn main() {
    let data = vec![1, 2, 3];
    let handle = thread::spawn(move || {     // `data` is moved into the closure
        data.len()
    });
    assert_eq!(handle.join().unwrap(), 3);
    // `data` is no longer usable here — it belongs to the thread.
}
```

C# closures capture by reference into a compiler-generated class and the GC keeps everything alive, so the
question never arises — and neither does the compiler warning when you capture a loop variable and get
surprising results. Rust makes the capture mode explicit and the lifetime provable.

### Scoped threads

Requiring `'static` is often too strict: you want workers to borrow a local slice and you know they finish
before the function returns. `thread::scope` (stable since 1.63) encodes exactly that:

```rust
use std::thread;

fn main() {
    let resources = vec!["res-1".to_owned(), "res-2".to_owned(), "res-3".to_owned()];
    let mut lengths = vec![0usize; resources.len()];

    thread::scope(|s| {
        // Borrow `resources` immutably from several threads...
        for (slot, name) in lengths.iter_mut().zip(&resources) {
            s.spawn(move || {
                *slot = name.len();           // ...and each thread gets a distinct &mut
            });
        }
    });   // scope joins every thread here, so the borrows are provably over

    assert_eq!(lengths, vec![5, 5, 5]);
    assert_eq!(resources.len(), 3);           // still usable
}
```

That is genuinely remarkable if you sit with it. Several threads hold `&mut` into the same `Vec`, and the
compiler accepts it because `iter_mut` proved the slots are disjoint. The equivalent C# — `Parallel.For`
writing into distinct array indices — is correct too, but nothing checks it; swap `slot` for a shared
variable and C# compiles happily while Rust refuses.

## `Send` and `Sync`

Two marker traits, no methods, implemented automatically by the compiler:

**`Send`** — a value of this type can be *moved* to another thread. **`Sync`** — `&T` can be shared with
another thread, equivalently `T` is safe for concurrent access through shared references. The formal
relationship is that `T: Sync` if and only if `&T: Send`.

Almost everything is both, derived structurally: a struct is `Send` if all its fields are. The exceptions
are the interesting part:

| Type | `Send` | `Sync` | Why |
|---|---|---|---|
| `i32`, `String`, `Vec<T>` | yes | yes | plain data |
| `Rc<T>` | **no** | **no** | non-atomic refcount would race |
| `Arc<T>` where `T: Send + Sync` | yes | yes | atomic refcount |
| `Cell<T>`, `RefCell<T>` | yes | **no** | mutation through `&` without synchronisation |
| `Mutex<T>` where `T: Send` | yes | yes | that is the point of a mutex |
| `MutexGuard<'_, T>` | **no** | yes | must unlock on the locking thread |
| `*const T`, `*mut T` | **no** | **no** | no safety guarantees at all |

`thread::spawn` requires `F: Send + 'static`, so the compiler checks these for you:

```rust,compile_fail
use std::rc::Rc;
use std::thread;

fn main() {
    let shared = Rc::new(5);
    thread::spawn(move || {
        println!("{}", shared);      // error: `Rc<i32>` cannot be sent between threads safely
    });
}
```

Read that error next to .NET's behaviour. `List<T>` is documented as not thread safe; nothing stops you
sharing one across threads; when you do, you get corruption or an occasional `IndexOutOfRangeException`
under load, weeks later, unreproducible. Rust turns the documentation into a type. Swap `Rc` for `Arc` and
it compiles — which is also a nudge that you have made a real decision.

The rule for what to reach for: `Arc<T>` for shared immutable data, `Arc<Mutex<T>>` or `Arc<RwLock<T>>` for
shared mutable data, and channels when you can avoid sharing entirely.

## Channels

Message passing is the preferred style — "do not communicate by sharing memory; share memory by
communicating" — and `std::sync::mpsc` provides multi-producer, single-consumer channels.

```rust
use std::sync::mpsc;
use std::thread;

fn main() {
    let (tx, rx) = mpsc::channel::<String>();

    for i in 0..3 {
        let tx = tx.clone();                       // one sender per producer
        thread::spawn(move || {
            tx.send(format!("finding-{i}")).expect("receiver alive");
        });
    }
    drop(tx);                                       // drop the original, or rx never ends

    // The receiver is an Iterator: it yields until every sender is dropped.
    let mut received: Vec<String> = rx.iter().collect();
    received.sort();
    assert_eq!(received, vec!["finding-0", "finding-1", "finding-2"]);
}
```

Two mechanics matter. **Senders are cloned, receivers are not** — `mpsc` means many producers, one
consumer. And **the channel closes when the last sender drops**, which is what terminates the `rx.iter()`
loop; forgetting `drop(tx)` on the original sender is the classic hang, because the loop waits forever for
a sender that will never send.

`channel()` is unbounded; `sync_channel(n)` bounds the queue and applies backpressure by blocking the
sender when full, with `sync_channel(0)` giving a rendezvous channel. The .NET analogue is
`System.Threading.Channels`, and the mapping is close: `Channel.CreateUnbounded` and
`Channel.CreateBounded`, with `ChannelWriter`/`ChannelReader` playing `tx`/`rx`. The difference is that
Rust's version is synchronous and blocking; the async equivalent is `tokio::sync::mpsc` in module 16.

When `mpsc` is not enough, **crossbeam-channel** is the standard upgrade: multi-producer *multi*-consumer,
faster, and with a `select!` macro for waiting on several channels at once.

```rust
use crossbeam_channel::{bounded, select, unbounded};
use std::thread;
use std::time::Duration;

fn main() {
    let (work_tx, work_rx) = bounded::<u32>(16);
    let (done_tx, done_rx) = unbounded::<u32>();

    // Several consumers can share one receiver — impossible with std mpsc.
    let mut workers = Vec::new();
    for _ in 0..3 {
        let rx = work_rx.clone();
        let done = done_tx.clone();
        workers.push(thread::spawn(move || {
            for job in rx {
                done.send(job * 2).unwrap();
            }
        }));
    }
    drop(work_rx);
    drop(done_tx);

    for i in 1..=6 { work_tx.send(i).unwrap(); }
    drop(work_tx);
    for w in workers { w.join().unwrap(); }

    let mut results: Vec<u32> = done_rx.iter().collect();
    results.sort();
    assert_eq!(results, vec![2, 4, 6, 8, 10, 12]);

    // select! waits on whichever channel is ready first.
    let (a_tx, a_rx) = unbounded::<&str>();
    let (b_tx, b_rx) = unbounded::<&str>();
    thread::spawn(move || {
        thread::sleep(Duration::from_millis(10));
        let _ = b_tx.send("b");
    });
    a_tx.send("a").unwrap();

    let first = select! {
        recv(a_rx) -> msg => msg.unwrap(),
        recv(b_rx) -> msg => msg.unwrap(),
    };
    assert_eq!(first, "a");
}
```

That worker-pool shape — a bounded work channel, N workers sharing the receiver, an unbounded results
channel — is the idiomatic Rust equivalent of `Task.Run` over a `BlockingCollection`, and it is worth
keeping in your pocket.

## Shared state

When message passing does not fit, share state behind a lock. Module 12 introduced `Mutex<T>` and
`RwLock<T>`; here is the concurrency-shaped view, along with the two things that differ most from C#.

```rust
use std::sync::{Arc, Mutex};
use std::collections::HashMap;
use std::thread;

fn main() {
    let counts: Arc<Mutex<HashMap<String, u32>>> = Arc::new(Mutex::new(HashMap::new()));

    thread::scope(|s| {
        for id in 0..4u32 {
            let counts = Arc::clone(&counts);
            s.spawn(move || {
                let key = if id % 2 == 0 { "even" } else { "odd" };
                // Keep the critical section as small as possible.
                let mut guard = counts.lock().unwrap();
                *guard.entry(key.to_owned()).or_insert(0) += 1;
            });
        }
    });

    let final_counts = counts.lock().unwrap();
    assert_eq!(final_counts["even"], 2);
    assert_eq!(final_counts["odd"], 2);
}
```

**The lock owns the data.** In C#, `lock (_sync) { _dict[k] = v; }` associates a lock object with the data
it protects only by convention, and the bug where one code path forgets the lock is both common and
invisible. In Rust there is no way to reach the `HashMap` except through `lock()`, so the association is
structural. This is the single best argument for Rust's concurrency design and it costs nothing.

**Locks are released by `Drop`, not by a block.** The guard unlocks when it goes out of scope, including on
early return and on panic — no `finally`, nothing to forget. The corollary is that you control the critical
section by controlling the guard's scope, so a long computation inside a `let guard = ...;` at function
scope holds the lock for the whole function. The idiomatic fixes are an explicit block, or `drop(guard)`,
or computing outside and assigning inside.

**`lock()` returns a `Result` because of poisoning.** If a thread panics while holding a `Mutex`, the mutex
is marked poisoned and subsequent `lock()` calls return `Err`, on the theory that the protected invariant
may be broken. The ubiquitous `.unwrap()` propagates that as a panic. `parking_lot::Mutex` drops poisoning
(and is smaller and faster), which is why so many projects depend on it.

For simple counters, atomics avoid the lock entirely and map directly onto `System.Threading.Interlocked`:

```rust
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use std::thread;

fn main() {
    let evaluated = Arc::new(AtomicUsize::new(0));

    thread::scope(|s| {
        for _ in 0..8 {
            let counter = Arc::clone(&evaluated);
            s.spawn(move || {
                counter.fetch_add(1, Ordering::Relaxed);
            });
        }
    });

    assert_eq!(evaluated.load(Ordering::Relaxed), 8);
}
```

`Ordering` is the memory-ordering parameter that .NET hides. `Relaxed` is right for a statistics counter
where you only need atomicity; `Acquire`/`Release` are right when the counter guards visibility of other
data; `SeqCst` is the always-correct, slowest default. If you are unsure, use `SeqCst` — but for a metric,
`Relaxed` is both correct and cheaper.

## Data parallelism with `rayon`

For "run this loop in parallel", `rayon` is the answer, and it is the closest thing in the ecosystem to PLINQ
— close enough that the translation is usually a one-word edit.

```toml
[dependencies]
rayon = "1.12.0"
```

```rust
use rayon::prelude::*;

fn expensive(n: u64) -> u64 {
    (1..=n).map(|x| x % 7).sum()
}

fn main() {
    let inputs: Vec<u64> = (1..=200).collect();

    // Sequential.
    let seq: u64 = inputs.iter().map(|n| expensive(*n)).sum();

    // Parallel: iter -> par_iter. That is the entire change.
    let par: u64 = inputs.par_iter().map(|n| expensive(*n)).sum();

    assert_eq!(seq, par);

    // The full adaptor vocabulary is available in parallel form.
    let big: Vec<u64> = inputs
        .par_iter()
        .filter(|n| **n % 3 == 0)
        .map(|n| n * 2)
        .collect();
    assert_eq!(big.len(), 66);

    // Parallel sort, fold, and reduce.
    let mut v: Vec<u64> = (0..1000).rev().collect();
    v.par_sort_unstable();
    assert_eq!(v[0], 0);

    let total = inputs.par_iter().copied().reduce(|| 0, |a, b| a + b);
    assert_eq!(total, inputs.iter().sum::<u64>());
}
```

`.iter()` becomes `.par_iter()`, `.into_iter()` becomes `.into_par_iter()`, and the rest of the chain is
unchanged. Under the hood rayon runs a work-stealing thread pool sized to your core count, exactly like the
.NET thread pool serving PLINQ.

The difference that matters is safety. `AsParallel()` in .NET will happily let your lambda mutate a shared
`List<T>` and corrupt it. Rayon's closures must be `Send + Sync`, so the same mistake does not compile:

```rust,compile_fail
use rayon::prelude::*;

fn main() {
    let mut results = Vec::new();
    (0..100).into_par_iter().for_each(|n| {
        results.push(n);          // error: cannot borrow `results` as mutable
    });
}
```

The fix is to `collect` — which is what you wanted anyway — or to use a lock or `fold`/`reduce` if
accumulation is genuinely needed:

```rust
use rayon::prelude::*;

fn main() {
    let results: Vec<u32> = (0..100u32).into_par_iter().collect();
    assert_eq!(results.len(), 100);

    // Order is preserved by collect on an indexed parallel iterator.
    assert_eq!(results[0], 0);
    assert_eq!(results[99], 99);

    // fold + reduce for a custom accumulator: fold per-thread, reduce across.
    let sum_of_squares: u64 = (1..=1000u64)
        .into_par_iter()
        .fold(|| 0u64, |acc, n| acc + n * n)
        .reduce(|| 0, |a, b| a + b);
    assert_eq!(sum_of_squares, (1..=1000u64).map(|n| n * n).sum());
}
```

`rayon::join` handles recursive divide-and-conquer, which is the shape behind parallel quicksort and tree
traversal:

```rust
fn sum_tree(values: &[u64]) -> u64 {
    if values.len() <= 64 {
        return values.iter().sum();
    }
    let mid = values.len() / 2;
    let (left, right) = values.split_at(mid);
    // Potentially parallel: rayon decides based on available workers.
    let (a, b) = rayon::join(|| sum_tree(left), || sum_tree(right));
    a + b
}

fn main() {
    let data: Vec<u64> = (1..=1000).collect();
    assert_eq!(sum_tree(&data), 500_500);
}
```

The rule for when to use rayon rather than threads: use rayon when the work is **CPU-bound and
data-parallel**, and threads or async when it is I/O-bound or has a distinct lifecycle. Do not mix rayon's
pool with blocking I/O — you will starve it, the same failure mode as blocking the .NET thread pool.

## Threads or async?

Module 16 covers async in depth, but the choice deserves stating here because it is the first question you
face.

**Use threads** when the work is CPU-bound, when you have a small fixed number of long-lived workers, or
when the code is simpler that way — which it usually is. A thread costs a few hundred microseconds to spawn
and roughly 8 MB of *virtual* address space for its stack (committed lazily), so a few hundred threads is
entirely reasonable.

**Use async** when you have many thousands of concurrent I/O operations — a network server, a crawler, a
proxy — where the per-task cost of a thread would dominate. An async task costs a few hundred bytes.

The .NET intuition here is misleading in a specific way. In .NET, `async` is nearly free to adopt because
the runtime, the thread pool, and the whole BCL are already async-aware, so "just make it async" is
reasonable default advice. In Rust, async means pulling in a runtime, a function-colouring split across
your API surface, and a genuinely harder set of borrow interactions. **Threads are the simpler default and
you should not feel behind the times for using them.** A CLI that scans a few thousand files is better as
`rayon` over threads than as tokio.

## `polcheck`: a parallel scan

The engine from module 12, rewritten with rayon and a channel-based reporter — the shape the capstone
uses for its non-async path.

```rust
use rayon::prelude::*;
use std::collections::HashMap;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::mpsc;
use std::thread;

#[derive(Debug, Clone)]
pub struct Resource {
    pub id: String,
    pub tags: HashMap<String, String>,
}

#[derive(Debug, Clone)]
pub struct Rule {
    pub name: String,
    pub required_tag: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub resource_id: String,
    pub rule: String,
}

/// Parallel scan: each resource is independent, so this is embarrassingly parallel.
pub fn scan(rules: &[Rule], resources: &[Resource], evaluated: &AtomicUsize) -> Vec<Finding> {
    resources
        .par_iter()
        .flat_map_iter(|r| {
            evaluated.fetch_add(1, Ordering::Relaxed);
            rules.iter().filter_map(move |rule| {
                (!r.tags.contains_key(&rule.required_tag)).then(|| Finding {
                    resource_id: r.id.clone(),
                    rule: rule.name.clone(),
                })
            })
        })
        .collect()
}

fn main() {
    let rules = vec![
        Rule { name: "require-owner".into(), required_tag: "owner".into() },
        Rule { name: "require-env".into(), required_tag: "env".into() },
    ];
    let resources: Vec<Resource> = (0..100)
        .map(|i| Resource {
            id: format!("res-{i}"),
            tags: if i % 2 == 0 {
                HashMap::from([("owner".to_owned(), "platform".to_owned())])
            } else {
                HashMap::new()
            },
        })
        .collect();

    let evaluated = AtomicUsize::new(0);
    let mut findings = scan(&rules, &resources, &evaluated);
    findings.sort_by(|a, b| a.resource_id.cmp(&b.resource_id).then(a.rule.cmp(&b.rule)));

    assert_eq!(evaluated.load(Ordering::Relaxed), 100);
    assert_eq!(findings.len(), 150);      // 50 even miss env; 50 odd miss both

    // Streaming the results to a writer thread, so reporting overlaps scanning.
    let (tx, rx) = mpsc::channel::<Finding>();
    let writer = thread::spawn(move || {
        let mut lines = Vec::new();
        for f in rx {
            lines.push(format!("{}: {}", f.resource_id, f.rule));
        }
        lines.len()
    });

    findings.into_par_iter().for_each_with(tx, |tx, f| {
        tx.send(f).expect("writer alive");
    });

    assert_eq!(writer.join().unwrap(), 150);
}
```

Two rayon idioms in there earn their keep. `flat_map_iter` is the right adaptor when the inner iterator is
cheap and sequential — `flat_map` would try to parallelise the inner loop too, which is pure overhead here.
And `for_each_with(tx, ...)` clones the sender once per worker thread rather than once per item, which is
how you feed a channel from a parallel iterator without cloning in the hot loop. Note also that
`for_each_with` consumes `tx`, so the channel closes when the parallel iteration ends and the writer's `for
f in rx` loop terminates naturally.

## Before you move on

Rust's concurrency guarantees fall out of ownership rather than being bolted on. `Send` and `Sync` are
compiler-derived markers that turn .NET's "this type is not thread safe, please be careful" documentation
into a type error, and `thread::spawn`'s `Send + 'static` bound is what enforces them at the boundary.
`thread::scope` relaxes the `'static` requirement when the compiler can prove the threads finish first,
which allows several threads to hold disjoint `&mut` into one collection — a thing C# permits but cannot
verify.

Message passing through `mpsc` (or crossbeam for multi-consumer and `select!`) is the preferred style, with
the two mechanics to remember being that senders clone and that the channel closes when the last sender
drops. When you must share state, `Mutex<T>` and `RwLock<T>` differ from C#'s `lock` in the way that
matters most: the lock owns the data, so there is no path to the data that skips the lock, and `Drop`
releases it on every exit path including panics. Atomics map to `Interlocked` with the addition of an
explicit memory `Ordering`.

For data parallelism, `rayon` is PLINQ with the races removed — `.iter()` becomes `.par_iter()` and the
compiler rejects the shared-mutation mistakes that `AsParallel()` allows.

The judgement to carry forward is that **threads are the simpler default in Rust**, unlike in .NET where
async is nearly free. Reach for async only when you genuinely have thousands of concurrent I/O operations,
which is the subject of the next module.

If you can explain why `Rc` is not `Send` but `RefCell` is, what `thread::scope` proves that `spawn`
cannot, and why `Mutex<T>` owning its data eliminates a bug class that `lock (obj)` cannot, you are ready
for futures.

Next: [16 — Async Rust and tokio](16-async-and-tokio.md).

### Sources

- *The Book*, ch. 16 "Fearless Concurrency". <https://doc.rust-lang.org/book/ch16-00-concurrency.html> — threads, channels, shared state, and `Send`/`Sync`.
- `std::thread`. <https://doc.rust-lang.org/std/thread/> — spawn semantics, detaching, and `thread::scope`.
- `std::sync::mpsc`. <https://doc.rust-lang.org/std/sync/mpsc/> — channel/`sync_channel`, and the disconnect semantics.
- *The Rustonomicon*, "Send and Sync". <https://doc.rust-lang.org/nomicon/send-and-sync.html> — the auto-trait derivation rules and negative impls.
- `std::sync::atomic`. <https://doc.rust-lang.org/std/sync/atomic/> — the `Ordering` variants and their guarantees.
- *Rayon* documentation. <https://docs.rs/rayon/> — parallel iterators, `join`, `scope`, and the work-stealing pool.
- *Rayon* FAQ. <https://github.com/rayon-rs/rayon/blob/main/FAQ.md> — when rayon is and is not appropriate, including the blocking-I/O warning.
- *crossbeam-channel* documentation. <https://docs.rs/crossbeam-channel/> — MPMC channels and the `select!` macro.
