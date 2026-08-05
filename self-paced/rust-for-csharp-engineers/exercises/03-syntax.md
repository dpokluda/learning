# Exercises 03 — Syntax orientation

> **Covers:** [03 — Syntax orientation](../03-syntax-orientation.md). **Code:** `drills/src/ch03.rs`. **Answers:** [answers/03-syntax.md](answers/03-syntax.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** In C#, `int x = 1;` and `int x = 2;` in the same scope is a compile error. In Rust, `let x = 1; let x = 2;` is idiomatic. What is actually happening, and why is it not just a relaxed rule?

**A2.** What does `let x = if cond { 1 } else { 2 };` compile to, and what is the equivalent C#? Where does the analogy stop?

**A3.** What happens on `let x: u8 = 255; let y = x + 1;` in a debug build? In a release build? Why is that not undefined behaviour?

**A4.** Rust has no implicit numeric conversion at all — not even `u8` to `u64`. Why did the language make a choice that C# rejected?

**A5.** What is the difference between `[i32; 4]`, `&[i32]`, and `Vec<i32>`, and which of the three is the right parameter type for a function that only reads?

**A6.** Tuples exist in both languages. Name one thing Rust tuples do that C# `ValueTuple` does not, and one thing C# does that Rust does not.

## Part B — Exercise

Open `drills/src/ch03.rs`. The goal is to write four small functions that
between them demonstrate that Rust's arithmetic is *defined* rather than
accidental, and that its blocks are expressions rather than statements.

Concretely: sum a slice in a way that reports overflow instead of silently
wrapping or panicking; classify a number into named bands using a single
expression with no early return; refine a string through several types using
shadowing rather than a family of similarly-named bindings; and increment a
counter with modular arithmetic stated explicitly. None of these is hard. The
point is that each one has a wrong version that a C# habit would produce, and
the tests are written to catch that version.

Run it with `cargo test ch03` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 03 — Syntax orientation: expressions, shadowing, integer overflow.
//!
//! Read the test names first: each one states the idea it proves.

/// Sum a slice, returning `None` on overflow rather than wrapping or panicking.
/// Look at the `checked_*` family on the integer types.
pub fn checked_sum(_values: &[i32]) -> Option<i32> {
    todo!("return None instead of overflowing")
}

/// Classify a compliance score as a single `match` *expression* whose value is
/// the return value — no `return`, no mutable accumulator.
/// 0 => "empty", 1..=49 => "failing", 50..=89 => "partial",
/// 90..=99 => "healthy", 100 => "perfect", anything else => "invalid".
pub fn classify(_score: u32) -> &'static str {
    todo!("one match expression, no early returns")
}

/// Count the distinct, non-empty, trimmed comma-separated ids in `raw`.
/// Use shadowing — repeated `let raw = ...` — rather than inventing `raw2`.
pub fn distinct_ids(_raw: &str) -> usize {
    todo!("shadow the binding as you refine it")
}

/// Increment, wrapping 255 back to 0 — explicitly.
pub fn wrapping_tick(_counter: u8) -> u8 {
    todo!("wrapping is opt-in, never implicit")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
