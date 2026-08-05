# 05 — Ownership and moves

This is the module where Rust stops being a familiar language with unusual syntax. Everything before
this was orientation; everything after it is consequence. Ownership is the mechanism that lets Rust free
memory correctly without a garbage collector, and because it is enforced in the type system rather than
by a runtime, it changes what your function signatures mean.

> **Prerequisite:** [04 — Strings, slices, and `Vec`](04-strings-and-slices.md).

The hardest part is not learning the rules — there are three and they fit on a napkin. The hard part is
that you already have a complete, correct, deeply internalised mental model of what happens when you
pass an object to a method, and in Rust that model is wrong in a specific way. So we will start by
making the C# model explicit, then break it.

## What your C# instincts currently say

In C#, when you write `Process(customer)`, you know exactly what happens: a reference is copied onto the
stack, both `customer` and the parameter now point at the same heap object, and the object lives until
the GC proves nothing points at it. Passing is cheap, sharing is implicit and unlimited, and lifetime is
somebody else's problem. If `Process` stashes the reference in a static field, that is fine; the object
simply lives longer.

Value types differ — `Process(myStruct)` copies the struct — but the split is decided by the *type's*
declaration, `class` versus `struct`, and it is invisible at the call site. You cannot tell from
`Process(x)` whether `x` was copied.

Rust makes a different cut. The question is not "is this a reference type or a value type?" but **"is
this call transferring ownership, or lending access?"** — and the answer is visible at the call site,
every time, in whether you wrote `&`.

## The three rules

1. Each value has exactly one **owner** — a variable binding.
2. There can be only one owner at a time.
3. When the owner goes out of scope, the value is **dropped** (its destructor runs, its memory is freed).

Rule 3 alone is already valuable and is the part C# developers immediately like:

```rust
fn main() {
    {
        let s = String::from("hello");   // heap allocation happens here
        println!("{s}");
    }                                    // s goes out of scope: freed, right here
    println!("after");
}
```

There is no GC pass, no finalizer queue, no nondeterminism. The deallocation happens at the closing
brace, on this thread, synchronously. This is `using`/`IDisposable` behaviour applied automatically to
every value in the language, with no way to forget it — and the automatic version does not need a
`using` statement because the compiler inserts the call for you. Module 09 shows how to hook it with the
`Drop` trait.

Rules 1 and 2 are the interesting ones, because they are what "move" means.

## Moves

```rust
fn main() {
    let a = String::from("hello");
    let b = a;                  // MOVE: ownership transfers from a to b
    println!("{b}");            // fine
    // println!("{a}");         // ERROR: borrow of moved value: `a`
}
```

Uncomment that last line and the compiler says:

```text
error[E0382]: borrow of moved value: `a`
 --> src/main.rs:5:20
  |
2 |     let a = String::from("hello");
  |         - move occurs because `a` has type `String`, which does not
  |           implement the `Copy` trait
3 |     let b = a;
  |             - value moved here
5 |     println!("{a}");
  |               ^^^ value borrowed here after move
```

Notice what did *not* happen: the string's heap buffer was not copied. `let b = a;` copies the three
words (pointer, length, capacity) from one stack slot to another — it is exactly as cheap as copying a
C# reference. What changed is that the compiler now considers `a` dead. This is the crucial insight, and
it is worth stating flatly: **a move is not a deep copy. A move is a shallow copy plus the invalidation
of the source.**

Why invalidate the source? Because of rule 3. If both `a` and `b` were live and both went out of scope,
both would free the same buffer — a double free, one of the classic C bugs. C++ solves this with copy
constructors that deep-copy (expensive, and the default) or move constructors that leave the source in a
valid-but-unspecified state (fast, but the source is still usable and usually garbage). Rust's answer is
to make the source *statically inaccessible*, which costs nothing at runtime and cannot be misused.

The same thing happens when you pass to a function, which is where it starts to matter:

```rust
fn consume(s: String) -> usize {
    s.len()
}                                   // s dropped here

fn main() {
    let text = String::from("hello");
    let n = consume(text);          // text MOVED into the function
    println!("{n}");
    // println!("{text}");          // ERROR: text no longer owns anything
}
```

A C# developer reads `consume(text)` and expects `text` to be usable afterwards. In Rust, taking a
parameter **by value** means taking ownership, and the caller loses the value. The function is now
responsible for it, and unless it returns it or moves it elsewhere, the value is destroyed when the
function ends.

