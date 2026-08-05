# 06 — Borrowing and lifetimes

Ownership answers *who frees this?* Borrowing answers *who may look at it, and when?* — and it is the
part of Rust that will actually fight you. This module is the longest in Part 1 for a reason: the borrow
checker is not a validator bolted onto a normal language, it is the thing that makes the rest of the
design possible, and every hour spent understanding it properly saves a day of `.clone()`-driven
flailing later.

> **Prerequisite:** [05 — Ownership and moves](05-ownership-and-moves.md).

We will do the rules, then the lifetime annotations that express them in signatures, and then — the part
you will come back to — a cookbook of the specific fights you are going to have, with the resolutions.

## The two rules

A **reference** lends access to a value without transferring ownership. There are two kinds, and the
whole borrow checker is two rules about them:

1. At any given time you may have **either** any number of shared references `&T`, **or** exactly one
   mutable reference `&mut T` — never both.
2. A reference must never outlive the value it points to.

Rule 1 is usually stated as "shared XOR mutable", and the names matter. `&T` is often called an
"immutable reference", which is slightly wrong; the accurate framing is that `&T` is a **shared**
reference and `&mut T` is an **exclusive** one. What Rust is really tracking is aliasing: can two paths
reach this data at once? If yes, nobody may mutate through them.

```rust
fn main() {
    let mut v = vec![1, 2, 3];

    // Many shared borrows: fine.
    let a = &v;
    let b = &v;
    println!("{} {}", a.len(), b.len());

    // One exclusive borrow: fine, but only while no shared borrow is live.
    let m = &mut v;
    m.push(4);
    println!("{m:?}");
}
```

C# has nothing corresponding to rule 1. In C#, any number of references to an object may exist and any of
them may mutate it at any time; that is the default and it is why `List<T>` throws
`InvalidOperationException` when you mutate during enumeration. That exception is a *runtime* detection
of exactly the situation rule 1 forbids at *compile time*:

```rust,compile_fail
fn main() {
    let mut v = vec![1, 2, 3];
    for x in &v {           // shared borrow of v, live for the whole loop
        v.push(*x);         // ERROR: cannot borrow `v` as mutable
    }
}
```

The C# version of that program compiles and throws at runtime. The Rust version does not compile, with
`error[E0502]: cannot borrow 'v' as mutable because it is also borrowed as immutable`. Same bug, caught
in a different decade of your development cycle.

And the deeper reason for the rule is not just iterator invalidation. `v.push(*x)` may reallocate the
vector's buffer; if a reference into the old buffer survived, it would dangle. Rule 1 is what makes
`&T` a guarantee that the referent is stable, and rule 2 is what makes it a guarantee that the referent
exists at all:

```rust,compile_fail
fn dangle() -> &String {
    let s = String::from("hello");
    &s                      // ERROR: `s` is dropped at the end of this function
}
```

In C# this is impossible to express, because the GC keeps the object alive as long as a reference exists.
In C it compiles and is a use-after-free. In Rust it is a compile error, and the compiler's suggestion is
to return `String` instead — which is exactly right, because if you want the caller to have it, give them
ownership.

## Non-lexical lifetimes: why this is less restrictive than it sounds

Reading rule 1, you would expect a great deal of pain. In practice you get much less, because since 2018
the borrow checker uses **non-lexical lifetimes** (NLL): a borrow lasts until its **last use**, not until
the end of the enclosing scope.

```rust
fn main() {
    let mut v = vec![1, 2, 3];

    let first = &v[0];          // shared borrow starts
    println!("{first}");        // ...and ends here, at its last use

    v.push(4);                  // fine! the shared borrow is already over
    println!("{v:?}");
}
```

Under the pre-2018 lexical rule, `first` would have been borrowed until the closing brace and `v.push(4)`
would have been rejected. NLL is why modern Rust feels tractable, and it is also why moving a
`println!` from one line to another can change whether your program compiles — the borrow now ends
later. When an error surprises you, look for a *later* use of the reference that is extending its life.

## Mutable borrows and method calls

`&mut` is exclusive, which means that while it exists, the owner itself cannot be used:

```rust,compile_fail
fn main() {
    let mut s = String::from("hello");
    let r = &mut s;
    println!("{s}");        // ERROR: cannot borrow `s` as immutable
    r.push('!');            // ...because `r` is still live here
}
```

