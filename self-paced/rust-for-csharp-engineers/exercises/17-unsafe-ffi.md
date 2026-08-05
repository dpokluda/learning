# Exercises 17 — Unsafe, FFI, and interop

> **Covers:** [17 — Unsafe, FFI, and interop](../17-unsafe-ffi-interop.md). **Code:** `drills/src/ch17.rs`. **Answers:** [answers/17-unsafe-ffi.md](answers/17-unsafe-ffi.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** What exactly does `unsafe` turn off, and what does it not?

**A2.** What is the point of a `// SAFETY:` comment, and what is the corresponding `# Safety` doc section?

**A3.** Edition 2024 changed two pieces of FFI syntax. What are they, and why?

**A4.** You are exposing a Rust library to C#. What are the rules for passing a string across, and who frees it?

**A5.** Why must a panic never cross an `extern "C"` boundary, and what happens if it does?

**A6.** `bindgen` and `cbindgen` — which direction is each, and what is the .NET analogue?

## Part B — Exercise

Open `drills/src/ch17.rs`. The goal is to write `unsafe` the way a library author
writes it: a small block, a stated invariant, and a safe signature around it.

`split_evenly` is `split_at_mut` reimplemented from raw pointers — yes, you
could just call the std function, and that is exactly why it is a good exercise:
you get to see what the safe wrapper is hiding. Then build a C ABI surface,
`drill_normalize` and `drill_free`, of the kind a .NET `LibraryImport` would
bind to, and get the ownership contract right in both directions.

Write the `// SAFETY:` comments and the `# Safety` doc sections. They are not
decoration — they are the difference between code a reviewer can check and code
that merely compiles.

Note one thing the stub already does for you: the two `extern "C"` functions
return placeholder values rather than calling `todo!()`. A panic cannot unwind
across a C ABI boundary, so it aborts the process instead — a `todo!()` there
would kill the entire test binary rather than fail one test. That is a real
constraint on FFI code, and the reason production FFI entry points wrap their
bodies in `catch_unwind`.

Run it with `cargo test ch17` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 17 — `unsafe`, raw pointers, and a C ABI surface.
//!
//! The discipline: `unsafe` is not "turn off the checks", it is "I assert an
//! invariant the compiler cannot verify". Every `unsafe` block below wants a
//! `// SAFETY:` comment stating that invariant — write them, they are graded by
//! your own conscience and by clippy's `undocumented_unsafe_blocks` lint.
//!
//! Edition 2024 changed two things most online material still gets wrong:
//! extern blocks are `unsafe extern "C"`, and `no_mangle` is written
//! `#[unsafe(no_mangle)]`. Both are already correct below.

// `CStr` and `CString` look unused until you write the bodies below.
#![allow(unused_imports)]

use std::ffi::{CStr, CString, c_char};

/// A *safe* function whose body needs `unsafe`: callers cannot break it,
/// because the signature guarantees the slice is valid and the midpoint is
/// computed rather than supplied. This is how `split_at_mut` is built in std —
/// and yes, you could just call it. Use raw pointers instead; that is the drill.
pub fn split_evenly(_slice: &mut [i32]) -> (&mut [i32], &mut [i32]) {
    todo!("as_mut_ptr, then std::slice::from_raw_parts_mut twice")
}

/// Sum the slice by reading through a raw pointer.
pub fn sum_via_raw(_values: &[i64]) -> i64 {
    todo!("as_ptr, then ptr.add(i)")
}

/// The C ABI surface a .NET `LibraryImport` would bind to: trim and lowercase
/// the incoming string, returning a freshly allocated C string. Return null on
/// a null input or on invalid UTF-8.
///
/// Note that this stub returns null rather than calling `todo!()`. A panic
/// cannot unwind across an `extern "C"` boundary — it aborts the process — so a
/// `todo!()` here would take the whole test binary down instead of failing one
/// test. That is a real constraint on FFI code, not an artefact of the drill.
///
/// # Safety
/// TODO: document the contract. What must the caller guarantee about `input`,
/// and who owns the returned pointer?
#[unsafe(no_mangle)]
pub unsafe extern "C" fn drill_normalize(_input: *const c_char) -> *mut c_char {
    // TODO: CStr::from_ptr to borrow, CString::into_raw to hand ownership out.
    std::ptr::null_mut()
}

/// The other half of the contract. Every allocation that crosses the boundary
/// needs a deallocator exported from the same allocator that made it — the FFI
/// equivalent of `IDisposable`, except the caller is in another language and
/// nothing will remind them.
///
/// # Safety
/// TODO: document it.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn drill_free(_ptr: *mut c_char) {
    // TODO: CString::from_raw, then drop — and treat null as a no-op.
}

// Calling *into* the C ABI. The declaration must match the real symbol exactly;
// nothing checks this across a library boundary, which is why `bindgen` exists.
//
// Note the plain `//` comments: rustdoc has nothing to attach a `///` to on an
// extern block and warns with `unused_doc_comment` if you try.
unsafe extern "C" {
    fn abs(input: i32) -> i32;
}

pub fn libc_abs(_value: i32) -> i32 {
    let _ = abs; // remove this line once you call it
    todo!()
}
```

The test module that follows this in the file is the specification — read it before you write anything.
