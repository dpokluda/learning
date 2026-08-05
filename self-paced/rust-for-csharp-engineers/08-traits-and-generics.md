# 08 — Traits and generics

If enums replaced your class hierarchies, traits replace your interfaces — and then keep going, absorbing
extension methods, generic constraints, operator overloading, and a good chunk of what you would reach
for Roslyn analyzers to enforce. A trait is C#'s interface with three superpowers bolted on: it can be
implemented for types you did not write, it can carry default method bodies and associated types, and it
can be used either as a compile-time constraint (zero cost) or as a runtime vtable (one pointer of
indirection) — your choice, spelled out in the signature.

> **Prerequisite:** [07 — Structs, enums, and pattern matching](07-structs-enums-matching.md).

## The first surprise: implementations are separate from types

In C#, a type declares the interfaces it implements at the point of declaration. `class Foo : IBar` is
part of `Foo`. If `Foo` comes from a NuGet package and does not implement `IBar`, you are out of luck —
you write an adapter, or an extension method that fakes it without ever satisfying the constraint
`where T : IBar`.

In Rust, the implementation is a *separate item*:

```rust
trait Describe {
    fn describe(&self) -> String;
}

struct Resource { id: String, kind: String }

// The impl is its own item. It could live in another file.
impl Describe for Resource {
    fn describe(&self) -> String {
        format!("{} ({})", self.id, self.kind)
    }
}

// And it works for types you did not define — including std types.
impl Describe for u32 {
    fn describe(&self) -> String {
        format!("the number {self}")
    }
}

impl Describe for Vec<String> {
    fn describe(&self) -> String {
        format!("{} strings", self.len())
    }
}

fn main() {
    let r = Resource { id: "res-1".to_owned(), kind: "storage".to_owned() };
    assert_eq!(r.describe(), "res-1 (storage)");
    assert_eq!(42u32.describe(), "the number 42");
    assert_eq!(vec!["a".to_owned()].describe(), "1 strings");
}
```

Read that again with C# eyes. We taught `u32` and `Vec<String>` to implement our interface, after the
fact, without wrapping them. Extension methods let you *call* a method on a type you do not own, but they
never make that type satisfy an interface constraint. Trait impls do. This is the mechanism behind most
of the ecosystem's ergonomics: `serde` works on your types because you can `impl Serialize for YourType`
(via derive); `rayon` gives your collection `.par_iter()` because it implements a trait for it.

### The orphan rule

That power has one guard rail. You may write `impl Trait for Type` only if **either the trait or the type
is local to your crate**. You cannot `impl Display for Vec<T>` — both are someone else's — because if two
crates did that, the compiler would not know which impl to use, and adding a dependency could break your
build. This is the *orphan rule* (formally, the coherence rules).

The workaround is the newtype pattern, which you will use often enough that it deserves seeing now:

```rust
use std::fmt;

// Can't impl Display for Vec<String>, so wrap it.
struct Locations(Vec<String>);

impl fmt::Display for Locations {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "[{}]", self.0.join(", "))
    }
}

fn main() {
    let l = Locations(vec!["westus2".to_owned(), "eastus".to_owned()]);
    assert_eq!(l.to_string(), "[westus2, eastus]");
}
```

A newtype is a zero-cost wrapper — the tuple struct compiles away entirely — and it is the idiomatic
answer to "I need different behaviour for an existing type". C# has no equivalent that is free; a wrapper
class costs an allocation and an indirection.

## Default methods and the extension-trait idiom

Traits can supply method bodies, which C# only gained (partially, and controversially) with default
interface members:

```rust
trait Describe {
    /// Required: implementors must provide this.
    fn name(&self) -> String;

    /// Optional: a default body implementors may override.
    fn describe(&self) -> String {
        format!("<{}>", self.name())
    }

    fn shout(&self) -> String {
        self.describe().to_uppercase()
    }
}

struct Resource { id: String }
impl Describe for Resource {
    fn name(&self) -> String { self.id.clone() }
}

struct Policy { title: String }
impl Describe for Policy {
    fn name(&self) -> String { self.title.clone() }
    fn describe(&self) -> String { format!("policy '{}'", self.title) }   // override
}

fn main() {
    let r = Resource { id: "res-1".to_owned() };
    assert_eq!(r.describe(), "<res-1>");
    assert_eq!(r.shout(), "<RES-1>");

    let p = Policy { title: "tagging".to_owned() };
    assert_eq!(p.describe(), "policy 'tagging'");
}
```

