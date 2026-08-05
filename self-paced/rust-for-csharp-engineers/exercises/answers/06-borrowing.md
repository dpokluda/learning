# Answers 06 — Borrowing and lifetimes

> Exercises: [06-borrowing.md](../06-borrowing.md)

## Part A

**A1. State the borrowing rules in one sentence each, and explain what they buy you beyond memory safety.**

At any moment you may have either any number of shared borrows (`&T`) or exactly one mutable borrow (`&mut T`), never both; and no borrow may outlive the value it points at. Beyond preventing use-after-free, the first rule eliminates data races by construction — a data race requires two accesses, one of them a write, and the rule forbids exactly that pairing. It also eliminates iterator invalidation, the `List<T>` bug C# catches at run time with a version counter and an exception. And it gives the optimiser information C compilers pay for with `restrict`: a `&mut T` is provably unaliased, so the compiler can keep values in registers across calls it would otherwise have to assume clobber memory.

**A2. What is non-lexical lifetimes (NLL), and what did code look like before it?**

NLL means a borrow ends at its last *use*, not at the end of the enclosing lexical scope. Before it, `let first = &v[0]; println!("{first}"); v.push(4);` was rejected, because `first`'s borrow was considered live until the closing brace even though nothing touched it after the print. Programmers worked around it with artificial inner scopes (`{ let first = &v[0]; ... }`) purely to end a borrow early. NLL removed most of that ceremony, and it is why modern Rust reads far less like fighting the compiler than the 2015-era material you will find online — worth knowing, because a lot of that material is still the top search result.

**A3. Why does `fn longest(a: &str, b: &str) -> &str` fail to compile, and what does adding `<'a>` actually tell the compiler?**

It fails because lifetime elision cannot decide which input the output borrows from — there are two candidates and the rules only cover the unambiguous cases. Writing `fn longest<'a>(a: &'a str, b: &'a str) -> &'a str` says: pick some lifetime `'a` that both inputs outlive, and the result is valid for that. It does not change how long anything lives; it is a *constraint*, and at the call site the compiler infers `'a` as (roughly) the shorter of the two inputs' lifetimes and then checks that the result is not used past it. That framing matters — beginners read lifetime annotations as "making things live longer", when they are assertions about relationships the compiler then verifies.

**A4. Give the three lifetime elision rules and say why `fn trim_scope(s: &str) -> &str` needs no annotation.**

First, each elided input lifetime gets its own fresh parameter. Second, if there is exactly one input lifetime, it is assigned to every elided output lifetime. Third, if one of the inputs is `&self` or `&mut self`, the lifetime of `self` is assigned to every elided output lifetime. `trim_scope` has a single input reference, so rule two applies and the output unambiguously borrows from `s`. Rule three is why methods almost never need annotations, and it is also the source of a subtle trap: a method returning data borrowed from a *field's referent* rather than from `self` will silently get the wrong (over-constrained) lifetime, and you have to write it out to fix it.

**A5. You need two mutable references into the same `Vec`. How do you get them, and why can't the compiler just work it out?**

Use `split_at_mut`, `iter_mut`, `chunks_mut`, or `split_first_mut` — the standard library provides safe primitives that hand out provably disjoint `&mut` views. The compiler cannot work it out for `&mut v[0]` and `&mut v[1]` because indexing goes through `IndexMut`, an ordinary method taking `&mut self`, and the borrow checker reasons about function signatures, not about arithmetic on index values. It has no way to know that `0` and `1` do not alias. `split_at_mut` solves it by encapsulating a small `unsafe` block whose signature — returning two slices with a shared lifetime — encodes the disjointness the compiler could not derive.

**A6. A struct field of type `&'a str` versus `String`: what does each choice commit you and your callers to?**

