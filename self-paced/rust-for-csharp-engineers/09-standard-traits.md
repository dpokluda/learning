# 09 — The standard traits

A large part of learning Rust is learning its standard traits, because they are the vocabulary the whole
ecosystem speaks. In .NET the equivalent knowledge is spread across `IEquatable<T>`, `IComparable<T>`,
`IFormattable`, `ICloneable`, `IDisposable`, `IEnumerable<T>`, `object.GetHashCode`, implicit conversion
operators, and a set of unwritten conventions. Rust gathers all of it into about twenty traits, most of
which you can derive, and the payoff for knowing them is that unfamiliar library APIs suddenly read as
obvious. When you see `fn open<P: AsRef<Path>>(path: P)` you should immediately know what you may pass and
why the author wrote it that way.

> **Prerequisite:** [08 — Traits and generics](08-traits-and-generics.md).

## The map

Here is the territory, grouped by what the trait is for. Keep this table; the rest of the module walks
through it.

| Trait | Purpose | Derivable | Nearest C# |
|---|---|---|---|
| `Debug` | developer-facing `{:?}` | yes | `DebuggerDisplay`, `ToString()` |
| `Display` | user-facing `{}` | no | `ToString()`, `IFormattable` |
| `Clone` | explicit deep-ish copy | yes | copy constructor, `ICloneable` |
| `Copy` | implicit bitwise copy | yes | `struct` assignment semantics |
| `Default` | a sensible zero value | yes | parameterless ctor, `default(T)` |
| `PartialEq` / `Eq` | `==` | yes | `IEquatable<T>`, `Equals` |
| `PartialOrd` / `Ord` | `<`, sorting | yes | `IComparable<T>` |
| `Hash` | hashed collection key | yes | `GetHashCode()` |
| `From` / `Into` | infallible conversion | no | implicit operator, ctor |
| `TryFrom` / `TryInto` | fallible conversion | no | `TryParse`, explicit operator |
| `FromStr` | parse from text | no | `Parse` / `TryParse` |
| `AsRef` / `AsMut` | cheap reference conversion | no | (no analogue) |
| `Deref` / `DerefMut` | smart-pointer transparency | no | (no analogue) |
| `Borrow` / `ToOwned` | the owned/borrowed bridge | no | (no analogue) |
| `Iterator` / `IntoIterator` | iteration | no | `IEnumerator<T>` / `IEnumerable<T>` |
| `Drop` | deterministic cleanup | no | `IDisposable` |
| `Send` / `Sync` | thread-safety marker | auto | (convention only) |
| `Sized` | known size at compile time | auto | (implicit) |
| `Error` | error interoperability | no | `Exception` |
| `Fn` / `FnMut` / `FnOnce` | callables | auto | `Func<>` / `Action<>` |

The "no analogue" rows are worth flagging early: `AsRef`, `Deref`, and `Borrow` exist because Rust
distinguishes owned from borrowed data, and C# does not need them because everything is a reference.

## `Debug` and `Display`: two different audiences

C# has one `ToString()` doing double duty for logs and for users. Rust splits it deliberately.

```rust
use std::fmt;

#[derive(Debug)]                      // {:?} — for you
struct Finding {
    resource_id: String,
    reason: String,
    severity: u8,
}

impl fmt::Display for Finding {        // {} — for the user
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{} — {}", self.resource_id, self.reason)
    }
}

fn main() {
    let f = Finding {
        resource_id: "res-1".to_owned(),
        reason: "missing tag 'owner'".to_owned(),
        severity: 3,
    };

    assert_eq!(format!("{f}"), "res-1 — missing tag 'owner'");
    assert!(format!("{f:?}").starts_with("Finding { resource_id:"));

    // {:#?} is the pretty-printed form — invaluable in tests and debugging.
    let pretty = format!("{f:#?}");
    assert!(pretty.contains("\n    severity: 3,"));
}
```

Three rules follow. **Derive `Debug` on essentially every type** — it costs nothing, and its absence turns
`{:?}`, `assert_eq!` failure messages, and `dbg!` into compile errors. **Implement `Display` only when
there is one obvious human rendering**, because implementing it also gives you `.to_string()` for free via
a blanket impl. And **`Display` cannot be derived**, deliberately: the compiler has no idea how you want to
present your type to a person.

The blanket impl is worth seeing, because it is the pattern from module 08 in the standard library:
`impl<T: Display + ?Sized> ToString for T`. Implement `Display` and `.to_string()` appears. That is why
`5.to_string()` and `finding.to_string()` both work with no `ToString` impl anywhere in sight.

