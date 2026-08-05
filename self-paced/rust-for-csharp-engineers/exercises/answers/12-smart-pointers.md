# Answers 12 — Smart pointers

> Exercises: [12-smart-pointers.md](../12-smart-pointers.md)

## Part A

**A1. `Box<T>` is the simplest smart pointer. Name the three situations that require it.**

First, recursive types: an enum or struct that contains itself has no finite size until you put the recursion behind a pointer. Second, trait objects: `dyn Trait` is unsized, so it must live behind `Box`, `&`, `Rc`, or `Arc` to be stored or returned. Third, moving a large value cheaply, or moving it off the stack — boxing turns a memcpy of the whole value into a memcpy of one pointer, which matters for big enums and for deep recursion. Everything a C# programmer does with a class reference falls into the second and third cases; the first is the one with no C# analogue, because every C# class is already a reference.

**A2. `Rc` versus `Arc` — what is the difference, and why isn't `Arc` just always used?**

Both are reference-counted shared ownership; `Rc` uses ordinary non-atomic increments and is therefore not `Send`, while `Arc` uses atomic increments and can cross threads. `Arc` is not the default because atomic read-modify-write operations are meaningfully more expensive than plain increments and inhibit some optimisations, so in single-threaded code — a parser's AST, a UI tree, a rule graph built once on one thread — `Rc` is strictly better. The compiler enforces the distinction: try to send an `Rc` to another thread and you get a `Send` error at compile time, which is the check .NET has no equivalent of, since every reference there is implicitly shareable and thread-safety is a matter of documentation.

**A3. Explain interior mutability. Why is `RefCell` not a hole in the safety guarantees?**

Interior mutability is the ability to mutate through a shared reference, which the borrow rules otherwise forbid. `RefCell` provides it by *moving the borrow check to run time*: it keeps a borrow flag, hands out `Ref`/`RefMut` guards, and panics if you ask for a mutable borrow while any borrow is outstanding. It is not a hole because the rule itself is still enforced — one mutable or many shared, never both — only the enforcement point has moved. What you trade is a compile error for a runtime panic, plus a small bookkeeping cost, and what you buy is the ability to express graphs and caches whose aliasing the static checker cannot follow.

**A4. Rust can leak memory in safe code. Show how, and explain why that is not considered a soundness bug.**

Two `Rc`s pointing at each other — a parent holding a child and the child holding the parent strongly — keep each other's counts above zero forever, so neither is ever dropped. `Box::leak` and `mem::forget` do it deliberately. This is not a soundness bug because leaking is not *unsafe*: no invalid memory is read or written, no aliasing rule is broken, and no undefined behaviour occurs. The language guarantees memory safety, not the absence of leaks, and the standard library says so explicitly. The fix for the cycle case is `Weak` for the back-edge, which participates in the count for liveness (`upgrade` returns `Option`) but not for ownership.

**A5. `Weak` versus C#'s `WeakReference` — what is the same and what is different?**

Both are non-owning handles that must be upgraded before use and may fail. The difference is what makes them fail: a `WeakReference` target dies when the *garbage collector* decides nothing strongly reaches it, at an unpredictable time, so `TryGetTarget` failing tells you a collection happened. A `Weak` target dies deterministically the moment the last `Rc` is dropped, so `upgrade()` returning `None` tells you exactly that the owner is gone. The Rust version is therefore usable for program logic — you can rely on when it becomes `None` — whereas the C# version is really only safe as a cache eviction hint.

**A6. `Mutex<T>` in Rust wraps the data; `lock(obj)` in C# guards a block. What does that change?**

It makes the association between a lock and the data it protects a compile-time fact rather than a comment. In Rust the only way to reach the `T` is through `lock()`, which returns a guard that derefs to `&mut T` and releases on `Drop`, so forgetting to lock is not expressible and forgetting to unlock is not possible — including on a panic. In C# nothing connects `lock(_gate)` to the fields it is supposed to protect; a new method that touches the field without taking the lock compiles fine and fails in production. The Rust design also makes lock *poisoning* visible: if a thread panics holding the lock, subsequent `lock()` calls return an `Err`, so the data's possibly-inconsistent state is surfaced rather than silently propagated.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 12 — `Box`, `Rc`, `RefCell`, `Weak`, and the cost of a cycle.

use std::cell::RefCell;
use std::rc::{Rc, Weak};

/// A management-group-style tree. Children are owned (`Rc`), parents are
/// borrowed (`Weak`) — if both directions were `Rc` the graph would leak, which
/// is the failure mode a tracing GC quietly saves you from.
#[derive(Debug)]
pub struct Node {
    pub name: String,
    pub parent: RefCell<Weak<Node>>,
    pub children: RefCell<Vec<Rc<Node>>>,
}

