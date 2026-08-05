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
