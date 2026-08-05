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