This is why module 04's rule — take `&str`, not `String` — matters so much. A parameter type of `String`
is a statement in the signature that says *"I am taking this away from you."* Sometimes that is exactly
right, and it is a useful thing to be able to say. Usually you just wanted to read it.

## `Copy`: the exception that keeps simple things simple

If every assignment moved, `let a = 5; let b = a;` would invalidate `a`, which would be absurd. So types
that are cheap to duplicate and have no destructor implement the `Copy` marker trait, and for those,
assignment copies instead of moving:

```rust
fn main() {
    let a = 5;
    let b = a;                  // COPY, not a move
    println!("{a} {b}");        // both usable

    let p = (1.0, 2.0);
    let q = p;                  // tuples of Copy types are Copy
    println!("{p:?} {q:?}");
}
```

`Copy` is implemented for all the primitives (integers, floats, `bool`, `char`), for shared references
`&T`, and for tuples and arrays whose elements are all `Copy`. It is *not* implemented for `String`,
`Vec<T>`, `Box<T>`, or anything owning a heap allocation — precisely because duplicating those would
either be expensive or would create the double-free problem.

The rule the compiler enforces is worth knowing because it explains the boundary: **a type can be `Copy`
only if it does not implement `Drop`.** A type that needs cleanup cannot be silently duplicated, because
then the cleanup would run twice. That single constraint explains the entire membership of the `Copy`
club.

The C# analogy is close but not exact. `Copy` types behave like C# `struct`s and non-`Copy` types behave
like C# `class`es *at the assignment site* — but the difference is what happens to the source. Assigning
a C# class reference leaves the source usable; moving a Rust `String` does not. The right way to hold it
is that Rust has three behaviours where C# has two:

| | C# | Rust |
|---|---|---|
| Bitwise copy, both usable | `struct` | `Copy` types |
| Shallow copy, both usable | `class` (reference) | `&T` borrows (module 06) |
| Shallow copy, source invalidated | — | move (non-`Copy` types) |

That third row is the new one, and it is the whole module.

## `Clone`: asking for the expensive thing explicitly

When you genuinely need two independent owned copies, you say so:

```rust
fn main() {
    let a = String::from("hello");
    let b = a.clone();          // deep copy: new heap allocation
    println!("{a} {b}");        // both usable, independent
}
```

`Clone` is a trait with an explicit method, deliberately not an operator, because for `String`, `Vec`, or
a large struct it means an allocation and a copy. The design principle from module 01 applies: Rust
refuses to hide a cost behind syntax. C#'s equivalents — `ICloneable`, copy constructors, `with`
expressions on records — are all explicit too, but C# rarely *forces* the question, because sharing a
reference is free and usually fine.

This creates a very common failure mode for people learning Rust, and it is worth naming so you can
catch yourself doing it. **When the borrow checker complains, `.clone()` always makes the error go away.**
It is the path of least resistance, and a codebase written this way works correctly but allocates
constantly and reads badly. Cloning is not a sin — module 28 has a section on when it is genuinely the
right call, and "this is a one-off in a cold path and the borrow-checker-clean version would need a
redesign" is a perfectly good reason. But if you are cloning inside a loop, or cloning to satisfy a
function that only reads its argument, the fix is a borrow, and that is the next module.

You can opt your own types into these traits:

```rust
#[derive(Clone, Copy, Debug, PartialEq)]
struct Point { x: f64, y: f64 }        // all fields Copy, so Copy is allowed

#[derive(Clone, Debug)]                 // Copy is NOT allowed: String isn't Copy
struct Resource { id: String }

fn main() {
    let p = Point { x: 1.0, y: 2.0 };
    let q = p;                          // copy
    assert_eq!(p, q);                   // p still usable

    let r = Resource { id: "res-1".to_owned() };
    let s = r.clone();                  // explicit deep copy
    assert_eq!(r.id, s.id);
}
```

## Where moves actually happen

Moves are not only assignments and calls. It is worth having the complete list in your head, because the
error message always says "value moved here" and you want to recognise the construct.

```rust
fn takes(_s: String) {}

fn main() {
    // 1. Assignment to another binding
    let a = String::from("a");
    let _b = a;

    // 2. Passing by value to a function
    let c = String::from("c");
    takes(c);

    // 3. Returning (moves out of the function, into the caller)
    let d = { let inner = String::from("d"); inner };
    assert_eq!(d, "d");

    // 4. Putting a value into a collection or struct
    let e = String::from("e");
    let v = vec![e];
    assert_eq!(v[0], "e");

    // 5. Pattern-matching by value
    let f = Some(String::from("f"));
    match f {
        Some(inner) => assert_eq!(inner, "f"),   // inner moved out of f
        None => {}
    }

    // 6. Iterating with into_iter() (or `for x in vec`, which calls it)
    let g = vec![String::from("g")];
    for item in g {                              // g moved into the loop
        assert_eq!(item, "g");
    }
}
```

