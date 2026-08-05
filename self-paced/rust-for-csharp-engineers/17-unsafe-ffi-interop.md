# 17 — Unsafe Rust, FFI, and calling Rust from .NET

Everything so far has been *safe* Rust, where the compiler proves that your program has no data races, no
use-after-free, no null dereferences, and no buffer overruns. That guarantee is the whole point of the
language, and the overwhelming majority of code you write should stay inside it.

But the guarantee has to end somewhere. Somebody has to call the operating system, dereference the pointer
the C library returned, and build the data structure whose invariants the borrow checker cannot express.
`unsafe` is the door for that work, and it is worth being precise about what walking through it means: not
"the checks are off", but "the compiler cannot verify this, so *you* are asserting it is correct."

For you specifically there is a second motivation. A very common reason a .NET shop adopts Rust is to write
a fast native component and call it from C#, and that path runs entirely through this module.

> **Prerequisite:** [12 — Smart pointers and interior mutability](12-smart-pointers.md).

## What `unsafe` actually does

The most common misconception is that `unsafe` disables the borrow checker. It does not. Inside an `unsafe`
block, ownership, borrowing, lifetimes, and type checking all work exactly as before. What `unsafe` adds is
permission to do exactly five things:

1. Dereference a raw pointer (`*const T`, `*mut T`).
2. Call an `unsafe` function, including any foreign function.
3. Access or modify a mutable `static`.
4. Implement an `unsafe` trait (`Send`, `Sync`).
5. Access a `union` field.

That is the complete list. Everything else the compiler still checks. The C# analogy is close: `unsafe` +
`fixed` in C# lets you take raw pointers while the rest of the language stays intact, and both languages
require an opt-in at the project or block level.

```rust
fn main() {
    let mut x = 5;

    // Creating raw pointers is safe — nothing has happened yet.
    let p: *const i32 = &x;
    let q: *mut i32 = &mut x;

    // Dereferencing them is not.
    unsafe {
        assert_eq!(*p, 5);
        *q = 10;
        assert_eq!(*p, 10);
    }
    assert_eq!(x, 10);
}
```

Raw pointers differ from references in the ways that matter: they may be null, they may dangle, they have no
lifetime, they are not automatically dereferenced, and they are exempt from the aliasing rules. They are
`*const T` and `*mut T`, and the `mut` distinction is documentation rather than enforcement.

### The contract you are signing

The important shift is philosophical. Writing `unsafe` is a **proof obligation**: you are telling the
compiler that the code upholds Rust's memory-safety invariants even though it cannot check them. If you are
wrong, the result is undefined behaviour — and UB in Rust is exactly as bad as UB in C++, with the added
irony that safe code elsewhere in the program can now break in ways that make no sense.

Two disciplines follow, and both are ecosystem norms rather than personal preference.

**Every `unsafe` block gets a `// SAFETY:` comment** explaining why the invariants hold. Clippy has a lint
(`undocumented_unsafe_blocks`) to enforce it.

**Every public `unsafe fn` gets a `# Safety` doc section** stating what the caller must guarantee. This is
mandatory in the API guidelines.

```rust
/// Returns the element at `index` without a bounds check.
///
/// # Safety
///
/// The caller must guarantee that `index < slice.len()`. Violating this reads
/// out-of-bounds memory, which is undefined behaviour.
pub unsafe fn get_unchecked_i32(slice: &[i32], index: usize) -> i32 {
    // SAFETY: the caller has guaranteed `index` is in bounds, so the pointer
    // arithmetic stays within the allocation and the read is aligned.
    unsafe { *slice.as_ptr().add(index) }
}

fn main() {
    let v = vec![10, 20, 30];
    // SAFETY: 1 < 3.
    let x = unsafe { get_unchecked_i32(&v, 1) };
    assert_eq!(x, 20);
}
```

Note that in edition 2024, an `unsafe fn` body is **no longer implicitly an unsafe block** — you must write
`unsafe { }` inside it. That is a deliberate change: it forces you to mark exactly which operations need the
permission rather than blanketing the whole function.

### The real goal: safe abstractions over unsafe internals

The point of `unsafe` is almost never to expose it. The pattern the whole standard library follows is to
wrap an unsafe implementation in a safe API whose type signature makes misuse impossible.

