# 26 — A field guide to the crates worth knowing

Rust's standard library is deliberately small. There is no `System.Text.Json`, no `DateTime`, no `Guid`, no
`Regex`, no `Random`, no concurrent dictionary. Coming from .NET's famously batteries-included BCL this feels
like poverty, and for your first week it is genuinely annoying. The reasoning is that the standard library
can never break compatibility, so anything whose design might need to evolve is better off on crates.io where
it can ship a 2.0 — and the ecosystem has repeatedly proven the point, with `rand`, `chrono`, and `time` all
having gone through redesigns that would have been impossible in `std`.

The practical consequence is that a competent Rust programmer carries a mental list of the twenty or so
crates that fill the BCL-shaped hole. This module is that list. It is a reference chapter rather than a
teaching one — read it once to know what exists, then come back when you need something.

> **Prerequisite:** [13 — Modules, crates, and workspaces](13-modules-and-crates.md), for how to evaluate and add a dependency.

Everything below was compiled and run against the versions pinned in `SETUP.md`.

## Iterator superpowers: `itertools`

Module 08 covered `Iterator` as the LINQ analogue and noted the gaps: no `GroupBy`, no `Zip` of unequal
lengths, no `Chunk`. `itertools` fills them, and it is the crate I add to almost every project.

```rust
use itertools::Itertools;

fn main() {
    let findings = vec![
        ("res-1", "require-owner"),
        ("res-2", "require-env"),
        ("res-1", "require-tag"),
    ];

    // The `GroupBy` you actually wanted: collect into a map.
    let by_resource: std::collections::HashMap<&str, Vec<&str>> = findings
        .iter()
        .map(|(r, rule)| (*r, *rule))
        .into_group_map();
    assert_eq!(by_resource["res-1"].len(), 2);

    // Join — like string.Join, but on any iterator.
    let names = ["require-owner", "require-env"].iter().join(", ");
    assert_eq!(names, "require-owner, require-env");

    // Deduplicate consecutive equal items (streaming, unlike Distinct).
    let runs: Vec<i32> = [1, 1, 2, 2, 2, 3].iter().copied().dedup().collect();
    assert_eq!(runs, vec![1, 2, 3]);

    // Fixed-size windows and chunks.
    let chunked: Vec<Vec<i32>> = (1..=7).chunks(3).into_iter().map(|c| c.collect()).collect();
    assert_eq!(chunked, vec![vec![1, 2, 3], vec![4, 5, 6], vec![7]]);

    // sorted() — because Iterator has no OrderBy.
    let sorted: Vec<i32> = [3, 1, 2].iter().copied().sorted().collect();
    assert_eq!(sorted, vec![1, 2, 3]);

    // Cartesian product, useful for rule x resource matrices.
    let pairs: Vec<(i32, char)> = (1..=2).cartesian_product('a'..='b').collect();
    assert_eq!(pairs.len(), 4);

    // exactly_one() encodes "I expect precisely one match" in the type.
    // (Its error type deliberately holds the iterator, so it isn't PartialEq —
    // compare with `.ok()` rather than `assert_eq!` on the Result.)
    assert_eq!([42].into_iter().exactly_one().ok(), Some(42));
    assert!([1, 2].into_iter().exactly_one().is_err());
}
```

`into_group_map` is the one to remember, because grouping is the most common thing LINQ does that `std`
cannot. `sorted()` is the second: `Iterator` has no `OrderBy` because sorting requires buffering the whole
sequence, which `std` declines to do implicitly — `itertools` is willing to, and says so in the name.

## Dates and times: `chrono` and `time`

There are two credible crates and the choice is genuinely contested, which is unusual for Rust.

`chrono` is the older and more widely used, with an API that will feel immediately familiar because it maps
closely onto `DateTime`/`DateTimeOffset`:

```rust
use chrono::{DateTime, Duration, NaiveDate, TimeZone, Utc};

fn main() {
    // DateTime<Utc> is DateTimeOffset with the offset pinned to zero.
    let t: DateTime<Utc> = Utc.with_ymd_and_hms(2025, 1, 15, 10, 30, 0).unwrap();
    assert_eq!(t.to_rfc3339(), "2025-01-15T10:30:00+00:00");

    // Arithmetic.
    let later = t + Duration::hours(3);
    assert_eq!(later.format("%Y-%m-%d %H:%M").to_string(), "2025-01-15 13:30");

    // Parsing, with a Result rather than TryParse's bool + out.
    let parsed: DateTime<Utc> = "2025-01-15T10:30:00Z".parse().unwrap();
    assert_eq!(parsed, t);

    // NaiveDate is DateOnly — a date with no zone at all.
    let d = NaiveDate::from_ymd_opt(2025, 1, 15).unwrap();
    assert_eq!(d.to_string(), "2025-01-15");

    // The difference between two instants is a signed Duration.
    let delta = later - t;
    assert_eq!(delta.num_minutes(), 180);
}
```

