# 07 — Structs, enums, and pattern matching

C# gives you one primary tool for modelling a domain: the class, extended by inheritance and refined
over the years with records, interfaces, and pattern matching. Rust gives you two — the struct and the
enum — and no inheritance at all. That sounds like a subtraction, and for the first week it feels like
one. It is not. Rust's enum is an *algebraic data type*, a construct C# has been slowly and partially
approximating for a decade, and once you can reach for it, a category of design problem you currently
solve with class hierarchies or discriminated-union workarounds simply dissolves.

> **Prerequisite:** [06 — Borrowing and lifetimes](06-borrowing-and-lifetimes.md).

## Structs

Three forms exist, and they are all the same thing with different amounts of ceremony.

```rust
/// Named-field struct: the workhorse.
#[derive(Debug, Clone, PartialEq)]
struct Resource {
    id: String,
    kind: String,
}

/// Tuple struct: fields by position. Mostly used for newtypes.
#[derive(Debug, Clone, Copy, PartialEq)]
struct ResourceId(u64);

/// Unit struct: no data. Used as a marker or a trait carrier.
#[derive(Debug)]
struct StrictMode;

fn main() {
    let r = Resource { id: "res-1".to_owned(), kind: "storage".to_owned() };
    let id = ResourceId(42);
    let _mode = StrictMode;

    println!("{} {} {}", r.id, r.kind, id.0);
}
```

The declaration holds no methods. Behaviour lives in a separate `impl` block, which is the first real
structural difference from C#:

```rust
struct Resource { id: String, kind: String }

impl Resource {
    /// Associated function — no `self`. This is a static method, and the
    /// convention is that `new` is the constructor.
    fn new(id: impl Into<String>, kind: impl Into<String>) -> Self {
        Self { id: id.into(), kind: kind.into() }
    }

    /// Method taking a shared borrow: reads only.
    fn is_storage(&self) -> bool {
        self.kind == "storage"
    }

    /// Method taking an exclusive borrow: mutates.
    fn relabel(&mut self, kind: &str) {
        self.kind = kind.to_owned();
    }

    /// Method taking ownership: consumes the receiver.
    fn into_id(self) -> String {
        self.id
    }
}

fn main() {
    let mut r = Resource::new("res-1", "storage");   // :: for associated functions
    assert!(r.is_storage());
    r.relabel("compute");                             // . for methods
    assert!(!r.is_storage());
    assert_eq!(r.into_id(), "res-1");
}
```

Four things are worth extracting from that. **`impl` is separate from the data declaration**, which means
you can have several `impl` blocks, split them across a file, and — crucially — write `impl SomeTrait for
MyType` blocks that add behaviour without touching the type. **`Self`** (capital S) is an alias for the
type being implemented, and `self` (lowercase) is the receiver. **`::` accesses associated items and `.`
calls methods**, so `Resource::new(...)` and `r.is_storage()` — a distinction C# collapses into `.`.
And there is **no constructor concept**: `new` is a plain associated function with a conventional name,
which means a type can have several constructors with meaningful names (`from_json`, `with_capacity`,
`parse`) instead of a pile of overloads.

That last point interacts with a C# habit worth unlearning. Since Rust has no overloading, you cannot
write three `new` functions; you write `Resource::new`, `Resource::from_id`, and `Resource::with_tags`.
Named constructors are strictly more readable, and the standard library is full of them
(`String::from`, `Vec::with_capacity`, `HashMap::from`).

There is no field-level default and no parameterless-constructor magic, but there is a `Default` trait
and a struct-update syntax that together cover the ground `with` expressions cover in C# records:

```rust
#[derive(Debug, Clone, Default, PartialEq)]
struct Settings {
    strict: bool,
    max_findings: usize,
    label: String,
}

fn main() {
    let base = Settings { max_findings: 100, ..Default::default() };
    let strict = Settings { strict: true, ..base.clone() };   // like C# `with`

    assert_eq!(base.max_findings, 100);
    assert!(!base.strict);
    assert!(strict.strict);
    assert_eq!(strict.max_findings, 100);
}
```

`..base` is the struct update syntax: take the remaining fields from `base`. Note it *moves* out of
`base` unless the fields are `Copy`, which is why the `.clone()` is there — a nice reminder that
ownership does not switch off for convenience syntax.

### Records, equality, and `derive`

`#[derive(...)]` is the closest thing Rust has to C# records, and it is more granular. Where `record`
gives you value equality, a `ToString`, a deconstructor, and `with` in one keyword, Rust makes you list
what you want:

