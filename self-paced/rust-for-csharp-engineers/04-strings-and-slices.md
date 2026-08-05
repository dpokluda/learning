# 04 — Strings, slices, and `Vec`

If you ask a room of C# developers learning Rust what frustrated them most in their first fortnight, the
answer is `String` versus `&str`. It presents as a paperwork problem — the compiler keeps rejecting
things and `.to_string()` keeps fixing them — and so people learn to sprinkle conversions until the
errors stop. That works, and it is the wrong lesson, because the distinction is not paperwork. It is the
ownership model made visible in the type system, and once you see that, the same shape explains
`Vec<T>` versus `&[T]`, `PathBuf` versus `&Path`, and `String` versus `Cow<str>`.

> **Prerequisite:** [03 — Syntax orientation](03-syntax-orientation.md).

## The one idea

In C#, `string` is a single type that is simultaneously the owner of its data and the thing you pass
around. It can be either because it is immutable and garbage-collected: sharing is free, nobody has to
decide who frees it, and the runtime cleans up when the last reference disappears.

Rust has no garbage collector, so it must answer *who owns this text?* And since that question genuinely
has two different answers depending on the situation, Rust gives you two types:

| | Owned | Borrowed |
|---|---|---|
| Text | `String` | `&str` |
| Sequence | `Vec<T>` | `&[T]` |
| Filesystem path | `PathBuf` | `&Path` |
| OS string | `OsString` | `&OsStr` |
| Any `T` on the heap | `Box<T>` | `&T` |

Read that table across, not down. **Every row is the same distinction**: the left column owns a heap
allocation and is responsible for freeing it, and the right column is a borrowed view into memory that
someone else owns. Learn the pattern once and four other pairs come free. The C# analogy that gets
closest is `string` versus `ReadOnlySpan<char>`, and if you have used spans in performance work, you
already have the instinct — a span is a window, it does not own, and it must not outlive its backing
array. `&str` is that, promoted from a specialist tool to the default way you pass text.

Concretely, a `String` is three machine words on the stack — pointer, length, capacity — plus a heap
buffer it owns and frees when dropped. A `&str` is two words — pointer and length — and owns nothing. A
string literal like `"hello"` has type `&'static str`: it points into your executable's read-only data
section, which is why it is borrowed rather than owned, and why it lives for the whole program.

```rust
fn main() {
    let literal: &str = "hello";              // points into the binary; no allocation
    let owned: String = String::from("hello"); // heap allocation, owned
    let borrowed: &str = &owned;               // a view into owned's buffer

    println!("{literal} {owned} {borrowed}");
    println!("{} {}", size_of::<String>(), size_of::<&str>()); // 24 8 on 64-bit
}
```

## The rule that makes it easy

Here is the guidance that resolves nearly every practical case, and it is worth committing to memory:

> **Take `&str` in function parameters. Return `String` when you produce new text. Store `String` in
> your structs.**

Take `&str` because it is maximally general — a caller with a `String`, a literal, or a slice of either
can all call you, and none of them pay for an allocation. Return `String` because if you built new text
you must hand over ownership; there is no one else to own it. Store `String` in structs because a struct
holding a `&str` acquires a lifetime parameter, which propagates into every type that contains it, and
you should not do that until you have a measured reason.

```rust
// Good: general, allocation-free for the caller.
fn is_prod(location: &str) -> bool {
    location.ends_with("-prod")
}

// Good: we create new text, so we hand over ownership.
fn qualify(kind: &str, id: &str) -> String {
    format!("{kind}/{id}")
}

// Avoid: forces every caller to own a String, even if they have a literal.
fn is_prod_bad(location: String) -> bool {
    location.ends_with("-prod")
}

fn main() {
    let owned = String::from("westus2-prod");

    assert!(is_prod("westus2-prod"));   // literal works
    assert!(is_prod(&owned));           // String coerces to &str
    assert!(is_prod(&owned[0..12]));    // a sub-slice works too

    assert_eq!(qualify("storage", "res-1"), "storage/res-1");

    // The bad version forces this, and `owned` is now gone (moved).
    assert!(is_prod_bad(owned));
}
```