Inside `fmt`, `write!` targets the formatter and returns `fmt::Result`; the `?` operator works there, so
multi-part implementations chain naturally. The `{f}` inline-capture syntax used above is available for
any identifier in scope (Rust 1.58+) and is the modern idiom; `format!("{}", f)` still works.

## `Clone` and `Copy`

You met these in module 05; here is the precise relationship. `Clone` is an explicit, potentially
expensive duplication you call by name. `Copy` is a marker saying "duplicating this is a memcpy, so let
assignment duplicate silently instead of moving". `Copy` requires `Clone` as a supertrait, and a type can
be `Copy` only if every field is.

```rust
#[derive(Debug, Clone, Copy, PartialEq)]
struct Severity(u8);                      // all fields Copy → can be Copy

#[derive(Debug, Clone, PartialEq)]        // contains String → cannot be Copy
struct Finding { reason: String }

fn main() {
    let a = Severity(3);
    let b = a;                            // copy: `a` still usable
    assert_eq!(a, b);

    let f = Finding { reason: "x".to_owned() };
    let g = f.clone();                    // must be explicit
    assert_eq!(f, g);                     // `f` still usable because we cloned
}
```

The design intent is that **an implicit operation should never be expensive**. C# takes the opposite
position: `struct` assignment copies whatever the struct contains, however large, silently. Rust's rule
means a plain `let b = a;` is either free (Copy) or a move (no work), never a hidden deep copy.

Two practical notes. Deriving `Clone` on a generic type adds a `T: Clone` bound automatically, which is
usually right but occasionally too strict — a hand-written impl fixes it. And `Clone` is *not*
transitively deep in the C# sense of "deep clone": `Rc<T>::clone` bumps a refcount rather than duplicating
the value, which is intentional and central to module 12.

## `Default`

`Default::default()` is the parameterless constructor, and its real value is composability with the struct
update syntax and with `#[serde(default)]` later on.

```rust
#[derive(Debug, Clone, PartialEq)]
struct Settings {
    strict: bool,
    max_findings: usize,
    format: String,
}

impl Default for Settings {
    fn default() -> Self {
        Self { strict: false, max_findings: 1000, format: "plain".to_owned() }
    }
}

fn main() {
    let s = Settings { strict: true, ..Settings::default() };
    assert!(s.strict);
    assert_eq!(s.max_findings, 1000);

    // Type-directed: the annotation tells default() which impl to use.
    let n: i32 = Default::default();
    assert_eq!(n, 0);
}
```

You can `#[derive(Default)]` when every field's default is right (`false`, `0`, `""`, `None`, empty
collection), and on an enum by marking one variant `#[default]`. Write the impl by hand when a field's
sensible default is not its zero — as `max_findings: 1000` is here.

## Equality and ordering: the `Partial` prefix

This is where Rust is more precise than C#, and the reason is floating point.

`PartialEq` provides `==`. `Eq` adds no methods; it is a marker promising the relation is a full
equivalence — in particular that `a == a` for every value. `f64` implements `PartialEq` but **not** `Eq`,
because `f64::NAN != f64::NAN`. Similarly `PartialOrd` provides the comparison operators and returns
`Option<Ordering>`, while `Ord` promises a total order and returns `Ordering` outright.

```rust
use std::cmp::Ordering;

fn main() {
    let nan = f64::NAN;
    assert!(nan != nan);                                // PartialEq, not Eq
    assert_eq!(nan.partial_cmp(&1.0), None);            // no ordering exists

    assert_eq!(3.partial_cmp(&5), Some(Ordering::Less));
    assert_eq!(3.cmp(&5), Ordering::Less);              // i32: Ord, so no Option
}
```

The practical consequence is that APIs demanding a total order demand `Ord`, so `[f64]::sort()` does not
exist — you must use `sort_by(|a, b| a.partial_cmp(b).unwrap())` or, since Rust 1.82, the purpose-built
`sort_by(f64::total_cmp)`. C# lets `List<double>.Sort()` compile and then behave surprisingly around NaN;
Rust makes the ambiguity your problem at the call site, which is annoying exactly once and correct forever.

`Hash` must agree with `Eq` — equal values must hash equally — the same contract as C#'s
`Equals`/`GetHashCode` pair, except Rust enforces it structurally by making you derive both from the same
field set. A type used as a `HashMap` key needs `Eq + Hash`; one used in a `BTreeMap` needs `Ord`.

