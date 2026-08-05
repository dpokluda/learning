# 10 — Collections and iterators

You already know LINQ, so you already know how to think in pipelines: a source, a chain of lazy
transformations, and a terminal operation that forces the work. Rust's iterators are that model with the
runtime cost removed and the type system tightened. The pipeline you write compiles down to roughly the
loop you would have written by hand — no delegates, no interface dispatch, no allocation per stage — and
the compiler will not let you accidentally iterate a collection you have moved away or mutate one you are
walking. The price is that `IEnumerable<T>`'s comfortable vagueness about ownership becomes something you
must state.

> **Prerequisite:** [09 — The standard traits](09-standard-traits.md).

## The collections

The standard library ships a deliberately small set. Here is the mapping, which covers nearly everything
you will reach for:

| Rust | C# / .NET | Notes |
|---|---|---|
| `Vec<T>` | `List<T>` | growable array; the default choice |
| `[T; N]` | `T[]` (fixed) | stack-allocated, size in the type |
| `&[T]` / `&mut [T]` | `ReadOnlySpan<T>` / `Span<T>` | borrowed view, no ownership |
| `VecDeque<T>` | `Queue<T>`, `Deque` | ring buffer, O(1) both ends |
| `HashMap<K, V>` | `Dictionary<K, V>` | needs `K: Eq + Hash` |
| `BTreeMap<K, V>` | `SortedDictionary<K, V>` | ordered, needs `K: Ord`, range queries |
| `HashSet<T>` | `HashSet<T>` | — |
| `BTreeSet<T>` | `SortedSet<T>` | — |
| `BinaryHeap<T>` | `PriorityQueue<T, T>` | **max**-heap by default |
| `LinkedList<T>` | `LinkedList<T>` | rarely the right answer |

There is no `IList<T>`, no `ICollection<T>`, and no `IEnumerable<T>`-as-a-parameter-type convention.
Where C# says "accept the interface, return the concrete type", Rust says **accept `&[T]` or
`impl IntoIterator<Item = T>`, return the concrete type**. A function taking `&[T]` will accept a `&Vec<T>`
by deref coercion, an array, or a sub-slice, which covers the same ground with zero indirection.

### `Vec<T>` in practice

```rust
fn main() {
    let mut v: Vec<i32> = Vec::new();
    v.push(3);
    v.extend([1, 2]);                         // like AddRange

    let v2 = vec![3, 1, 2];                   // like new List<int> { 3, 1, 2 }
    assert_eq!(v, v2);

    // Indexing panics out of range; get() returns Option.
    assert_eq!(v[0], 3);
    assert_eq!(v.get(99), None);

    v.sort();                                 // in place, requires Ord
    assert_eq!(v, vec![1, 2, 3]);
    v.sort_by_key(|n| std::cmp::Reverse(*n)); // descending
    assert_eq!(v, vec![3, 2, 1]);

    assert_eq!(v.pop(), Some(1));             // from the end
    v.retain(|n| *n != 2);                    // like RemoveAll, inverted
    assert_eq!(v, vec![3]);

    // Capacity control, exactly as in List<T>.
    let mut big = Vec::with_capacity(1000);
    big.extend(0..1000);
    assert_eq!(big.len(), 1000);
    assert!(big.capacity() >= 1000);
}
```

Two divergences from `List<T>` matter. **`v[i]` panics on an out-of-range index rather than throwing a
catchable exception**, so use `.get(i) -> Option<&T>` whenever the index is not provably valid — it is the
`TryGetValue` shape and it composes with `?` and `match`. And **`sort` is stable and requires `Ord`**,
which is why sorting `Vec<f64>` needs `sort_by(f64::total_cmp)` as module 09 explained.

### `HashMap<K, V>` and the entry API

