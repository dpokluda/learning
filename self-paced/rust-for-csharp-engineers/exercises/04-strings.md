# Exercises 04 — Strings and slices

> **Covers:** [04 — Strings and slices](../04-strings-and-slices.md). **Code:** `drills/src/ch04.rs`. **Answers:** [answers/04-strings.md](answers/04-strings.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** `String` and `&str` are both UTF-8 text. Describe each one's memory layout and say why the language needs both when C# gets by with one `string`.

**A2.** Why does `&s[0..1]` sometimes panic, and what is the C# equivalent of the mistake?

**A3.** A function needs to accept text it will only read. Should it take `String`, `&String`, `&str`, or `impl AsRef<str>`?

**A4.** What does `.to_string()`, `.to_owned()`, `String::from()`, and `.into()` each do to a `&str`, and does the choice matter?

**A5.** What problem does `Cow<'_, str>` solve, and what would you write in C# instead?

**A6.** `"hello".len()` returns 5, and `"héllo".len()` returns 6. What is `len()` actually counting, and what should you call instead?

## Part B — Exercise

Open `drills/src/ch04.rs`. The goal is to stop thinking of text as a `string`
object and start thinking about who owns the bytes.

Three of the four functions must return a *borrow* of their input rather than an
allocation, which the signatures already commit you to — your job is to make the
bodies honour it. The interesting one is `truncate_chars`, which must limit a
string to a number of characters without ever panicking and without splitting a
UTF-8 code point. There is a test that calls it with every truncation length
from zero to twice the string's length and asserts it never panics; the obvious
byte-slicing implementation fails it immediately. Getting that right is the
moment the byte-versus-character distinction stops being trivia.

Run it with `cargo test ch04` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 04 — `String` vs `&str`, byte indices, and char boundaries.
//!
//! The recurring trap: `&s[..n]` slices by *bytes* and panics if `n` is not a
//! UTF-8 code point boundary. Two tests here exist to catch exactly that.

/// Return the first whitespace-delimited word as a *borrow* of the input.
/// Do not allocate — the signature already ties the result's life to `input`.
pub fn first_word(_input: &str) -> &str {
    todo!("borrow a sub-slice; no String, no to_string()")
}

/// Truncate to at most `max_chars` **characters**, never panicking and never
/// splitting a code point. `char_indices` hands you the byte offset of each.
pub fn truncate_chars(_input: &str, _max_chars: usize) -> &str {
    todo!("count chars, slice on the byte offset you get back")
}

/// Lowercase the scope and guarantee a leading `/`, allocating once.
pub fn normalize_scope(_scope: &str) -> String {
    todo!()
}

/// The longest whitespace-delimited word, measured in characters.
/// The `AsRef<str>` bound is what lets callers pass `&str` *or* `String`.
pub fn longest_word<S: AsRef<str>>(_text: S) -> String {
    todo!("max_by_key over chars().count()")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