Derived ordering is lexicographic by field declaration order, which is often exactly what you want and
occasionally a trap:

```rust
#[derive(Debug, PartialEq, Eq, PartialOrd, Ord)]
struct Version { major: u32, minor: u32, patch: u32 }

fn main() {
    let mut v = vec![
        Version { major: 1, minor: 2, patch: 0 },
        Version { major: 1, minor: 0, patch: 9 },
        Version { major: 0, minor: 9, patch: 9 },
    ];
    v.sort();
    assert_eq!(v[0], Version { major: 0, minor: 9, patch: 9 });
    assert_eq!(v[2], Version { major: 1, minor: 2, patch: 0 });
}
```

Reorder the fields and you silently change the sort. When ordering matters semantically, write `impl Ord`
by hand and use `Ordering::then_with` to chain keys explicitly.

## Conversions: `From`, `Into`, `TryFrom`, `FromStr`

This family is the reason Rust APIs feel flexible without overloading.

**Implement `From`; never implement `Into`.** A blanket impl in the standard library
(`impl<T, U: From<T>> Into<U> for T`) gives you `Into` for free in the right direction, and implementing
both would conflict.

```rust
#[derive(Debug, PartialEq)]
struct ResourceId(String);

impl From<&str> for ResourceId {
    fn from(s: &str) -> Self { ResourceId(s.to_owned()) }
}

impl From<String> for ResourceId {
    fn from(s: String) -> Self { ResourceId(s) }
}

fn main() {
    let a = ResourceId::from("res-1");
    let b: ResourceId = "res-1".into();          // free, via the blanket impl
    let c: ResourceId = String::from("res-1").into();
    assert_eq!(a, b);
    assert_eq!(b, c);
}
```

That pair is what makes `fn new(id: impl Into<String>)` work: the function accepts anything that knows
how to become a `String`, which subsumes the two or three C# overloads you would have written. The cost is
one generic parameter and a monomorphised copy per argument type — still cheaper than a runtime
conversion.

`From` is also load-bearing for error handling. The `?` operator applies `From::from` to the error it
propagates, which is precisely how a `std::io::Error` turns into your `PolcheckError` with no explicit
conversion code. Module 11 makes that mechanical.

When conversion can fail, the fallible twins take over, returning `Result`:

```rust
use std::convert::TryFrom;

#[derive(Debug, PartialEq)]
struct Severity(u8);

impl TryFrom<i32> for Severity {
    type Error = String;

    fn try_from(v: i32) -> Result<Self, Self::Error> {
        match v {
            0..=5 => Ok(Severity(v as u8)),
            other => Err(format!("severity {other} out of range 0..=5")),
        }
    }
}

fn main() {
    assert_eq!(Severity::try_from(3), Ok(Severity(3)));
    assert!(Severity::try_from(99).is_err());

    // std provides TryFrom between integer types — this replaces unchecked casts.
    let big: i64 = 300;
    assert!(u8::try_from(big).is_err());
    assert_eq!(u8::try_from(200i64), Ok(200u8));
}
```

`u8::try_from(big)` deserves a moment. In C#, `(byte)300L` silently gives you 44 unless the expression is
inside `checked`. In Rust, `as` also truncates silently — but `TryFrom` gives you a checked alternative
that the compiler will not let you ignore, and clippy's `cast_possible_truncation` lint will nag you
towards it. Reach for `try_from` whenever a narrowing cast is not provably safe.

`FromStr` is the parsing trait, and it is what `.parse()` calls:

```rust
use std::str::FromStr;

#[derive(Debug, PartialEq)]
enum Format { Plain, Csv, Json }

impl FromStr for Format {
    type Err = String;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s.to_ascii_lowercase().as_str() {
            "plain" => Ok(Format::Plain),
            "csv" => Ok(Format::Csv),
            "json" => Ok(Format::Json),
            other => Err(format!("unknown format '{other}'")),
        }
    }
}

fn main() {
    assert_eq!("CSV".parse::<Format>(), Ok(Format::Csv));
    assert!("xml".parse::<Format>().is_err());

    // Turbofish or annotation — parse is generic over its return type.
    let n: i32 = "42".parse().unwrap();
    assert_eq!(n, 42);
}
```

Note that `.parse()` is generic over the *return* type, which C# cannot express — `int.Parse` and
`double.Parse` are separate static methods because C# does not dispatch on return type. This is a small
but repeated ergonomic win.

