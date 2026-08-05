# Exercises 10 — Collections and iterators

> **Covers:** [10 — Collections and iterators](../10-collections-and-iterators.md). **Code:** `drills/src/ch10.rs`. **Answers:** [answers/10-collections.md](answers/10-collections.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** Rust iterators and LINQ both compose lazily. Name the biggest structural difference and what it implies for performance.

**A2.** What is `entry`, and what does it do that `ContainsKey` + indexer cannot?

**A3.** Explain `collect::<Result<Vec<_>, _>>()`. What is it doing, and what is the C# equivalent?

**A4.** `iter()`, `iter_mut()`, and `into_iter()` — what does each yield, and how do you choose?

**A5.** When would you reach for `BTreeMap` over `HashMap`, given `HashMap` is faster?

**A6.** What does `fold` do that a chain of adaptors cannot, and when is it the wrong tool?

## Part B — Exercise

Open `drills/src/ch10.rs`. The goal is to rebuild your LINQ reflexes on Rust's
iterator machinery and to meet the two APIs that have no LINQ equivalent.

Most of the functions are direct translations you will find easy. Two are not.
`lazily_taken` asks you to return both the mapped output *and* a count of how
many times the mapping closure actually ran — if you answer `values.len()` you
have not yet internalised that adaptors are pull-based. And `parse_all` asks for
a one-line fallible batch parse, which is `collect` into a `Result` on the
outside; its sibling `parse_partitioned` shows the keep-going shape for when
short-circuiting is wrong.

Watch the `top_n` test in particular: it forces a deterministic tie-break,
because `HashMap` iteration order is randomised per process and any code that
depends on it is a flaky test waiting to happen.

Run it with `cargo test ch10` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 10 — Collections and iterators: the LINQ muscle, retrained.

use std::collections::{BTreeMap, HashMap};

/// Count words, lowercased and stripped of surrounding punctuation, skipping
/// anything that ends up empty. Use `entry` — one hash lookup, not two.
pub fn word_frequency(_text: &str) -> HashMap<String, usize> {
    todo!("*counts.entry(key).or_insert(0) += 1")
}

/// Top `n` by count descending, ties broken alphabetically ascending.
/// `HashMap` iteration order is unspecified, so the tie-break is what makes
/// this deterministic — do not skip it.
pub fn top_n(_counts: &HashMap<String, usize>, _n: usize) -> Vec<(String, usize)> {
    todo!()
}

/// Parse every string, short-circuiting on the first failure. This is one line:
/// the `Result` goes on the *outside* of the `collect`.
pub fn parse_all(_raw: &[&str]) -> Result<Vec<i64>, std::num::ParseIntError> {
    todo!("collect::<Result<Vec<_>, _>>()")
}

/// The other shape: keep going, returning (successes, raw failures).
pub fn parse_partitioned(_raw: &[&str]) -> (Vec<i64>, Vec<String>) {
    todo!()
}

/// Return `(max, count)` in a single pass using `fold`. Empty slice => max is
/// `i64::MIN`.
pub fn summarize(_values: &[i64]) -> (i64, usize) {
    todo!()
}

/// Double each value and take the first `take` — and *also* return how many
/// times the mapping closure actually ran. If your answer is `values.len()`,
/// you have not internalised laziness yet.
pub fn lazily_taken(_values: &[i64], _take: usize) -> (Vec<i64>, usize) {
    todo!("increment a counter inside the map closure")
}

/// Group ids by first character. `BTreeMap` iterates in key order, always.
pub fn grouped_by_prefix(_ids: &[&str]) -> BTreeMap<char, Vec<String>> {
    todo!("entry(..).or_default()")
}

/// Breadth-first traversal from `start`, visiting each node once.
pub fn bfs_order(_edges: &HashMap<&str, Vec<&str>>, _start: &str) -> Vec<String> {
    todo!("VecDeque as the queue, HashSet for seen")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
