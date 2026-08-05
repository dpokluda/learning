# 03 — Syntax orientation

This module gets you to the point where Rust code stops looking foreign. It is not a grammar reference —
the [Rust Reference](https://doc.rust-lang.org/reference/) is that, and you should bookmark it — but a
tour of the places where Rust's surface syntax encodes a decision that C# made differently. Most of the
syntax you can absorb by reading. The four things that genuinely trip up experienced C# developers are
expression orientation, shadowing, immutability-by-default, and integer overflow behaviour, so those get
most of the space.

> **Prerequisite:** [02 — The toolchain and project model](02-toolchain-and-cargo.md).

## Almost everything is an expression

C# has a clear statement/expression split that has been eroding for years — expression-bodied members,
the ternary operator, switch expressions, `with` expressions. Rust started at the destination: **nearly
every construct is an expression that evaluates to a value.** Blocks, `if`, `match`, and loops all
produce values.

The mechanical rule is short and worth memorising, because it explains a class of confusing errors. A
block's value is its **final expression with no trailing semicolon**. Adding a semicolon turns an
expression into a statement, and a statement's value is the unit type `()` — Rust's `void`, except that
it is a real type with exactly one value.

```rust
fn classify(n: i32) -> &'static str {
    // `if` is an expression; this is the return value.
    if n < 0 {
        "negative"
    } else if n == 0 {
        "zero"
    } else {
        "positive"
    }
}

fn main() {
    // A block is an expression, so this is a natural way to scope temporaries.
    let area = {
        let width = 3;
        let height = 4;
        width * height          // no semicolon => this is the block's value
    };

    // `match` is an expression, like a C# switch expression but exhaustive
    // over any pattern, not just constants.
    let label = match area {
        0 => "empty",
        1..=9 => "small",
        _ => "large",
    };

    println!("{area} {label} {}", classify(-1));
}
```

The `return` keyword exists but is reserved for *early* return. Writing `return x;` as the last line of a
function is legal, and clippy will tell you to drop it. This is genuinely a style shift: in C# every
method ends with `return`, and in Rust the trailing expression is the idiom, with `return` reading as a
deliberate escape.

The semicolon rule catches everyone once. If a function is supposed to return `i32` and you write
`x + 1;` on the last line, the block evaluates to `()` and you get a type-mismatch error pointing at the
function signature. The compiler will usually tell you to remove the semicolon, in those words.

One consequence with no C# analogue: some expressions have the type `!`, the *never type*, meaning they
do not produce a value because control flow does not continue. `panic!(...)`, `return`, `continue`, and
`std::process::exit` all have this type, and `!` coerces to any type. That is why this compiles:

```rust
fn get(v: &[i32], i: usize) -> i32 {
    match v.get(i) {
        Some(x) => *x,
        None => panic!("index {i} out of range"),   // type `!`, coerces to i32
    }
}
```

## `let`, mutability, and shadowing

Three ideas collide in one keyword, so take them separately.

**Bindings are immutable by default.** `let x = 5;` creates a binding you cannot assign to. To get the
C# default you must opt in with `mut`. This inverts C#'s convention — where `readonly` and `const` are
the annotations — and the inversion is deliberate: immutability is the common case in well-written code,
so it should be the cheap one to express.

**Type inference is local but strong.** Rust infers from the whole function body, not just the
initialiser, so `let v = Vec::new();` compiles if a later `v.push(1)` pins the element type. This is
more capable than `var`, which only ever looks at the right-hand side. Function signatures, however, are
never inferred — every parameter and return type must be written. That boundary is intentional: it keeps
inference tractable and makes public APIs self-documenting.

**Shadowing is a first-class idiom, and it is not mutation.** You can `let` the same name again in the
same scope, creating a genuinely new binding — possibly of a different type — that hides the old one.

```rust
fn main() {
    let spaces = "   ";           // &str
    let spaces = spaces.len();    // usize — a NEW binding, different type
    println!("{spaces}");

    let mut count = 0;            // opt in to mutation
    count += 1;
    println!("{count}");
}
```

C# forbids this outright inside a method scope, and a C# developer's first reaction is that it looks like
a bug waiting to happen. In practice it is the opposite: shadowing is how Rust avoids the
`inputString` / `parsedInput` / `validatedInput` naming ladder. The idiomatic pattern is to shadow a
value as it moves through stages of refinement, so the name always refers to the most processed form and
the earlier, less-valid form becomes inaccessible. That is a safety property, not a hazard.

The distinction from `mut` matters. `mut` means "this storage location can change"; shadowing means
"this name now refers to a different value, of possibly a different type". Shadowing works on immutable
bindings and can change type; `mut` does neither.

## Primitive types

The type list holds few surprises, but the naming and the sizing rules differ from C# in ways worth
tabulating.

| Rust | C# | Notes |
|---|---|---|
| `i8` `i16` `i32` `i64` `i128` | `sbyte` `short` `int` `long` — | `i128` has no C# equivalent |
| `u8` `u16` `u32` `u64` `u128` | `byte` `ushort` `uint` `ulong` — | |
| `isize` / `usize` | `nint` / `nuint` | Pointer-sized. **`usize` is the type of all indices and lengths.** |
| `f32` / `f64` | `float` / `double` | No `decimal` in std; use the `rust_decimal` crate |
| `bool` | `bool` | No implicit conversion to/from integers |
| `char` | `char` | **4 bytes, a Unicode scalar value** — not UTF-16 |
| `()` | `void` | A real type with one value, usable as a generic argument |
| `!` | — | The never type |

Two rows deserve elaboration. `usize` being the index type means you will convert between it and `i32`
more than you would like, and Rust will never do it implicitly — there are **no implicit numeric
conversions at all**, not even the widening ones C# performs silently. `let x: i64 = my_i32;` is an
error; you write `my_i32 as i64`, or better `i64::from(my_i32)` when the conversion is lossless, because
`as` also permits lossy casts and silently truncates.

`char` being a 4-byte Unicode scalar value rather than a UTF-16 code unit is a real semantic difference.
In C#, `char` is 16 bits and anything outside the Basic Multilingual Plane is a surrogate pair, so
`"😀".Length` is 2. In Rust, `'😀'` is a single `char`, but `"😀".len()` is **4**, because `len()` on a
string returns bytes. Module 04 unpacks this properly.

## Integer overflow: a genuine behavioural difference

This one bites people, so it gets its own section.

In C#, integer arithmetic is unchecked by default: `int.MaxValue + 1` silently wraps to `int.MinValue`
unless you opt into a `checked` context or set `<CheckForOverflowUnderflow>`. In Rust the default depends
on the build profile. **In debug builds, overflow panics.** In release builds, it wraps using two's
complement.

```rust,ignore
fn main() {
    let x: u8 = 255;
    let y = x + 1;      // debug: panics with 'attempt to add with overflow'
    println!("{y}");    // release: prints 0
}
```

(That snippet is marked `ignore` because when both operands are compile-time constants, rustc
refuses it outright with a `this arithmetic operation will overflow` lint rather than deferring the
question to runtime. The profile-dependent behaviour described above applies to values the compiler
cannot evaluate ahead of time — anything derived from input.)

This is a deliberate and slightly uncomfortable compromise: the panic catches bugs during development,
and the wrap avoids paying for a check in hot release code. What it means for you is that **you must not
rely on the default behaviour when overflow is semantically possible.** Say what you mean instead, using
the explicit families that exist on every integer type:

```rust
fn main() {
    let x: u8 = 255;

    // Wrapping: two's-complement wrap, in every profile.
    assert_eq!(x.wrapping_add(1), 0);

    // Checked: None on overflow. This is the TryParse-shaped option.
    assert_eq!(x.checked_add(1), None);
    assert_eq!(x.checked_add(0), Some(255));

    // Saturating: clamp at the bound.
    assert_eq!(x.saturating_add(1), 255);

    // Overflowing: the wrapped value plus a did-it-overflow flag.
    assert_eq!(x.overflowing_add(1), (0, true));
}
```

The C# habit of writing `a + b` and trusting it is fine for values you know are bounded, and a latent bug
for values derived from input. Reach for `checked_*` when parsing or aggregating untrusted data — it
returns `Option`, which composes with the error handling in module 11.

## Control flow

`if` requires a `bool`; there is no truthiness and no implicit conversion, so `if x` where `x` is an
integer is a type error rather than a subtle bug. Parentheses around the condition are not used, and
braces are always required even for a single statement, which eliminates the dangling-else class of
mistake entirely.

Rust has three loop forms. `loop` is an infinite loop, and it is the only one that can carry a value out
via `break value` — a small feature that removes a lot of sentinel-variable boilerplate:

```rust
fn main() {
    let mut attempts = 0;
    let result = loop {
        attempts += 1;
        if attempts * attempts > 50 {
            break attempts;          // `loop` evaluates to this
        }
    };
    println!("{result}");            // 8
}
```

`while` is what you expect. `for` is *only* the `foreach` form — there is no C-style
`for (int i = 0; ...)` — and it iterates anything implementing `IntoIterator`, which is the
`IEnumerable` analogue covered in module 10. Ranges give you counting:

```rust
fn main() {
    for i in 0..5 { print!("{i}"); }        // 01234   — exclusive, like C# `for (i=0; i<5; i++)`
    println!();
    for i in 0..=5 { print!("{i}"); }       // 012345  — inclusive
    println!();
    for i in (0..5).rev() { print!("{i}"); } // 43210
    println!();

    let items = ["a", "b", "c"];
    for (idx, item) in items.iter().enumerate() {
        print!("{idx}{item} ");             // 0a 1b 2c
    }
    println!();
}
```

Loop labels exist and are more useful than C#'s `goto`-based escape from nested loops:

```rust
fn main() {
    'outer: for i in 0..5 {
        for j in 0..5 {
            if i * j > 6 { break 'outer; }
        }
    }
}
```

## Tuples, arrays, slices, and `Vec`

Rust's sequence types map onto C# concepts you know, but the distinctions are sharper because they
correspond to different memory layouts rather than different interfaces.

**Tuples** are anonymous fixed-size heterogeneous products, like C#'s `ValueTuple`. They destructure in
`let`, which is the common use:

```rust
fn min_max(v: &[i32]) -> (i32, i32) {
    let mut lo = v[0];
    let mut hi = v[0];
    for &x in v {
        if x < lo { lo = x; }
        if x > hi { hi = x; }
    }
    (lo, hi)
}

fn main() {
    let (lo, hi) = min_max(&[3, 1, 4, 1, 5]);
    println!("{lo} {hi}");

    let point = (1.0, 2.0);
    println!("{}", point.0);       // positional access, like C# Item1 but 0-based
}
```

Unlike C# named tuples, Rust tuples have no field names; if you want names, use a struct. The unit type
`()` is the zero-element tuple, which is why `void` and "empty tuple" are the same thing in Rust.

**Arrays** are `[T; N]` — fixed length, length part of the type, stored inline. This is close to C#'s
`T[]` in spirit but importantly different in layout: a Rust `[i32; 4]` is 16 contiguous bytes with no
header and no heap allocation, more like a C# `fixed` buffer or four adjacent fields than like `int[]`,
which is a heap object with a length header.

**`Vec<T>`** is the growable heap-allocated sequence, and it is the true analogue of C#'s `List<T>`.

**Slices** are `&[T]` — a borrowed view of a contiguous run of elements, represented as a pointer plus a
length. This is exactly `Span<T>`/`ReadOnlySpan<T>`, and the analogy is unusually good: both are
non-owning windows, both are two words wide, both make subranges free. The difference is that in Rust,
slices are the *normal* way to pass sequences, whereas `Span<T>` in C# is a performance tool you reach
for deliberately.

```rust
fn sum(values: &[i32]) -> i32 {     // takes a slice: works for arrays AND Vecs
    let mut total = 0;
    for v in values { total += v; }
    total
}

fn main() {
    let arr: [i32; 4] = [1, 2, 3, 4];
    let vec: Vec<i32> = vec![1, 2, 3, 4];

    println!("{}", sum(&arr));        // &[i32; 4] coerces to &[i32]
    println!("{}", sum(&vec));        // &Vec<i32>  coerces to &[i32] via Deref
    println!("{}", sum(&vec[1..3]));  // a sub-slice: no copy, no allocation
}
```

That function signature is the single most important API-design habit in this module: **accept `&[T]`,
not `&Vec<T>`.** Taking the slice makes the function work with arrays, `Vec`s, and sub-ranges alike, and
it costs nothing. It is the same instinct as accepting `IEnumerable<T>` in C# rather than `List<T>`,
except that here the general version is also the faster one, since there is no interface dispatch.

Indexing is bounds-checked and panics on failure, like C#. The non-panicking form returns an `Option`:

```rust
fn main() {
    let v = vec![1, 2, 3];
    println!("{}", v[1]);                    // 2; panics if out of range
    println!("{:?}", v.get(10));             // None
    println!("{}", v.get(10).copied().unwrap_or(-1));  // -1
}
```

## Functions, closures, and formatting

Function syntax is `fn name(param: Type) -> ReturnType`. Parameter types are mandatory, the return type
is omitted when it is `()`, and there is no overloading — a name means one function, which is why the
standard library has `checked_add`, `wrapping_add`, and `saturating_add` rather than three overloads.
There are also no default parameter values and no named arguments; the idiomatic replacements are the
builder pattern and `Option` parameters, both covered in module 28.

Closures use `|args| body`, with types usually inferred:

```rust
fn main() {
    let add = |a: i32, b: i32| a + b;
    let double = |x| x * 2;                       // type inferred from use below
    let nums: Vec<i32> = (1..=5).map(double).collect();
    println!("{} {:?}", add(1, 2), nums);
}
```

The syntax is lighter than C#'s lambdas, but there is a deep difference: a Rust closure has an anonymous
concrete type and captures by borrow, by mutable borrow, or by move, chosen by the compiler based on
what the body does. `move |x| ...` forces capture by value. C# closures always capture variables by
reference into a compiler-generated display class, which is why mutating a captured local is visible to
the caller. In Rust the capture mode is part of the closure's semantics, and it interacts with ownership
— which is module 05.

Finally, string formatting, which you will use constantly. `println!` is a macro (the `!` marks it) with
compile-time-checked format strings, closer to a source generator than to `string.Format`:

```rust
fn main() {
    let name = "polcheck";
    let count = 3;

    println!("{name} found {count} issues");      // inline captured identifiers
    println!("{} found {} issues", name, count);  // positional
    println!("{0} {1} {0}", name, count);         // indexed
    println!("{:>8}|{:<8}|{:^8}|", "r", "l", "c");// right/left/centre in width 8
    println!("{:.2}", 3.14159);                   // 3.14
    println!("{:08.3}", 3.14159);                 // 0003.142
    println!("{:#x} {:#b}", 255, 5);              // 0xff 0b101
    println!("{:?}", vec![1, 2]);                 // Debug:  [1, 2]
    println!("{:#?}", vec![1, 2]);                // pretty Debug, multi-line
}
```

`{}` requires the type to implement `Display` and `{:?}` requires `Debug`. This is the key difference
from C#, where every object has `ToString()` inherited from `System.Object`. In Rust there is no
universal base class, so a type that has not opted into `Display` cannot be printed with `{}` — and most
types you define will start out with only `#[derive(Debug)]`, which is why `{:?}` is so common. Module 09
covers both traits.

## Before you move on

The syntax itself is not the point of this module; the four semantic differences hiding inside it are.
Expression orientation means blocks, `if`, and `match` all produce values, and the presence or absence of
a trailing semicolon decides whether a block evaluates to something or to `()`. Immutability is the
default and `mut` is the opt-in, which inverts C#'s convention. Shadowing is a legitimate and common
idiom that creates a new binding rather than mutating an old one, and it exists so that a name can follow
a value through successive refinements. And integer overflow panics in debug and wraps in release, which
means any arithmetic on untrusted input should use the explicit `checked_`, `saturating_`, or `wrapping_`
family rather than the bare operator.

The one API-design habit to take with you is to accept `&[T]` rather than `&Vec<T>`, for the same reason
you accept `IEnumerable<T>` rather than `List<T>` — except that in Rust the general form is also free.

If you can explain why `x + 1;` with a semicolon breaks a function that returns `i32`, and describe the
difference between `let mut x` and re-`let`ing `x`, you are ready for the module that trips up more C#
developers than any other.

Next: [04 — Strings, slices, and `Vec`](04-strings-and-slices.md).

### Sources

- *The Rust Reference*, "Expressions". <https://doc.rust-lang.org/reference/expressions.html> — normative description of expression orientation, block values, and the never type.
- *The Rust Reference*, "Overflow". <https://doc.rust-lang.org/reference/expressions/operator-expr.html#overflow> — specifies that overflow checks are on in debug and off (wrapping) in release, and that this is controlled by `debug-assertions`/`overflow-checks`.
- *The Book*, ch. 3.1 "Variables and Mutability" and ch. 3.2 "Data Types". <https://doc.rust-lang.org/book/ch03-01-variables-and-mutability.html> — shadowing versus mutability, and the primitive type list.
- `std::primitive::u8` API documentation. <https://doc.rust-lang.org/std/primitive.u8.html> — the `checked_*`, `wrapping_*`, `saturating_*`, and `overflowing_*` method families.
- `std::fmt` module documentation. <https://doc.rust-lang.org/std/fmt/> — the full format-specification grammar, and the `Display`/`Debug` distinction.