The line `is_prod(&owned)` is doing something worth naming, because it is why this ergonomically works at
all: **deref coercion**. `&String` is automatically converted to `&str` at a call site that wants `&str`,
because `String` implements `Deref<Target = str>`. The same mechanism converts `&Vec<T>` to `&[T]` and
`&PathBuf` to `&Path`. It is the closest thing Rust has to an implicit conversion, it is confined to
references, and it is why the owned/borrowed split does not make the language painful to use. Module 09
covers `Deref` properly.

## Converting between them

There are more conversion spellings than you need, so here is the honest short list.

```rust
fn main() {
    // &str -> String  (all allocate and copy; pick on style, not performance)
    let a: String = "hi".to_string();     // most common; via Display
    let b: String = "hi".to_owned();      // most precise: "give me the owned form"
    let c: String = String::from("hi");   // explicit
    let d: String = "hi".into();          // when the target type is inferable
    assert_eq!((a, b, c), (d.clone(), d.clone(), d));

    // String -> &str  (free: no allocation, just a view)
    let s = String::from("hello world");
    let e: &str = &s;
    let f: &str = s.as_str();
    let g: &str = &s[0..5];               // "hello" — byte indices!
    assert_eq!((e, f, g), ("hello world", "hello world", "hello"));

    // Building text
    let mut buf = String::new();
    buf.push_str("polcheck");
    buf.push('/');
    buf += "v1";
    assert_eq!(buf, "polcheck/v1");

    // format! is the usual tool; it always allocates.
    let joined = format!("{}-{}", "res", 1);
    assert_eq!(joined, "res-1");
}
```

`to_string()` and `to_owned()` do the same thing for `&str`. The community convention is `to_owned()`
when you specifically mean "convert this borrowed thing to its owned counterpart" and `to_string()` when
you mean "render this as text" — but nobody will fight you over it, and clippy is neutral.

## Why you cannot index a string

This is the second surprise, and it is a consequence of Unicode rather than of ownership.

```rust,compile_fail
fn main() {
    let s = String::from("hello");
    let c = s[0];    // ERROR: `String` cannot be indexed by `{integer}`
}
```

C# lets you write `s[0]` and gives you a `char`, because a C# string is an array of UTF-16 code units and
indexing is O(1). It is also, strictly speaking, frequently wrong — `"😀".Length` is 2 in C#, and `s[0]`
gives you half a surrogate pair, a value that is not a character in any meaningful sense.

Rust stores strings as **UTF-8**, where a character occupies between one and four bytes. There is no O(1)
way to find the *n*-th character, so Rust refuses to offer an operation that looks O(1) and isn't. More
importantly, it refuses to let you silently produce a byte that is half a character. Instead you say
which unit you mean:

```rust
fn main() {
    let s = "héllo";                  // 'é' is two bytes in UTF-8

    println!("{}", s.len());          // 6 — BYTES, not characters
    println!("{}", s.chars().count()); // 5 — Unicode scalar values (O(n))

    // Iterate characters
    for c in s.chars() { print!("[{c}]"); }
    println!();

    // Iterate bytes
    println!("{:?}", &s.as_bytes()[0..2]);

    // Character at a position, when you really need it
    println!("{:?}", s.chars().nth(1));       // Some('é')

    // Slicing is by BYTE index and panics if it splits a character.
    println!("{}", &s[0..1]);                 // "h" — fine, boundary is valid
    // println!("{}", &s[0..2]);              // PANIC: not a char boundary
}
```

The `.len()` returning bytes is the single most common source of off-by-N bugs for newcomers. Say what
you mean: `.len()` for a byte count (which is what you want for buffer sizing and wire protocols),
`.chars().count()` for a character count (O(n), and usually a sign you should be doing something else).

If you need real user-perceived characters — where "é" written as `e` plus a combining accent is one
grapheme made of two scalar values — the standard library deliberately does not help, because the rules
are large, versioned with Unicode, and belong in a crate. Use `unicode-segmentation`.

## Working with strings in practice

The methods you will actually reach for, most of which have obvious C# counterparts:

```rust
fn main() {
    let raw = "  env=prod, owner=platform  ";

    // Trimming and case
    assert_eq!(raw.trim(), "env=prod, owner=platform");
    assert_eq!("ABC".to_lowercase(), "abc");

    // Testing
    assert!(raw.contains("prod"));
    assert!(raw.trim().starts_with("env"));

    // Splitting: returns a lazy iterator, not an array (cf. string.Split)
    let parts: Vec<&str> = raw.trim().split(", ").collect();
    assert_eq!(parts, vec!["env=prod", "owner=platform"]);

    // split_once is the ergonomic win over C#'s IndexOf dance
    let (key, value) = "env=prod".split_once('=').unwrap();
    assert_eq!((key, value), ("env", "prod"));

    // Joining
    assert_eq!(parts.join(" | "), "env=prod | owner=platform");

    // Replacing (returns a new String; str is immutable like C#'s)
    assert_eq!("a-b-c".replace('-', "_"), "a_b_c");

    // Parsing: the TryParse analogue, returning Result
    let n: i32 = "42".parse().unwrap();
    assert_eq!(n, 42);
    assert!("nope".parse::<i32>().is_err());
}
```

Two of those deserve a note. **`split` returns a lazy iterator**, where C#'s `string.Split` returns an
allocated array; you `.collect()` only if you actually need the collection, and often you don't. And
**`parse` is generic over the target type** and returns `Result`, so it is `TryParse` with a better
signature — the type annotation on the left (or the turbofish `parse::<i32>()`) is what selects the
implementation. This is a small but pleasing example of Rust's type-directed dispatch, and module 09
explains the `FromStr` trait behind it.

## `Vec<T>` and slices: the same shape again

Now that the pattern is clear, the sequence types need much less space, because they are the identical
idea with `T` in place of characters.

```rust
fn main() {
    let mut v: Vec<i32> = Vec::new();
    v.push(3);
    v.push(1);
    v.extend([4, 1, 5]);

    let v2 = vec![3, 1, 4, 1, 5];        // the vec! macro
    assert_eq!(v, v2);

    // Slices: borrowed windows, free to create
    let all: &[i32] = &v;
    let middle: &[i32] = &v[1..4];
    assert_eq!(middle, &[1, 4, 1]);

    // Sorting mutates in place; sort is stable, sort_unstable is faster
    let mut sorted = v.clone();
    sorted.sort();
    assert_eq!(sorted, vec![1, 1, 3, 4, 5]);

    // Searching
    assert_eq!(v.iter().position(|&x| x == 4), Some(2));
    assert!(v.contains(&5));

    // Removal
    let mut w = vec![1, 2, 3, 4];
    w.retain(|&x| x % 2 == 0);            // like List<T>.RemoveAll, inverted
    assert_eq!(w, vec![2, 4]);
    assert_eq!(all.len(), 5);
}
```

`Vec<T>` is `List<T>`: a heap buffer with length and capacity, amortised O(1) push, contiguous. The
performance difference from C# is that `Vec<i32>` stores raw `i32`s inline with no per-element object
header and no boxing — the same as `List<int>` in .NET, which is also specialised — but `Vec<MyStruct>`
stores structs inline too, whereas a C# `List<MyClass>` stores references to separately allocated
objects. Rust's default is value semantics with explicit indirection; C#'s default is reference
semantics with explicit `struct`. That difference dominates cache behaviour in data-heavy code and is
one of the concrete reasons Rust programs are often faster than the instruction counts suggest.

The `&[T]` parameter rule from module 03 is the same rule as the `&str` parameter rule, for the same
reason:

```rust
// General: accepts &Vec<T>, &[T; N], and sub-slices.
fn mean(values: &[f64]) -> Option<f64> {
    if values.is_empty() {
        return None;
    }
    Some(values.iter().sum::<f64>() / values.len() as f64)
}

fn main() {
    let v = vec![1.0, 2.0, 3.0];
    let a = [10.0, 20.0];
    assert_eq!(mean(&v), Some(2.0));
    assert_eq!(mean(&a), Some(15.0));
    assert_eq!(mean(&v[0..2]), Some(1.5));
    assert_eq!(mean(&[]), None);
}
```

## Paths deserve their own types

A small practical note that saves real bugs: do not use `String` for filesystem paths. Use `PathBuf` and
`&Path`, which are the owned/borrowed pair for paths and which handle separators, extensions, and
platform differences properly.

```rust
use std::path::{Path, PathBuf};

fn config_file(dir: &Path) -> PathBuf {
    dir.join("polcheck").join("rules.json")
}

fn main() {
    let p = config_file(Path::new("/etc"));
    println!("{}", p.display());               // Display, because paths may not be UTF-8
    assert_eq!(p.extension().and_then(|e| e.to_str()), Some("json"));
    assert_eq!(p.file_stem().and_then(|e| e.to_str()), Some("rules"));
}
```