The type-level distinction is the part worth internalising. `DateTime<Utc>` and `DateTime<Local>` are
*different types*, so a function that requires UTC cannot be handed a local time by accident. .NET expresses
this with a `Kind` enum that is famously easy to lose track of, and the number of production bugs caused by a
`DateTime` with the wrong `Kind` is not small. Rust puts the zone in the type and the problem disappears.

`time` is the newer alternative: smaller, `no_std`-friendly, with a more conservative API and compile-time
format descriptions. If you are starting fresh and do not need `chrono`'s breadth of timezone handling,
`time` is a defensible choice. The ecosystem is roughly split, and both are maintained.

For a *monotonic* clock — measuring elapsed time, immune to the wall clock jumping — use `std::time::Instant`,
which is `Stopwatch`. Never subtract two `DateTime`s to measure a duration, for the same reason you would not
in .NET.

## Identifiers: `uuid`

```rust
use uuid::Uuid;

fn main() {
    let id = Uuid::new_v4();                 // Guid.NewGuid()
    assert_eq!(id.to_string().len(), 36);
    assert_eq!(id.get_version_num(), 4);

    // Parsing is fallible and says so.
    let parsed = Uuid::parse_str("67e55044-10b1-426f-9247-bb680e5fe0c8").unwrap();
    assert_eq!(parsed.to_string(), "67e55044-10b1-426f-9247-bb680e5fe0c8");
    assert!(Uuid::parse_str("not-a-uuid").is_err());

    // The nil UUID is Guid.Empty.
    assert!(Uuid::nil().is_nil());

    // Deterministic v5 UUIDs from a namespace + name — useful for stable ids.
    let ns = Uuid::NAMESPACE_URL;
    let a = Uuid::new_v5(&ns, b"https://example.com/res-1");
    let b = Uuid::new_v5(&ns, b"https://example.com/res-1");
    assert_eq!(a, b);
}
```

Two notes. `new_v4` requires the `v4` feature and `new_v5` the `v5` feature — nothing is on by default, which
is the recurring Rust theme. And v7 UUIDs (time-ordered, index-friendly) are available behind the `v7`
feature and are usually the better choice for database keys, a nicety .NET only gained recently with
`Guid.CreateVersion7()`.

## Regular expressions: `regex`

The `regex` crate is unusual and the difference from .NET is worth understanding rather than just noting.

```rust
use regex::Regex;

fn main() {
    // Compile once. Compiling in a loop is the classic performance bug.
    let re = Regex::new(r"^/subscriptions/(?<sub>[^/]+)/resourceGroups/(?<rg>[^/]+)").unwrap();

    let id = "/subscriptions/abc-123/resourceGroups/rg-prod/providers/Microsoft.Compute/x";
    let caps = re.captures(id).unwrap();
    assert_eq!(&caps["sub"], "abc-123");
    assert_eq!(&caps["rg"], "rg-prod");

    assert!(re.is_match(id));
    assert!(!re.is_match("/tenants/x"));

    // Replacement with $name references.
    let masked = re.replace(id, "/subscriptions/***/resourceGroups/$rg");
    assert!(masked.starts_with("/subscriptions/***/resourceGroups/rg-prod"));

    // All matches, as an iterator.
    let words = Regex::new(r"\w+").unwrap();
    let n = words.find_iter("require owner tag").count();
    assert_eq!(n, 3);
}
```

The design difference: Rust's `regex` guarantees **linear time** in the length of the input, because it uses
finite automata rather than backtracking. The price is that backreferences and lookaround are *not supported*
— they cannot be, in this model. The benefit is that catastrophic backtracking, the ReDoS vulnerability class
that has taken down real .NET services, is impossible by construction. `System.Text.RegularExpressions` gained
`RegexOptions.NonBacktracking` in .NET 7 for the same reason; Rust simply made it the only option.

If you truly need lookaround, `fancy-regex` wraps `regex` and adds a backtracking layer, with the performance
characteristics that implies.

Compile your regex once. The idiomatic way is a `LazyLock` static, which brings us to the next entry.

## One-time initialisation: `OnceLock` and `LazyLock`

`once_cell` was for years the standard answer to "how do I have a lazily-initialised static", and you will see
it in a great deal of existing code. It has now been absorbed into the standard library — `OnceLock` in Rust
1.70 and `LazyLock` in 1.80 — so **new code should use `std` and not add the dependency**.