## `AsRef`, `Deref`, `Borrow`: the borrowed/owned bridge

These three exist only because Rust distinguishes owned data from borrowed data, so there is nothing to
map back to C#. They are worth understanding because they explain a lot of signatures.

**`AsRef<T>` is cheap reference-to-reference conversion**, and it is the standard way to write a function
that accepts several spellings of the same borrowed thing:

```rust
use std::path::Path;

fn extension_of(p: impl AsRef<Path>) -> Option<String> {
    p.as_ref().extension().map(|e| e.to_string_lossy().into_owned())
}

fn main() {
    use std::path::PathBuf;
    assert_eq!(extension_of("rules.json"), Some("json".to_owned()));
    assert_eq!(extension_of(String::from("a/b.toml")), Some("toml".to_owned()));
    assert_eq!(extension_of(PathBuf::from("x.yaml")), Some("yaml".to_owned()));
}
```

That one signature accepts `&str`, `String`, `&Path`, `PathBuf`, and `&OsStr`. It is why nearly every
filesystem API in `std` looks like this.

**`Deref` makes a wrapper transparent**, which is how `String` gives you every `&str` method and `Vec<T>`
gives you every slice method. The compiler inserts derefs automatically when resolving a method or
matching a reference type — *deref coercion*, which you met in module 04.

```rust
use std::ops::Deref;

struct Tracked<T> { value: T, reads: std::cell::Cell<u32> }

impl<T> Deref for Tracked<T> {
    type Target = T;
    fn deref(&self) -> &T {
        self.reads.set(self.reads.get() + 1);
        &self.value
    }
}

fn main() {
    let t = Tracked { value: String::from("hello"), reads: std::cell::Cell::new(0) };
    assert_eq!(t.len(), 5);            // String::len via Deref
    assert!(t.starts_with("he"));      // str::starts_with via two derefs
    assert_eq!(t.reads.get(), 2);
}
```