| Derive | Gives you | C# analogue |
|---|---|---|
| `Debug` | `{:?}` formatting | `ToString()` on a record |
| `Clone` | `.clone()` | `ICloneable`, copy constructor |
| `Copy` | implicit bitwise copy | `struct` semantics |
| `PartialEq`, `Eq` | `==` | record value equality, `IEquatable<T>` |
| `PartialOrd`, `Ord` | `<`, `.sort()` | `IComparable<T>` |
| `Hash` | use as a `HashMap` key | `GetHashCode()` |
| `Default` | `Type::default()` | parameterless constructor |

The pragmatic advice is to derive `Debug` on essentially everything (you will want to print it, and
without it `{:?}` is a compile error), `Clone` when duplication makes sense, and `PartialEq` when
equality does. Deriving is cheap and adding one later is a non-breaking change.

## Enums are the real story

A C# `enum` is a named integer. A Rust `enum` is a **sum type**: a value that is exactly one of several
variants, and **each variant can carry different data**. That second half is what makes it a different
language feature.

```rust
#[derive(Debug, Clone)]
enum Rule {
    RequireTag { key: String },              // struct-like variant
    TagEquals { key: String, value: String },
    LocationIn(Vec<String>),                 // tuple-like variant
    Not(Box<Rule>),                          // recursive
    All(Vec<Rule>),
    Any(Vec<Rule>),
}

fn main() {
    let r = Rule::All(vec![
        Rule::RequireTag { key: "owner".to_owned() },
        Rule::Not(Box::new(Rule::LocationIn(vec!["eastus".to_owned()]))),
    ]);
    println!("{r:?}");
}
```

Try to express that in C#. The usual attempts are an abstract `Rule` base class with six subclasses, or
a class with a `RuleKind` discriminator enum plus six nullable property groups, or a visitor. All three
work; all three have the same defect. **Nothing forces you to handle every case.** Add a seventh rule
type and the compiler is silent; you find the missing branch in production. C# 8's switch expressions
with type patterns get closer, but exhaustiveness checking over a class hierarchy is only complete if the
hierarchy is sealed, and even then the compiler's guarantee is weaker than what follows.

In Rust, `match` on an enum **must** cover every variant, and adding a variant turns every incomplete
`match` in the codebase into a compile error that points at the exact line. That is not a linting nicety;
it is a refactoring superpower, and it is the single most persuasive reason to model with enums.

### The two enums you will use constantly

Before going further, note that two types you have already met are just enums from the standard library,
with no special compiler support:

```rust,ignore
enum Option<T> { None, Some(T) }
enum Result<T, E> { Ok(T), Err(E) }
```

`Option<T>` replaces `null`, and `Result<T, E>` replaces exceptions. They get modules 11 to themselves,
but the point here is structural: they are ordinary enums, and everything in this module applies to them.
Their variants are in the prelude, so you write `Some(x)` and `Ok(v)` rather than `Option::Some(x)`.

## Pattern matching

`match` is a switch expression whose cases are **patterns** rather than constants, and whose coverage is
checked. Start with the shape:

```rust
#[derive(Debug)]
enum Rule {
    RequireTag { key: String },
    LocationIn(Vec<String>),
    Not(Box<Rule>),
}

fn describe(rule: &Rule) -> String {
    match rule {
        Rule::RequireTag { key } => format!("must have tag {key}"),
        Rule::LocationIn(locs) => format!("location in {} options", locs.len()),
        Rule::Not(inner) => format!("not ({})", describe(inner)),
    }
}

fn main() {
    let r = Rule::Not(Box::new(Rule::RequireTag { key: "env".to_owned() }));
    assert_eq!(describe(&r), "not (must have tag env)");
}
```

Delete one arm and the compiler refuses to build, naming the variant you missed. That is exhaustiveness.

Patterns compose, and the full vocabulary is worth seeing in one place because it is richer than C#'s:

```rust
fn classify(pair: (i32, &str)) -> &'static str {
    match pair {
        (0, _) => "zero-prefixed",                    // wildcard
        (n, _) if n < 0 => "negative",                // match guard
        (1..=9, "small") => "single digit, small",    // range + literal
        (n, s) if s.len() as i32 == n => "self-describing",
        (_, "") => "empty label",
        _ => "other",                                  // catch-all
    }
}

fn main() {
    assert_eq!(classify((0, "x")), "zero-prefixed");
    assert_eq!(classify((-5, "x")), "negative");
    assert_eq!(classify((5, "small")), "single digit, small");
    assert_eq!(classify((3, "abc")), "self-describing");
    assert_eq!(classify((42, "")), "empty label");
    assert_eq!(classify((42, "z")), "other");
}
```

