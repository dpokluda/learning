# Answers 10 — Collections and iterators

> Exercises: [10-collections.md](../10-collections.md)

## Part A

**A1. Rust iterators and LINQ both compose lazily. Name the biggest structural difference and what it implies for performance.**

LINQ-to-objects builds a chain of heap-allocated iterator objects and dispatches `MoveNext` virtually at every stage; Rust iterator adaptors are generic structs whose `next` is monomorphised and inlined, so a `map().filter().sum()` chain typically compiles to the same loop you would have written by hand, with no allocation and no indirect calls. The implication is that in Rust you can use the functional style in a hot loop without apology, whereas in C# the guidance is genuinely to drop to a `for` loop when it matters. The second difference is that there is no `IQueryable` — no expression trees, no provider model — because Rust iterators are code, not data, so nothing can translate them to SQL.

**A2. What is `entry`, and what does it do that `ContainsKey` + indexer cannot?**

`map.entry(key)` performs a single hash lookup and returns a handle to the slot, occupied or vacant, which you then fill (`or_insert`, `or_default`, `or_insert_with`) or modify (`and_modify`). The C# pattern `if (!d.ContainsKey(k)) d[k] = 0; d[k]++;` hashes three times; `TryGetValue` plus an assignment hashes twice. More importantly, in Rust the two-step version does not even compile in the common case, because the lookup holds a borrow of the map that the insert would conflict with — so `entry` is not merely an optimisation, it is the API that makes the pattern expressible at all.

**A3. Explain `collect::<Result<Vec<_>, _>>()`. What is it doing, and what is the C# equivalent?**

There is an impl of `FromIterator<Result<T, E>>` for `Result<Vec<T>, E>`, so collecting an iterator of results turns it inside out: you get `Ok(vec)` if every item succeeded, or the *first* `Err` otherwise, and iteration short-circuits at that point. It is the idiomatic way to parse or validate a batch where any failure aborts the whole thing. C# has no equivalent — you write a `foreach` with a `try/catch`, or `Select(...).ToList()` and let the first exception propagate, which works but gives you no control over short-circuiting and no typed error. The same trick works for `Option`, and swapping to `partition` or `filter_map` gives you the keep-going shape instead.

**A4. `iter()`, `iter_mut()`, and `into_iter()` — what does each yield, and how do you choose?**

`iter()` yields `&T` and borrows the collection, `iter_mut()` yields `&mut T` and mutably borrows it, and `into_iter()` yields `T` and consumes the collection. You choose by what you need to do with the elements: read, modify in place, or take ownership. The subtlety worth memorising is that `for x in &collection` desugars to `iter()`, `for x in &mut collection` to `iter_mut()`, and `for x in collection` to `into_iter()` — so the ampersand in a `for` loop is the whole decision, and forgetting it is why your collection is unexpectedly gone on the next line.

**A5. When would you reach for `BTreeMap` over `HashMap`, given `HashMap` is faster?**

When you need ordered iteration, range queries (`map.range("a".."m")`), or the smallest/largest key, none of which `HashMap` provides. Also when you need deterministic output — `HashMap`'s iteration order is deliberately randomised per process to defend against hash-flooding, so any test or log that iterates a `HashMap` without sorting is non-deterministic. That randomisation is a real difference from .NET's `Dictionary`, whose order is unspecified but in practice stable, which is exactly the kind of incidental behaviour people accidentally depend on. `BTreeMap` is the `SortedDictionary` analogue and is the right default whenever output is user-visible.

**A6. What does `fold` do that a chain of adaptors cannot, and when is it the wrong tool?**

`fold` threads an accumulator of arbitrary type through the sequence, so it can compute several results in one pass (a max and a count together, say) or build a value whose shape does not match any adaptor. It is the general case that `sum`, `count`, `max`, and `collect` are specialisations of. It is the wrong tool when a specialisation exists — `fold` with an addition is just `sum`, and writing it out obscures the intent — and it is the wrong tool when the accumulator is a mutable collection, where a plain `for` loop reads better than a closure that returns its own argument. Reach for `fold` when you genuinely need the generality, not to prove you can.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 10 — Collections and iterators: the LINQ muscle, retrained.

use std::collections::{BTreeMap, HashMap, HashSet, VecDeque};

/// `entry` is the idiom that has no clean LINQ equivalent: one hash lookup that
/// either yields the existing slot or inserts a default, all under one borrow.
pub fn word_frequency(text: &str) -> HashMap<String, usize> {
    let mut counts = HashMap::new();
    for word in text.split_whitespace() {
        let key = word.trim_matches(|c: char| !c.is_alphanumeric()).to_ascii_lowercase();
        if key.is_empty() {
            continue;
        }
        *counts.entry(key).or_insert(0) += 1;
    }
    counts
}

/// Top-N by count descending, ties broken alphabetically. Note that `HashMap`
/// iteration order is *unspecified*, so the tie-break is what makes this test
/// deterministic — a trap for anyone expecting Dictionary's incidental order.
pub fn top_n(counts: &HashMap<String, usize>, n: usize) -> Vec<(String, usize)> {
    let mut pairs: Vec<(String, usize)> =
        counts.iter().map(|(k, v)| (k.clone(), *v)).collect();
    pairs.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(&b.0)));
    pairs.truncate(n);
    pairs
}