Swap the last two lines and it compiles, because then `r`'s last use precedes the read of `s`. This is
NLL doing its job, and it is worth typing both versions to see the difference.

Method calls borrow implicitly, which is the source of a lot of confusion until you see it. When you
write `v.push(4)`, the receiver is borrowed mutably because `push` is declared as
`fn push(&mut self, value: T)`. When you write `v.len()`, it is borrowed shared, because `len` takes
`&self`. So the rules apply to ordinary method calls even though no `&` appears in your code:

```rust,compile_fail
fn main() {
    let mut v = vec![1, 2, 3];
    // `first` holds a shared borrow of v; push needs an exclusive one.
    let first = &v[0];
    v.push(4);
    println!("{first}");        // ERROR: first is used AFTER the push
}
```

This one is the canonical borrow-checker error, and the fix depends on what you meant. If you needed the
value rather than a reference to it, copy it out — `let first = v[0];` — and the borrow ends immediately.
That is usually what you wanted.

## Lifetimes: naming the relationships

So far the compiler has worked out borrow durations on its own. **Lifetime annotations** are needed when
a signature is ambiguous about which input a returned reference came from.

The motivating case:

```rust,compile_fail
fn longer(a: &str, b: &str) -> &str {
    if a.len() >= b.len() { a } else { b }
}
```

This is rejected with "missing lifetime specifier: this function's return type contains a borrowed value,
but the signature does not say whether it is borrowed from `a` or `b`". The compiler is not being pedantic
— it genuinely cannot check callers without knowing. So you tell it:

```rust
fn longer<'a>(a: &'a str, b: &'a str) -> &'a str {
    if a.len() >= b.len() { a } else { b }
}

fn main() {
    let x = String::from("longer string");
    let y = String::from("short");
    println!("{}", longer(&x, &y));
}
```

Read `<'a>` as a generic parameter — because that is exactly what it is, a generic parameter over
lifetimes rather than types. The signature says: *for some lifetime `'a`, both arguments must be valid
for at least `'a`, and the returned reference is valid for `'a`.* At each call site the compiler picks
`'a` to be the shorter of the two inputs' actual lifetimes, and then checks that the result is not used
beyond it.

The single most important thing to understand about lifetime annotations is this: **they do not change
how long anything lives.** They are not directives; they are descriptions. Nothing is kept alive longer
because you wrote `'a`. You are documenting a relationship that already exists in your code so the
compiler can verify callers. If you find yourself adding lifetimes hoping to fix a dangling reference, you
have misdiagnosed the problem — the fix there is to return an owned value.

Here is the error the annotation buys you, which is the whole point:

```rust,compile_fail
fn longer<'a>(a: &'a str, b: &'a str) -> &'a str {
    if a.len() >= b.len() { a } else { b }
}

fn main() {
    let x = String::from("long string is long");
    let result;
    {
        let y = String::from("short");
        result = longer(&x, &y);       // 'a is limited by y's scope
    }                                   // y dropped here
    println!("{result}");               // ERROR: `y` does not live long enough
}
```

### Elision: why you rarely write them

You will read far more lifetime annotations than you write, because the compiler applies three **elision
rules** that cover the overwhelming majority of signatures:

1. Each elided lifetime in the parameters becomes its own distinct lifetime parameter.
2. If there is exactly one input lifetime, it is assigned to all elided output lifetimes.
3. If one of the parameters is `&self` or `&mut self`, **the lifetime of `self` is assigned to all elided
   output lifetimes**.

Rule 3 is why methods almost never need annotations, and it encodes a sensible default: a reference
returned from a method borrows from the receiver.

```rust
struct Parser { source: String, pos: usize }

impl Parser {
    // Elided; the compiler reads this as fn rest<'a>(&'a self) -> &'a str
    fn rest(&self) -> &str {
        &self.source[self.pos..]
    }
}

fn main() {
    let p = Parser { source: "hello world".to_owned(), pos: 6 };
    assert_eq!(p.rest(), "world");
}
```

### `'static` and structs that hold references

