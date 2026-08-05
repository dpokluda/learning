# Answers 03 — Syntax orientation

> Exercises: [03-syntax.md](../03-syntax.md)

## Part A

**A1. In C#, `int x = 1;` and `int x = 2;` in the same scope is a compile error. In Rust, `let x = 1; let x = 2;` is idiomatic. What is actually happening, and why is it not just a relaxed rule?**

The second `let` does not assign to the first binding — it creates an entirely new one that *shadows* it. The old value still exists (and its destructor still runs at the end of scope); it is simply no longer nameable. Because it is a new binding, the type may change, which is what makes the idiom useful: `let raw = "5"; let raw: u32 = raw.parse()?;` refines a value through several representations without inventing `rawStr`, `rawParsed`, `rawFinal`. It is not a relaxed rule, it is a different mechanism: C#'s error is about reusing a *name for a slot*, and Rust has no slot reuse here at all.

**A2. What does `let x = if cond { 1 } else { 2 };` compile to, and what is the equivalent C#? Where does the analogy stop?**

It is an ordinary `if` used in expression position — the same as C#'s conditional expression `var x = cond ? 1 : 2;`. The analogy stops at scale: in Rust *every* block is an expression, so `match`, `loop`, and plain `{ ... }` all produce values, and the trailing expression of a block (no semicolon) is its value. C# had to grow separate expression forms — the ternary, then switch expressions, then collection expressions — because statements do not yield values. Rust never needed the split. The corollary is that a stray semicolon changes the type of a block from `T` to `()`, which is the single most common early confusion.

**A3. What happens on `let x: u8 = 255; let y = x + 1;` in a debug build? In a release build? Why is that not undefined behaviour?**

In debug, the addition panics with `attempt to add with overflow`. In release, `overflow-checks` defaults off and the result wraps to 0. Crucially this is *not* undefined behaviour — it is two well-defined behaviours selected by a compiler flag, unlike C where signed overflow is UB the optimiser may exploit. The practical rule is that you should never rely on either: reach for `checked_add` when overflow is a real possibility (it returns `Option`), `saturating_add` when clamping is right, and `wrapping_add` when you actually want modular arithmetic. If the values are compile-time constants the compiler rejects the program outright, which is why the literal example above is a hard error rather than a runtime event.

**A4. Rust has no implicit numeric conversion at all — not even `u8` to `u64`. Why did the language make a choice that C# rejected?**

Because implicit widening is only harmless in the cases where it is harmless, and the compiler cannot see which those are once generics and inference are involved. C# permits widening conversions implicitly, which is fine until an `int` meets a `long` in an expression whose type inference then surprises you, or until a narrowing conversion is written explicitly and silently truncates. Rust requires `u64::from(x)` for the lossless direction (available only when it *is* lossless) and `x as u64` or `u64::try_from(x)` otherwise. The cost is verbosity; the benefit is that every conversion is visible at the point it happens, and `TryFrom` gives you a `Result` for the ones that can fail.

**A5. What is the difference between `[i32; 4]`, `&[i32]`, and `Vec<i32>`, and which of the three is the right parameter type for a function that only reads?**

`[i32; 4]` is a fixed-size array whose length is part of its type, stored inline — the closest C# analogue is a fixed-size buffer or a `Span` over stack memory. `Vec<i32>` is a growable heap-allocated buffer, the `List<int>` analogue, and it owns its data. `&[i32]` is a *slice*: a borrowed view of a contiguous run of `i32`, represented as a pointer plus a length, which is essentially `ReadOnlySpan<int>`. A reading function should take `&[i32]`, because both arrays and `Vec`s coerce to it, so one signature serves every caller without allocating or copying. Taking `&Vec<i32>` instead is a common beginner tell: it needlessly restricts callers to one container.

**A6. Tuples exist in both languages. Name one thing Rust tuples do that C# `ValueTuple` does not, and one thing C# does that Rust does not.**

Rust tuples destructure in pattern position anywhere a pattern is allowed — function parameters, `let`, `match` arms, `for` loops over `zip` — and the destructuring participates in exhaustiveness checking. `let (a, b) = pair;` is the same mechanism as a `match` arm, not a special form. What C# has that Rust does not is *named* tuple elements (`(int Count, string Name)`), which give you `.Count` and `.Name`. Rust tuple fields are positional only (`.0`, `.1`), and the idiomatic response once you want names is to declare a struct — which is cheap, since a struct with all-public fields is three lines and costs nothing at run time.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 03 — Syntax orientation: expressions, shadowing, and integer overflow.

/// Sum a slice, returning `None` on overflow instead of wrapping or panicking.
pub fn checked_sum(values: &[i32]) -> Option<i32> {
    let mut total: i32 = 0;
    for &v in values {
        total = total.checked_add(v)?;
    }
    Some(total)
}

/// Classify a compliance score. Written as a single `match` *expression* whose
/// value is returned — no `return`, no mutable accumulator.
pub fn classify(score: u32) -> &'static str {
    match score {
        0 => "empty",
        1..=49 => "failing",
        50..=89 => "partial",
        90..=99 => "healthy",
        100 => "perfect",
        _ => "invalid",
    }
}

/// Demonstrates shadowing: each `let` creates a *new* binding, so the type may
/// change. Returns the number of resource ids after parsing and de-duplicating.
pub fn distinct_ids(raw: &str) -> usize {
    let raw = raw.trim();
    let raw: Vec<&str> = raw.split(',').map(str::trim).filter(|s| !s.is_empty()).collect();
    let mut raw: Vec<&str> = raw;
    raw.sort_unstable();
    raw.dedup();
    raw.len()
}

/// Wrapping is *opt-in* and explicit, never implicit.
pub fn wrapping_tick(counter: u8) -> u8 {
    counter.wrapping_add(1)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn checked_sum_adds_normally() {
        assert_eq!(checked_sum(&[1, 2, 3]), Some(6));
        assert_eq!(checked_sum(&[]), Some(0));
    }

    #[test]
    fn checked_sum_reports_overflow_instead_of_wrapping() {
        assert_eq!(checked_sum(&[i32::MAX, 1]), None);
        assert_eq!(checked_sum(&[i32::MIN, -1]), None);
    }

    #[test]
    fn classify_is_exhaustive_over_the_range() {
        assert_eq!(classify(0), "empty");
        assert_eq!(classify(49), "failing");
        assert_eq!(classify(50), "partial");
        assert_eq!(classify(90), "healthy");
        assert_eq!(classify(100), "perfect");
        assert_eq!(classify(101), "invalid");
    }

    #[test]
    fn shadowing_changes_the_type_of_a_binding() {
        assert_eq!(distinct_ids(" vm-1, vm-2 ,vm-1 , "), 2);
        assert_eq!(distinct_ids(""), 0);
    }

    #[test]
    fn wrapping_is_explicit() {
        assert_eq!(wrapping_tick(254), 255);
        assert_eq!(wrapping_tick(255), 0);
    }
}
```