Combine default methods with a **blanket impl** — an impl for *every* type satisfying some bound — and you
get the extension-trait idiom, which is how the ecosystem adds methods to whole categories of type:

```rust
trait Tap: Sized {
    /// Run a side effect on a value and pass it along. Handy in chains.
    fn tap(self, f: impl FnOnce(&Self)) -> Self {
        f(&self);
        self
    }
}

// Blanket impl: every sized type gets `tap`, for free, with no per-type code.
impl<T: Sized> Tap for T {}

fn main() {
    let mut seen = Vec::new();
    let total: i32 = vec![1, 2, 3]
        .into_iter()
        .map(|x| x * 2)
        .sum::<i32>()
        .tap(|v| seen.push(*v));

    assert_eq!(total, 12);
    assert_eq!(seen, vec![12]);
}
```

That is a static-dispatch, zero-allocation, constraint-satisfying extension method — strictly more capable
than C#'s `static class Extensions`. The catch, and it is a real one: **the trait must be in scope for its
methods to be callable.** If a crate's docs tell you to `use foo::prelude::*` or `use anyhow::Context`,
this is why. Forgetting the import produces "no method named `context` found", which is one of the most
common early confusions, and the fix is always an import rather than a code change.

## Generics and bounds

Generic syntax will look familiar; the semantics differ in one important way.

```rust
use std::fmt::Debug;

// Inline bound.
fn largest<T: PartialOrd + Copy>(items: &[T]) -> T {
    let mut best = items[0];
    for &item in items {
        if item > best { best = item; }
    }
    best
}

// where clause: same thing, better for long bounds.
fn dump<T, U>(a: T, b: U) -> String
where
    T: Debug,
    U: Debug + Clone,
{
    format!("{a:?} / {:?}", b.clone())
}

fn main() {
    assert_eq!(largest(&[3, 7, 2]), 7);
    assert_eq!(largest(&[1.5, 0.2]), 1.5);
    assert_eq!(dump("x", vec![1, 2]), "\"x\" / [1, 2]");
}
```

The syntax maps almost one-to-one onto C#: `<T: PartialOrd + Copy>` is `<T> where T : IComparable<T>`,
and `where` clauses exist in both. The difference is what the compiler does with it.

**C# generics are reified and shared.** One IL definition exists; at runtime the JIT shares a single
native implementation across all reference-type instantiations and specialises value types. Type
information survives to runtime, which is why `typeof(T)` and reflection over generics work.

**Rust generics are monomorphised.** For every concrete `T` you actually use, the compiler emits a
separate, fully specialised copy of the function and then optimises it as if you had written it by hand.
`largest::<i32>` and `largest::<f64>` become two distinct functions in the binary. There is no runtime
type information, no boxing, no shared slow path — and calls through the generic parameter inline exactly
like direct calls.

The consequences run in both directions and are worth internalising:

| | C# generics | Rust generics |
|---|---|---|
| Dispatch | virtual call through interface (unless devirtualised) | direct call, usually inlined |
| Runtime cost | boxing for value types via interfaces; JIT shared code | zero |
| Binary size | one definition | one copy per instantiation ("code bloat") |
| Compile time | fast | slower; monomorphisation is real work |
| `typeof(T)` / reflection | yes | no (types are erased after codegen) |
| Constraint checking | at definition, and at use | at definition, and at use |

That last row matters. Both languages check generic code *at the definition site* — you cannot call a
method on `T` unless the constraint permits it. This is the opposite of C++ templates, where errors only
appear on instantiation, and it is why Rust generic errors are readable.

"Code bloat" sounds alarming and is usually fine; where it bites (a generic function instantiated over
dozens of types) the standard trick is a thin generic wrapper that immediately calls a non-generic inner
function, which is exactly what `std::fs::read` does with its `AsRef<Path>` parameter.

## `impl Trait`: the ergonomic shorthand

Two positions accept `impl Trait`, and they mean different things.

**In argument position**, `impl Trait` is sugar for an anonymous generic parameter:

```rust
fn print_all(items: impl IntoIterator<Item = String>) {
    for s in items { println!("{s}"); }
}

// Exactly equivalent to:
fn print_all_explicit<I: IntoIterator<Item = String>>(items: I) {
    for s in items { println!("{s}"); }
}

fn main() {
    print_all(vec!["a".to_owned()]);
    print_all_explicit(vec!["b".to_owned()]);
}
```

You have already seen the most common instance of this: `fn new(id: impl Into<String>)` accepts `&str`,
`String`, or anything convertible, which is Rust's answer to C# overloading a constructor for `string`
and `ReadOnlySpan<char>`.

