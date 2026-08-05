# Answers 13 — Modules and crates

> Exercises: [13-modules.md](../13-modules.md)

## Part A

**A1. A crate, a module, and a workspace map onto what in .NET terms — and where does the mapping break?**

A crate is the unit of compilation and the closest analogue of an assembly: it is what is versioned, published, and depended upon, and it is the boundary `pub` refers to. A module is a namespace-like organisational unit *inside* a crate, except that unlike a namespace it is also a privacy boundary and it nests meaningfully. A workspace is a solution: several crates sharing one `Cargo.lock`, one `target/` directory, and one build. The mapping breaks in two places — a crate is compiled as a single unit, so there is no per-file compilation and no partial classes, and modules do not have to correspond to directories the way C# convention ties namespaces to folders.

**A2. Rust items are private by default. What are the visibility levels, and which one is `internal`?**

Private (the default, meaning visible in the defining module and its descendants), `pub(self)` which is the same thing written out, `pub(super)` for the parent module, `pub(crate)` for the whole crate, `pub(in path)` for a named ancestor module, and `pub` for everyone. `pub(crate)` is the `internal` analogue. The default being private is the notable difference from C#, where the default is `internal` at the type level and `private` at the member level; in Rust everything starts closed and you open it deliberately, which makes the public surface of a crate something you construct rather than something that accumulates.

**A3. Explain why a child module can see its parent's private items but not vice versa.**

Privacy in Rust is defined as "visible in the defining module and everything nested inside it". A child is nested inside its parent, so the parent's private items are in scope for it; the parent is not nested inside the child, so the reverse does not hold. The mental model is that a module is a box, and things inside the box can see everything in the box including the boxes further in — but the outer box cannot reach inside. This is what makes it practical to keep a large module's helpers private while splitting the implementation into submodules, and it is why `super::` shows up so often in real code.

**A4. What are Cargo features for, and what is the trap with them?**

Features are named, additive compile-time options that enable optional dependencies and `#[cfg(feature = "...")]` code — the mechanism behind `serde`'s `derive`, `tokio`'s `full`, and `reqwest`'s TLS backends. The trap is that they are *unified*: if two crates in your graph depend on the same crate with different feature sets, Cargo enables the union for everyone, so a feature is only safe if enabling it never removes or changes behaviour. That is why the guideline is that features must be purely additive, and why mutually exclusive features are an anti-pattern that produces build failures nobody in your dependency chain can fix.

**A5. `Cargo.lock`: commit it or not?**

Commit it for binaries and anything else you ship or deploy, so builds are reproducible and everyone gets the same dependency graph. Historically the advice was to omit it for libraries, so that downstream consumers resolve their own versions and CI exercises the newest compatible set; modern practice is more nuanced — committing it in a library is fine and gives reproducible CI, because a `Cargo.lock` in a dependency is *ignored* by the consuming build. The .NET analogue is closest to `packages.lock.json`, with the same reasoning, though NuGet's floating-version behaviour differs enough that the habits do not transfer directly.

**A6. What does `build.rs` do, and what is the closest MSBuild concept?**

It is a Rust program Cargo compiles and runs before building your crate, used to generate code, compile bundled C, probe the environment, or emit `cargo::rustc-cfg` and `cargo::rerun-if-changed` directives back to the build system. The closest MSBuild concept is a custom target or task hooked into the build, though `build.rs` is a plain program rather than XML, which makes it far more approachable and far easier to make slow. The rules to internalise are that it runs on the *host* (which matters when cross-compiling), that its output goes in `OUT_DIR` and is included with `include!`, and that without correct `rerun-if-changed` directives it will either rerun on every build or fail to rerun when it should.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

Note the fence is marked `ignore`, and for a reason that is itself the chapter's subject: this solution *is* a module tree, and its inner `tests` module reaches its sibling through `super::engine`. That path only resolves when the file is a crate module, which is how it lives in `drills/src/ch13.rs`. Extracted into a standalone doctest — where rustdoc wraps everything in a `fn main` and `super` therefore means the crate root — the same code cannot compile. Paths in Rust are relative to where an item *is*, and this is what that means in practice.

```rust,ignore
//! Drill 13 — Modules, paths, visibility, and re-exports.
//!
//! Nothing here needs a second crate: the module tree is the interesting part,
//! and it is entirely a compile-time construct. The mental shift from C# is that
//! a module is *not* a namespace — it is a privacy boundary that nests, and the
//! default is private rather than `internal`.

/// `pub` on the module makes the module *reachable*; it says nothing about what
/// is inside it. Both halves must be public for an item to escape.
pub mod engine {
    /// Private to `engine`. Not even the crate root can see it.
    fn secret_salt() -> u64 {
        0x5eed
    }

    /// `pub(crate)` is the closest thing to C#'s `internal`: visible everywhere
    /// in this crate, invisible to anyone who depends on it.
    pub(crate) fn internal_seed() -> u64 {
        secret_salt() ^ 0xff
    }

    /// Fully public: part of the crate's semver contract.
    pub fn fingerprint(input: &str) -> u64 {
        let mut acc = internal_seed();
        for b in input.bytes() {
            acc = acc.rotate_left(5) ^ u64::from(b);
        }
        acc
    }

    pub mod rules {
        /// `pub(super)` restricts visibility to the *parent* module, `engine`.
        pub(super) fn parent_only() -> &'static str {
            "engine may call me"
        }

        /// A child module can always reach into its ancestors, private items
        /// included. Privacy in Rust points outward, not inward.
        pub fn describe() -> String {
            format!("salt-derived: {:#x}", super::secret_salt())
        }
    }

    pub fn call_child_private() -> &'static str {
        rules::parent_only()
    }
}

/// A facade module that re-exports someone else's item under its own path.
/// `pub use` is how a crate presents a flat, curated surface over a deep
/// internal tree — the `polcheck` lib root does exactly this.
pub mod api {
    pub use super::engine::fingerprint;
    pub use super::engine::rules::describe;
}

/// Struct fields are private by default even when the struct is public. This is
/// the opposite of a C# `record`, and it is what makes the newtype pattern and
/// invariant-preserving constructors the default rather than a discipline.
pub mod model {
    #[derive(Debug, PartialEq, Eq)]
    pub struct ScopeId {
        raw: String,
    }

    impl ScopeId {
        /// The only way in, so the invariant cannot be bypassed.
        pub fn new(raw: &str) -> Option<Self> {
            if raw.starts_with('/') {
                Some(Self { raw: raw.to_ascii_lowercase() })
            } else {
                None
            }
        }

        pub fn as_str(&self) -> &str {
            &self.raw
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
```