/// The single most useful `collect` trick: a `Result` on the *outside*. The
/// iterator short-circuits on the first error. LINQ has no equivalent — you
/// would write a foreach with a try/catch.
pub fn parse_all(raw: &[&str]) -> Result<Vec<i64>, std::num::ParseIntError> {
    raw.iter().map(|s| s.trim().parse::<i64>()).collect()
}

/// The other shape: partition the successes from the failures instead of
/// short-circuiting.
pub fn parse_partitioned(raw: &[&str]) -> (Vec<i64>, Vec<String>) {
    let mut ok = Vec::new();
    let mut bad = Vec::new();
    for s in raw {
        match s.trim().parse::<i64>() {
            Ok(v) => ok.push(v),
            Err(_) => bad.push((*s).to_string()),
        }
    }
    (ok, bad)
}

/// `fold` is `Aggregate`. Here it computes a running maximum and a count in a
/// single pass, which is where fold earns its keep over chained adaptors.
pub fn summarize(values: &[i64]) -> (i64, usize) {
    values.iter().fold((i64::MIN, 0), |(max, count), &v| (max.max(v), count + 1))
}

/// Laziness made observable: nothing runs until the consumer pulls. The counter
/// proves `map` was called exactly twice, not once per element.
pub fn lazily_taken(values: &[i64], take: usize) -> (Vec<i64>, usize) {
    let mut touched = 0;
    let out: Vec<i64> = values
        .iter()
        .map(|&v| {
            touched += 1;
            v * 2
        })
        .take(take)
        .collect();
    (out, touched)
}

/// `BTreeMap` is the sorted-order collection: iteration is by key, always.
pub fn grouped_by_prefix(ids: &[&str]) -> BTreeMap<char, Vec<String>> {
    let mut groups: BTreeMap<char, Vec<String>> = BTreeMap::new();
    for id in ids {
        if let Some(first) = id.chars().next() {
            groups.entry(first).or_default().push((*id).to_string());
        }
    }
    groups
}

/// A breadth-first walk, which is what `VecDeque` is for.
pub fn bfs_order(edges: &HashMap<&str, Vec<&str>>, start: &str) -> Vec<String> {
    let mut seen: HashSet<&str> = HashSet::new();
    let mut queue: VecDeque<&str> = VecDeque::new();
    let mut order = Vec::new();

    queue.push_back(start);
    seen.insert(start);

    while let Some(node) = queue.pop_front() {
        order.push(node.to_string());
        for next in edges.get(node).into_iter().flatten() {
            if seen.insert(next) {
                queue.push_back(next);
            }
        }
    }
    order
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn entry_counts_without_a_double_lookup() {
        let counts = word_frequency("deny Deny audit, DENY!");
        assert_eq!(counts.get("deny"), Some(&3));
        assert_eq!(counts.get("audit"), Some(&1));
        assert_eq!(counts.len(), 2);
    }

    #[test]
    fn top_n_must_break_ties_deterministically() {
        let counts = word_frequency("b b a a c");
        assert_eq!(
            top_n(&counts, 2),
            vec![("a".to_string(), 2), ("b".to_string(), 2)]
        );
    }

    #[test]
    fn collect_into_result_short_circuits() {
        assert_eq!(parse_all(&["1", " 2 ", "3"]), Ok(vec![1, 2, 3]));
        assert!(parse_all(&["1", "oops", "3"]).is_err());
    }

    #[test]
    fn partitioning_keeps_going_past_the_first_failure() {
        let (ok, bad) = parse_partitioned(&["1", "oops", "3", "x"]);
        assert_eq!(ok, vec![1, 3]);
        assert_eq!(bad, vec!["oops", "x"]);
    }

    #[test]
    fn fold_does_two_jobs_in_one_pass() {
        assert_eq!(summarize(&[3, 9, 4]), (9, 3));
        assert_eq!(summarize(&[]), (i64::MIN, 0));
    }

    #[test]
    fn adaptors_are_lazy_so_take_limits_the_work() {
        let (out, touched) = lazily_taken(&[1, 2, 3, 4, 5], 2);
        assert_eq!(out, vec![2, 4]);
        assert_eq!(touched, 2, "map must not run for elements that were never pulled");
    }

    #[test]
    fn btreemap_iterates_in_key_order() {
        let groups = grouped_by_prefix(&["vm-1", "app-2", "vm-3", "db-4"]);
        let keys: Vec<char> = groups.keys().copied().collect();
        assert_eq!(keys, vec!['a', 'd', 'v']);
        assert_eq!(groups[&'v'], vec!["vm-1", "vm-3"]);
    }

    #[test]
    fn vecdeque_gives_breadth_first() {
        let edges: HashMap<&str, Vec<&str>> =
            [("a", vec!["b", "c"]), ("b", vec!["d"]), ("c", vec!["d"])].into();
        assert_eq!(bfs_order(&edges, "a"), vec!["a", "b", "c", "d"]);
    }
}
```