`Vec<T>` is nothing but raw pointer arithmetic and manual allocation, and `split_at_mut` hands out two
`&mut` into the same allocation — something the borrow checker cannot possibly approve. Here it is,
essentially as std implements it:

```rust
use std::slice;

/// Splits a mutable slice into two disjoint halves at `mid`.
fn split_at_mut_manual<T>(values: &mut [T], mid: usize) -> (&mut [T], &mut [T]) {
    let len = values.len();
    let ptr = values.as_mut_ptr();
    assert!(mid <= len, "mid out of range");

    // SAFETY: `mid <= len` was just checked, so both ranges are within the same
    // allocation, and they are disjoint, so the two &mut never alias.
    unsafe {
        (
            slice::from_raw_parts_mut(ptr, mid),
            slice::from_raw_parts_mut(ptr.add(mid), len - mid),
        )
    }
}

fn main() {
    let mut v = vec![1, 2, 3, 4, 5, 6];
    let (left, right) = split_at_mut_manual(&mut v, 3);
    left[0] = 100;
    right[0] = 200;
    assert_eq!(v, vec![100, 2, 3, 200, 5, 6]);
}
```

The caller cannot misuse this. The `assert!` handles the one precondition, and the returned lifetimes tie
both slices to the input borrow. That is the shape to aim for: **unsafe on the inside, safe and
unmisusable on the outside.**

## FFI: calling C from Rust

Foreign functions are declared in an `extern` block. In edition 2024 that block must itself be marked
`unsafe`, because declaring a foreign signature is an assertion about code the compiler cannot see:

```rust
use std::ffi::c_int;

// Edition 2024: `extern` blocks are `unsafe extern`.
unsafe extern "C" {
    fn abs(input: c_int) -> c_int;
    fn strlen(s: *const std::ffi::c_char) -> usize;
}

fn main() {
    // SAFETY: `abs` from libc has no preconditions beyond a valid c_int.
    let a = unsafe { abs(-42) };
    assert_eq!(a, 42);

    let s = c"hello";        // c"..." is a CStr literal (Rust 1.77+)
    // SAFETY: `s` is a valid, NUL-terminated C string with static lifetime.
    let n = unsafe { strlen(s.as_ptr()) };
    assert_eq!(n, 5);
}
```

`"C"` is the ABI, and it is the same choice you make in a `DllImport`. The `std::ffi` module supplies the
C type aliases (`c_int`, `c_char`, `c_void`, `c_double`) so you do not have to guess platform widths — the
equivalent of getting your `int` versus `long` marshalling right in a P/Invoke signature.

The type mapping is the part to get right:

| C | Rust | C# |
|---|---|---|
| `int` | `c_int` (`i32`) | `int` |
| `unsigned int` | `c_uint` (`u32`) | `uint` |
| `long long` | `c_longlong` (`i64`) | `long` |
| `size_t` | `usize` | `nuint` / `nint` |
| `double` | `f64` | `double` |
| `char*` (string) | `*const c_char` | `byte*` / `string` + marshalling |
| `void*` | `*mut c_void` | `IntPtr` / `void*` |
| `struct` | `#[repr(C)] struct` | `[StructLayout(LayoutKind.Sequential)]` |
| `enum` | `#[repr(C)] enum` | `enum : int` |
| function pointer | `extern "C" fn` | delegate + `UnmanagedFunctionPointer` |

`#[repr(C)]` is essential and easy to forget. By default Rust makes **no guarantees at all** about struct
layout — it reorders fields to minimise padding, and the order can change between compiler versions.
`#[repr(C)]` pins the layout to the C ABI, which is exactly what `LayoutKind.Sequential` does in .NET and
for exactly the same reason.

```rust
use std::ffi::c_char;

/// Layout is guaranteed to match a C struct with these fields in this order.
#[repr(C)]
pub struct FindingRaw {
    pub code: u32,
    pub severity: u8,
    pub message: *const c_char,
}

/// A C-compatible enum with an explicit discriminant type.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Status {
    Ok = 0,
    Invalid = 1,
    NotFound = 2,
}

fn main() {
    assert_eq!(Status::NotFound as i32, 2);
    // The struct's size is ABI-stable because of repr(C).
    assert!(std::mem::size_of::<FindingRaw>() >= std::mem::size_of::<*const c_char>());
}
```