```rust
use std::sync::LazyLock;
use regex::Regex;

// Initialised on first access, exactly once, thread-safely.
static RESOURCE_ID: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^/subscriptions/([^/]+)").unwrap());

fn main() {
    assert!(RESOURCE_ID.is_match("/subscriptions/abc"));
    assert!(!RESOURCE_ID.is_match("/tenants/abc"));
}
```

```rust
use std::sync::OnceLock;

// Set once, at runtime, from a value you don't have at compile time.
static CONFIG_PATH: OnceLock<String> = OnceLock::new();

fn config_path() -> &'static str {
    CONFIG_PATH.get_or_init(|| "polcheck.toml".to_string())
}

fn main() {
    assert_eq!(config_path(), "polcheck.toml");
    // A second set is rejected rather than overwriting.
    assert!(CONFIG_PATH.set("other".into()).is_err());
}
```

`LazyLock` is `Lazy<T>` with `LazyThreadSafetyMode.ExecutionAndPublication`, and `OnceLock` is the
write-once cell you would otherwise build with `Interlocked.CompareExchange`. The distinction is whether the
initialiser is known at declaration time.

## Faster locks: `parking_lot`

```rust
use parking_lot::{Mutex, RwLock};

fn main() {
    let m = Mutex::new(0u32);
    {
        // No Result — parking_lot has no lock poisoning.
        let mut g = m.lock();
        *g += 1;
    }
    assert_eq!(*m.lock(), 1);

    let rw = RwLock::new(vec![1, 2, 3]);
    assert_eq!(rw.read().len(), 3);
    rw.write().push(4);
    assert_eq!(rw.read().len(), 4);

    // try_lock for the non-blocking path.
    let g = m.lock();
    assert!(m.try_lock().is_none());
    drop(g);
    assert!(m.try_lock().is_some());
}
```

The differences from `std::sync::Mutex` are small but pleasant: the guard comes back directly rather than
wrapped in a `Result`, because `parking_lot` does not implement poisoning; the locks are smaller and usually
faster under contention; and fair unlocking is available. The absence of poisoning is the one to think about
— `std` marks a mutex poisoned if a thread panics while holding it, forcing every subsequent caller to
acknowledge that the data may be inconsistent. That is a real safety property, and giving it up is a choice.
Most code `unwrap()`s the poisoning `Result` anyway, which is an argument that the property was not being
used.

Note that these are *blocking* locks. In async code use `tokio::sync::Mutex` when the lock is held across an
`.await`, and a blocking lock otherwise — module 16 covered why.

## Concurrent maps: `dashmap`

`ConcurrentDictionary<K,V>` has no `std` equivalent. `dashmap` is it: a sharded map with the interior
mutability handled for you, so you do not need `Arc<Mutex<HashMap<..>>>`.

```rust
use dashmap::DashMap;

fn main() {
    let counts: DashMap<String, usize> = DashMap::new();

    // AddOrUpdate, spelled as an entry API.
    *counts.entry("require-owner".into()).or_insert(0) += 1;
    *counts.entry("require-owner".into()).or_insert(0) += 1;
    *counts.entry("require-env".into()).or_insert(0) += 1;

    assert_eq!(*counts.get("require-owner").unwrap(), 2);
    assert_eq!(counts.len(), 2);

    // insert takes &self, not &mut self — that's the whole point.
    counts.insert("require-tag".into(), 5);
    assert_eq!(counts.len(), 3);

    let total: usize = counts.iter().map(|e| *e.value()).sum();
    assert_eq!(total, 8);
}
```

The one hazard, and it is a real one: `DashMap` shards internally, and holding a reference into one shard
while trying to access another can **deadlock**. Concretely, do not call `counts.get(...)` while a guard from
`counts.entry(...)` is still alive in the same scope. Keep guard lifetimes short and never nest them. This is
the kind of bug `ConcurrentDictionary` does not have, and it is the price of the sharded design.

## Efficient byte buffers: `bytes`

```rust
use bytes::{Bytes, BytesMut, BufMut};

fn main() {
    let mut buf = BytesMut::with_capacity(64);
    buf.put_slice(b"POLCHECK/1.0 ");
    buf.put_u32(42);

    let frozen: Bytes = buf.freeze();     // now immutable and cheaply cloneable
    assert_eq!(frozen.len(), 17);

    // Slicing is O(1) and shares the underlying allocation — no copy.
    let header = frozen.slice(0..8);
    assert_eq!(&header[..], b"POLCHECK");

    // Clone is a refcount bump, not a memcpy.
    let also = frozen.clone();
    assert_eq!(also.len(), frozen.len());
}
```

