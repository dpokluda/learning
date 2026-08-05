# Exercises 05 — Ownership and moves

> **Covers:** [05 — Ownership and moves](../05-ownership-and-moves.md). **Code:** `drills/src/ch05.rs`. **Answers:** [answers/05-ownership.md](answers/05-ownership.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** Explain what happens to `a` in `let a = String::from("x"); let b = a;` and contrast it with the same two lines in C#.

**A2.** What is the difference between `Copy` and `Clone`, and why is `Copy` not simply derived for every type where it would work?

**A3.** You have `fn consume(r: Resource)` and you need to call it twice on the same value. What are your options, ranked?

**A4.** Why does `std::mem::take(&mut self.tags)` exist, and what would happen without it?

**A5.** A value is dropped at the end of its scope. What is the Rust analogue of `using`/`IDisposable`, and where do the two models differ most?

**A6.** What does 'partial move' mean, and when will you hit it?

## Part B — Exercise

Open `drills/src/ch05.rs`. The goal is to feel the difference between a move, a
copy, and a clone, and to learn the two `std::mem` functions that get you out of
the situation where you need to move something out of a `&mut`.

Start by reading the first test: it will not compile until you add a derive to
`Severity`, and the error message — `borrow of moved value` — is the whole
lesson about why `Copy` changes assignment semantics. Then implement the rest.
Two of the functions can be written with a clone and will still pass some tests
while failing others; the assertions are written to tell you which one you
wrote.

Run it with `cargo test ch05` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 05 — Moves, `Copy`, `Clone`, and moving out of a `&mut`.
//!
//! A solution that clones its way out of trouble passes some of these tests and
//! fails others. Read the assertions.

/// Deliberately *not* `Copy`: it owns a heap allocation.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Resource {
    pub id: String,
    pub tags: Vec<String>,
}

impl Resource {
    pub fn new(_id: &str) -> Self {
        todo!()
    }
}

/// Every field is `Copy`, so this type can be too — and the first test will not
/// compile until it is. Add the derive.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Severity(pub u8);

/// Takes ownership: after this call the caller's binding is dead.
pub fn consume(_resource: Resource) -> usize {
    todo!()
}

/// Borrows: the caller keeps ownership.
pub fn inspect(_resource: &Resource) -> usize {
    todo!()
}

/// Move the tag vector *out* of a `&mut`, leaving an empty one behind.
/// No clone. Look in `std::mem`.
pub fn drain_tags(_resource: &mut Resource) -> Vec<String> {
    todo!("std::mem has exactly the function for this")
}

/// Set a new id and return the old one, in a single move.
pub fn rename(_resource: &mut Resource, _new_id: &str) -> String {
    todo!("std::mem again — a different function")
}
```

The test module that follows this in the file is the specification — read it before you write anything.
