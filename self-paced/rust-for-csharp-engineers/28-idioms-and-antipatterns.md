# 28 — Idioms, patterns, and anti-patterns

You can write compiling Rust after a fortnight. Writing Rust that another Rust programmer reads without
wincing takes longer, and the gap is almost entirely made of idiom — the accumulated conventions that
distinguish code written *in* the language from C# transliterated into it.

This chapter is that layer. It covers the patterns worth adopting deliberately, the mistakes a C# background
predisposes you to, and — at the end — the mental-model shifts that matter most, collected in one place so
you can come back to them.

> **Prerequisite:** [27 — Capstone: building polcheck](27-capstone-polcheck.md).

## The newtype pattern

If you take one pattern from this chapter, take this one. A newtype is a tuple struct wrapping a single
value, and it costs nothing at runtime:

```rust
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct ResourceId(String);

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct SubscriptionId(String);

impl ResourceId {
    /// The only way in — so validation cannot be bypassed.
    pub fn parse(s: &str) -> Result<Self, &'static str> {
        if s.starts_with('/') && s.len() > 1 {
            Ok(ResourceId(s.to_string()))
        } else {
            Err("resource ids must be absolute paths")
        }
    }
    pub fn as_str(&self) -> &str { &self.0 }
}

fn delete(_id: &ResourceId) {}

fn main() {
    let rid = ResourceId::parse("/subscriptions/s1/vm-01").unwrap();
    assert!(ResourceId::parse("vm-01").is_err());

    delete(&rid);

    // The point: a SubscriptionId cannot be passed where a ResourceId is
    // expected, even though both wrap a String. The following would not
    // compile:
    //     delete(&SubscriptionId("s1".into()));
    let _sub = SubscriptionId("s1".into());
    assert_eq!(rid.as_str(), "/subscriptions/s1/vm-01");
}
```

C# has this pattern too — you have probably written a `readonly record struct ResourceId(string Value)` — but
two things make it far more common in Rust. The wrapper genuinely compiles to nothing, so there is no
allocation or indirection to weigh against the safety. And the orphan rule from module 07 *forces* it: you
cannot implement `Display` for `Vec<String>` because you own neither, but you can wrap it and implement
whatever you like. The newtype is both a modelling tool and the standard escape hatch from a coherence rule.

Use it whenever a primitive carries meaning: ids, units, validated strings, quantities. `f64` is not a
`Celsius`, and `String` is not an `EmailAddress`.

## The builder pattern

Rust has no named arguments, no optional parameters, and no method overloading, so a type with more than
three or four configurable fields wants a builder. You already know the shape; the Rust variant has one twist
worth understanding.

```rust
#[derive(Debug, PartialEq)]
pub struct ScanConfig {
    endpoint: String,
    concurrency: usize,
    strict: bool,
    timeout_ms: u64,
}

pub struct ScanConfigBuilder {
    endpoint: String,
    concurrency: usize,
    strict: bool,
    timeout_ms: u64,
}

impl ScanConfig {
    pub fn builder(endpoint: impl Into<String>) -> ScanConfigBuilder {
        ScanConfigBuilder {
            endpoint: endpoint.into(),
            concurrency: 4,
            strict: false,
            timeout_ms: 30_000,
        }
    }
}

impl ScanConfigBuilder {
    /// Taking `self` by value and returning it enables chaining.
    #[must_use]
    pub fn concurrency(mut self, n: usize) -> Self {
        self.concurrency = n;
        self
    }
    #[must_use]
    pub fn strict(mut self, yes: bool) -> Self {
        self.strict = yes;
        self
    }
    pub fn build(self) -> ScanConfig {
        ScanConfig {
            endpoint: self.endpoint,
            concurrency: self.concurrency,
            strict: self.strict,
            timeout_ms: self.timeout_ms,
        }
    }
}

fn main() {
    let cfg = ScanConfig::builder("https://example.com")
        .concurrency(32)
        .strict(true)
        .build();

    assert_eq!(cfg.concurrency, 32);
    assert!(cfg.strict);
    assert_eq!(cfg.timeout_ms, 30_000);
}
```

The twist is `mut self` rather than `&mut self`. Taking ownership and returning it means the chain moves the
builder along rather than borrowing it, which avoids the temporary-lifetime problems that `&mut self` chains
run into. Required fields go in `builder()`'s parameters so they cannot be forgotten — the compiler enforces
what a C# builder would have to check at runtime in `Build()`.