The API guidelines are firm that `Deref` is for **smart pointers only** — `Box`, `Rc`, `Arc`, `String`,
`Vec`, `MutexGuard`. Using it to simulate inheritance ("my `Dog` derefs to `Animal` so it inherits the
methods") is an anti-pattern; the method-resolution magic makes call sites unreadable and there is no
polymorphism behind it.

**`Borrow<T>` looks like `AsRef` and differs in one contract detail**: `Borrow` additionally promises that
the borrowed form hashes and compares identically to the owned form. That extra promise is what lets
`HashMap<String, V>` be queried with a `&str`:

```rust
use std::collections::HashMap;

fn main() {
    let mut m: HashMap<String, u32> = HashMap::new();
    m.insert("owner".to_owned(), 1);

    // get takes &Q where String: Borrow<Q>. Because String: Borrow<str>,
    // and str hashes the same as String, this works without allocating.
    assert_eq!(m.get("owner"), Some(&1));
}
```

Without `Borrow`, every lookup would need `m.get(&"owner".to_string())` — an allocation per lookup, which
is the shape of the C# `Dictionary<string, T>` API only because C# strings are already references.
`ToOwned` is the inverse (`str → String`, `[T] → Vec<T>`) and is what `.to_owned()` calls.

## `Iterator` and `IntoIterator`

Module 10 covers these properly; the structural point belongs here. `Iterator` is one required method and
about eighty provided ones:

```rust
struct Countdown(u32);

impl Iterator for Countdown {
    type Item = u32;

    fn next(&mut self) -> Option<u32> {
        if self.0 == 0 { return None; }
        self.0 -= 1;
        Some(self.0 + 1)
    }
}

fn main() {
    // We wrote `next`. Everything else came from default methods.
    let v: Vec<u32> = Countdown(3).collect();
    assert_eq!(v, vec![3, 2, 1]);
    assert_eq!(Countdown(5).filter(|n| n % 2 == 1).sum::<u32>(), 9);
}
```

That is the extension-trait payoff at full scale: implement `next`, receive `map`, `filter`, `zip`,
`take_while`, `fold`, and the rest as default methods that are all statically dispatched and inlined. C#'s
LINQ achieves something similar with extension methods on `IEnumerable<T>`, but every stage is a virtual
`MoveNext` call through an interface, and the compiler cannot usually collapse the chain.

`IntoIterator` is what `for` desugars to, and the three impls on collections are the ownership story in
miniature:

| Expression | Trait impl used | Yields |
|---|---|---|
| `for x in &v` | `impl IntoIterator for &Vec<T>` | `&T` |
| `for x in &mut v` | `impl IntoIterator for &mut Vec<T>` | `&mut T` |
| `for x in v` | `impl IntoIterator for Vec<T>` | `T` (consumes `v`) |

Forgetting the `&` and accidentally consuming a collection is a top-five beginner error, and this table is
the antidote.

## `Drop`: `IDisposable` without the discipline

`Drop` runs code when a value goes out of scope. The comparison to `IDisposable` is close and the
differences are all in Rust's favour.

```rust
struct Guard(&'static str);

impl Drop for Guard {
    fn drop(&mut self) {
        println!("dropping {}", self.0);
    }
}

fn main() {
    let _outer = Guard("outer");
    {
        let _inner = Guard("inner");
        println!("in scope");
    }                                  // "dropping inner" here
    println!("after block");
}                                      // "dropping outer" here — reverse declaration order
```

In C#, cleanup happens only if the caller remembers `using`, and forgetting is a silent leak that no
compiler catches — the best you get is an analyzer warning. In Rust there is no opt-in: **`drop` runs
automatically at end of scope, in reverse declaration order, on every path including early `return`, `?`
propagation, and panic unwinding.** That is `try/finally` semantics applied by the type system to every
value that needs it, which is why Rust has no `using` statement and no `finally`.

Three details. You cannot call `.drop()` yourself; to destroy early you call `std::mem::drop(value)`,
which just takes ownership and lets the value fall out of scope. A type implementing `Drop` cannot also be
`Copy`, for obvious reasons. And `Drop` cannot return a value or an error, which is genuinely awkward for
things like flushing a buffered file — the idiomatic answer is an explicit `fn close(self) -> Result<()>`
for callers who care, with `Drop` as the best-effort fallback. `std::fs::File` and `BufWriter` both work
this way.

Finalizers have no equivalent at all, and you should not miss them: Rust's `Drop` is deterministic, runs on
the owning thread, and cannot resurrect the object.

## `Send`, `Sync`, and `Sized`

Three auto traits — implemented automatically by the compiler when the structure permits, rather than by
you. Module 15 covers the concurrency ones in depth; the definitions belong here.

**`Send`** means a value can be moved to another thread. **`Sync`** means `&T` can be shared across
threads, equivalently that `T` is safe to access concurrently through shared references. Almost everything
is both; the exceptions are the interior-mutability types without synchronisation (`Rc<T>` is neither,
`RefCell<T>` is `Send` but not `Sync`) and raw pointers.

The comparison to .NET is stark. .NET has no thread-safety type system at all: `List<T>` is not thread
safe, `ConcurrentDictionary` is, and the *only* thing stopping you sharing the former across threads is
documentation and discipline. In Rust, `thread::spawn` requires its closure to be `Send`, so sharing an
`Rc` across threads is a compile error naming the offending type. This is what "fearless concurrency"
means in practice — not that concurrency becomes easy, but that data races become a compile-time category.

**`Sized`** means the size is known at compile time. Every generic parameter has an implicit `T: Sized`
bound, which is why you see `?Sized` — "size may not be known" — on functions that want to accept `str`
or `[T]` or `dyn Trait` behind a reference:

```rust
use std::fmt::Debug;

// Without ?Sized, you could not pass &dyn Debug or &str here.
fn show<T: Debug + ?Sized>(x: &T) -> String {
    format!("{x:?}")
}

fn main() {
    assert_eq!(show("hi"), "\"hi\"");
    assert_eq!(show(&5), "5");
    let d: &dyn Debug = &true;
    assert_eq!(show(d), "true");
}
```

## `Fn`, `FnMut`, `FnOnce`

Closures implement one to three of these, depending on what they do with their captures, and the compiler
picks automatically. The hierarchy is `Fn ⊂ FnMut ⊂ FnOnce`.

```rust
fn call_twice(f: impl Fn() -> i32) -> i32 { f() + f() }
fn call_mut(mut f: impl FnMut()) { f(); f(); }
fn call_once(f: impl FnOnce() -> String) -> String { f() }

fn main() {
    let base = 10;
    assert_eq!(call_twice(|| base), 20);            // captures by shared ref → Fn

    let mut count = 0;
    call_mut(|| count += 1);                        // captures by mut ref → FnMut
    assert_eq!(count, 2);

    let owned = String::from("moved");
    assert_eq!(call_once(move || owned), "moved");  // consumes capture → FnOnce
}
```

C#'s `Func<>`/`Action<>` make no such distinction because captured variables are hoisted into a
heap-allocated closure class and the GC handles the rest. Rust's three-way split is what lets a
non-capturing closure compile to a plain function pointer with zero allocation, and what lets the compiler
reject a closure that would need to consume a captured value twice. When you write a function taking a
callback, **accept the weakest bound that works** — `FnOnce` if you call it once, `FnMut` if repeatedly
with state, `Fn` only if you need to call it from several places at once.

## `Error`

The trait that makes error types interoperate:

```rust
use std::error::Error;
use std::fmt;

#[derive(Debug)]
struct RuleError {
    rule: String,
    source: Option<Box<dyn Error + Send + Sync>>,
}

impl fmt::Display for RuleError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "rule '{}' failed", self.rule)
    }
}

impl Error for RuleError {
    fn source(&self) -> Option<&(dyn Error + 'static)> {
        self.source.as_ref().map(|b| b.as_ref() as &(dyn Error + 'static))
    }
}

fn main() {
    let e = RuleError { rule: "require-owner".to_owned(), source: None };
    assert_eq!(e.to_string(), "rule 'require-owner' failed");
    assert!(e.source().is_none());
}
```

`Error` requires `Debug + Display` and adds an optional `source()` — the exact analogue of
`Exception.InnerException`, giving you a chain you can walk to build a "caused by" report. Module 19 shows
how `thiserror` derives all of that boilerplate away, which is why almost nobody writes the impl above by
hand.

## Before you move on

The standard traits are the shared vocabulary, and knowing them converts unfamiliar signatures into
obvious ones. `Debug` and `Display` split .NET's overloaded `ToString` into a developer view you should
always derive and a user view you implement only when there is one right rendering. `Clone` and `Copy`
encode the rule that implicit operations must be cheap. The `Partial` prefix on `PartialEq` and
`PartialOrd` exists because floating point has no total order, and the resulting friction around sorting
`f64` is correctness surfacing rather than a design flaw.

The conversion family — implement `From`, get `Into` free; use `TryFrom` when it can fail; implement
`FromStr` so `.parse()` works — is what removes the need for overloading, and `From` is also the hidden
machinery inside the `?` operator. The borrowed/owned bridge of `AsRef`, `Deref`, and `Borrow` has no C#
counterpart because C# has no owned/borrowed distinction; `AsRef` makes functions accept every spelling of
a path or string, `Deref` makes smart pointers transparent, and `Borrow`'s extra hashing promise is why you
can look up a `HashMap<String, V>` with a `&str`.

`Drop` is `IDisposable` that the compiler applies for you on every exit path, which is why Rust needs
neither `using` nor `finally` nor finalizers. `Send` and `Sync` turn .NET's thread-safety conventions into
compiler-checked properties. And the `Fn`/`FnMut`/`FnOnce` split is what makes closures zero-cost.

If you can say why `impl Into<String> for MyType` is the wrong thing to write, what `Eq` promises beyond
`PartialEq`, and what would go wrong if `Borrow` were merged into `AsRef`, you have the vocabulary. Next
we put `Iterator` to work.

Next: [10 — Collections and iterators](10-collections-and-iterators.md).

### Sources

- `std` API documentation. <https://doc.rust-lang.org/std/> — the normative reference for every trait in this module.
- *Rust API Guidelines*, "Interoperability" (C-COMMON-TRAITS, C-CONV-TRAITS, C-DEREF). <https://rust-lang.github.io/api-guidelines/interoperability.html> — which traits to implement eagerly, why to implement `From` rather than `Into`, and why `Deref` is for smart pointers only.
- *The Book*, ch. 10. <https://doc.rust-lang.org/book/ch10-02-traits.html> — trait basics and blanket impls, including `ToString`.
- *The Book*, ch. 15.3 "Running Code on Cleanup with the Drop Trait". <https://doc.rust-lang.org/book/ch15-03-drop.html> — drop order and `std::mem::drop`.
- *The Rustonomicon*, "Send and Sync". <https://doc.rust-lang.org/nomicon/send-and-sync.html> — the auto-trait rules and what unsafely implementing them means.
- `f64::total_cmp`. <https://doc.rust-lang.org/std/primitive.f64.html#method.total_cmp> — total ordering for floats, stabilised in Rust 1.62.
- `std::borrow::Borrow`. <https://doc.rust-lang.org/std/borrow/trait.Borrow.html> — the hashing/equality contract that distinguishes it from `AsRef`.
