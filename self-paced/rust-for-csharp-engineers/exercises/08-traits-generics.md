# Exercises 08 — Traits and generics

> **Covers:** [08 — Traits and generics](../08-traits-and-generics.md). **Code:** `drills/src/ch08.rs`. **Answers:** [answers/08-traits-generics.md](answers/08-traits-generics.md).

Answer Part A from memory before you open the code — writing the answer out is what tells you whether you actually understood the chapter or merely followed it. Then do Part B in the editor, and only then check yourself against the answer book.

## Part A — Questions

**A1.** A Rust trait and a C# interface look alike. Give three concrete differences that change how you design.

**A2.** Explain monomorphisation, and contrast it with how .NET handles generics.

**A3.** What is a trait object, when do you need one, and what does it cost?

**A4.** What is object safety, and why can't you make a `dyn` out of every trait?

**A5.** State the orphan rule and explain what problem it prevents.

**A6.** What is a blanket impl, and what does it give you over a C# extension method?

## Part B — Exercise

Open `drills/src/ch08.rs`. The goal is to build the same abstraction twice — once
with static dispatch and once with dynamic — so the difference stops being
theoretical.

You will implement a `Check` trait with one required method and two provided
ones, three implementors, and then four call sites: `impl Trait` in argument
position, a `where`-bounded generic over a homogeneous slice, a
`Vec<Box<dyn Check>>` for a heterogeneous list, and a blanket impl that gives
every implementor a `negated()` combinator for free. The last one is the piece
with no C# equivalent, and it is worth pausing on: an extension method could
give you the syntax, but not the ability for `Negated<T>` to itself satisfy
`Check` and flow back into the generic machinery.

Run it with `cargo test ch08` from the `exercises/drills` directory.

### Starter stub

```rust,ignore
//! Drill 08 — Traits, generics, static vs dynamic dispatch.

/// One required method, two provided ones.
pub trait Check {
    /// The only thing an implementor must supply.
    fn passes(&self, value: &str) -> bool;

    /// Default body: return `"unnamed check"`.
    fn name(&self) -> String {
        todo!()
    }

    /// Default body, built on the other two: `"{name}: pass"` when it passes,
    /// `"{name}: FAIL ({value:?})"` when it does not.
    fn describe(&self, _value: &str) -> String {
        todo!()
    }
}

pub struct NonEmpty;
pub struct MaxLen(pub usize);
pub struct Lowercase;

impl Check for NonEmpty {
    /// Passes when the value is not blank.
    fn passes(&self, _value: &str) -> bool {
        todo!()
    }
    /// Override: `"non-empty"`.
    fn name(&self) -> String {
        todo!()
    }
}

impl Check for MaxLen {
    /// Passes when the *character* count is at most `self.0`.
    fn passes(&self, _value: &str) -> bool {
        todo!()
    }
    /// Override: `"max-len(N)"`.
    fn name(&self) -> String {
        todo!()
    }
}

impl Check for Lowercase {
    /// Passes when no character is uppercase.
    fn passes(&self, _value: &str) -> bool {
        todo!()
    }
    // Deliberately does *not* override `name` — a test pins that down.
}

/// Static dispatch: monomorphised, inlinable, no vtable.
pub fn run_static(_check: impl Check, _value: &str) -> bool {
    todo!()
}

/// The same bound written as a `where` clause, over a slice of one concrete
/// type — so still no trait object.
pub fn count_passing<C>(_checks: &[C], _value: &str) -> usize
where
    C: Check,
{
    todo!()
}

/// Dynamic dispatch: a heterogeneous list needs a trait object and pays one
/// vtable indirection per call.
pub fn run_all(_checks: &[Box<dyn Check>], _value: &str) -> Vec<String> {
    todo!()
}

/// A generic container whose *impl blocks* carry different bounds. That split
/// is the point: `Report<T>` is constructible for any `T`, but `render` exists
/// only when `T: Display`.
pub struct Report<T> {
    pub items: Vec<T>,
}

impl<T> Report<T> {
    pub fn new() -> Self {
        todo!()
    }
    pub fn push(&mut self, _item: T) {
        todo!()
    }
}

impl<T> Default for Report<T> {
    fn default() -> Self {
        Self::new()
    }
}

impl<T: std::fmt::Display> Report<T> {
    /// Join the items with `"; "`.
    pub fn render(&self) -> String {
        todo!()
    }
}

/// A blanket impl gives *every* `Check` a `negated()` method — strictly more
/// powerful than a C# extension method, because it participates in generic
/// resolution rather than being resolved on the static type at the call site.
pub trait CheckExt: Check + Sized {
    fn negated(self) -> Negated<Self> {
        Negated(self)
    }
}

impl<T: Check> CheckExt for T {}

pub struct Negated<T>(pub T);

impl<T: Check> Check for Negated<T> {
    fn passes(&self, _value: &str) -> bool {
        todo!()
    }
    /// Renders as `"not(inner-name)"`.
    fn name(&self) -> String {
        todo!()
    }
}
```

The test module that follows this in the file is the specification — read it before you write anything.