**In return position**, `impl Trait` means something stronger: "I return *one specific* concrete type that
implements this trait, but I am not telling you which." It is not a trait object; it is static dispatch
with a hidden type.

```rust
fn evens(limit: u32) -> impl Iterator<Item = u32> {
    (0..limit).filter(|n| n % 2 == 0)
}

fn main() {
    assert_eq!(evens(10).collect::<Vec<_>>(), vec![0, 2, 4, 6, 8]);
}
```

The actual return type there is `Filter<Range<u32>, {closure}>` — unnameable, because closures have no
names. Before `impl Trait`, you had to return `Box<dyn Iterator<Item = u32>>` and pay an allocation plus
dynamic dispatch. Now it is free. This is the single biggest reason iterator-heavy Rust reads well.

Two restrictions. You cannot return different concrete types from different branches — `if x { a.iter() }
else { b.chain(c) }` will not compile, because those are different types; that case genuinely needs a
`Box<dyn Iterator>` or an enum. And in edition 2024 there is a subtlety about lifetime capture that you
will occasionally hit:

```rust
/// `use<'_>` says explicitly which lifetimes the opaque type captures.
fn words(text: &str) -> impl Iterator<Item = &str> + use<'_> {
    text.split_whitespace()
}

fn main() {
    let s = String::from("alpha beta gamma");
    assert_eq!(words(&s).count(), 3);
}
```

In edition 2024 return-position `impl Trait` captures all in-scope lifetimes by default, so the `use<'_>`
is usually unnecessary — but you will see it in code that wants to be explicit or that needs to capture
*fewer* lifetimes than the default.

## Static versus dynamic dispatch

Here is the decision C# makes for you and Rust makes you state.

```rust
trait Check {
    fn check(&self, n: i32) -> bool;
}

struct IsPositive;
impl Check for IsPositive { fn check(&self, n: i32) -> bool { n > 0 } }

struct IsEven;
impl Check for IsEven { fn check(&self, n: i32) -> bool { n % 2 == 0 } }

/// Static dispatch: monomorphised, inlined, zero cost. One copy per T.
fn run_static<C: Check>(c: &C, n: i32) -> bool {
    c.check(n)
}

/// Dynamic dispatch: one copy of the function, vtable lookup at the call.
fn run_dynamic(c: &dyn Check, n: i32) -> bool {
    c.check(n)
}

fn main() {
    assert!(run_static(&IsPositive, 5));
    assert!(run_dynamic(&IsEven, 4));

    // The real reason to use dyn: a heterogeneous collection.
    let checks: Vec<Box<dyn Check>> = vec![Box::new(IsPositive), Box::new(IsEven)];
    let all_pass = checks.iter().all(|c| c.check(4));
    assert!(all_pass);

    // You cannot write Vec<C: Check> — a Vec holds exactly one type.
}
```

`&dyn Check` is a **fat pointer**: two words, one pointing at the data and one at a vtable. That is
precisely how a C# interface reference works under the hood, except C# puts the method table pointer in
the object header rather than in the reference. The practical difference is that a Rust value only carries
a vtable when you ask for one, so a `Vec<IsPositive>` has no per-element overhead at all, while a C#
`List<IsPositive>` of a class has an object header per element whether or not anyone uses the interface.

Choose `dyn` when you need a heterogeneous collection, when you want to keep a plugin boundary open,
when monomorphisation would explode compile times or binary size, or when the type is genuinely only known
at runtime. Choose generics otherwise, which in practice means most of the time.

### Object safety

Not every trait can become a `dyn Trait`. The compiler must be able to build a vtable, and some signatures
make that impossible:

```rust,compile_fail
trait Bad {
    fn make() -> Self;              // no self: nothing to dispatch on
}

fn use_it(b: &dyn Bad) {}           // error: `Bad` is not dyn compatible
```

The rules that matter in practice: a method usable through `dyn` must take some form of `self`, must not
be generic over its own type parameters, and must not return `Self` by value. Traits like `Clone` (returns
`Self`) and `PartialEq` (generic-ish, takes `&Self`) are therefore not directly usable as trait objects,
which is why you see `Box<dyn Error>` everywhere but never `Box<dyn Clone>`.

If a trait is *almost* object-safe, the standard fix is to move the offending method behind a `where Self:
Sized` bound, which excludes it from the vtable while keeping it callable on concrete types:

```rust
trait Check {
    fn check(&self, n: i32) -> bool;

    /// Not part of the vtable — only callable when Self is concrete.
    fn boxed(self) -> Box<dyn Check>
    where
        Self: Sized + 'static,
    {
        Box::new(self)
    }
}

struct IsPositive;
impl Check for IsPositive { fn check(&self, n: i32) -> bool { n > 0 } }

fn main() {
    let b: Box<dyn Check> = IsPositive.boxed();
    assert!(b.check(1));
}
```

## Associated types

C# expresses "this interface has a related type" with a generic parameter: `IEnumerable<T>`. Rust can do
that too, but it also offers **associated types**, and the difference is about how many implementations a
type may have.

```rust
// Generic parameter: a type may implement this many times, once per T.
trait Convert<T> {
    fn convert(&self) -> T;
}

struct Meters(f64);
impl Convert<f64> for Meters { fn convert(&self) -> f64 { self.0 } }
impl Convert<String> for Meters { fn convert(&self) -> String { format!("{}m", self.0) } }

// Associated type: a type may implement this exactly once, fixing Output.
trait Parse {
    type Output;
    fn parse_it(&self, s: &str) -> Option<Self::Output>;
}

struct IntParser;
impl Parse for IntParser {
    type Output = i64;
    fn parse_it(&self, s: &str) -> Option<i64> { s.parse().ok() }
}

fn main() {
    let m = Meters(3.5);
    let as_f: f64 = m.convert();
    let as_s: String = m.convert();      // needs the annotation to disambiguate
    assert_eq!(as_f, 3.5);
    assert_eq!(as_s, "3.5m");

    // No annotation needed: Output is determined by the impl.
    assert_eq!(IntParser.parse_it("42"), Some(42));
}
```

The `let as_f: f64` annotation is the tell. With a generic parameter, the compiler cannot know which impl
you meant, so you must say. With an associated type there is only one answer, so inference just works.
This is exactly why `Iterator` uses `type Item` rather than `Iterator<T>` — writing
`fn sum<I: Iterator<Item = i32>>` reads better than a world where every iterator method needs turbofish.

The rule of thumb: **use an associated type when there is one natural choice per implementing type; use a
generic parameter when a type should genuinely implement the trait several ways.** `From<T>` is the
canonical generic-parameter trait (a type converts from many others); `Iterator` and `Deref` are the
canonical associated-type traits.

## Supertraits and generic structs

A trait can require another trait, which is C#'s `interface IFoo : IBar`:

```rust
use std::fmt::Display;

trait Reportable: Display {              // supertrait
    fn severity(&self) -> u8;

    fn report(&self) -> String {
        format!("[{}] {self}", self.severity())   // can use Display because of the bound
    }
}

struct Finding(String);
impl Display for Finding {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}", self.0)
    }
}
impl Reportable for Finding {
    fn severity(&self) -> u8 { 3 }
}

fn main() {
    assert_eq!(Finding("missing tag".to_owned()).report(), "[3] missing tag");
}
```

And structs are generic in the obvious way, with the small wrinkle that `impl` blocks can be conditional
on bounds — a form of specialisation C# cannot express:

```rust
use std::fmt::Debug;

struct Wrapper<T> { value: T }

// Available for every T.
impl<T> Wrapper<T> {
    fn new(value: T) -> Self { Self { value } }
    fn into_inner(self) -> T { self.value }
}

// Only available when T: Debug.
impl<T: Debug> Wrapper<T> {
    fn dump(&self) -> String { format!("{:?}", self.value) }
}

fn main() {
    assert_eq!(Wrapper::new(5).dump(), "5");
    assert_eq!(Wrapper::new("x").into_inner(), "x");
}
```

`Wrapper<SomethingNotDebug>` still works — it just has no `dump` method. C#'s closest equivalent is
splitting into two types or throwing at runtime.

## `polcheck`: making the evaluator pluggable

Time to apply this. Our `Rule` enum is closed, which is what we want, but the *reporting* side benefits
from being open — different output formats, added by different code, without touching the evaluator.
That is a trait.