```rust
use std::collections::HashMap;

fn main() {
    let mut counts: HashMap<String, u32> = HashMap::new();
    counts.insert("storage".to_owned(), 1);

    // Lookup with a borrowed key — no allocation, thanks to Borrow.
    assert_eq!(counts.get("storage"), Some(&1));
    assert_eq!(counts.get("compute"), None);
    assert!(counts.contains_key("storage"));

    // The entry API: one hash lookup for the read-modify-write.
    *counts.entry("storage".to_owned()).or_insert(0) += 1;
    *counts.entry("compute".to_owned()).or_insert(0) += 1;
    assert_eq!(counts["storage"], 2);
    assert_eq!(counts["compute"], 1);

    // or_insert_with avoids constructing the default unless needed.
    let mut groups: HashMap<&str, Vec<&str>> = HashMap::new();
    for id in ["a1", "a2", "b1"] {
        groups.entry(&id[..1]).or_insert_with(Vec::new).push(id);
    }
    assert_eq!(groups["a"], vec!["a1", "a2"]);

    // Construction from pairs.
    let m = HashMap::from([("x", 1), ("y", 2)]);
    assert_eq!(m.len(), 2);
}
```

The **entry API** is the standout, and C# has no equivalent. `GetOrAdd` exists on
`ConcurrentDictionary` but not on `Dictionary`, so the common C# idiom is
`if (!d.TryGetValue(k, out var v)) { v = new(); d[k] = v; }` — two lookups and two hashes. `entry` performs
one lookup, hands you a handle to the slot, and lets you insert or modify through it. Once you have the
habit you will find yourself missing it in C#.

Also note `counts["storage"]` returns a `&V` and **panics** if absent — `Index` cannot return an `Option`.
Use `get` unless you know the key is present. And there is no insertion-order guarantee (the hasher is
randomly seeded per process, which is a deliberate HashDoS defence); if you need order, use `BTreeMap`
or the `indexmap` crate.

`BTreeMap` earns its place when you need sorted iteration or range queries:

```rust
use std::collections::BTreeMap;

fn main() {
    let mut m = BTreeMap::new();
    m.insert(30, "c");
    m.insert(10, "a");
    m.insert(20, "b");

    // Iteration is in key order, always.
    let keys: Vec<i32> = m.keys().copied().collect();
    assert_eq!(keys, vec![10, 20, 30]);

    // Range queries — no HashMap equivalent, and no Dictionary equivalent either.
    let mid: Vec<&str> = m.range(15..=30).map(|(_, v)| *v).collect();
    assert_eq!(mid, vec!["b", "c"]);

    assert_eq!(m.first_key_value(), Some((&10, &"a")));
}
```

## Iterators

`Iterator` is one method — `fn next(&mut self) -> Option<Self::Item>` — plus the adaptor library built on
it. Everything is lazy until a consuming operation pulls.

### The three ways to iterate

This is the ownership decision, and it is the thing to get right before learning any adaptors:

```rust
fn main() {
    let mut v = vec![String::from("a"), String::from("b")];

    // 1. iter() — yields &T. The collection is untouched.
    let lens: Vec<usize> = v.iter().map(|s| s.len()).collect();
    assert_eq!(lens, vec![1, 1]);

    // 2. iter_mut() — yields &mut T. Mutate in place.
    for s in v.iter_mut() {
        s.push('!');
    }
    assert_eq!(v, vec!["a!", "b!"]);

    // 3. into_iter() — yields T. Consumes the collection.
    let joined: String = v.into_iter().collect();
    assert_eq!(joined, "a!b!");
    // `v` is gone here.
}
```

In C# all three are the same `foreach` because everything is a reference and mutation is unconstrained.
Here the choice is in the method name, and picking wrong produces a borrow error rather than a subtle bug.
The `for` loop sugar maps directly: `for x in &v` is `iter()`, `for x in &mut v` is `iter_mut()`, and
`for x in v` is `into_iter()`.

### Adaptors: the LINQ translation table

Most of your LINQ vocabulary transfers with a rename:

| LINQ | Rust | Note |
|---|---|---|
| `Select` | `map` | |
| `Where` | `filter` | |
| `SelectMany` | `flat_map` / `flatten` | |
| `Aggregate` | `fold` / `reduce` | `reduce` uses the first element as seed |
| `Any` | `any` | short-circuits |
| `All` | `all` | short-circuits |
| `First` / `FirstOrDefault` | `next()` / `find` | returns `Option` |
| `Take` / `Skip` | `take` / `skip` | |
| `TakeWhile` / `SkipWhile` | `take_while` / `skip_while` | |
| `Count` | `count` | consumes; O(n) unless specialised |
| `Sum` / `Max` / `Min` | `sum` / `max` / `min` | |
| `Distinct` | `collect::<HashSet<_>>()` or `itertools::unique` | not in std |
| `OrderBy` | `.collect::<Vec<_>>()` then `sort_by_key` | not lazy |
| `GroupBy` | fold into a `HashMap`, or `itertools::into_group_map` | not in std |
| `Zip` | `zip` | |
| `Reverse` | `rev` | needs `DoubleEndedIterator` |
| `ToList` / `ToArray` | `collect` | |
| `ToDictionary` | `collect::<HashMap<_, _>>()` | |
| — | `enumerate` | index + item pairs |
| — | `peekable` | look ahead without consuming |
| — | `windows` / `chunks` (on slices) | overlapping / disjoint groups |