Destructuring works on structs, nested structures, and slices, which removes a lot of accessor noise:

```rust
struct Point { x: i32, y: i32 }
struct Line { from: Point, to: Point }

fn describe(l: &Line) -> String {
    match l {
        Line { from: Point { x: 0, y: 0 }, to } => format!("from origin to ({}, {})", to.x, to.y),
        Line { from, to } if from.x == to.x => format!("vertical at x={}", from.x),
        Line { from, to } if from.y == to.y => format!("horizontal at y={}", from.y),
        _ => "diagonal".to_owned(),
    }
}

fn main() {
    let l = Line { from: Point { x: 0, y: 0 }, to: Point { x: 3, y: 4 } };
    assert_eq!(describe(&l), "from origin to (3, 4)");

    let v = Line { from: Point { x: 2, y: 0 }, to: Point { x: 2, y: 9 } };
    assert_eq!(describe(&v), "vertical at x=2");
}
```

Slice patterns are a genuinely delightful feature with no C# equivalent (C# 11 list patterns are close):

```rust
fn summarize(args: &[&str]) -> String {
    match args {
        [] => "no arguments".to_owned(),
        [one] => format!("just {one}"),
        [first, .., last] => format!("{first} through {last}"),
    }
}

fn main() {
    assert_eq!(summarize(&[]), "no arguments");
    assert_eq!(summarize(&["a"]), "just a");
    assert_eq!(summarize(&["a", "b", "c"]), "a through c");
}
```

Two more pieces of syntax you will read constantly. The `@` binding captures a value *and* tests it, and
`|` matches alternatives:

```rust
fn bucket(n: u32) -> String {
    match n {
        0 => "none".to_owned(),
        small @ (1 | 2 | 3) => format!("small: {small}"),
        big @ 4..=100 => format!("big: {big}"),
        _ => "huge".to_owned(),
    }
}

fn main() {
    assert_eq!(bucket(2), "small: 2");
    assert_eq!(bucket(50), "big: 50");
    assert_eq!(bucket(1000), "huge");
}
```

## `if let`, `let else`, and let-chains

Full `match` is heavy when you care about one case. Three lighter forms exist, and idiomatic Rust uses
them constantly.

**`if let`** handles one pattern with an optional else:

```rust
fn main() {
    let maybe: Option<i32> = Some(7);

    if let Some(n) = maybe {
        println!("got {n}");
    } else {
        println!("nothing");
    }
}
```

**`let else`** is the early-return form, and it is the one C# developers fall in love with. It binds on
success and *must* diverge on failure, which keeps the happy path unindented:

```rust
fn parse_port(s: &str) -> Option<u16> {
    let Ok(n) = s.parse::<u32>() else {
        return None;                    // must diverge: return, break, continue, or panic
    };
    if n > u16::MAX as u32 { return None; }
    Some(n as u16)
}

fn main() {
    assert_eq!(parse_port("8080"), Some(8080));
    assert_eq!(parse_port("nope"), None);
    assert_eq!(parse_port("99999"), None);
}
```

Compare that to the C# `if (!int.TryParse(s, out var n)) return null;` idiom — same shape, but `let else`
works for *any* pattern, not just the `TryParse` convention, and the bound variable is in scope for the
rest of the function rather than the rest of the block.

**Let-chains** (stable since Rust 1.88, edition 2024 only) let you combine several `let` patterns and
boolean conditions with `&&`, which removes the nested-`if let` staircase:

```rust
use std::collections::HashMap;

fn owner_team(tags: &HashMap<String, String>) -> Option<&str> {
    if let Some(owner) = tags.get("owner")
        && let Some((team, _user)) = owner.split_once('/')
        && !team.is_empty()
    {
        Some(team)
    } else {
        None
    }
}

fn main() {
    let tags = HashMap::from([("owner".to_owned(), "platform/alice".to_owned())]);
    assert_eq!(owner_team(&tags), Some("platform"));

    let bad = HashMap::from([("owner".to_owned(), "alice".to_owned())]);
    assert_eq!(owner_team(&bad), None);
}
```

Before let-chains that was three levels of nesting. This is one of the genuinely nice ergonomic wins of
the 2024 edition, and it is worth knowing that code written for older editions cannot use it.

## Match ergonomics: why `&` mostly disappears

One detail that saves confusion. When you match on a reference, Rust automatically binds the inner
patterns by reference too, so you do not have to write `&` and `ref` everywhere:

