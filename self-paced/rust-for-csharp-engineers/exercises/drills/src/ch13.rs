//! Drill 13 — Modules, paths, visibility, and re-exports.
//!
//! Nothing here needs a second crate: the module tree is a compile-time
//! construct. Build it so every test compiles *and* the visibility comments
//! stay true. Making everything `pub` also passes — and teaches you nothing,
//! so don't.

// The private helpers look unused until the wrappers below call them.
#![allow(dead_code)]

pub mod engine {
    /// Private to `engine`. Must stay invisible to the crate root.
    fn secret_salt() -> u64 {
        0x5eed
    }

    /// The `internal` analogue: visible crate-wide, invisible to dependents.
    /// TODO: this needs a visibility modifier for the tests to reach it.
    fn internal_seed() -> u64 {
        secret_salt() ^ 0xff
    }

    /// Part of the crate's public contract. Start from `internal_seed()`, then
    /// for each byte of the input: `acc = acc.rotate_left(5) ^ u64::from(b)`.
    pub fn fingerprint(_input: &str) -> u64 {
        todo!()
    }

    pub mod rules {
        /// Visible to the *parent* module only. TODO: which modifier is that?
        fn parent_only() -> &'static str {
            "engine may call me"
        }

        /// A child may reach into its ancestors' private items — privacy in
        /// Rust points outward, not inward. Prove it by returning
        /// `format!("salt-derived: {:#x}", ...)` using `secret_salt`.
        pub fn describe() -> String {
            todo!("super::secret_salt()")
        }

        /// Keeps `parent_only` from being reported as dead code before you
        /// have wired it up. Delete this line once `call_child_private` works.
        #[allow(dead_code)]
        fn _keep_alive() -> &'static str {
            parent_only()
        }
    }

    pub fn call_child_private() -> &'static str {
        todo!("this is the only place allowed to call rules::parent_only()")
    }
}

/// A facade that re-exports a curated surface over the tree above — how a lib
/// root presents a flat public API without flattening its internals.
pub mod api {
    // TODO: `pub use` `fingerprint` and `describe` so they are reachable here.
    pub use super::engine::fingerprint;
    pub use super::engine::rules::describe;
}

pub mod model {
    /// Fields are private by default even when the struct is public — the
    /// opposite of a C# record, and what makes an invariant-preserving
    /// constructor the default rather than a discipline.
    #[derive(Debug, PartialEq, Eq)]
    pub struct ScopeId {
        pub(crate) raw: String,
    }

    impl ScopeId {
        /// `Some` only if `raw` starts with `/`; store it lowercased.
        pub fn new(_raw: &str) -> Option<Self> {
            todo!()
        }

        pub fn as_str(&self) -> &str {
            todo!()
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn public_items_are_reachable_through_their_full_path() {
        assert_eq!(engine::fingerprint(""), engine::internal_seed());
        assert_ne!(engine::fingerprint("a"), engine::fingerprint("b"));
    }

    #[test]
    fn pub_crate_is_the_internal_analogue() {
        // Legal here because the test module is in the same crate.
        assert_eq!(engine::internal_seed(), 0x5eed ^ 0xff);
    }

    #[test]
    fn a_child_may_see_its_parents_private_items() {
        assert_eq!(engine::rules::describe(), "salt-derived: 0x5eed");
    }

    #[test]
    fn pub_super_restricts_visibility_to_the_parent() {
        // Reached only via `engine`; `engine::rules::parent_only()` from here
        // would be a privacy error.
        assert_eq!(engine::call_child_private(), "engine may call me");
    }

    #[test]
    fn re_exports_flatten_a_deep_tree() {
        assert_eq!(api::fingerprint("x"), engine::fingerprint("x"));
        assert_eq!(api::describe(), engine::rules::describe());
    }

    #[test]
    fn private_fields_force_construction_through_the_constructor() {
        let scope = model::ScopeId::new("/Subscriptions/ABC").unwrap();
        assert_eq!(scope.as_str(), "/subscriptions/abc");
        assert!(model::ScopeId::new("subscriptions/abc").is_none());
        // `model::ScopeId { raw: ... }` does not compile outside `model`.
    }
}