Note `impl Into<String>` on the constructor. It accepts `&str`, `String`, and `Cow<str>` alike, which is the
idiomatic way to be generous about input without forcing callers to allocate. And `#[must_use]` on the
chaining methods means ignoring the returned builder is a warning, catching the classic mistake of writing
`builder.concurrency(32);` on its own line and wondering why nothing changed.

For real projects, `derive_builder` or `bon` generate all of this.

## Typestate

Typestate encodes an object's lifecycle in its *type*, so calling a method in the wrong state is a compile
error rather than an `InvalidOperationException`.

```rust
use std::marker::PhantomData;

pub struct Draft;
pub struct Validated;

pub struct RuleSet<State> {
    rules: Vec<String>,
    _state: PhantomData<State>,
}

impl RuleSet<Draft> {
    pub fn new(rules: Vec<String>) -> Self {
        RuleSet { rules, _state: PhantomData }
    }

    /// Consumes the draft and produces a validated set — or fails.
    pub fn validate(self) -> Result<RuleSet<Validated>, String> {
        if self.rules.iter().any(|r| r.is_empty()) {
            return Err("empty rule name".into());
        }
        Ok(RuleSet { rules: self.rules, _state: PhantomData })
    }
}

// `evaluate` exists ONLY on the validated type.
impl RuleSet<Validated> {
    pub fn evaluate(&self) -> usize {
        self.rules.len()
    }
}

fn main() {
    let draft = RuleSet::<Draft>::new(vec!["require-owner".into()]);
    // draft.evaluate();  <-- does not compile: no such method on RuleSet<Draft>

    let validated = draft.validate().unwrap();
    assert_eq!(validated.evaluate(), 1);

    let bad = RuleSet::<Draft>::new(vec![String::new()]);
    assert!(bad.validate().is_err());
}
```

`PhantomData<State>` is a zero-sized marker that lets a type parameter appear without being stored. The
result is that "you must validate before evaluating" is checked at compile time and costs nothing at runtime.
This is achievable in C# with generic phantom parameters but it is rare, because C# programmers reach for a
runtime state check and an exception. Rust's community reaches for the type system, and the API is better for
it — `HttpRequestBuilder` in .NET throwing "request already sent" is precisely the class of bug typestate
eliminates.

Do not over-apply it. Two or three states is elegant; six becomes unreadable.

## `impl Trait` in argument and return position

`impl Trait` is two different features sharing a spelling, and knowing which one you are using matters.

In **argument** position it is shorthand for a generic parameter — the function is monomorphised per caller
type, exactly as if you had written `<T: Display>`:

```rust
use std::fmt::Display;

// These two are equivalent.
fn log_a(item: impl Display) { let _ = item.to_string(); }
fn log_b<T: Display>(item: T) { let _ = item.to_string(); }

fn main() {
    log_a("res-1");
    log_a(42);
    log_b(3.5);
}
```

Prefer the `impl Trait` form when the parameter is used once and you never need to name `T`; use the explicit
generic when you have multiple parameters of the same type, or need `T` in a `where` clause.

In **return** position it means something different and more useful: "I return some concrete type that
implements this trait, and I am not telling you which." That lets you return closures and iterators, whose
real types are unnameable:

```rust
/// Returns an iterator without allocating a Vec and without boxing.
fn high_severity<'a>(
    findings: &'a [(String, u8)],
    min: u8,
) -> impl Iterator<Item = &'a str> + 'a {
    findings
        .iter()
        .filter(move |(_, sev)| *sev >= min)
        .map(|(name, _)| name.as_str())
}

fn main() {
    let findings = vec![
        ("require-owner".to_string(), 3u8),
        ("nice-to-have".to_string(), 1u8),
    ];
    let names: Vec<&str> = high_severity(&findings, 2).collect();
    assert_eq!(names, vec!["require-owner"]);
}
```

The C# analogue is returning `IEnumerable<T>` from an iterator method — but there the return is an interface,
so every `MoveNext` is a virtual call. `impl Iterator` returns the concrete type, so the whole chain inlines
and there is no dynamic dispatch and no allocation. That is the zero-cost-abstraction claim in one example.