`'static` means "valid for the entire program". String literals are `&'static str` because they live in
the binary. It is a genuine lifetime, not a magic keyword, and — importantly — `T: 'static` as a bound
does **not** mean "lives forever"; it means "contains no references with a shorter lifetime", which every
owned type such as `String` or `Vec<T>` satisfies. That distinction matters when you meet `Send + 'static`
bounds on thread spawning in module 15, and it confuses everyone once.

A struct that holds a reference must declare a lifetime parameter, and this is where the infection
spreads:

```rust
/// A view over text we do not own. Cannot outlive the text.
struct Excerpt<'a> {
    part: &'a str,
}

impl<'a> Excerpt<'a> {
    fn new(part: &'a str) -> Self { Excerpt { part } }
    fn part(&self) -> &str { self.part }
}

fn main() {
    let novel = String::from("Call me Ishmael. Some years ago...");
    let first = novel.split('.').next().expect("no sentence");
    let e = Excerpt::new(first);
    assert_eq!(e.part(), "Call me Ishmael");
}
```

This is legitimate and useful — zero-copy parsers are built this way — but note the cost: `Excerpt` can
never outlive `novel`, cannot be stored in a long-lived collection, cannot be sent to another thread
without care, and every type that contains an `Excerpt` also needs a lifetime parameter. This is exactly
the reason module 04 told you to put `String` in your structs rather than `&str`. Reach for a
lifetime-carrying struct when you have measured that the copying matters, not before.

## The borrow-checker fight cookbook

What follows is the set of errors you are actually going to hit. Each has a diagnosis and a fix, and the
fix is almost never `.clone()`.

### Fight 1: "cannot borrow as mutable more than once"

```rust,compile_fail
fn main() {
    let mut v = vec![1, 2, 3];
    let a = &mut v[0];
    let b = &mut v[1];      // ERROR: second mutable borrow of `v`
    *a += 1;
    *b += 1;
}
```

The compiler cannot prove that indices 0 and 1 are different elements — `v[i]` goes through `IndexMut`,
which borrows the whole vector. The fix is an API that splits the borrow for you:

```rust
fn main() {
    let mut v = vec![1, 2, 3];
    let (left, right) = v.split_at_mut(1);   // two disjoint &mut slices
    left[0] += 1;
    right[0] += 1;
    assert_eq!(v, vec![2, 3, 3]);
}
```

The general lesson: when you need two exclusive borrows of different parts of one thing, look for a
standard-library method that performs the split (`split_at_mut`, `iter_mut`, `chunks_mut`,
`get_disjoint_mut`). These are safe wrappers over an `unsafe` operation the library has verified, which is
exactly what the standard library is for.

### Fight 2: mutating a collection while iterating it

```rust,compile_fail
fn main() {
    let mut names = vec!["a".to_owned(), "b".to_owned()];
    for n in &names {
        if n == "a" {
            names.push("c".to_owned());  // ERROR: already borrowed
        }
    }
}
```

Three standard resolutions, in order of preference. **Collect the changes and apply them after**, which
is also clearer:

```rust
fn main() {
    let mut names = vec!["a".to_owned(), "b".to_owned()];
    let additions: Vec<String> = names.iter()
        .filter(|n| *n == "a")
        .map(|n| format!("{n}-copy"))
        .collect();
    names.extend(additions);
    assert_eq!(names, vec!["a", "b", "a-copy"]);
}
```

**Mutate in place with `iter_mut`** when you are changing rather than adding:

```rust
fn main() {
    let mut nums = vec![1, 2, 3];
    for n in nums.iter_mut() { *n *= 10; }
    assert_eq!(nums, vec![10, 20, 30]);
}
```

Or **use `retain`/`drain`** when you are removing. What you should not do is clone the collection to
iterate the copy while mutating the original; it works, and it hides the fact that you never decided what
the semantics should be.

### Fight 3: returning a reference to a local

```rust,compile_fail
fn build_label(kind: &str) -> &str {
    let label = format!("{kind}-label");
    &label                              // ERROR: returns a reference to local data
}
```

There is no lifetime annotation that fixes this, and adding one is the classic beginner detour. The
value is destroyed at the end of the function, so the only correct answer is to return ownership:

```rust
fn build_label(kind: &str) -> String {
    format!("{kind}-label")
}

fn main() { assert_eq!(build_label("storage"), "storage-label"); }
```

### Fight 4: the struct-method double borrow

This is the one that hits real codebases hardest.

