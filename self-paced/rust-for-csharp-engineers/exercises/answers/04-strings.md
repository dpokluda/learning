# Answers 04 — Strings and slices

> Exercises: [04-strings.md](../04-strings.md)

## Part A

**A1. `String` and `&str` are both UTF-8 text. Describe each one's memory layout and say why the language needs both when C# gets by with one `string`.**

`String` is a growable, owned, heap-allocated UTF-8 buffer: a pointer, a length, and a capacity. `&str` is a *borrowed view* of UTF-8 bytes — just a pointer and a length — and it can point into a `String`, into a static literal baked into the binary, or into the middle of either. C# gets by with one type because `string` is immutable and garbage-collected, so sharing a reference is always safe and substrings simply allocate a new object. Rust has no GC, so the language needs to distinguish "I own this buffer and will free it" from "I am looking at someone else's buffer, which must outlive me" — and that distinction is exactly `String` versus `&str`. The payoff is that slicing a `&str` out of a `String` is free, where C#'s `Substring` allocates (which is why `ReadOnlySpan<char>` was eventually added to claw the cost back).

**A2. Why does `&s[0..1]` sometimes panic, and what is the C# equivalent of the mistake?**

String indices in Rust are *byte* offsets, and slicing panics if either end falls inside a multi-byte UTF-8 code point. `"déní"[0..1]` is fine (`d` is one byte) but `[0..2]` splits `é` and panics with `byte index 2 is not a char boundary`. The closest C# mistake is indexing a `string` by `char` and splitting a surrogate pair — `"😀".Substring(0, 1)` hands you half an astral character. The difference is that C# silently gives you nonsense while Rust refuses at run time, which is the better failure. The fix is to work in `char_indices()` when you need character semantics, or to accept that most text operations (`split`, `trim`, `find`, `starts_with`) already return boundary-correct slices.

**A3. A function needs to accept text it will only read. Should it take `String`, `&String`, `&str`, or `impl AsRef<str>`?**

`&str` is the default and correct answer: it accepts string literals, `String`s (through deref coercion), and sub-slices of either, with no allocation and no ownership transfer. `&String` is strictly worse — it forbids literals and adds a pointer hop for nothing. `String` is right only when the function genuinely needs to *keep* the text, and taking it by value makes the caller's transfer of ownership explicit at the call site. `impl AsRef<str>` is a generic superset that also accepts `Cow<str>`, `Box<str>` and friends; it is worth reaching for in public library APIs where callers hold varied types, but it monomorphises per caller type and makes the signature noisier, so it is not the everyday choice.

**A4. What does `.to_string()`, `.to_owned()`, `String::from()`, and `.into()` each do to a `&str`, and does the choice matter?**

All four produce an owned `String` containing a copy of the bytes, and for a `&str` they compile to the same thing. The distinctions are about intent and generality: `String::from(s)` is the direct constructor; `.to_owned()` is the `ToOwned` trait method that says "give me the owned form of this borrowed thing" and generalises to `[T] -> Vec<T>`; `.into()` is inference-driven and reads well when the target type is already obvious from context; and `.to_string()` goes through `Display`, so it also works on anything printable. Idiomatically, prefer `to_owned()` when converting a borrow to its owned counterpart and `to_string()` when formatting a value into text. The one to watch is `.to_string()` on something that is already a `String` — that is a needless clone, and clippy flags it.

**A5. What problem does `Cow<'_, str>` solve, and what would you write in C# instead?**

`Cow` — clone-on-write — lets a function return either a borrow of its input or a newly allocated value, chosen at run time, behind one type. A normaliser that lowercases only when the input contains uppercase can return `Cow::Borrowed(input)` in the common case and allocate only when it must, so the caller writes the same code either way. In C# there is no equivalent type; you either always allocate (`return s.ToLowerInvariant();`) or hand-roll a `bool changed` flag and a nullable result, and callers must handle both. `Cow` is the shape that makes "usually free, occasionally not" expressible in a signature.

**A6. `"hello".len()` returns 5, and `"héllo".len()` returns 6. What is `len()` actually counting, and what should you call instead?**

It counts *bytes* in the UTF-8 encoding, which is the number that matters for allocation, slicing, and I/O, and is why it is the one exposed as `len()`. For characters you want `.chars().count()`, which is O(n) because UTF-8 is variable-width — the cost is visible in the API rather than hidden. Note that even `chars()` counts Unicode scalar values, not user-perceived characters: a family emoji or a combining accent is several `char`s. C#'s `string.Length` counts UTF-16 code units, which has exactly the same class of problem (it is 2 for an emoji), so this is less a Rust quirk than a Unicode fact that Rust declines to paper over.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 04 — `String` vs `&str`, byte indices, and char boundaries.

/// Return the first whitespace-delimited word as a *borrow* of the input.
/// No allocation: the return type ties the result's lifetime to `input`.
pub fn first_word(input: &str) -> &str {
    match input.find(char::is_whitespace) {
        Some(idx) => &input[..idx],
        None => input,
    }
}

/// Truncate to at most `max_chars` **characters** (not bytes), never panicking
/// and never splitting a UTF-8 code point. This is the drill: `&s[..n]` is a
/// *byte* slice and panics on a non-boundary index.
pub fn truncate_chars(input: &str, max_chars: usize) -> &str {
    match input.char_indices().nth(max_chars) {
        Some((byte_idx, _)) => &input[..byte_idx],
        None => input,
    }
}

/// Only allocates when it actually has to change something — the manual
/// version of what `Cow` automates.
pub fn normalize_scope(scope: &str) -> String {
    if scope.starts_with('/') {
        scope.to_ascii_lowercase()
    } else {
        format!("/{}", scope.to_ascii_lowercase())
    }
}

/// Accepts `&str`, `&String`, or `&Path`-like input via deref coercion at the
/// call site; the generic bound is the idiomatic way to be permissive.
pub fn longest_word<S: AsRef<str>>(text: S) -> String {
    text.as_ref()
        .split_whitespace()
        .max_by_key(|w| w.chars().count())
        .unwrap_or("")
        .to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn first_word_borrows_rather_than_allocates() {
        let owned = String::from("policy assignment scope");
        assert_eq!(first_word(&owned), "policy");
        assert_eq!(first_word("single"), "single");
        assert_eq!(first_word(""), "");
    }

    #[test]
    fn truncate_counts_chars_not_bytes() {
        // "é" is two bytes; a naive &s[..2] would be fine here but &s[..1] panics.
        assert_eq!(truncate_chars("déní", 2), "dé");
        assert_eq!(truncate_chars("déní", 99), "déní");
        assert_eq!(truncate_chars("", 3), "");
    }

    #[test]
    fn truncate_never_splits_a_code_point() {
        let s = "αβγδ"; // 2 bytes each
        for n in 0..=6 {
            // The point of the drill: this must not panic for any n.
            let t = truncate_chars(s, n);
            assert!(s.starts_with(t));
        }
    }

    #[test]
    fn normalize_adds_the_leading_slash_only_when_missing() {
        assert_eq!(normalize_scope("/Subscriptions/A"), "/subscriptions/a");
        assert_eq!(normalize_scope("Subscriptions/A"), "/subscriptions/a");
    }

    #[test]
    fn as_ref_accepts_both_string_and_str() {
        assert_eq!(longest_word("a bb ccc"), "ccc");
        assert_eq!(longest_word(String::from("a bb ccc")), "ccc");
        assert_eq!(longest_word(""), "");
    }
}
```