Number 6 is a frequent surprise. `for item in my_vec` consumes the vector — after the loop, `my_vec` is
gone. If you wanted to keep it, iterate over a borrow: `for item in &my_vec` gives you `&String` items
and leaves the vector intact. This is the same choice as everywhere else, expressed the same way, and it
is why `.iter()` and `.into_iter()` both exist (module 10).

## Partial moves and the field trap

A subtle case that catches people: you can move a single field out of a struct, and doing so partially
invalidates the struct.

```rust
struct Config {
    name: String,
    retries: u32,
}

fn main() {
    let cfg = Config { name: "polcheck".to_owned(), retries: 3 };

    let name = cfg.name;             // moves just the `name` field out
    // println!("{:?}", cfg.name);   // ERROR: value moved
    println!("{}", cfg.retries);     // fine: u32 is Copy, and this field wasn't moved
    println!("{name}");
}
```

After the partial move, `cfg` as a whole can no longer be used or passed anywhere, but the fields that
were not moved remain readable. This is more permissive than you might expect and occasionally exactly
what you want when destructuring a config object. The place it bites is when you meant to borrow: writing
`let name = cfg.name;` where you meant `let name = &cfg.name;`.

A related restriction with no obvious workaround at first: **you cannot move out of a borrow.** If you
only have `&Config`, you cannot take its `name`, because you would be stealing from something you do not
own. The options are to clone it, to take `self` by value instead of `&self`, or to use
`std::mem::take`/`std::mem::replace`, which swap in a default and hand you the original:

```rust
#[derive(Default, Debug)]
struct Buffer { items: Vec<String> }

impl Buffer {
    /// Take the accumulated items, leaving the buffer empty.
    fn drain_items(&mut self) -> Vec<String> {
        std::mem::take(&mut self.items)      // moves out, leaves Vec::default()
    }
}

fn main() {
    let mut b = Buffer { items: vec!["a".to_owned()] };
    let taken = b.drain_items();
    assert_eq!(taken, vec!["a".to_owned()]);
    assert!(b.items.is_empty());
}
```

`std::mem::take` is the idiomatic tool for "give me ownership of this field, and reset it", and it is one
of those functions you will not miss until you know it exists, after which you use it constantly.

## Ownership in signatures: how to read an API

The payoff for all of this is that a Rust function signature tells you what it does to your data, which a
C# signature cannot. Learn to read these four shapes:

```rust
# struct Thing;
# impl Thing { fn field(&self) -> u32 { 0 } }
// Borrows: you keep ownership, callee may only read.
fn inspect(t: &Thing) -> u32 { t.field() }

// Mutably borrows: you keep ownership, callee may modify.
fn adjust(t: &mut Thing) { let _ = t; }

// Takes ownership: your value is gone (consumed or destroyed).
fn consume(t: Thing) { let _ = t; }

// Takes and returns ownership: the builder pattern.
fn with_retries(t: Thing, _n: u32) -> Thing { t }
```

That vocabulary is the reason Rust APIs are self-documenting in a way C# APIs are not. In C#, you must
read the documentation — or the source — to learn whether `Configure(options)` mutates `options`, stores
a reference to it, or copies what it needs. In Rust, `fn configure(options: &Options)` is a compiler-
enforced promise that nothing was mutated and nothing was retained past the call.

The fourth shape, taking `self` and returning `Self`, is how Rust does fluent builders. Because the
method consumes the receiver, you cannot accidentally keep using the pre-build object, which makes the
pattern safer than its C# counterpart:

```rust
#[derive(Debug)]
struct ClientBuilder { retries: u32, timeout_ms: u64 }

impl ClientBuilder {
    fn new() -> Self { Self { retries: 0, timeout_ms: 1000 } }
    fn retries(mut self, n: u32) -> Self { self.retries = n; self }
    fn timeout_ms(mut self, ms: u64) -> Self { self.timeout_ms = ms; self }
}

fn main() {
    let b = ClientBuilder::new().retries(3).timeout_ms(500);
    assert_eq!((b.retries, b.timeout_ms), (3, 500));
}
```

Note `mut self` in the parameter list: the method takes ownership *and* wants to mutate its own copy
before handing it back. That is not a borrow — it is ownership with local mutability, and it is exactly
what a builder needs.