The absences are informative. **There is no `OrderBy` adaptor** because sorting cannot be lazy — you must
see every element before yielding the first — so Rust makes you materialise into a `Vec` and sort it,
which is what LINQ does internally anyway but hides. **There is no `GroupBy` or `Distinct` in std**;
`itertools` supplies both, and folding into a `HashMap` is the idiomatic std answer.

Here is a chain that uses a representative spread:

```rust
fn main() {
    let words = ["alpha", "beta", "gamma", "delta", "epsilon"];

    let result: Vec<String> = words
        .iter()
        .enumerate()
        .filter(|(_, w)| w.len() > 4)
        .map(|(i, w)| format!("{i}:{}", w.to_uppercase()))
        .take(3)
        .collect();

    assert_eq!(result, vec!["0:ALPHA", "2:GAMMA", "3:DELTA"]);

    // Fold with an explicit accumulator.
    let total_len = words.iter().fold(0usize, |acc, w| acc + w.len());
    assert_eq!(total_len, 5 + 4 + 5 + 5 + 7);

    // Short-circuiting predicates.
    assert!(words.iter().any(|w| w.starts_with('g')));
    assert!(!words.iter().all(|w| w.len() == 5));

    // Grouping, the std way.
    use std::collections::HashMap;
    let by_len: HashMap<usize, Vec<&str>> =
        words.iter().fold(HashMap::new(), |mut acc, w| {
            acc.entry(w.len()).or_default().push(*w);
            acc
        });
    assert_eq!(by_len[&5], vec!["alpha", "gamma", "delta"]);
}
```

Laziness works exactly as in LINQ, and the classic demonstration is worth running:

```rust
fn main() {
    let mut evaluated = Vec::new();

    let first = (1..100)
        .map(|n| { evaluated.push(n); n * 2 })
        .find(|n| *n > 10);

    assert_eq!(first, Some(12));
    // Only six elements were ever touched.
    assert_eq!(evaluated, vec![1, 2, 3, 4, 5, 6]);
}
```

One thing the compiler will tell you off about: an adaptor chain with no consumer does nothing, and
`#[must_use]` on iterators turns that into a warning. LINQ has the same semantics but no warning, which is
the source of a familiar class of C# bug where someone writes `list.Where(...)` and expects mutation.

## `collect` and the turbofish

`collect` is the most flexible method in the standard library, and understanding it removes most of the
confusion around type annotations. Its signature is `fn collect<B: FromIterator<Self::Item>>(self) -> B`
— it is generic over the *output* type, so you must tell the compiler what you want:

```rust
use std::collections::{BTreeMap, HashMap, HashSet};

fn main() {
    let src = vec![("a", 1), ("b", 2), ("a", 3)];

    // Annotation on the binding.
    let v: Vec<(&str, i32)> = src.clone().into_iter().collect();
    assert_eq!(v.len(), 3);

    // Turbofish on the call.
    let m = src.clone().into_iter().collect::<HashMap<_, _>>();
    assert_eq!(m["a"], 3);                       // later wins, like a dictionary assign

    let b = src.clone().into_iter().collect::<BTreeMap<_, _>>();
    assert_eq!(b.keys().copied().collect::<Vec<_>>(), vec!["a", "b"]);

    let set: HashSet<&str> = src.iter().map(|(k, _)| *k).collect();
    assert_eq!(set.len(), 2);

    // Strings collect from chars or from &str.
    let s: String = vec!["a", "b", "c"].into_iter().collect();
    assert_eq!(s, "abc");
}
```