### Strings across the boundary

Strings are where FFI bugs live, because Rust's `String` (UTF-8, length-prefixed, **not** NUL-terminated)
and C's `char*` (NUL-terminated, no length, unspecified encoding) are genuinely different things.

`CString` is an owned, NUL-terminated buffer you build to hand *to* C. `CStr` is a borrowed view of a
NUL-terminated buffer you received *from* C.

```rust
use std::ffi::{CStr, CString};
use std::ffi::c_char;

fn main() {
    // Rust -> C: build a CString, pass its pointer. It must outlive the call.
    let owned = CString::new("policy-name").expect("no interior NUL");
    let ptr: *const c_char = owned.as_ptr();
    // SAFETY: `owned` is alive for this whole scope, so `ptr` is valid.
    let back = unsafe { CStr::from_ptr(ptr) };
    assert_eq!(back.to_str().unwrap(), "policy-name");

    // The classic bug: a temporary that dies before the pointer is used.
    // let dangling = CString::new("oops").unwrap().as_ptr();  // UB!

    // C -> Rust: wrap the pointer, then copy if you need to keep it.
    let literal = c"from-c";
    // SAFETY: a c"" literal is NUL-terminated and 'static.
    let s = unsafe { CStr::from_ptr(literal.as_ptr()) };
    let rust_string: String = s.to_string_lossy().into_owned();
    assert_eq!(rust_string, "from-c");
}
```

`CString::new` returns an error if the input contains an interior NUL, and `to_str()` returns an error if
the bytes are not UTF-8 — with `to_string_lossy()` substituting replacement characters instead. Those two
fallible conversions are the encoding checks .NET's marshaller performs silently.

### Generating bindings

Writing `extern` blocks by hand for a large C header is miserable and error-prone, so two tools do it:

**bindgen** reads C headers and generates Rust `extern` declarations — the direction you need when calling
a C library from Rust. It is the counterpart of `ClangSharpPInvokeGenerator` or hand-written `DllImport`s.

**cbindgen** reads your Rust code and generates a C header — the direction you need when *exposing* Rust to
C or C#.

Both usually run from `build.rs`:

```rust,ignore
// build.rs — bindgen example
fn main() {
    println!("cargo::rerun-if-changed=wrapper.h");
    let bindings = bindgen::Builder::default()
        .header("wrapper.h")
        .parse_callbacks(Box::new(bindgen::CargoCallbacks::new()))
        .generate()
        .expect("bindgen");
    let out = std::path::PathBuf::from(std::env::var("OUT_DIR").unwrap());
    bindings.write_to_file(out.join("bindings.rs")).expect("write");
}
```

The generated file is then pulled in with `include!(concat!(env!("OUT_DIR"), "/bindings.rs"));`. Convention
is a `foo-sys` crate holding the raw bindings and a `foo` crate wrapping them in a safe API — the
`-sys`/wrapper split you will see all over crates.io.

## Exposing Rust to .NET

Now the direction you probably care about. The plan: build a `cdylib`, export `extern "C"` functions, and
call them from C# with `LibraryImport`. Everything in this section was compiled and run against .NET 10 and
Rust 1.95.

### The Rust side

```toml
# Cargo.toml
[package]
name = "polcheck_ffi"
version = "0.1.0"
edition = "2024"

[lib]
# cdylib produces a .dll / .so / .dylib with no Rust metadata — what P/Invoke needs.
# rlib is added so Rust tests can still link the crate.
crate-type = ["cdylib", "rlib"]
```

```rust
use std::ffi::{c_char, c_int, CStr, CString};

/// Simple value-in, value-out. Nothing to free, nothing to get wrong.
///
/// In edition 2024 `no_mangle` must be written `#[unsafe(no_mangle)]`.
#[unsafe(no_mangle)]
pub extern "C" fn polcheck_add(a: c_int, b: c_int) -> c_int {
    a + b
}

