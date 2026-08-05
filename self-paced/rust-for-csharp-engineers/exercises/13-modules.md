# Exercises 13 — Modules and crates

> **Covers:** [13 — Modules and crates](../13-modules-and-crates.md). **Code:** `drills/src/ch13.rs`. **Answers:** [answers/13-modules.md](answers/13-modules.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** A crate, a module, and a workspace map onto what in .NET terms — and where does the mapping break?

**A2.** Rust items are private by default. What are the visibility levels, and which one is `internal`?

**A3.** Explain why a child module can see its parent's private items but not vice versa.

**A4.** What are Cargo features for, and what is the trap with them?

**A5.** `Cargo.lock`: commit it or not?

**A6.** What does `build.rs` do, and what is the closest MSBuild concept?

## Part B — Exercise

Open `drills/src/ch13.rs`. The goal is to build a module tree whose privacy is
load-bearing rather than decorative.

The file starts with a deliberate compile error: `internal_seed` is private and
the tests need it. Fixing that is the exercise — choose the visibility modifier
that means "visible throughout this crate, invisible to anything that depends on
it", and be able to say which C# keyword it corresponds to. There is a second
one in the nested `rules` module, where a function must be visible to its parent
and to nothing else.

Note that making everything `pub` also makes the tests pass. That is the trap:
the assertions cannot distinguish a correct visibility from an over-permissive
one, so this is the one drill where you have to grade yourself. Read the doc
comment on each item and honour it.

Run it with `cargo test ch13` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
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
```

The test module that follows this in the file is the specification — read it before you write anything.
