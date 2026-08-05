//! Part 2 crate drills for *Rust for C# Engineers*.
//!
//! Each module corresponds to one ecosystem chapter of the book. The type
//! definitions, trait impls and function signatures are all here; the bodies
//! are `todo!()`. Your job is to replace each `todo!()` with a real
//! implementation until `cargo test` is green.
//!
//! Run one chapter at a time:
//!
//! ```text
//! cargo test ch16          # async and tokio
//! cargo test ch18          # clap and anyhow
//! cargo test ch19          # thiserror
//! cargo test ch20          # serde
//! cargo test ch22          # axum, reqwest and tracing
//! cargo test ch24          # figment configuration
//! ```
//!
//! Unlike the Part 1 drills, this project has dependencies. Run `cargo build`
//! once while you have a network connection; everything after that works
//! offline from the local package cache.

pub mod ch16;
pub mod ch18;
pub mod ch19;
pub mod ch20;
pub mod ch22;
pub mod ch24;