/// Returns a newly allocated C string. The caller **must** return it to
/// `polcheck_free`, because it was allocated by Rust's allocator.
///
/// # Safety
///
/// `name` must be either null or a valid, NUL-terminated C string.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn polcheck_greet(name: *const c_char) -> *mut c_char {
    if name.is_null() {
        return std::ptr::null_mut();
    }
    // SAFETY: the caller guaranteed `name` is a valid NUL-terminated string.
    let s = unsafe { CStr::from_ptr(name) }.to_string_lossy().into_owned();
    match CString::new(format!("hello {s}")) {
        Ok(c) => c.into_raw(),          // ownership handed to the caller
        Err(_) => std::ptr::null_mut(),
    }
}

/// Frees a string returned by this library.
///
/// # Safety
///
/// `p` must be null, or a pointer previously returned by `polcheck_greet`
/// and not yet freed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn polcheck_free(p: *mut c_char) {
    if !p.is_null() {
        // SAFETY: the caller guaranteed this came from CString::into_raw.
        drop(unsafe { CString::from_raw(p) });
    }
}

fn main() {
    assert_eq!(polcheck_add(2, 40), 42);

    let name = CString::new("david").unwrap();
    // SAFETY: `name` is valid and alive across the call.
    unsafe {
        let greeting = polcheck_greet(name.as_ptr());
        assert!(!greeting.is_null());
        assert_eq!(CStr::from_ptr(greeting).to_str().unwrap(), "hello david");
        polcheck_free(greeting);
    }
}
```

Four details carry the weight here.

**`#[unsafe(no_mangle)]`** keeps the symbol name intact so the dynamic linker can find it. The
`unsafe(...)` wrapper is an edition-2024 requirement — older tutorials and most Stack Overflow answers show
a bare `#[no_mangle]`, which is now a compile error. The reason for the change is that `no_mangle` can
silently collide with another symbol and cause the linker to pick the wrong function, which is genuinely
unsafe.

**`extern "C"`** sets the calling convention, matching `CallingConvention.Cdecl`.

**`into_raw` / `from_raw`** transfer ownership out of and back into Rust. This is the crux of cross-language
memory management: the string was allocated by Rust's allocator, so it **must** be freed by Rust's
allocator. Calling `Marshal.FreeHGlobal` on it would corrupt the heap. That is why the library exports a
`polcheck_free` — the same discipline as a C API that pairs `xyz_create` with `xyz_destroy`.

**Never let a panic cross the boundary.** Unwinding into foreign code is undefined behaviour. `extern "C"`
functions abort rather than unwind by default in current Rust, which is safe but abrupt; for a real library
wrap the body in `catch_unwind` and convert a panic into an error code:

```rust
use std::ffi::c_int;
use std::panic::catch_unwind;

#[unsafe(no_mangle)]
pub extern "C" fn polcheck_checked_div(a: c_int, b: c_int, out: *mut c_int) -> c_int {
    let result = catch_unwind(|| {
        if b == 0 {
            panic!("division by zero");
        }
        a / b
    });

    match result {
        Ok(v) if !out.is_null() => {
            // SAFETY: `out` was checked non-null; the caller guarantees it is
            // a valid, aligned, writable c_int.
            unsafe { *out = v };
            0                                  // success
        }
        Ok(_) => 1,                            // null out-pointer
        Err(_) => 2,                           // panicked
    }
}

fn main() {
    let mut out: c_int = 0;
    assert_eq!(polcheck_checked_div(10, 2, &mut out), 0);
    assert_eq!(out, 5);

    // A panic becomes an error code instead of undefined behaviour.
    let prev = std::panic::take_hook();
    std::panic::set_hook(Box::new(|_| {}));    // silence the panic message
    assert_eq!(polcheck_checked_div(1, 0, &mut out), 2);
    std::panic::set_hook(prev);
}
```

That "error code plus out-parameter" shape is the standard C-ABI idiom, and it is exactly the
`HRESULT` + `out` convention you know from COM interop.

### The C# side

Build with `cargo build --release`, which produces `target/release/polcheck_ffi.dll` on Windows
(`libpolcheck_ffi.so` on Linux, `.dylib` on macOS). Then:

```csharp
using System.Runtime.InteropServices;

internal static partial class Native
{
    // LibraryImport is the source-generated, AOT-friendly successor to DllImport.
    // The name has no extension; the runtime adds .dll / .so / .dylib.
    [LibraryImport("polcheck_ffi")]
    internal static partial int polcheck_add(int a, int b);

    [LibraryImport("polcheck_ffi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr polcheck_greet(string name);

    [LibraryImport("polcheck_ffi")]
    internal static partial void polcheck_free(IntPtr p);
}

public static class Polcheck
{
    public static int Add(int a, int b) => Native.polcheck_add(a, b);

    public static string Greet(string name)
    {
        IntPtr p = Native.polcheck_greet(name);
        if (p == IntPtr.Zero) throw new InvalidOperationException("greet failed");
        try
        {
            return Marshal.PtrToStringUTF8(p) ?? string.Empty;
        }
        finally
        {
            // Rust allocated it, so Rust must free it.
            Native.polcheck_free(p);
        }
    }
}
```

Running that prints `42` and `hello david`. A few notes from having actually built it.

**`LibraryImport` rather than `DllImport`.** It is a source generator that emits the marshalling code at
compile time, so it works under NativeAOT and produces better diagnostics. `DllImport` still works and is
what you will see in older code.

**`StringMarshalling = StringMarshalling.Utf8`** matters because .NET strings are UTF-16 and Rust's are
UTF-8. Without it you will pass UTF-16 bytes to a function expecting UTF-8 and get garbage — one of the
most common cross-language bugs, and completely silent.

**The `try/finally` around the free** is the discipline that replaces `Drop`. Once a pointer crosses into
managed code, Rust's automatic cleanup is gone and you are back to manual lifetime management, with a
`finally` block standing in for RAII. This asymmetry is worth designing around: prefer APIs where the caller
supplies a buffer, or where values are copied at the boundary, so there is nothing to free.

**Where the DLL must live.** The runtime probes the application directory, so copy the `.dll` next to your
executable — via an MSBuild `<Content Include="..." CopyToOutputDirectory="PreserveNewest" />` item, or by
packing it into a NuGet package under `runtimes/{rid}/native/` if you are shipping it properly.

### Designing a good boundary

Three principles are worth stating because they save real pain.

**Keep the FFI surface small and dumb.** Marshal primitives, opaque handles, and flat `#[repr(C)]` structs.
Do not try to expose a rich object graph — you will end up hand-writing what a serialiser does better.

**Consider serialising instead of marshalling.** For anything structured, passing a JSON or MessagePack
buffer across the boundary and deserialising on each side is frequently simpler, safer, and fast enough. It
turns a hard ABI problem into an easy data problem, and both sides get to use their idiomatic types.

**Use opaque handles for stateful objects.** Return a `*mut c_void` from a `create` function, take it in
every other function, and free it in a `destroy` function. On the C# side that is a `SafeHandle`, which
gives you finalizer-backed cleanup:

```rust
use std::ffi::c_void;

pub struct Engine { rules: Vec<String> }

#[unsafe(no_mangle)]
pub extern "C" fn engine_create() -> *mut c_void {
    let engine = Box::new(Engine { rules: Vec::new() });
    Box::into_raw(engine) as *mut c_void       // leak it deliberately; C# owns it now
}

/// # Safety
/// `handle` must come from `engine_create` and must not have been destroyed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn engine_rule_count(handle: *mut c_void) -> usize {
    if handle.is_null() { return 0; }
    // SAFETY: the caller guaranteed this is a live Engine pointer. We take a
    // shared borrow without reclaiming ownership.
    let engine = unsafe { &*(handle as *const Engine) };
    engine.rules.len()
}

/// # Safety
/// `handle` must come from `engine_create` and must not be used afterwards.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn engine_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        // SAFETY: reconstitutes the Box so Rust's allocator frees it.
        drop(unsafe { Box::from_raw(handle as *mut Engine) });
    }
}

fn main() {
    let h = engine_create();
    // SAFETY: `h` came from engine_create and is still live.
    unsafe {
        assert_eq!(engine_rule_count(h), 0);
        engine_destroy(h);
    }
}
```

`Box::into_raw` is a deliberate leak — it hands the allocation to the foreign caller — and `Box::from_raw`
reclaims it. Note that `engine_rule_count` takes a *shared reference* without reclaiming ownership, which is
the pattern to copy; accidentally writing `Box::from_raw` in a getter would free the object on every call.

## When it is worth it

FFI is real work — a build step, a versioning problem, a debugging story that spans two toolchains, and a
class of bug that neither language's tooling fully covers. It is worth it when the numbers justify it:
a hot loop that is genuinely CPU-bound, a parser or codec where allocation pressure dominates, a
cryptographic or compression routine, or code that must run with predictable latency and no GC pauses.