```rust
#[derive(Debug)]
enum Msg { Text(String), Code(i32) }

fn render(m: &Msg) -> String {
    match m {
        // `m` is &Msg, so `s` is automatically &String, not String.
        Msg::Text(s) => s.to_uppercase(),
        // `n` is &i32; `*n` derefs it, though arithmetic auto-derefs anyway.
        Msg::Code(n) => format!("code {}", *n),
    }
}

fn main() {
    assert_eq!(render(&Msg::Text("hi".to_owned())), "HI");
    assert_eq!(render(&Msg::Code(404)), "code 404");
}
```

This is called *match ergonomics*, and it is why the evaluator in module 06 never had to say `ref`. The
mental rule: **if you matched on a borrow, your bindings are borrows.** If you matched on an owned value,
your bindings take ownership — which is how `match some_option { Some(s) => ... }` moves the string out.

## Modelling: enum or trait object?

Now the design question, since you have two ways to express "one of several kinds of thing" and C#
instincts push you towards the wrong one.

Use an **enum** when the set of variants is **closed and known to you** — you own the type and you want
to add operations freely. Adding an operation is easy (write a new `match`); adding a variant is a
breaking change that the compiler helps you complete. This is `Rule`, `Compliance`, `Option`, `Result`,
and most domain modelling.

Use a **trait object** (`dyn Trait`, module 08) when the set of implementations is **open** — plugins,
extension points, things a downstream crate should be able to add. Adding an implementation is easy;
adding a method to the trait is a breaking change.

That is the classic expression problem, and Rust makes you choose explicitly where C# nudges you towards
the class hierarchy by default. For `polcheck`, `Rule` is an enum because we own the rule language and
want to write evaluators, serialisers, and optimisers over it — every one of which is a `match` the
compiler will keep complete for us. If we wanted third-party rule types loaded from plugins, we would
want a trait.

A useful middle ground exists and is common in real codebases: an enum whose variants are the built-in
cases plus one `Custom(Box<dyn CustomRule>)` variant for extension. You get exhaustive matching on the
known cases and an escape hatch for the open ones.

## Bringing it together: `polcheck`'s model

Here is the domain with everything from this module applied — named constructors, derives, an enum with
mixed variant shapes, recursion through `Box`, and a `match`-based evaluator with a guard.

```rust
use std::collections::HashMap;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Resource {
    pub id: String,
    pub kind: String,
    pub location: String,
    pub tags: HashMap<String, String>,
}

impl Resource {
    pub fn new(id: impl Into<String>, kind: impl Into<String>, location: impl Into<String>) -> Self {
        Self {
            id: id.into(),
            kind: kind.into(),
            location: location.into(),
            tags: HashMap::new(),
        }
    }

    pub fn with_tag(mut self, k: impl Into<String>, v: impl Into<String>) -> Self {
        self.tags.insert(k.into(), v.into());
        self
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Rule {
    RequireTag { key: String },
    TagEquals { key: String, value: String },
    LocationIn(Vec<String>),
    KindIs(String),
    Not(Box<Rule>),
    All(Vec<Rule>),
    Any(Vec<Rule>),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Compliance {
    Compliant,
    NonCompliant { reason: String },
}

impl Compliance {
    pub fn is_compliant(&self) -> bool {
        matches!(self, Compliance::Compliant)      // the matches! macro: pattern -> bool
    }

    fn fail(reason: impl Into<String>) -> Self {
        Compliance::NonCompliant { reason: reason.into() }
    }
}

pub fn evaluate(rule: &Rule, r: &Resource) -> Compliance {
    match rule {
        Rule::RequireTag { key } if r.tags.contains_key(key) => Compliance::Compliant,
        Rule::RequireTag { key } => Compliance::fail(format!("missing tag '{key}'")),

        Rule::TagEquals { key, value } => match r.tags.get(key) {
            Some(actual) if actual == value => Compliance::Compliant,
            Some(actual) => Compliance::fail(format!("tag '{key}' is '{actual}', want '{value}'")),
            None => Compliance::fail(format!("missing tag '{key}'")),
        },

        Rule::LocationIn(allowed) if allowed.iter().any(|l| *l == r.location) => Compliance::Compliant,
        Rule::LocationIn(_) => Compliance::fail(format!("location '{}' not allowed", r.location)),

        Rule::KindIs(k) if *k == r.kind => Compliance::Compliant,
        Rule::KindIs(k) => Compliance::fail(format!("kind '{}' is not '{k}'", r.kind)),

        Rule::Not(inner) => match evaluate(inner, r) {
            Compliance::Compliant => Compliance::fail("negated rule matched"),
            Compliance::NonCompliant { .. } => Compliance::Compliant,
        },

        Rule::All(rules) => rules
            .iter()
            .map(|x| evaluate(x, r))
            .find(|c| !c.is_compliant())
            .unwrap_or(Compliance::Compliant),

        Rule::Any(rules) if rules.is_empty() => Compliance::fail("empty Any rule"),
        Rule::Any(rules) => {
            if rules.iter().any(|x| evaluate(x, r).is_compliant()) {
                Compliance::Compliant
            } else {
                Compliance::fail("no alternative matched")
            }
        }
    }
}

fn main() {
    let r = Resource::new("res-1", "storage", "westus2")
        .with_tag("env", "prod")
        .with_tag("owner", "platform");

    let rule = Rule::All(vec![
        Rule::RequireTag { key: "owner".to_owned() },
        Rule::TagEquals { key: "env".to_owned(), value: "prod".to_owned() },
        Rule::Any(vec![
            Rule::LocationIn(vec!["westus2".to_owned(), "eastus".to_owned()]),
            Rule::KindIs("exempt".to_owned()),
        ]),
    ]);
    assert_eq!(evaluate(&rule, &r), Compliance::Compliant);

    let strict = Rule::TagEquals { key: "env".to_owned(), value: "dev".to_owned() };
    assert_eq!(
        evaluate(&strict, &r),
        Compliance::NonCompliant { reason: "tag 'env' is 'prod', want 'dev'".to_owned() }
    );
}
```

