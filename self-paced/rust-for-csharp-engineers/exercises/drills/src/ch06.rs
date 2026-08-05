//! Drill 06 — Borrowing, NLL, disjoint mutable borrows, and lifetimes.
//!
//! The lifetime annotations are supplied here so the file compiles. Your job is
//! the bodies — but read the signatures and be able to say *why* `longest`
//! needs `<'a>` and `trim_scope` does not.

// Fields look unread while the bodies are still `todo!()`.
#![allow(dead_code)]

/// Return whichever argument is longer. The result may borrow from either
/// input, which is why they must share one lifetime parameter.
pub fn longest<'a>(_a: &'a str, _b: &'a str) -> &'a str {
    todo!()
}

/// Strip leading and trailing `/`. Elision covers this: one input reference, so
/// the output must borrow from it. Adding `<'a>` here would be noise.
pub fn trim_scope(_scope: &str) -> &str {
    todo!()
}

/// A struct that *holds* a borrow. The `<'a>` is the compiler's proof that
/// `source` outlives every `ScopeParser` built from it.
#[derive(Debug)]
pub struct ScopeParser<'a> {
    pub(crate) source: &'a str,
}

impl<'a> ScopeParser<'a> {
    pub fn new(_source: &'a str) -> Self {
        todo!()
    }

    /// Split on `/`, discarding empty segments. Note the return type: the
    /// slices borrow from the *source*, not from `self`.
    pub fn segments(&self) -> Vec<&'a str> {
        todo!()
    }
}

/// Double the first half of the slice and decrement the second half, holding
/// two mutable borrows at once. The obvious attempt is rejected; find the safe
/// std function that proves the halves are disjoint.
pub fn normalize_halves(_scores: &mut [i32]) {
    todo!("split_at_mut")
}

/// Increment the counter at `key`, inserting 0 first if absent, and return the
/// new value. Lookup-then-insert is a borrow error; the fix does both under one
/// borrow.
pub fn bump(_map: &mut std::collections::HashMap<String, u32>, _key: &str) -> u32 {
    todo!("HashMap::entry")
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    #[test]
    fn longest_requires_a_shared_lifetime() {
        let a = String::from("subscriptions");
        let winner = {
            let b = String::from("rg");
            // Both live long enough *here*, so this compiles.
            longest(&a, &b).to_string()
        };
        assert_eq!(winner, "subscriptions");
    }

    #[test]
    fn elision_covers_the_single_input_case() {
        assert_eq!(trim_scope("/subscriptions/a/"), "subscriptions/a");
    }

    #[test]
    fn a_struct_may_borrow_its_source() {
        let source = String::from("/subscriptions/abc/resourceGroups/rg1");
        let parser = ScopeParser::new(&source);
        assert_eq!(parser.segments(), vec!["subscriptions", "abc", "resourceGroups", "rg1"]);
    }

    #[test]
    fn disjoint_mutable_borrows_are_allowed_when_proven() {
        let mut scores = [1, 2, 3, 4];
        normalize_halves(&mut scores);
        assert_eq!(scores, [2, 4, 2, 3]);
    }

    #[test]
    fn nll_ends_a_borrow_at_its_last_use() {
        let mut owner = vec![1, 2, 3];
        let first = owner[0]; // borrow ends immediately (i32 is Copy)
        owner.push(4); // legal: no live borrow
        assert_eq!(first, 1);
        assert_eq!(owner.len(), 4);
    }

    #[test]
    fn entry_performs_lookup_and_insert_under_one_borrow() {
        let mut map: HashMap<String, u32> = HashMap::new();
        assert_eq!(bump(&mut map, "deny"), 1);
        assert_eq!(bump(&mut map, "deny"), 2);
        assert_eq!(bump(&mut map, "audit"), 1);
    }
}
