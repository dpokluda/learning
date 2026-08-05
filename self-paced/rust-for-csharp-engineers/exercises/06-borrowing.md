# Exercises 06 — Borrowing and lifetimes

> **Covers:** [06 — Borrowing and lifetimes](../06-borrowing-and-lifetimes.md). **Code:** `drills/src/ch06.rs`. **Answers:** [answers/06-borrowing.md](answers/06-borrowing.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** State the borrowing rules in one sentence each, and explain what they buy you beyond memory safety.

**A2.** What is non-lexical lifetimes (NLL), and what did code look like before it?

**A3.** Why does `fn longest(a: &str, b: &str) -> &str` fail to compile, and what does adding `<'a>` actually tell the compiler?

**A4.** Give the three lifetime elision rules and say why `fn trim_scope(s: &str) -> &str` needs no annotation.

**A5.** You need two mutable references into the same `Vec`. How do you get them, and why can't the compiler just work it out?

**A6.** A struct field of type `&'a str` versus `String`: what does each choice commit you and your callers to?

## Part B — Exercise

Open `drills/src/ch06.rs`. The goal is to resolve, by hand, the four
borrow-checker fights you will hit most often in real code.

The lifetime annotations are supplied so the file compiles, but you should be
able to explain each of them: why `longest` needs an explicit `<'a>` and
`trim_scope` does not, and why `ScopeParser::segments` returns `Vec<&'a str>`
rather than `Vec<&str>`. Then implement `normalize_halves`, which needs two
mutable borrows into one slice at once, and `bump`, whose naive
lookup-then-insert form is rejected by the borrow checker. In both cases the
fix is a standard-library function that encapsulates the reasoning the checker
could not do — find it rather than reaching for a clone.

Run it with `cargo test ch06` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 06 — Borrowing, NLL, disjoint mutable borrows, and lifetimes.
//!
//! The lifetime annotations are supplied here so the file compiles. Your job is
//! the bodies — but read the signatures and be able to say *why* `longest`
//! needs `<'a>` and `trim_scope` does not.

// Fields look unread while the bodies are still `todo!()`.
#![allow(dead_code)]

/// Return whichever argument is longer. The result may borrow from either
/// input, which is why they must share one lifetime parameter.
pub fn longest<'a>(_a: &'a str, _b: &'a str) -> &'a str {
    todo!()
}

/// Strip leading and trailing `/`. Elision covers this: one input reference, so
/// the output must borrow from it. Adding `<'a>` here would be noise.
pub fn trim_scope(_scope: &str) -> &str {
    todo!()
}

/// A struct that *holds* a borrow. The `<'a>` is the compiler's proof that
/// `source` outlives every `ScopeParser` built from it.
#[derive(Debug)]
pub struct ScopeParser<'a> {
    pub(crate) source: &'a str,
}

impl<'a> ScopeParser<'a> {
    pub fn new(_source: &'a str) -> Self {
        todo!()
    }

    /// Split on `/`, discarding empty segments. Note the return type: the
    /// slices borrow from the *source*, not from `self`.
    pub fn segments(&self) -> Vec<&'a str> {
        todo!()
    }
}

/// Double the first half of the slice and decrement the second half, holding
/// two mutable borrows at once. The obvious attempt is rejected; find the safe
/// std function that proves the halves are disjoint.
pub fn normalize_halves(_scores: &mut [i32]) {
    todo!("split_at_mut")
}

/// Increment the counter at `key`, inserting 0 first if absent, and return the
/// new value. Lookup-then-insert is a borrow error; the fix does both under one
/// borrow.
pub fn bump(_map: &mut std::collections::HashMap<String, u32>, _key: &str) -> u32 {
    todo!("HashMap::entry")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
