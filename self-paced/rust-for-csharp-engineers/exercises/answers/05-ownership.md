# Answers 05 — Ownership and moves

> Exercises: [05-ownership.md](../05-ownership.md)

## Part A

**A1. Explain what happens to `a` in `let a = String::from("x"); let b = a;` and contrast it with the same two lines in C#.**

The `String` is *moved* into `b`: the three words that make up the `String` (pointer, length, capacity) are copied into the new binding, and `a` is marked dead by the compiler. Using `a` afterwards is a compile error, `borrow of moved value`. In C#, both `a` and `b` would be references to the same heap object, both usable, and the GC would free the object once neither could reach it. The Rust model has exactly one owner at a time, which is what lets the compiler insert the deallocation at a statically known point — no GC, no reference counting, no finalizer queue.

**A2. What is the difference between `Copy` and `Clone`, and why is `Copy` not simply derived for every type where it would work?**

`Clone` is an explicit, potentially expensive duplication you invoke by calling `.clone()`. `Copy` is a marker saying "duplicating this value is a bitwise copy with no extra work", which changes assignment semantics: assigning a `Copy` value copies it instead of moving it, so the original stays usable. `Copy` is not automatic because it is a *semantic* choice with API consequences: once a public type is `Copy`, making it non-`Copy` later — by adding a `String` field, say — is a breaking change for every caller who relied on the original staying alive after assignment. It is also mutually exclusive with `Drop`, since a type with a destructor cannot be duplicated by memcpy without double-freeing.

**A3. You have `fn consume(r: Resource)` and you need to call it twice on the same value. What are your options, ranked?**

First, ask whether it should take `&Resource` — if it only reads, borrowing removes the problem entirely and is almost always the right answer. Second, if it genuinely consumes but you need the value afterwards, restructure so it returns the value back (`fn consume(r: Resource) -> Resource`), which is the builder-style threading pattern. Third, clone: correct but pays for a heap copy, and reaching for it reflexively is the C# habit that produces slow Rust. Fourth, if the value is genuinely shared and long-lived, wrap it in `Rc`/`Arc` so cloning the handle is cheap. The ranking matters because a beginner's instinct is to jump straight to `.clone()` and never revisit it.

**A4. Why does `std::mem::take(&mut self.tags)` exist, and what would happen without it?**

You cannot move a value out of a `&mut` reference, because doing so would leave the referent in an invalid, partially-moved state that the borrow checker cannot track — the caller still holds a live reference to something that no longer contains a value. `mem::take` resolves it by swapping `Default::default()` into the slot as it moves the old value out, so the referent is always valid. Without it you would be forced to clone the vector and then clear the original, paying a full copy for nothing, or to restructure the type to hold `Option<Vec<_>>` so you could `.take()` the option. `mem::replace` is the same trick when the replacement is not the default.

**A5. A value is dropped at the end of its scope. What is the Rust analogue of `using`/`IDisposable`, and where do the two models differ most?**

`Drop` is the analogue, and the crucial difference is that it is not opt-in at the call site: a value with a `Drop` impl runs its destructor automatically when its owner goes out of scope, in reverse declaration order, on every path including panics. C# requires the caller to remember `using` (or to route through `await using`), and a forgotten `using` is a silent leak that only a finalizer might eventually paper over. In Rust the compiler places the call, so the failure mode inverts: leaks require effort (`mem::forget`, `Box::leak`, or an `Rc` cycle) rather than being the default when you are careless. The other difference is that Rust has no finalizers at all — there is no second-chance mechanism — which is why `Drop` is reliable enough to build `MutexGuard` and `File` on.

**A6. What does 'partial move' mean, and when will you hit it?**

Moving one field out of a struct by value moves *that field only*, leaving the struct partially moved: you may still use the untouched fields, but you cannot use the struct as a whole, pass it by value, or let it be dropped as a unit. You hit it in destructuring (`let Resource { id, .. } = r;` moves `id` out of `r`) and in match arms that bind by value. The usual fixes are to destructure everything at once so nothing is left behind, to bind by reference with `ref`/`&` patterns, or to use `mem::take` on the field you need. It is worth recognising the error text — `use of partially moved value` — because the cause is rarely where the message points.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 05 — Moves, `Copy`, `Clone`, and moving out of a `&mut`.

/// A deliberately non-`Copy` type: it owns a heap allocation.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Resource {
    pub id: String,
    pub tags: Vec<String>,
}

impl Resource {
    pub fn new(id: &str) -> Self {
        Self { id: id.to_string(), tags: Vec::new() }
    }
}

/// A `Copy` type: every field is `Copy`, so assignment duplicates bits and the
/// original stays usable. This is the C# `struct` analogue, but opt-in.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Severity(pub u8);

/// Takes ownership. After calling this, the caller's binding is dead.
pub fn consume(resource: Resource) -> usize {
    resource.tags.len()
}

/// Borrows. The caller keeps ownership and can use the value afterwards.
pub fn inspect(resource: &Resource) -> usize {
    resource.tags.len()
}

/// Drain a collection out of a `&mut` without cloning and without leaving a
/// hole the borrow checker would reject. `mem::take` swaps in `Default`.
pub fn drain_tags(resource: &mut Resource) -> Vec<String> {
    std::mem::take(&mut resource.tags)
}

/// Replace and hand back the old value in one move — no clone, no `Option`
/// dance. The C# equivalent needs a temporary and two assignments.
pub fn rename(resource: &mut Resource, new_id: &str) -> String {
    std::mem::replace(&mut resource.id, new_id.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn copy_types_survive_assignment() {
        let a = Severity(3);
        let b = a; // copy, not move
        assert_eq!(a, b); // `a` is still usable — this is the whole point
        assert_eq!(a.0, 3);
    }

    #[test]
    fn clone_is_an_explicit_deep_duplicate() {
        let mut original = Resource::new("vm-1");
        original.tags.push("prod".into());

        let copy = original.clone();
        original.tags.push("mutated".into());

        assert_eq!(copy.tags, vec!["prod".to_string()]);
        assert_eq!(original.tags.len(), 2);
    }

    #[test]
    fn borrowing_leaves_the_caller_in_possession() {
        let mut r = Resource::new("vm-1");
        r.tags.push("prod".into());

        assert_eq!(inspect(&r), 1);
        assert_eq!(inspect(&r), 1); // still ours

        assert_eq!(consume(r), 1); // now it is gone
        // `r` may not be used here; that is a compile error, not a runtime one.
    }

    #[test]
    fn take_moves_out_and_leaves_a_default_behind() {
        let mut r = Resource::new("vm-1");
        r.tags.push("prod".into());
        r.tags.push("linux".into());

        let taken = drain_tags(&mut r);

        assert_eq!(taken.len(), 2);
        assert!(r.tags.is_empty(), "take must leave Default::default() in place");
    }

    #[test]
    fn replace_returns_the_previous_value() {
        let mut r = Resource::new("vm-1");
        let old = rename(&mut r, "vm-2");
        assert_eq!(old, "vm-1");
        assert_eq!(r.id, "vm-2");
    }
}
```
