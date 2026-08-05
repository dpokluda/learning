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