`Bytes` is what `ReadOnlyMemory<byte>` and `ArraySegment<byte>` are reaching for: a view into a shared buffer
that can be sliced and cloned without copying. It is the currency of the tokio/hyper ecosystem, so if you
work with HTTP bodies or a codec you will meet it whether or not you choose it.

## Randomness: `rand`

```rust
use rand::{Rng, RngExt};

fn main() {
    let mut rng = rand::rng();                 // thread-local, seeded from the OS
    let n: u32 = rng.random();
    let _ = n;

    let dice = rng.random_range(1..=6);
    assert!((1..=6).contains(&dice));

    let flip: bool = rng.random();
    let _ = flip;

    // Shuffling requires the SliceRandom trait.
    use rand::seq::SliceRandom;
    let mut v = vec![1, 2, 3, 4, 5];
    v.shuffle(&mut rng);
    assert_eq!(v.len(), 5);

    // Reproducible sequences need an explicitly seeded generator.
    use rand::{SeedableRng, rngs::StdRng};
    let mut a = StdRng::seed_from_u64(42);
    let mut b = StdRng::seed_from_u64(42);
    assert_eq!(a.random::<u64>(), b.random::<u64>());
}
```

Three warnings, and the last one is fresh enough that nothing on the internet has caught up. First, `rand`
renamed its core API in 0.9 — `thread_rng()` became `rng()`, and `gen()` became `random()` because `gen`
became a reserved word in edition 2024. Second, in **0.10 the `Rng` trait was split**: `Rng` is now the
low-level `rand_core` trait, and the ergonomic `random()` / `random_range()` methods live on a new **`RngExt`**
extension trait. Importing only `rand::Rng`, as every existing example does, produces a baffling
"no method named `random` found ... the method is available for `ThreadRng` here" error. Import
`rand::RngExt`, or just `use rand::prelude::*` and stop thinking about it. Third, `rand`'s default generator
is **not** cryptographically secure for key material; use `rand::rngs::OsRng` or a dedicated crypto crate for
anything security-sensitive, exactly as you would prefer `RandomNumberGenerator` over `Random` in .NET.

This is a good illustration of the earlier point about `std` staying small. Had `Random` been in the standard
library, that trait split could never have shipped.

## Data parallelism: `rayon`

Module 15 covered this, but it belongs on the list. `rayon` is PLINQ: change `iter()` to `par_iter()` and
the work spreads across a thread pool.

```rust
use rayon::prelude::*;

fn main() {
    let resources: Vec<u64> = (1..=1000).collect();

    let total: u64 = resources.par_iter().map(|r| r * 2).sum();
    assert_eq!(total, 1_001_000);

    // Filtering and collecting in parallel, order preserved.
    let evens: Vec<u64> = resources.par_iter().copied().filter(|r| r % 2 == 0).collect();
    assert_eq!(evens.len(), 500);
}
```

The reason this is safe — and the reason PLINQ needs you to be careful where rayon does not — is `Send` and
`Sync`. The closure you pass must be `Sync`, and any captured mutable state must be synchronised, so the
data race PLINQ cheerfully allows is a compile error here.

## Everything else, briefly

A handful of crates you will meet often enough to recognise, with a one-line justification each.

| Crate | What it is | .NET analogue |
|---|---|---|
| `tempfile` | temp files and dirs, deleted on drop | `Path.GetTempFileName` + `try/finally` |
| `walkdir` | recursive directory traversal | `Directory.EnumerateFiles(..., Recursive)` |
| `indicatif` | progress bars and spinners for CLIs | — |
| `humantime` | parse and print `"5m30s"` durations | `TimeSpan.Parse` |
| `num_cpus` | logical CPU count | `Environment.ProcessorCount` |
| `crossbeam` | channels and lock-free structures | `System.Threading.Channels` |
| `base64` | encoding and decoding | `Convert.ToBase64String` |
| `sha2` / `blake3` | hashing | `System.Security.Cryptography` |
| `url` | RFC-compliant URL parsing | `Uri` |
| `semver` | version parsing and ranges | `NuGetVersion` |
| `mime` / `mime_guess` | content types | `MediaTypeHeaderValue` |
| `directories` | platform config/cache paths | `Environment.SpecialFolder` |

## How to evaluate a crate

You cannot memorise crates.io, so the durable skill is judging a dependency quickly. NuGet has taught you
most of this already, but the signals differ slightly.