Two idioms in there are worth naming. `matches!(self, Compliance::Compliant)` is a macro that turns a
pattern into a `bool`, and it saves a three-line `match` constantly. And `with_tag(mut self, ...) -> Self`
is the consuming builder from module 05, which is why `Resource::new(...).with_tag(...).with_tag(...)`
chains.

Notice also that the guard-plus-fallback pairs — `Rule::RequireTag { key } if ...` followed by
`Rule::RequireTag { key }` — are the idiomatic way to express "this variant, when the condition holds"
versus "this variant otherwise". Guards do not count towards exhaustiveness, which is why the unguarded
arm must still be there; the compiler is right to insist.

## Before you move on

The durable idea is that Rust replaces the class hierarchy with the algebraic data type as the default
tool for "a value that is one of several kinds". An enum variant can carry its own data, `match` must
handle every variant, and adding a variant produces compile errors at exactly the places that need
updating. That combination — data-carrying variants plus enforced exhaustiveness — is what makes enum
modelling qualitatively better than a discriminator field or a sealed hierarchy, and it is why you should
reach for an enum first and a trait object only when the set of cases must be open to other people's
code.

The pattern language is worth real investment, because it is where a lot of Rust's expressiveness lives:
destructuring, guards, ranges, `|` alternatives, `@` bindings, slice patterns, and the lighter `if let`,
`let else`, and let-chain forms. Match ergonomics means that matching on a borrow gives you borrowed
bindings, which is why idiomatic code has far fewer `&` and `ref` annotations than you would expect.

Structurally, remember that `impl` blocks are separate from data declarations, that `new` is a convention
rather than a language feature, and that named constructors replace the constructor overloads you would
write in C#.

If you can explain what a C# `sealed record` hierarchy plus an exhaustive switch expression still fails to
guarantee that a Rust enum plus `match` does guarantee, and say when you would choose `dyn Trait` over an
enum, you are ready for the trait system.

Next: [08 — Traits and generics](08-traits-and-generics.md).

### Sources

- *The Book*, ch. 5 "Using Structs to Structure Related Data". <https://doc.rust-lang.org/book/ch05-00-structs.html> — the three struct forms, `impl` blocks, and associated functions.
- *The Book*, ch. 6 "Enums and Pattern Matching". <https://doc.rust-lang.org/book/ch06-00-enums.html> — data-carrying variants, `Option`, and exhaustive `match`.
- *The Rust Reference*, "Patterns". <https://doc.rust-lang.org/reference/patterns.html> — the normative pattern grammar, including slice patterns, `@` bindings, and rest patterns.
- *The Book*, ch. 18 "Patterns and Matching". <https://doc.rust-lang.org/book/ch18-00-patterns.html> — where patterns may appear, refutability, and match guards.
- *The Edition Guide*, Rust 2024. <https://doc.rust-lang.org/edition-guide/rust-2024/index.html> — let-chains and their edition gating; stabilised in Rust 1.88 for edition 2024.
- `std::matches!` macro. <https://doc.rust-lang.org/std/macro.matches.html> — turning a pattern test into a boolean.
