# Answers 17 — Unsafe, FFI, and interop

> Exercises: [17-unsafe-ffi.md](../17-unsafe-ffi.md)

## Part A

**A1. What exactly does `unsafe` turn off, and what does it not?**

It permits exactly five things: dereferencing a raw pointer, calling an `unsafe` function, implementing an `unsafe` trait, mutating a `static mut`, and accessing a union field. It does *not* turn off the borrow checker, the type checker, or lifetime checking — safe Rust's rules still apply to everything else in the block. This is the most commonly misunderstood point: `unsafe` is not an escape hatch that makes the code C, it is a narrow set of additional powers, and the compiler keeps doing its job around them. What it does turn off is the compiler's ability to *verify* the invariants those five operations require, which is why the block is a promise you are making.

**A2. What is the point of a `// SAFETY:` comment, and what is the corresponding `# Safety` doc section?**

A `// SAFETY:` comment on an `unsafe` block states the invariant you are asserting and why it holds here — it is the proof obligation discharged in writing, and it is what makes review possible. A `# Safety` section in the doc comment of an `unsafe fn` states the invariant the *caller* must uphold, because an `unsafe fn` moves the obligation outward. The pairing is the whole discipline: every `unsafe fn` documents its precondition, and every `unsafe` block cites why the precondition is met. Clippy's `undocumented_unsafe_blocks` and `missing_safety_doc` lints enforce both, and turning them on is the cheapest quality win available in a crate that uses `unsafe` at all.

**A3. Edition 2024 changed two pieces of FFI syntax. What are they, and why?**

Extern blocks must now be written `unsafe extern "C" { ... }`, and attributes such as `no_mangle`, `link_section`, and `export_name` must be wrapped as `#[unsafe(no_mangle)]`. Both changes exist because the old syntax let you introduce unsoundness without the word `unsafe` appearing anywhere: declaring an extern function with the wrong signature is a promise the compiler cannot check, and `no_mangle` can silently collide with or replace another symbol. This matters practically because nearly every FFI tutorial, Stack Overflow answer, and blog post predates it, so code copied from the internet will not compile on a 2024-edition crate — and the error message does not obviously say why.

**A4. You are exposing a Rust library to C#. What are the rules for passing a string across, and who frees it?**

Rust strings are UTF-8 and not NUL-terminated; C strings are NUL-terminated bytes; .NET strings are UTF-16. Across the boundary you use `*const c_char`/`*mut c_char` with `CStr` to borrow an incoming string and `CString::into_raw` to hand one out, and on the C# side `LibraryImport` with `StringMarshalling.Utf8`. The allocation rule is absolute: memory must be freed by the allocator that made it, so a pointer Rust returned must come back to a Rust-exported free function — you export `mylib_free(ptr)` that reconstitutes the `CString` with `from_raw` and drops it. Letting the .NET marshaller free a Rust allocation, or calling `free()` on it from C, is heap corruption.

**A5. Why must a panic never cross an `extern "C"` boundary, and what happens if it does?**

There is no defined way to unwind a Rust panic through a foreign frame — the foreign code has no landing pads and no notion of Rust's unwinding tables — so doing it is undefined behaviour. Modern Rust therefore aborts the process at the boundary rather than attempting it, which is safe but drastic: one `todo!()` in an exported function takes down the whole program, as the drill in this chapter demonstrates. The fix in real code is to wrap the body in `std::panic::catch_unwind` and convert a caught panic into an error code or a null return, so the failure crosses as data rather than as control flow.

**A6. `bindgen` and `cbindgen` — which direction is each, and what is the .NET analogue?**

`bindgen` reads C headers and generates Rust declarations, so you can call an existing C library from Rust; `cbindgen` reads your Rust source and generates a C header, so C or C# consumers can call you. The .NET analogues are, roughly, ClangSharp or the old `tlbimp`/P-Invoke generators for the inbound direction, and there is no real outbound equivalent because .NET assemblies are self-describing — NativeAOT's `[UnmanagedCallersOnly]` plus a generated header is the closest thing. The practical advice is the same in both ecosystems: generate the bindings in a build step rather than hand-maintaining them, because a signature that drifts is silent memory corruption, not a compile error.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 17 — `unsafe`, raw pointers, and a C ABI surface.
//!
//! The discipline being drilled: `unsafe` is not "turn off the checks", it is
//! "I am asserting an invariant the compiler cannot verify". The job is always
//! to wrap it in a safe API whose signature makes misuse impossible, and to
//! write down the invariant you are asserting.
//!
//! Edition 2024 tightened two things that most material online still gets
//! wrong: `extern` blocks must be written `unsafe extern "C"`, and attributes
//! like `no_mangle` must be wrapped as `#[unsafe(no_mangle)]`.

use std::ffi::{CStr, CString, c_char};