Look at **downloads and reverse dependencies** on crates.io — a crate that hyper or tokio depends on has been
vetted harder than you can vet it. Check **the last release date and the open-issue trend**; Rust crates
often reach genuine "done" and stop releasing, so age alone is not damning, but age plus unanswered issues
is. Read the **docs.rs page**, which is generated for every crate from its source and is the single best
quality signal — a crate with thorough docs and examples was written by someone who cared. Check the
**version number**: pre-1.0 means the author reserves the right to break you, and a great deal of the
ecosystem lives at 0.x indefinitely. Look at the **dependency tree** with `cargo tree`, because Rust's
culture of small crates means one innocuous addition can pull in eighty. And run **`cargo audit`** (or
`cargo deny`) in CI to catch known vulnerabilities, which is the analogue of NuGet's vulnerability
scanning.

One structural difference from NuGet worth knowing: crates.io is **append-only**. A published version can be
*yanked* — which stops new projects from selecting it — but never deleted, so an existing `Cargo.lock` keeps
working. This is a stronger guarantee than NuGet's unlisting and it means left-pad-style breakage cannot
happen.

## Before you move on

This chapter is a map rather than an argument, so the thing to retain is the shape of it: `std` is small on
purpose, and the ecosystem fills the BCL's role with crates that can evolve independently. `itertools` gives
you the LINQ operators `Iterator` lacks, with `into_group_map` and `sorted` the two you will reach for
first. `chrono` or `time` is your `DateTime`, with the crucial improvement that the timezone lives in the
type rather than in a `Kind` enum you can lose track of. `uuid` is `Guid`, and `regex` is `Regex` with
guaranteed linear time — hence no backreferences or lookaround, and no ReDoS.

For lazily-initialised statics, `LazyLock` and `OnceLock` are now in `std`, so `once_cell` is legacy in new
code. `parking_lot` offers faster locks with no poisoning, `dashmap` is `ConcurrentDictionary` with a
sharding-related deadlock hazard to respect, `bytes` is the zero-copy buffer the async ecosystem trades in,
and `rayon` is PLINQ made safe by `Send`/`Sync`. Watch `rand`'s renamed API, since the internet is full of
`thread_rng` and `gen` calls that no longer compile.

The lasting skill is evaluation rather than memorisation: downloads and reverse dependencies, release
cadence, docs.rs quality, whether the crate has reached 1.0, the size of the tree in `cargo tree`, and
`cargo audit` in CI.

If you can say why `regex` cannot support lookaround and why that is a feature, and why new code should stop
adding `once_cell`, you have the two ideas here that are more than lookup.

Next: [27 — Capstone: building polcheck](27-capstone-polcheck.md).

### Sources

- `itertools`. <https://docs.rs/itertools/0.15/itertools/trait.Itertools.html> — `into_group_map`, `chunks`, `dedup`, `sorted`, `exactly_one`.
- `chrono`. <https://docs.rs/chrono/0.4/chrono/> — `DateTime<Utc>`, `NaiveDate`, formatting and parsing.
- `time`. <https://docs.rs/time/0.3/time/> — the alternative date/time crate.
- `uuid`. <https://docs.rs/uuid/1/uuid/> — versions 4, 5, and 7; feature flags.
- `regex`. <https://docs.rs/regex/1/regex/> — syntax, named captures, and the linear-time guarantee.
- "Regular Expression Matching Can Be Simple And Fast", Russ Cox. <https://swtch.com/~rsc/regexp/regexp1.html> — the automata-vs-backtracking argument the crate implements.
- `std::sync::OnceLock` and `LazyLock`. <https://doc.rust-lang.org/std/sync/struct.LazyLock.html> — stabilised in 1.70 and 1.80 respectively.
- `parking_lot`. <https://docs.rs/parking_lot/0.12/parking_lot/> — no poisoning, smaller locks.
- `dashmap`. <https://docs.rs/dashmap/6/dashmap/struct.DashMap.html> — sharded concurrent map and its deadlock caveat.
- `bytes`. <https://docs.rs/bytes/1/bytes/> — `Bytes`, `BytesMut`, `freeze`, O(1) slicing.
- `rand`. <https://docs.rs/rand/0.10/rand/> — `rng()`, `random()`, `random_range()`, `SeedableRng`.
- `rayon`. <https://docs.rs/rayon/1/rayon/> — parallel iterators.
- crates.io publishing and yank policy. <https://doc.rust-lang.org/cargo/reference/publishing.html#cargo-yank> — why published versions are never deleted.
- `cargo-audit` and the RustSec advisory database. <https://rustsec.org/> — vulnerability scanning.