It is usually *not* worth it for I/O-bound work, for anything already fast enough, or for a component that
changes weekly. And before reaching for Rust, check whether .NET's own tools close the gap: `Span<T>`,
`ArrayPool<T>`, `struct` generics, SIMD intrinsics, and NativeAOT have removed a lot of the historical
reasons to go native.

The pragmatic middle path many teams take is a **separate process** rather than a shared library: run the
Rust component as a subprocess or a local service and talk over stdin/stdout, a pipe, or gRPC. You lose a
little latency and gain fault isolation, an independent release cadence, no ABI to keep stable, and
debuggability. For `polcheck`, shipping a standalone binary that emits JSON is a far better integration
story than a `.dll` with a hand-maintained C ABI.

## Before you move on

`unsafe` is not an off switch. Ownership, borrowing, and type checking all still apply; the keyword grants
exactly five extra powers, of which dereferencing raw pointers and calling foreign functions are the ones
you will actually use. What changes is who is responsible: you are now asserting invariants the compiler
cannot verify, which is why `// SAFETY:` comments and `# Safety` doc sections are non-negotiable norms
rather than politeness. The goal is always to keep `unsafe` inside a safe abstraction whose API cannot be
misused, the way `split_at_mut` does.

For FFI, the essentials are `unsafe extern "C"` blocks to import, `#[unsafe(no_mangle)] pub extern "C"` to
export, `#[repr(C)]` on every type that crosses the boundary, and `CString`/`CStr` for strings — with the
edition-2024 `unsafe(...)` attribute wrapper being the detail that most existing tutorials get wrong.
Ownership must be handed over explicitly with `into_raw`/`from_raw` or `Box::into_raw`/`Box::from_raw`, and
whoever allocated must free. Panics must never cross the boundary, so wrap exported bodies in
`catch_unwind` and return error codes.

On the .NET side, `LibraryImport` with `StringMarshalling.Utf8` is the modern binding, a `try/finally`
calling back into Rust's free function replaces `Drop`, and `SafeHandle` is the right home for an opaque
handle. Keep the boundary small, prefer serialised payloads over marshalled object graphs, and seriously
consider a separate process instead of a shared library.

If you can explain why `#[repr(C)]` is mandatory rather than advisory, why a Rust-allocated string must not
be freed by the .NET marshaller, and what `unsafe` does *not* turn off, you have finished Part 1.

Next: [18 — clap: command-line interfaces](18-clap.md), where Part 2 begins.

### Sources

- *The Book*, ch. 20.1 "Unsafe Rust". <https://doc.rust-lang.org/book/ch20-01-unsafe-rust.html> — the five superpowers and the safe-abstraction pattern.
- *The Rustonomicon*. <https://doc.rust-lang.org/nomicon/> — the authoritative treatment of undefined behaviour, aliasing, and what unsafe code must uphold.
- *The Rust Reference*, "External blocks". <https://doc.rust-lang.org/reference/items/external-blocks.html> — ABI strings and the edition-2024 `unsafe extern` requirement.
- *The Edition Guide*, "Unsafe attributes". <https://doc.rust-lang.org/edition-guide/rust-2024/unsafe-attributes.html> — why `no_mangle`, `export_name`, and `link_section` now require `unsafe(...)`.
- *The Edition Guide*, "Unsafe extern blocks". <https://doc.rust-lang.org/edition-guide/rust-2024/unsafe-extern.html> — the rationale for `unsafe extern`.
- `std::ffi`. <https://doc.rust-lang.org/std/ffi/> — `CString`, `CStr`, `OsString`, and the C type aliases.
- *The bindgen User Guide*. <https://rust-lang.github.io/rust-bindgen/> — generating Rust bindings from C headers.
- *cbindgen*. <https://github.com/mozilla/cbindgen> — generating C headers from Rust.
- Microsoft Learn, "Source generation for platform invokes". <https://learn.microsoft.com/dotnet/standard/native-interop/pinvoke-source-generation> — `LibraryImport`, string marshalling, and NativeAOT compatibility.
- Microsoft Learn, `SafeHandle`. <https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.safehandle> — the managed side of opaque-handle ownership.