```rust,compile_fail
struct Engine { rules: Vec<String>, log: Vec<String> }

impl Engine {
    fn record(&mut self, msg: &str) { self.log.push(msg.to_owned()); }

    fn run(&mut self) {
        for rule in &self.rules {         // shared borrow of self.rules
            self.record(rule);            // ERROR: needs &mut self (all of it)
        }
    }
}
```

The compiler sees `&self.rules` borrow all of `self`, then `self.record(...)` wanting `self` exclusively.
It does not know that `record` only touches `self.log`. This is a real limitation: **Rust's borrow
checking is per-field within a function body, but per-value across a function call boundary.**

The cleanest fix is to borrow the fields separately, which requires inlining the work or splitting the
struct:

```rust
struct Engine { rules: Vec<String>, log: Vec<String> }

impl Engine {
    fn run(&mut self) {
        // Split the borrow explicitly: the compiler CAN see these are disjoint fields.
        let Engine { rules, log } = self;
        for rule in rules.iter() {
            log.push(rule.clone());
        }
    }
}

fn main() {
    let mut e = Engine { rules: vec!["r1".to_owned()], log: Vec::new() };
    e.run();
    assert_eq!(e.log, vec!["r1".to_owned()]);
}
```

Destructuring `self` into its fields is the idiomatic move here, and it is worth remembering because it
is not obvious. The alternatives are to make the helper a free function taking only what it needs
(`fn record(log: &mut Vec<String>, msg: &str)`), or to restructure so the two pieces of state live in
separate types. All three are better than cloning the whole rule list.

### Fight 5: closures capturing too much

```rust,compile_fail
fn main() {
    let mut total = 0;
    let mut add = |x: i32| total += x;     // captures `total` mutably
    add(1);
    println!("{total}");                    // ERROR: `total` still borrowed by `add`
    add(2);
}
```

The closure holds a mutable borrow for as long as it is live. Either finish with the closure before
reading the value (NLL makes this work if `add`'s last use precedes the read), or drop the closure
explicitly. When a closure needs to outlive the current scope — being spawned onto a thread, or stored —
use `move` to transfer ownership of what it captures:

```rust
fn main() {
    let data = vec![1, 2, 3];
    let handle = std::thread::spawn(move || data.len());   // `data` moved into the closure
    assert_eq!(handle.join().unwrap(), 3);
}
```

### Fight 6: `self` is moved by a method you did not expect

```rust,compile_fail
struct Report { lines: Vec<String> }

impl Report {
    fn into_text(self) -> String { self.lines.join("\n") }   // takes self BY VALUE
}

fn main() {
    let r = Report { lines: vec!["a".to_owned()] };
    let t = r.into_text();
    println!("{}", r.lines.len());     // ERROR: `r` was moved by into_text
    println!("{t}");
}
```

The naming convention is your warning system, and it is worth learning as vocabulary because the standard
library follows it rigorously. A method starting with **`into_`** consumes `self`. One starting with
**`to_`** borrows and allocates a new value. One starting with **`as_`** is a cheap borrowed view. So
`into_iter` consumes, `to_owned` copies, `as_str` is free — and when you see `into_` you know your value
is about to be gone.

## Applying it: evaluating rules without copying

Here is `polcheck`'s evaluator handling the recursive `Rule` tree entirely through borrows. Note that no
part of this allocates except the failure reasons, and that the recursion passes `&Rule` down, so a rule
tree is evaluated in place regardless of depth.

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
    Not(Box<Rule>),
    All(Vec<Rule>),
}

#[derive(Debug, PartialEq)]
pub enum Compliance {
    Compliant,
    NonCompliant { reason: String },
}

pub fn evaluate(rule: &Rule, resource: &Resource) -> Compliance {
    match rule {
        Rule::RequireTag { key } => {
            if resource.tags.contains_key(key.as_str()) {
                Compliance::Compliant
            } else {
                Compliance::NonCompliant { reason: format!("missing tag '{key}'") }
            }
        }
        Rule::LocationIn(allowed) => {
            // `allowed.iter()` borrows; `l` is &String; compare against &str.
            if allowed.iter().any(|l| l.as_str() == resource.location) {
                Compliance::Compliant
            } else {
                Compliance::NonCompliant {
                    reason: format!("location '{}' not allowed", resource.location),
                }
            }
        }
        Rule::Not(inner) => match evaluate(inner, resource) {
            Compliance::Compliant => Compliance::NonCompliant {
                reason: "negated rule matched".to_owned(),
            },
            Compliance::NonCompliant { .. } => Compliance::Compliant,
        },
        Rule::All(rules) => {
            // Return the first failure, borrowing throughout.
            for r in rules {
                if let Compliance::NonCompliant { reason } = evaluate(r, resource) {
                    return Compliance::NonCompliant { reason };
                }
            }
            Compliance::Compliant
        }
    }
}