The limitation: a function returning `impl Trait` can return only **one** concrete type. Two branches
returning different iterator types will not compile, and then you need `Box<dyn Iterator>` — accepting
allocation and virtual dispatch, which is where C# started.

## When to clone

Newcomers from GC languages oscillate between two failure modes: fighting the borrow checker for hours to
avoid a clone that would cost 40 nanoseconds, and cloning everything until the program is a copy machine.
Calibration comes from knowing what a clone actually costs.

Cloning is **cheap and correct** when the type is `Copy` (integers, floats, `bool`, `char`, and small structs
of them — the compiler does it implicitly anyway), when it is an `Arc` or `Rc` (a refcount increment, not a
deep copy), when the data is small and the alternative is a lifetime parameter that infects five type
signatures, or when you are in setup code that runs once.

Cloning is **worth avoiding** inside a hot loop, on large collections, when the clone is inside a function
called per element, and — most importantly — when it is hiding a design problem rather than solving one.

```rust
use std::sync::Arc;

#[derive(Debug)]
struct Settings { endpoint: String }

fn main() {
    // Cheap: an Arc clone is a refcount bump. This is the standard way to
    // share configuration with spawned tasks.
    let settings = Arc::new(Settings { endpoint: "https://x".into() });
    let for_task = Arc::clone(&settings);
    assert_eq!(for_task.endpoint, settings.endpoint);
    assert_eq!(Arc::strong_count(&settings), 2);

    // Free: Copy types don't even need the call.
    let a = 5i32;
    let b = a;
    assert_eq!(a + b, 10);
}
```

My rule of thumb: **write the clone, measure, and remove it if it matters.** A working program you optimise
later beats an elegant one you never finish. The exception is a clone in a per-item loop, where you should
think first — that is where the cost compounds.

Note the convention of writing `Arc::clone(&x)` rather than `x.clone()`. Both compile; the explicit form
signals to the reader that this is a refcount bump and not a deep copy, and the API guidelines recommend it.

## Anti-patterns

Here are the mistakes I see most often from people arriving with a C# background, in roughly descending
order of frequency.

### Reaching for `Rc<RefCell<T>>` too early

This is the big one. A C# object graph is a web of mutable references, and the first instinct on hitting the
borrow checker is to reproduce that with `Rc<RefCell<T>>` — which does work, and gives up compile-time
guarantees for runtime panics.

```rust
use std::cell::RefCell;
use std::rc::Rc;

fn main() {
    let shared = Rc::new(RefCell::new(vec![1, 2, 3]));
    shared.borrow_mut().push(4);
    assert_eq!(shared.borrow().len(), 4);

    // The hazard: two simultaneous borrows panic at RUNTIME.
    let _guard = shared.borrow_mut();
    assert!(shared.try_borrow_mut().is_err());
}
```

Before reaching for it, try in order: restructuring so there is a single owner and others borrow; using
indices into a `Vec` instead of pointers between nodes (the standard way to build graphs and trees in Rust);
splitting the struct so different parts are borrowed independently; and passing data through channels instead
of sharing it. `Rc<RefCell<T>>` is legitimate for genuine shared mutable ownership — observer registries,
some GUI trees — but it should be a considered choice, not the first thing you type.

### `unwrap()` in library code

`unwrap()` and `expect()` panic, and a panic in a library is a decision to terminate someone else's process.
In a `main`, a prototype, or a test it is fine. In a library, return `Result` and let the caller decide.

When you do use `expect`, make the message state the *invariant that was violated*, not what you were doing:
"rule set was validated before evaluation" is useful, "failed to get rule" is not. The message is what
someone reads in a bug report at 3am.

### Over-generic APIs

Because generics are free at runtime, it is tempting to make everything generic. But every type parameter is
a thing the reader must understand and the compiler must monomorphise, and it inflates compile times and
binary size. Write the concrete version first. Generalise when you have a second caller that needs it.

### `&String`, `&Vec<T>`, and `&Box<T>` in parameters

Take `&str`, `&[T]`, and `&T` instead. The slice forms accept strictly more callers at no cost:

```rust
// Accepts &String and &str and string literals.
fn count_rules(names: &[&str]) -> usize { names.len() }
fn shout(s: &str) -> String { s.to_uppercase() }

fn main() {
    let owned = String::from("require-owner");
    assert_eq!(shout(&owned), "REQUIRE-OWNER");   // deref coercion
    assert_eq!(shout("literal"), "LITERAL");

    let v = vec!["a", "b"];
    assert_eq!(count_rules(&v), 2);               // Vec derefs to slice
}
```

