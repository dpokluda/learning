# Exercises 12 — Smart pointers

> **Covers:** [12 — Smart pointers](../12-smart-pointers.md). **Code:** `drills/src/ch12.rs`. **Answers:** [answers/12-smart-pointers.md](answers/12-smart-pointers.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** `Box<T>` is the simplest smart pointer. Name the three situations that require it.

**A2.** `Rc` versus `Arc` — what is the difference, and why isn't `Arc` just always used?

**A3.** Explain interior mutability. Why is `RefCell` not a hole in the safety guarantees?

**A4.** Rust can leak memory in safe code. Show how, and explain why that is not considered a soundness bug.

**A5.** `Weak` versus C#'s `WeakReference` — what is the same and what is different?

**A6.** `Mutex<T>` in Rust wraps the data; `lock(obj)` in C# guards a block. What does that change?

## Part B — Exercise

Open `drills/src/ch12.rs`. The goal is to build a tree with parent links that does
not leak, and then to break it deliberately.

The pointer types are given, because the reason for each is the lesson rather
than the guess: children are held by `Rc`, parents by `Weak`, and both sit in a
`RefCell` because the tree is mutated through shared handles. Implement the
construction, the upward walk via `upgrade`, and the recursive count.

Then do the follow-up, which is the real exercise. Change `parent` to hold an
`Rc` instead of a `Weak`, adjust until it compiles, and run the tests again.
`weak_parents_prevent_the_cycle_from_leaking` will fail — and what it is
reporting is a genuine memory leak, in entirely safe Rust, that no borrow check
will ever catch for you. Sit with that for a minute; it is the clearest
demonstration of what the ownership system does and does not promise.

Run it with `cargo test ch12` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 12 — `Box`, `Rc`, `RefCell`, `Weak`, and the cost of a cycle.
//!
//! The pointer types are given, because the *reason* for each one is the
//! lesson rather than the guess. When the tests pass, do the follow-up: change
//! `parent` to `RefCell<Option<Rc<Node>>>`, adjust until it compiles, and watch
//! `weak_parents_prevent_the_cycle_from_leaking` fail. That failure is a real
//! memory leak, in safe Rust, that no borrow check will catch for you.

// Fields look unread while the bodies are still `todo!()`.
#![allow(dead_code)]

use std::cell::RefCell;
use std::rc::{Rc, Weak};

/// A management-group-style tree: children are owned, parents are borrowed.
#[derive(Debug)]
pub struct Node {
    pub name: String,
    pub parent: RefCell<Weak<Node>>,
    pub children: RefCell<Vec<Rc<Node>>>,
}

impl Node {
    pub fn new(_name: &str) -> Rc<Node> {
        todo!()
    }

    /// Link both directions: the child into the parent's vector, and the
    /// parent into the child's `parent` slot as a *weak* reference.
    pub fn add_child(_parent: &Rc<Node>, _child: Rc<Node>) {
        todo!("Rc::downgrade for the back-link")
    }

    /// Walk up to the root and render `"root/.../self"`.
    pub fn path(self: &Rc<Node>) -> String {
        todo!("Weak::upgrade returns Option — the null check, enforced")
    }

    pub fn descendant_count(&self) -> usize {
        todo!()
    }
}

/// `Box` is what gives a recursive type a known size. Without it the compiler
/// reports an infinitely-sized type and suggests exactly this fix.
#[derive(Debug, PartialEq)]
pub enum Expr {
    Literal(i64),
    Add(Box<Expr>, Box<Expr>),
    Neg(Box<Expr>),
}

impl Expr {
    pub fn eval(&self) -> i64 {
        todo!()
    }
}

/// Interior mutability. Note that `record` takes `&self`, not `&mut self` —
/// that is the entire point of the type.
#[derive(Debug, Default)]
pub struct Counter {
    pub(crate) hits: RefCell<u32>,
}

impl Counter {
    /// Increment and return the new value.
    pub fn record(&self) -> u32 {
        todo!()
    }

    pub fn get(&self) -> u32 {
        todo!()
    }
}
```

The test module that follows this in the file is the specification — read it before you write anything.