impl Node {
    pub fn new(name: &str) -> Rc<Node> {
        Rc::new(Node {
            name: name.to_string(),
            parent: RefCell::new(Weak::new()),
            children: RefCell::new(Vec::new()),
        })
    }

    pub fn add_child(parent: &Rc<Node>, child: Rc<Node>) {
        *child.parent.borrow_mut() = Rc::downgrade(parent);
        parent.children.borrow_mut().push(child);
    }

    /// Walk up via the weak link, upgrading each step. `upgrade` returns
    /// `Option`, which is `WeakReference.TryGetTarget` with the null-check
    /// enforced by the type system.
    pub fn path(self: &Rc<Node>) -> String {
        let mut parts = vec![self.name.clone()];
        let mut current = self.parent.borrow().upgrade();
        while let Some(node) = current {
            parts.push(node.name.clone());
            current = node.parent.borrow().upgrade();
        }
        parts.reverse();
        parts.join("/")
    }

    pub fn descendant_count(&self) -> usize {
        self.children
            .borrow()
            .iter()
            .map(|c| 1 + c.descendant_count())
            .sum()
    }
}

/// `Box` gives a recursive type a known size. Without it, `Expr` would be
/// infinitely large and the compiler says so.
#[derive(Debug, PartialEq)]
pub enum Expr {
    Literal(i64),
    Add(Box<Expr>, Box<Expr>),
    Neg(Box<Expr>),
}

impl Expr {
    pub fn eval(&self) -> i64 {
        match self {
            Expr::Literal(v) => *v,
            Expr::Add(a, b) => a.eval() + b.eval(),
            Expr::Neg(e) => -e.eval(),
        }
    }
}

/// Interior mutability: a shared, immutable-looking handle that can still be
/// mutated. `RefCell` moves the borrow check from compile time to run time, so
/// the failure mode is a panic rather than a compile error.
#[derive(Debug, Default)]
pub struct Counter {
    hits: RefCell<u32>,
}

impl Counter {
    /// Note `&self`, not `&mut self` — that is the whole point.
    pub fn record(&self) -> u32 {
        let mut hits = self.hits.borrow_mut();
        *hits += 1;
        *hits
    }

    pub fn get(&self) -> u32 {
        *self.hits.borrow()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn box_makes_a_recursive_enum_representable() {
        let e = Expr::Add(
            Box::new(Expr::Literal(40)),
            Box::new(Expr::Neg(Box::new(Expr::Literal(-2)))),
        );
        assert_eq!(e.eval(), 42);
    }

    #[test]
    fn rc_shares_ownership_and_counts_it() {
        let root = Node::new("tenant");
        assert_eq!(Rc::strong_count(&root), 1);

        let child = Node::new("mg-prod");
        Node::add_child(&root, Rc::clone(&child));

        // `root` holds one strong ref to `child`, and so does our local binding.
        assert_eq!(Rc::strong_count(&child), 2);
        // The child's `parent` is Weak, so root's strong count is unchanged.
        assert_eq!(Rc::strong_count(&root), 1);
    }

    #[test]
    fn weak_parents_prevent_the_cycle_from_leaking() {
        let child_weak;
        {
            let root = Node::new("tenant");
            let child = Node::new("mg-prod");
            Node::add_child(&root, Rc::clone(&child));
            child_weak = Rc::downgrade(&child);
            assert!(child_weak.upgrade().is_some());
        }
        // Root dropped -> its children vector dropped -> child dropped.
        // With an Rc parent link this would still be alive: a leak.
        assert!(child_weak.upgrade().is_none(), "the tree must not be self-sustaining");
    }

    #[test]
    fn upgrading_a_weak_link_walks_back_up() {
        let root = Node::new("tenant");
        let mg = Node::new("mg-prod");
        let sub = Node::new("sub-1");
        Node::add_child(&root, Rc::clone(&mg));
        Node::add_child(&mg, Rc::clone(&sub));

        assert_eq!(sub.path(), "tenant/mg-prod/sub-1");
        assert_eq!(root.descendant_count(), 2);
    }

    #[test]
    fn refcell_permits_mutation_through_a_shared_reference() {
        let counter = Counter::default();
        let shared: &Counter = &counter;
        assert_eq!(shared.record(), 1);
        assert_eq!(shared.record(), 2);
        assert_eq!(counter.get(), 2);
    }

    #[test]
    #[should_panic(expected = "already borrowed")]
    fn refcell_moves_the_borrow_error_to_runtime() {
        let cell = RefCell::new(1);
        let _read = cell.borrow();
        // The compiler happily allows this; RefCell catches it at runtime.
        let _write = cell.borrow_mut();
    }
}
```