Note `p.display()` rather than printing the path directly. On Unix a path is an arbitrary byte string and
on Windows it is arbitrary UTF-16, so a `Path` is *not* guaranteed to be valid UTF-8 and therefore does
not implement `Display` directly. `.display()` gives you a lossy renderer for human output, and
`.to_str()` gives you `Option<&str>` when you need the real thing. This is the same distinction as
`OsString`/`OsStr`, which exists for exactly the same reason.

## Applying it to `polcheck`

Here is our domain model from module 01, now with the string decisions made deliberately rather than
accidentally:

```rust
use std::collections::HashMap;

pub struct Resource {
    pub id: String,                          // owned: the struct outlives its inputs
    pub kind: String,
    pub location: String,
    pub tags: HashMap<String, String>,
}

impl Resource {
    /// Borrow a tag value. Returns `Option<&str>`: no allocation, and the
    /// caller cannot outlive `self` because the lifetime is tied to it.
    pub fn tag(&self, key: &str) -> Option<&str> {
        self.tags.get(key).map(|v| v.as_str())
    }

    /// Produce new text, so return an owned `String`.
    pub fn describe(&self) -> String {
        format!("{} ({}) in {}", self.id, self.kind, self.location)
    }
}

fn main() {
    let r = Resource {
        id: "res-1".to_owned(),
        kind: "storage".to_owned(),
        location: "westus2".to_owned(),
        tags: HashMap::from([("env".to_owned(), "prod".to_owned())]),
    };

    assert_eq!(r.tag("env"), Some("prod"));
    assert_eq!(r.tag("owner"), None);
    assert_eq!(r.describe(), "res-1 (storage) in westus2");
}
```

Every choice there follows the rule. Fields are `String` because `Resource` owns its data and must not
carry a lifetime parameter. `tag` takes `&str` so any caller can query it, and returns `Option<&str>` so
the common case of reading a tag costs nothing. `describe` returns `String` because it builds new text.

## Before you move on

The durable idea is that `String`/`&str` is not a quirk of text handling — it is one instance of a
pattern that runs through the whole language, in which an owning type is paired with a borrowed view of
the same data. `Vec<T>`/`&[T]`, `PathBuf`/`&Path`, and `OsString`/`OsStr` are the same distinction, and
deref coercion is the mechanism that makes passing the owned form where the borrowed form is expected
feel automatic. When you find yourself writing `.to_string()` to make an error go away, stop and ask
whether the function should have taken the borrowed form instead; nine times out of ten it should.

The practical rule is short enough to keep in working memory: **parameters take `&str` and `&[T]`,
return types and struct fields use `String` and `Vec<T>`.** The exceptions are real but rare, and you
will recognise them when you meet them.

The second idea is that UTF-8 is the reason indexing a string is forbidden. `.len()` counts bytes,
slicing uses byte offsets and panics on a non-boundary, and `.chars()` is the O(n) way to walk
characters. This is stricter than C# and it is stricter in the direction of correctness, because C#'s
O(1) indexing into UTF-16 hands you half a surrogate pair without complaint.

If you can explain why a function should take `&str` rather than `String`, and why `"héllo".len()` is 6,
you are ready for the module this has all been leading up to.

Next: [05 — Ownership and moves](05-ownership-and-moves.md).

### Sources

- `std::string::String` API documentation. <https://doc.rust-lang.org/std/string/struct.String.html> — the owned UTF-8 string type, its representation, and its relationship to `str`.
- `std::primitive::str` API documentation. <https://doc.rust-lang.org/std/primitive.str.html> — the borrowed string slice, byte-oriented `len`, `chars`, and the char-boundary panic on slicing.
- *The Book*, ch. 8.2 "Storing UTF-8 Encoded Text with Strings". <https://doc.rust-lang.org/book/ch08-02-strings.html> — the canonical explanation of why indexing is not offered.
- *The Book*, ch. 15.2 "Treating Smart Pointers Like Regular References with `Deref`". <https://doc.rust-lang.org/book/ch15-02-deref.html> — deref coercion, including the `&String` → `&str` case.
- `std::path` module documentation. <https://doc.rust-lang.org/std/path/> — `Path`/`PathBuf`, and why paths are not guaranteed to be UTF-8.
- *Rust API Guidelines*, "Flexibility". <https://rust-lang.github.io/api-guidelines/flexibility.html> — the convention of accepting borrowed types in arguments.