/// A *safe* function whose body needs `unsafe`. Callers can never break it: the
/// signature guarantees `slice` is valid and `mid` is checked before use.
///
/// This is exactly how `slice::split_at_mut` is implemented in std.
pub fn split_evenly(slice: &mut [i32]) -> (&mut [i32], &mut [i32]) {
    let mid = slice.len() / 2;
    let len = slice.len();
    let ptr = slice.as_mut_ptr();

    // SAFETY: `mid <= len` by construction, `ptr` is valid for `len` elements
    // because it came from a live `&mut [i32]`, and the two ranges `[0, mid)`
    // and `[mid, len)` are disjoint, so the two `&mut` never alias.
    unsafe {
        (
            std::slice::from_raw_parts_mut(ptr, mid),
            std::slice::from_raw_parts_mut(ptr.add(mid), len - mid),
        )
    }
}

/// Reading through a raw pointer. The `unsafe` block is small and the invariant
/// is stated; everything outside it is ordinary safe Rust.
pub fn sum_via_raw(values: &[i64]) -> i64 {
    let ptr = values.as_ptr();
    let mut total = 0;
    for i in 0..values.len() {
        // SAFETY: `i < values.len()`, and `ptr` is valid for that many reads
        // for as long as the borrow of `values` lasts.
        total += unsafe { *ptr.add(i) };
    }
    total
}

/// The C ABI surface a .NET `LibraryImport` would bind to. `extern "C"` fixes
/// the calling convention; `#[unsafe(no_mangle)]` fixes the symbol name.
///
/// # Safety
/// `input` must be a non-null pointer to a NUL-terminated, valid UTF-8 C string
/// that stays alive for the duration of the call. The returned pointer is owned
/// by the caller and must be released with [`drill_free`], never with `free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn drill_normalize(input: *const c_char) -> *mut c_char {
    if input.is_null() {
        return std::ptr::null_mut();
    }
    // SAFETY: guaranteed by this function's documented contract.
    let borrowed = unsafe { CStr::from_ptr(input) };

    let normalized = match borrowed.to_str() {
        Ok(s) => s.trim().to_ascii_lowercase(),
        Err(_) => return std::ptr::null_mut(),
    };

    match CString::new(normalized) {
        // `into_raw` transfers ownership out of Rust; nothing will free it
        // until it comes back through `drill_free`.
        Ok(c) => c.into_raw(),
        Err(_) => std::ptr::null_mut(),
    }
}

/// The other half of the contract. Every allocation that crosses the boundary
/// needs a matching deallocator exported from the *same* allocator that made
/// it — this is the FFI equivalent of `IDisposable`, except the caller is in
/// another language and nothing will remind them.
///
/// # Safety
/// `ptr` must be null, or a pointer previously returned by [`drill_normalize`]
/// and not yet freed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn drill_free(ptr: *mut c_char) {
    if ptr.is_null() {
        return;
    }
    // SAFETY: the contract says this came from `CString::into_raw`, so
    // reconstituting and dropping it is the correct deallocation.
    drop(unsafe { CString::from_raw(ptr) });
}

// Calling *into* the C ABI from Rust, which is what the round-trip test does.
// The declaration must match the definition exactly; nothing checks this
// across a real library boundary, which is why `bindgen` exists.
//
// Note the plain `//` comments: rustdoc has nothing to attach a `///` to on an
// `extern` block, and warns with `unused_doc_comment` if you try.
unsafe extern "C" {
    fn abs(input: i32) -> i32;
}

pub fn libc_abs(value: i32) -> i32 {
    // SAFETY: `abs` is a pure function from libc with no preconditions beyond
    // the argument type, which the signature already enforces.
    unsafe { abs(value) }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_safe_wrapper_hides_the_unsafe_entirely() {
        let mut data = [1, 2, 3, 4, 5];
        let (left, right) = split_evenly(&mut data);
        assert_eq!(left, &mut [1, 2][..]);
        assert_eq!(right, &mut [3, 4, 5][..]);

        right[0] = 30;
        assert_eq!(data, [1, 2, 30, 4, 5]);
    }

    #[test]
    fn raw_reads_agree_with_safe_ones() {
        let values = [10i64, -3, 7];
        assert_eq!(sum_via_raw(&values), values.iter().sum::<i64>());
        assert_eq!(sum_via_raw(&[]), 0);
    }

    #[test]
    fn the_c_string_round_trip_is_balanced() {
        let input = CString::new("  /Subscriptions/ABC  ").unwrap();

        // SAFETY: `input` is a live, valid C string for the whole call.
        let out = unsafe { drill_normalize(input.as_ptr()) };
        assert!(!out.is_null());

        // SAFETY: `out` is non-null and NUL-terminated, produced just above.
        let text = unsafe { CStr::from_ptr(out) }.to_str().unwrap().to_string();
        assert_eq!(text, "/subscriptions/abc");

        // SAFETY: `out` came from `drill_normalize` and has not been freed.
        unsafe { drill_free(out) };
    }

    #[test]
    fn null_in_null_out_rather_than_undefined_behaviour() {
        // SAFETY: null is explicitly part of the documented contract.
        let out = unsafe { drill_normalize(std::ptr::null()) };
        assert!(out.is_null());
        // SAFETY: freeing null is documented as a no-op.
        unsafe { drill_free(std::ptr::null_mut()) };
    }

    #[test]
    fn calling_into_libc_works() {
        assert_eq!(libc_abs(-42), 42);
        assert_eq!(libc_abs(42), 42);
    }
}
```