## `polcheck`: ownership decisions in the evaluator

Let's apply this to the running example. We want a function that evaluates one rule against one resource.
The question is what it should take.

```rust
use std::collections::HashMap;

pub struct Resource {
    pub id: String,
    pub location: String,
    pub tags: HashMap<String, String>,
}

pub enum Rule {
    RequireTag { key: String },
    LocationIn(Vec<String>),
}

#[derive(Debug, PartialEq)]
pub enum Compliance {
    Compliant,
    NonCompliant { reason: String },
}

/// Borrows both inputs: evaluating a rule neither consumes nor changes anything.
/// The `String` in the result is newly created, so it is owned.
pub fn evaluate(rule: &Rule, resource: &Resource) -> Compliance {
    match rule {
        Rule::RequireTag { key } => {
            if resource.tags.contains_key(key) {
                Compliance::Compliant
            } else {
                Compliance::NonCompliant { reason: format!("missing tag '{key}'") }
            }
        }
        Rule::LocationIn(allowed) => {
            if allowed.iter().any(|l| l == &resource.location) {
                Compliance::Compliant
            } else {
                Compliance::NonCompliant {
                    reason: format!("location '{}' not allowed", resource.location),
                }
            }
        }
    }
}

fn main() {
    let r = Resource {
        id: "res-1".to_owned(),
        location: "westus2".to_owned(),
        tags: HashMap::from([("env".to_owned(), "prod".to_owned())]),
    };

    let rule = Rule::RequireTag { key: "owner".to_owned() };
    assert_eq!(
        evaluate(&rule, &r),
        Compliance::NonCompliant { reason: "missing tag 'owner'".to_owned() }
    );

    // Both rule and r are still owned by main and fully usable.
    let rule2 = Rule::LocationIn(vec!["westus2".to_owned()]);
    assert_eq!(evaluate(&rule2, &r), Compliance::Compliant);
    assert_eq!(r.id, "res-1");
}
```

The signature `fn evaluate(rule: &Rule, resource: &Resource) -> Compliance` is doing real work as
documentation. It promises that evaluation does not consume the rule (so you can apply it to a thousand
resources), does not modify the resource (so evaluation is side-effect free), and produces a fresh
owned verdict. In C# you would write `Compliance Evaluate(Rule rule, Resource resource)` and none of
that would be guaranteed by anything.

## Before you move on

The one idea to carry is that **a move is a shallow copy plus invalidation of the source**, and it exists
because exactly one binding must be responsible for freeing a value. That is the whole of ownership; the
rest is consequence. Passing by value transfers responsibility, which is why a parameter of type `String`
means "I am taking this from you" and a parameter of type `&str` means "let me read it". `Copy` types
opt out of moving because they are cheap and have no destructor — and the reason `Copy` and `Drop` are
mutually exclusive is that duplicating a value with cleanup would run the cleanup twice.

The habit to build, starting now, is to notice when you reach for `.clone()`. It is a legitimate tool and
sometimes the right answer, but during your first months it is far more often a signal that you wanted to
borrow and did not yet know how. Resist it for one more module.

The pleasant surprise is deterministic destruction. You get `IDisposable` semantics on every value in the
language, automatically, with no `using` to forget and no finalizer nondeterminism — and this falls out of
rule 3 rather than being a separate feature.

If you can explain why `let b = a;` invalidates `a` for a `String` but not for an `i32`, and say what
`fn process(data: Vec<u8>)` promises that `fn process(data: &[u8])` does not, you are ready to learn how
to lend values instead of giving them away.

Next: [06 — Borrowing and lifetimes](06-borrowing-and-lifetimes.md).

### Sources

- *The Book*, ch. 4.1 "What Is Ownership?". <https://doc.rust-lang.org/book/ch04-01-what-is-ownership.html> — the three rules, move semantics, and the interaction with `Clone`.
- `std::marker::Copy` API documentation. <https://doc.rust-lang.org/std/marker/trait.Copy.html> — which types are `Copy`, and the normative statement that a type cannot be both `Copy` and `Drop`.
- `std::clone::Clone` API documentation. <https://doc.rust-lang.org/std/clone/trait.Clone.html> — explicit duplication and its relationship to `Copy`.
- `std::mem::take` and `std::mem::replace`. <https://doc.rust-lang.org/std/mem/fn.take.html> — moving a value out of a mutable borrow by substituting a default.
- *The Rust Reference*, "Destructors". <https://doc.rust-lang.org/reference/destructors.html> — normative rules for when values are dropped, including drop order and partial moves.