`::<HashMap<_, _>>` is the **turbofish**, and the underscores mean "infer these". It exists because
`collect::<Vec<i32>>()` would otherwise be ambiguous with the generic parameter list of `collect` itself —
the `::` before `<` disambiguates. C#'s equivalent problem does not arise because `ToList()` fixes the
container in the method name.

### Collecting into `Result`: the killer feature

This one has no LINQ analogue and it is genuinely excellent. `Result<Vec<T>, E>` implements
`FromIterator<Result<T, E>>`, so a sequence of fallible operations collects into a single `Result` that
short-circuits on the first error:

```rust
fn main() {
    let good = ["1", "2", "3"];
    let parsed: Result<Vec<i32>, _> = good.iter().map(|s| s.parse::<i32>()).collect();
    assert_eq!(parsed, Ok(vec![1, 2, 3]));

    let bad = ["1", "oops", "3"];
    let parsed: Result<Vec<i32>, _> = bad.iter().map(|s| s.parse::<i32>()).collect();
    assert!(parsed.is_err());

    // Option works the same way.
    let all: Option<Vec<i32>> = vec![Some(1), Some(2)].into_iter().collect();
    assert_eq!(all, Some(vec![1, 2]));
    let some_missing: Option<Vec<i32>> = vec![Some(1), None].into_iter().collect();
    assert_eq!(some_missing, None);

    // Or partition successes from failures instead of short-circuiting.
    let (ok, err): (Vec<_>, Vec<_>) = bad
        .iter()
        .map(|s| s.parse::<i32>())
        .partition(|r| r.is_ok());
    assert_eq!(ok.len(), 2);
    assert_eq!(err.len(), 1);
}
```

In C# this is a `foreach` with a `try/catch` or a `TryParse` accumulator loop, every time. Here the type
system does it. `filter_map` is the close cousin that discards failures: `.filter_map(|s| s.parse().ok())`
keeps the successes and drops the rest.

## Why there is no `IQueryable`

C# has two pipeline abstractions that look identical and behave completely differently. `IEnumerable<T>`
runs delegates in process; `IQueryable<T>` captures the lambda as an **expression tree** that a provider
(EF Core, a Cosmos driver) translates into SQL or another query language. The trick depends on C#'s ability
to reify a lambda as data at runtime, and on the runtime type information that makes provider dispatch
possible.

Rust has neither. Closures have no runtime representation you can inspect, and monomorphisation erases
types before the binary exists, so a `.filter(|x| x.age > 30)` chain cannot become a `WHERE` clause. This
is why Rust's database story (module 25) is different in kind: **`sqlx` checks SQL you actually wrote
against a live schema at compile time** rather than generating SQL from method calls. You give up
`IQueryable`'s composability and gain the elimination of the entire class of "this LINQ expression cannot
be translated" runtime failures — an exception every EF Core user has met. Which trade you prefer will
depend on the project; the important thing is knowing that no crate is going to hand you `IQueryable`.

Nothing prevents a *builder* API that composes SQL fragments — `sea-query` and `diesel` do exactly that —
but the composition is explicit method calls building a query value, not a compiler-captured lambda.

## Writing your own iterator

Two routes. Implement `Iterator` on a struct when you need a named, reusable type; return
`impl Iterator<Item = _>` when you just want to build one out of existing pieces.

```rust
/// A named iterator: implement Iterator, get all adaptors free.
struct Fib { a: u64, b: u64 }

impl Iterator for Fib {
    type Item = u64;
    fn next(&mut self) -> Option<u64> {
        let out = self.a;
        self.a = self.b;
        self.b = out + self.b;
        Some(out)
    }
}

fn fib() -> Fib { Fib { a: 0, b: 1 } }

/// Composed from existing adaptors: no struct needed.
fn evens_up_to(n: u64) -> impl Iterator<Item = u64> {
    (0..=n).filter(|x| x % 2 == 0)
}

fn main() {
    assert_eq!(fib().take(8).collect::<Vec<_>>(), vec![0, 1, 1, 2, 3, 5, 8, 13]);
    assert_eq!(evens_up_to(6).collect::<Vec<_>>(), vec![0, 2, 4, 6]);
}
```

`Fib` is infinite, which is fine — `take` bounds it. Note there is no `yield` and no state machine
generated for you: C#'s iterator methods with `yield return` are compiler-generated state machines, and
Rust's equivalent (generators / `gen` blocks) is still unstable, so you write the state machine yourself.
For simple cases `std::iter::successors` and `std::iter::from_fn` do it for you:

```rust
fn main() {
    let fib = std::iter::successors(Some((0u64, 1u64)), |(a, b)| Some((*b, a + b)))
        .map(|(a, _)| a);
    assert_eq!(fib.take(8).collect::<Vec<_>>(), vec![0, 1, 1, 2, 3, 5, 8, 13]);

    let mut n = 0;
    let counter = std::iter::from_fn(move || { n += 1; if n <= 3 { Some(n) } else { None } });
    assert_eq!(counter.collect::<Vec<_>>(), vec![1, 2, 3]);
}
```

## The borrow checker meets iteration

One category of error you will hit early, and its fixes:

```rust,compile_fail
fn main() {
    let mut v = vec![1, 2, 3];
    for x in &v {
        if *x == 2 {
            v.push(99);       // error: cannot borrow `v` as mutable
        }
    }
}
```

The iterator holds a shared borrow of `v` for the whole loop, so mutation is rejected. In C# this compiles
and throws `InvalidOperationException: Collection was modified` at runtime — same bug, later discovery.
Three fixes, in order of preference:

```rust
fn main() {
    // 1. Collect the work, then apply it.
    let mut v = vec![1, 2, 3];
    let extra: Vec<i32> = v.iter().filter(|x| **x == 2).map(|_| 99).collect();
    v.extend(extra);
    assert_eq!(v, vec![1, 2, 3, 99]);

    // 2. Use an index loop when you must mutate the container.
    let mut w = vec![1, 2, 3];
    let mut i = 0;
    while i < w.len() {
        if w[i] == 2 { w.push(99); }
        i += 1;
    }
    assert_eq!(w, vec![1, 2, 3, 99]);

    // 3. Use retain / iter_mut for in-place edits that don't resize.
    let mut z = vec![1, 2, 3];
    z.iter_mut().for_each(|x| *x *= 10);
    z.retain(|x| *x != 20);
    assert_eq!(z, vec![10, 30]);
}
```

## `polcheck`: the evaluation pipeline

Putting collections and iterators together for the running example. This is the shape of the real
`polcheck` engine: evaluate every rule against every resource, keep only the failures, and summarise.

```rust
use std::collections::{BTreeMap, HashMap};

#[derive(Debug, Clone)]
pub struct Resource {
    pub id: String,
    pub kind: String,
    pub location: String,
    pub tags: HashMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub resource_id: String,
    pub rule: String,
    pub reason: String,
}

#[derive(Debug, Clone)]
pub struct Rule {
    pub name: String,
    pub required_tag: String,
}

impl Rule {
    fn check(&self, r: &Resource) -> Option<Finding> {
        if r.tags.contains_key(&self.required_tag) {
            None
        } else {
            Some(Finding {
                resource_id: r.id.clone(),
                rule: self.name.clone(),
                reason: format!("missing tag '{}'", self.required_tag),
            })
        }
    }
}

/// Every (rule, resource) pair, keeping only the failures.
pub fn scan(rules: &[Rule], resources: &[Resource]) -> Vec<Finding> {
    resources
        .iter()
        .flat_map(|r| rules.iter().filter_map(move |rule| rule.check(r)))
        .collect()
}

/// Findings per resource kind, in deterministic order for stable output.
pub fn summarize(findings: &[Finding], resources: &[Resource]) -> BTreeMap<String, usize> {
    let kind_of: HashMap<&str, &str> =
        resources.iter().map(|r| (r.id.as_str(), r.kind.as_str())).collect();

    findings.iter().fold(BTreeMap::new(), |mut acc, f| {
        let kind = kind_of.get(f.resource_id.as_str()).copied().unwrap_or("unknown");
        *acc.entry(kind.to_owned()).or_insert(0) += 1;
        acc
    })
}

fn main() {
    let resources = vec![
        Resource {
            id: "res-1".into(),
            kind: "storage".into(),
            location: "westus2".into(),
            tags: HashMap::from([("owner".to_owned(), "platform".to_owned())]),
        },
        Resource {
            id: "res-2".into(),
            kind: "storage".into(),
            location: "eastus".into(),
            tags: HashMap::new(),
        },
        Resource {
            id: "res-3".into(),
            kind: "compute".into(),
            location: "eastus".into(),
            tags: HashMap::from([("env".to_owned(), "prod".to_owned())]),
        },
    ];

    let rules = vec![
        Rule { name: "require-owner".into(), required_tag: "owner".into() },
        Rule { name: "require-env".into(), required_tag: "env".into() },
    ];

    let findings = scan(&rules, &resources);
    assert_eq!(findings.len(), 4);          // res-1 misses env; res-2 misses both; res-3 misses owner

    let summary = summarize(&findings, &resources);
    assert_eq!(summary["storage"], 3);
    assert_eq!(summary["compute"], 1);

    // Report the worst offenders first, then by id for stability.
    let mut by_resource: Vec<(&str, usize)> = findings
        .iter()
        .fold(BTreeMap::new(), |mut acc: BTreeMap<&str, usize>, f| {
            *acc.entry(f.resource_id.as_str()).or_insert(0) += 1;
            acc
        })
        .into_iter()
        .collect();
    by_resource.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(b.0)));
    assert_eq!(by_resource[0], ("res-2", 2));
}
```