Clippy flags this automatically. It is the most mechanical improvement you can make to a Rust API.

### Fighting the borrow checker instead of listening to it

When the borrow checker rejects something, the reflex is to add `.clone()`, `Rc`, or a lifetime annotation
until it compiles. Sometimes the right move; often the error is telling you the design has two owners for one
piece of data, and the fix is structural. Module 06's cookbook covers the six fights you will actually have.
Read the error message — Rust's are genuinely excellent — and ask what invariant is being protected before
you reach for the workaround.

### Ignoring clippy

`cargo clippy -- -D warnings` in CI is not optional. It catches real bugs (the `result_large_err` in the
capstone was found this way), teaches idiom continuously, and costs nothing. Treat it as a much better
Roslyn analyzer set that everyone in the ecosystem has already agreed on.

### `async` everywhere

Async colours your entire call graph, adds a runtime dependency, and complicates every signature. If your
program does not have many concurrent I/O operations in flight, threads and blocking I/O are simpler and
often faster. .NET makes async cheap enough to use reflexively; Rust does not, and the reflex is worth
unlearning.

## API design guidelines

The Rust API Guidelines are the community's equivalent of the .NET Framework Design Guidelines, and worth an
hour of your time. The points that most often catch C# programmers out:

Naming follows a strict convention — `snake_case` for functions, `CamelCase` for types, `SCREAMING_CASE` for
constants — and conversion methods have prescribed prefixes with prescribed meanings. `as_` is a cheap
borrowed view, `to_` is an expensive conversion that allocates, and `into_` consumes the receiver. Getting
this right tells your reader the cost of a call from its name alone, which C# conveys only by convention and
often not at all.

Derive liberally. `Debug` on every public type is close to mandatory — a type without it is painful to use in
`assert_eq!` or a log line — and `Clone`, `PartialEq`, `Eq`, `Hash`, `Default`, `PartialOrd`, and `Ord` should
be derived wherever they make sense. This is the closest thing Rust has to `ToString`/`Equals`/`GetHashCode`
being on every object, except that here you opt in and the compiler writes them.

Accept generously and return concretely: take `impl Into<String>` or `&str` in parameters, return owned
concrete types. Put `#[must_use]` on anything whose result must not be discarded. And remember that adding a
public field or a new enum variant is a breaking change, so use `#[non_exhaustive]` on public enums you expect
to grow — it forces downstream `match`es to include a wildcard arm, which is Rust's answer to the
add-a-subclass compatibility problem.

## The top twenty mental-model shifts

The consolidated list. If you read nothing else in this chapter, read this.

**1. Values have exactly one owner.** Assignment moves rather than aliases. Almost nothing in C# prepares you
for this, and it explains most of the borrow checker's behaviour.

**2. A reference is a compile-time-checked borrow, not a pointer you may keep.** `&T` and `&mut T` carry
lifetimes the compiler verifies; C#'s `ref` has nothing like this.

**3. Either one mutable borrow or any number of shared ones — never both.** This single rule is what makes
data races impossible.

**4. Immutable by default.** `let` binds immutably; `mut` is the opt-in. C# has it backwards.

**5. `String` and `&str` are different types.** Owned growable buffer versus borrowed view. There is no single
`string` type, and this is the friction you will feel most in week one.

**6. Errors are values, not control flow.** `Result<T, E>` is returned and propagated with `?`. There is no
`catch`, no exception filters, and no invisible unwinding path through your code.

**7. `Option<T>` is a real type, not an annotation.** You cannot use it without unwrapping it. C#'s nullable
reference types are a warning; this is a type error.

**8. Panics are for bugs, not for expected failures.** A panic means an invariant broke. Use `Result` for
anything a caller could reasonably handle.

**9. Traits are implemented from outside the type.** You can implement your trait for `i32`. This makes
extension far more powerful than C# extension methods, which cannot satisfy an interface.

**10. Generics are monomorphised, not reified.** Each instantiation is separately compiled and inlined — so
zero-cost, but no runtime type information, no `typeof(T)`, and no reflection over type arguments.