```rust
use std::collections::HashMap;
use std::fmt;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Resource {
    pub id: String,
    pub kind: String,
    pub location: String,
    pub tags: HashMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub resource_id: String,
    pub reason: String,
}

impl fmt::Display for Finding {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}: {}", self.resource_id, self.reason)
    }
}

/// Open extension point: anyone can add a format.
pub trait Reporter {
    /// The only required method.
    fn render_finding(&self, finding: &Finding) -> String;

    /// Default bodies build the whole report out of the one required piece.
    fn header(&self) -> String { String::new() }

    fn render(&self, findings: &[Finding]) -> String {
        let mut out = self.header();
        for f in findings {
            out.push_str(&self.render_finding(f));
            out.push('\n');
        }
        out
    }
}

pub struct PlainReporter;
impl Reporter for PlainReporter {
    fn render_finding(&self, finding: &Finding) -> String { finding.to_string() }
}

pub struct CsvReporter;
impl Reporter for CsvReporter {
    fn header(&self) -> String { "resource,reason\n".to_owned() }
    fn render_finding(&self, f: &Finding) -> String {
        format!("{},\"{}\"", f.resource_id, f.reason.replace('"', "\"\""))
    }
}

/// Static dispatch: the caller knows the reporter, so pay nothing.
pub fn report_with<R: Reporter>(reporter: &R, findings: &[Finding]) -> String {
    reporter.render(findings)
}

/// Dynamic dispatch: the reporter came from a `--format` flag at runtime.
pub fn reporter_for(name: &str) -> Box<dyn Reporter> {
    match name {
        "csv" => Box::new(CsvReporter),
        _ => Box::new(PlainReporter),
    }
}

fn main() {
    let findings = vec![Finding {
        resource_id: "res-1".to_owned(),
        reason: "missing tag 'owner'".to_owned(),
    }];

    assert_eq!(report_with(&PlainReporter, &findings), "res-1: missing tag 'owner'\n");

    let dynamic = reporter_for("csv");
    assert_eq!(
        dynamic.render(&findings),
        "resource,reason\nres-1,\"missing tag 'owner'\"\n"
    );
}
```

This is the shape of a lot of real Rust. The *domain* is a closed enum, matched exhaustively. The
*policy* — how to render, where to send, what to do next — is an open trait, dispatched statically inside
the library and dynamically at the one place where a runtime string picks the implementation. Note that
`reporter_for` is exactly where you would use a DI container in C#, and here it is a four-line `match`
returning a boxed trait object.

## Before you move on

The mental shift is that a trait is not attached to a type at declaration; it is a separate relationship
that either crate can establish, subject only to the orphan rule. That makes traits the vehicle for
retrofitting behaviour onto foreign types, for blanket impls that extend whole categories at once, and for
the newtype pattern when coherence stands in your way. The one operational consequence you will hit daily
is that a trait must be *in scope* for its methods to be callable.

Generics look like C# and behave like handwritten specialised code: monomorphisation means zero dispatch
cost and no boxing, paid for in compile time and binary size, with no runtime type information at all.
When you need one function to work over values whose types differ at runtime, you opt into `dyn Trait`
and its two-word fat pointer, subject to the object-safety rules that keep a vtable buildable.
`impl Trait` in argument position is just an anonymous generic; in return position it is a promise of a
single hidden concrete type, and it is what makes iterator chains free.

Associated types versus generic parameters is the design question that trips people up: one natural
choice per implementor means associated type, several legitimate implementations means generic parameter.

If you can explain why `impl Serialize for MyType` is possible but `impl Display for Vec<String>` is not,
why `run_static` and `run_dynamic` compile to different machine code, and why `Iterator` uses `type Item`
instead of a type parameter, the trait system is yours. Next we walk the standard traits themselves —
which is where these ideas stop being abstract.

Next: [09 — The standard traits](09-standard-traits.md).

### Sources

- *The Book*, ch. 10 "Generic Types, Traits, and Lifetimes". <https://doc.rust-lang.org/book/ch10-00-generics.html> — trait definition, bounds, and the definition-site checking of generic code.
- *The Book*, ch. 18 "Object-Oriented Programming Features". <https://doc.rust-lang.org/book/ch18-00-oop.html> — trait objects, dyn compatibility, and the tradeoff against generics.
- *The Rust Reference*, "Implementations" / coherence. <https://doc.rust-lang.org/reference/items/implementations.html#trait-implementation-coherence> — the normative statement of the orphan rule.
- *The Rust Reference*, "Dyn compatibility". <https://doc.rust-lang.org/reference/items/traits.html#dyn-compatibility> — the exact rules for which traits can become trait objects.
- *The Rust Reference*, "Impl trait". <https://doc.rust-lang.org/reference/types/impl-trait.html> — argument-position vs return-position `impl Trait` and lifetime capture.
- *The Edition Guide*, "RPIT lifetime capture rules". <https://doc.rust-lang.org/edition-guide/rust-2024/rpit-lifetime-capture.html> — why edition 2024 changed default capture and what `use<..>` does.
- *Rust API Guidelines*, "Interoperability". <https://rust-lang.github.io/api-guidelines/interoperability.html> — which std traits to implement, and when.