Two details worth pointing at. The `move` in `.filter_map(move |rule| rule.check(r))` is necessary because
the inner closure outlives the `flat_map` call and must capture `r` (a `&Resource`, which is `Copy`) by
value. And `sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(b.0)))` is the idiomatic multi-key sort —
`then_with` chains comparators lazily, which is the `ThenBy` of LINQ without the allocation.

`BTreeMap` rather than `HashMap` for the summary is a deliberate choice: CLI output should be
deterministic, and `HashMap` iteration order varies between runs.

## Before you move on

Rust's collections are a small, deliberately concrete set, and the interface-oriented habits from C# do not
transfer: accept `&[T]` or `impl IntoIterator`, return the concrete type. `Vec<T>` is `List<T>` with
panicking indexing and a `.get()` escape hatch; `HashMap` is `Dictionary` with the entry API, which is
strictly better than the `TryGetValue`-then-insert dance; `BTreeMap` earns its keep whenever you need
ordered iteration or ranges, and it is the right default for anything a human will read.

Iterators are LINQ with the costs removed and the ownership made explicit. The single most important thing
to internalise is the `iter` / `iter_mut` / `into_iter` triple, because that choice is the whole ownership
story in one method name. The adaptor vocabulary transfers almost directly, minus `OrderBy` (sorting
cannot be lazy), `GroupBy`, and `Distinct` (fold into a map, or use `itertools`). `collect` is generic over
its output, which is why the turbofish exists, and collecting into `Result<Vec<_>, E>` to short-circuit a
fallible pipeline is a genuine improvement over anything LINQ offers.

There is no `IQueryable` and there will not be one, because expression trees need runtime lambda reification
and reflection that Rust deliberately does not have. That absence shapes the whole database story.

If you can explain what `for x in v` does that `for x in &v` does not, why `collect` usually needs a type
annotation, and what the entry API saves over `TryGetValue`, you are ready to make failure a first-class
value.

Next: [11 — Error handling](11-error-handling.md).

### Sources

- `std::collections` module docs. <https://doc.rust-lang.org/std/collections/> — includes an excellent "which collection should I use" decision guide and complexity table.
- `std::iter::Iterator`. <https://doc.rust-lang.org/std/iter/trait.Iterator.html> — the full adaptor list with examples.
- *The Book*, ch. 13 "Functional Language Features: Iterators and Closures". <https://doc.rust-lang.org/book/ch13-00-functional-features.html> — laziness, closures, and the zero-cost claim with benchmarks.
- `HashMap::entry`. <https://doc.rust-lang.org/std/collections/struct.HashMap.html#method.entry> — the single-lookup read-modify-write API.
- `impl FromIterator<Result<A, E>> for Result<V, E>`. <https://doc.rust-lang.org/std/result/enum.Result.html#impl-FromIterator%3CResult%3CA,+E%3E%3E-for-Result%3CV,+E%3E> — the short-circuiting collect.
- `std::collections::HashMap` — hashing section. <https://doc.rust-lang.org/std/collections/struct.HashMap.html#hashing-algorithms> — why iteration order is randomised and how to swap the hasher.
- `itertools` crate documentation. <https://docs.rs/itertools/> — `unique`, `group_by`, `into_group_map`, and the other adaptors std omits.