`&'a str` makes the struct a *view*: it allocates nothing, is cheap to build, and is ideal for a parser or iterator that lives briefly over a buffer someone else owns. The cost is that the lifetime parameter is viral — it appears in the struct, in every impl block, and in every type that contains it — and the struct can never outlive its source, so it cannot be stored in a long-lived collection or sent to another thread that outlives the buffer. `String` makes the struct self-contained: no lifetime parameter, no constraints on callers, freely storable and sendable, at the cost of an allocation and a copy per value. The pragmatic default for domain types is `String`; reach for the borrowed form when profiling says the allocations matter or when the type is explicitly a short-lived view.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 06 — Borrowing, NLL, disjoint mutable borrows, and lifetimes.

/// The canonical lifetime exercise: the return borrows from *either* argument,
/// so both must share one lifetime parameter. Elision cannot infer this.
pub fn longest<'a>(a: &'a str, b: &'a str) -> &'a str {
    if a.len() >= b.len() { a } else { b }
}

/// Elision *does* handle this one: exactly one input reference, so the output
/// borrows from it. Writing `<'a>` here would be noise.
pub fn trim_scope(scope: &str) -> &str {
    scope.trim_matches('/')
}

/// A struct that holds a borrow must declare the lifetime. This is the shape
/// that has no C# analogue: the compiler guarantees `source` outlives `self`.
#[derive(Debug)]
pub struct ScopeParser<'a> {
    source: &'a str,
}

impl<'a> ScopeParser<'a> {
    pub fn new(source: &'a str) -> Self {
        Self { source }
    }

    /// Note the return lifetime: segments borrow from `source`, *not* `self`.
    pub fn segments(&self) -> Vec<&'a str> {
        self.source.split('/').filter(|s| !s.is_empty()).collect()
    }
}

/// Two mutable borrows of the *same* slice, made legal by proving to the
/// compiler that the halves are disjoint. `split_at_mut` is the safe primitive.
pub fn normalize_halves(scores: &mut [i32]) {
    let mid = scores.len() / 2;
    let (left, right) = scores.split_at_mut(mid);
    for v in left.iter_mut() {
        *v = v.saturating_mul(2);
    }
    for v in right.iter_mut() {
        *v = v.saturating_sub(1);
    }
}

/// Fight-and-fix: the naive version borrows `map` immutably (the lookup) while
/// wanting a mutable borrow (the insert). The fix is `entry`, which performs
/// the lookup and the insert under a single borrow.
pub fn bump(map: &mut std::collections::HashMap<String, u32>, key: &str) -> u32 {
    let counter = map.entry(key.to_string()).or_insert(0);
    *counter += 1;
    *counter
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    #[test]
    fn longest_requires_a_shared_lifetime() {
        let a = String::from("subscriptions");
        let winner = {
            let b = String::from("rg");
            // Both live long enough *here*, so this compiles.
            longest(&a, &b).to_string()
        };
        assert_eq!(winner, "subscriptions");
    }

    #[test]
    fn elision_covers_the_single_input_case() {
        assert_eq!(trim_scope("/subscriptions/a/"), "subscriptions/a");
    }

    #[test]
    fn a_struct_may_borrow_its_source() {
        let source = String::from("/subscriptions/abc/resourceGroups/rg1");
        let parser = ScopeParser::new(&source);
        assert_eq!(parser.segments(), vec!["subscriptions", "abc", "resourceGroups", "rg1"]);
    }

    #[test]
    fn disjoint_mutable_borrows_are_allowed_when_proven() {
        let mut scores = [1, 2, 3, 4];
        normalize_halves(&mut scores);
        assert_eq!(scores, [2, 4, 2, 3]);
    }

    #[test]
    fn nll_ends_a_borrow_at_its_last_use() {
        let mut owner = vec![1, 2, 3];
        let first = owner[0]; // borrow ends immediately (i32 is Copy)
        owner.push(4); // legal: no live borrow
        assert_eq!(first, 1);
        assert_eq!(owner.len(), 4);
    }

    #[test]
    fn entry_performs_lookup_and_insert_under_one_borrow() {
        let mut map: HashMap<String, u32> = HashMap::new();
        assert_eq!(bump(&mut map, "deny"), 1);
        assert_eq!(bump(&mut map, "deny"), 2);
        assert_eq!(bump(&mut map, "audit"), 1);
    }
}
```