**11. `dyn Trait` is the interface-dispatch model, and it is opt-in.** You choose per use site between static
dispatch (fast, larger binary) and dynamic dispatch (a vtable, like every C# interface call).

**12. Iterators are lazy and compile away.** An iterator chain becomes a loop with no allocations and no
virtual calls, unlike `IEnumerable<T>`'s interface dispatch per element. And there is no `IQueryable`
equivalent, because there is no expression-tree reflection.

**13. `Drop` is deterministic and automatic.** Cleanup happens at scope exit, guaranteed, without a `using`.
There are no finalizers and no GC — but `Drop` cannot be async.

**14. There is no runtime.** No GC, no thread pool, no JIT, no async scheduler. Async needs an external
runtime you choose and start yourself.

**15. `Send` and `Sync` are compiler-checked thread-safety.** Thread safety is a property of the type that
the compiler verifies, not a convention documented in XML comments.

**16. Compilation is the review.** Exhaustive `match`, unused `Result`, borrow errors — a great deal of what
you would catch in a C# code review or a unit test is caught by rustc.

**17. Crates are compilation units, not deployment units.** A crate is closer to a C# project than a NuGet
package; `Cargo.lock`, semver, and feature flags together give you far more reproducibility than
`packages.config` ever did.

**18. The standard library is deliberately small.** Dates, JSON, regex, random, and HTTP live on crates.io so
they can evolve. Expect to add dependencies for things the BCL includes.

**19. Macros run at compile time on the syntax tree.** `#[derive]` and `serde` are Roslyn source generators,
not reflection — which is why serde is fast and why there is no runtime type discovery.

**20. Explicitness is the point.** Costs are visible in the source: allocation, copying, dispatch,
concurrency. Nothing is ambient. That is more typing up front, and it is what buys you the guarantees.

## Before you move on

The patterns worth deliberate adoption are the newtype — free at runtime, and the standard way around the
orphan rule as well as a modelling tool — the owning `mut self` builder for types with many optional fields,
typestate with `PhantomData` when a lifecycle should be compile-checked rather than exception-guarded, and
`impl Trait` in return position to hand back iterators and closures with no boxing and no virtual dispatch.

The anti-patterns are mostly C# habits arriving intact. `Rc<RefCell<T>>` reproduces a GC object graph and
trades compile-time guarantees for runtime panics — try single ownership, indices, struct splitting, or
channels first. `unwrap()` in a library decides to kill someone else's process. `&String` and `&Vec<T>`
should be `&str` and `&[T]`. Over-generic APIs cost compile time and readability for a flexibility you may
never need. And async should be a decision, not a default.

On cloning, calibrate rather than moralise: `Copy` types and `Arc` clones are cheap, setup code does not
matter, and per-element clones in hot loops do. Write it, measure, remove it if it counts — and spell it
`Arc::clone(&x)` so the reader knows which kind it is.

If you can explain why `impl Iterator` in return position is cheaper than returning `IEnumerable<T>`, and
name three things to try before `Rc<RefCell<T>>`, you have the judgement this chapter exists to build.

Next: [29 — Reference: glossary, FAQ, and sources](29-reference.md).

### Sources

- Rust API Guidelines. <https://rust-lang.github.io/api-guidelines/> — naming, conversions, `#[must_use]`, and derive conventions.
- Rust API Guidelines, "Naming". <https://rust-lang.github.io/api-guidelines/naming.html> — the `as_`/`to_`/`into_` cost convention.
- Rust by Example, "New Type Idiom". <https://doc.rust-lang.org/rust-by-example/generics/new_types.html>
- `std::marker::PhantomData`. <https://doc.rust-lang.org/std/marker/struct.PhantomData.html> — zero-sized type parameters for typestate.
- The Rust Reference, "Impl trait". <https://doc.rust-lang.org/reference/types/impl-trait.html> — argument versus return position.
- The Rust Reference, `#[non_exhaustive]`. <https://doc.rust-lang.org/reference/attributes/type_system.html#the-non_exhaustive-attribute> — evolving public enums compatibly.
- Clippy lint index. <https://rust-lang.github.io/rust-clippy/master/> — the lint catalogue worth reading once.
- The Rust Programming Language, ch. 17, "Object-Oriented Programming Features". <https://doc.rust-lang.org/book/ch17-00-oop.html> — trait objects versus inheritance.
- Microsoft Learn, ".NET Framework Design Guidelines". <https://learn.microsoft.com/dotnet/standard/design-guidelines/> — the comparison point for API conventions.