fn main() {
    let r = Resource {
        id: "res-1".to_owned(),
        location: "westus2".to_owned(),
        tags: HashMap::from([("env".to_owned(), "prod".to_owned())]),
    };

    let rule = Rule::All(vec![
        Rule::RequireTag { key: "env".to_owned() },
        Rule::Not(Box::new(Rule::LocationIn(vec!["eastus".to_owned()]))),
    ]);

    assert_eq!(evaluate(&rule, &r), Compliance::Compliant);
    assert_eq!(r.id, "res-1");   // nothing was consumed
}
```

`Rule::All` iterating with `for r in rules` is worth a second look. Because `rules` here is already a
`&Vec<Rule>` (bound by the `match` on `&Rule`), the loop yields `&Rule` items rather than consuming the
vector — the `for` loop's `into_iter()` on a reference is `iter()`. That is the ergonomic payoff of
match ergonomics, which module 07 covers.

## Before you move on

The rule to hold onto is **shared XOR mutable**: any number of `&T`, or exactly one `&mut T`, never both,
and no reference may outlive its referent. Everything the borrow checker says to you is one of those two
sentences applied to a specific line. The reason the rule exists is not bureaucratic — it is what makes
`&T` mean "this data is stable and alive", which in turn is what eliminates iterator invalidation,
use-after-free, and (in module 15) data races, all with the same machinery.

Non-lexical lifetimes are why this is workable in practice: a borrow ends at its last use, not at the end
of the scope. When an error surprises you, look for a later use of the reference that is keeping it alive
longer than you intended.

Lifetime annotations describe relationships that already exist; they never extend anything. You write
them when a function returns a reference and the compiler cannot tell which input it came from, and
elision rule 3 — `&self` lends its lifetime to elided outputs — is why methods almost never need them. A
struct holding a reference needs a lifetime parameter, and that parameter is contagious, which is the
practical argument for owning your data in structs until profiling says otherwise.

Finally, the cookbook. When you get stuck: copy the value out instead of referencing it; split the borrow
with `split_at_mut` or by destructuring `self`; collect changes and apply them after the loop; return
owned data instead of a reference to a local; and read `into_`/`to_`/`as_` as the ownership vocabulary
they are.

If you can explain why `for x in &v { v.push(*x); }` is rejected and what the C# equivalent does instead,
and why adding a lifetime annotation cannot fix a function that returns a reference to a local, then the
hardest module in this book is behind you.

Next: [07 — Structs, enums, and pattern matching](07-structs-enums-matching.md).

### Sources

- *The Book*, ch. 4.2 "References and Borrowing". <https://doc.rust-lang.org/book/ch04-02-references-and-borrowing.html> — the shared-XOR-mutable rule and the dangling-reference case.
- *The Book*, ch. 10.3 "Validating References with Lifetimes". <https://doc.rust-lang.org/book/ch10-03-lifetime-syntax.html> — lifetime parameters, structs holding references, and `'static`.
- *The Rust Reference*, "Lifetime elision". <https://doc.rust-lang.org/reference/lifetime-elision.html> — the normative statement of the three elision rules.
- *The Edition Guide*, "Non-lexical lifetimes". <https://doc.rust-lang.org/edition-guide/rust-2018/ownership-and-lifetimes/non-lexical-lifetimes.html> — borrows ending at last use rather than end of scope.
- *Rust API Guidelines*, "Naming". <https://rust-lang.github.io/api-guidelines/naming.html> — the `as_`/`to_`/`into_` conventions and their ownership implications.
- `slice::split_at_mut`. <https://doc.rust-lang.org/std/primitive.slice.html#method.split_at_mut> — the canonical safe API for obtaining two disjoint exclusive borrows.
