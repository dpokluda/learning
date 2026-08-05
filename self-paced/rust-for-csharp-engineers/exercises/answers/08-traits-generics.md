# Answers 08 — Traits and generics

> Exercises: [08-traits-generics.md](../08-traits-generics.md)

## Part A

**A1. A Rust trait and a C# interface look alike. Give three concrete differences that change how you design.**

First, traits can be implemented for types you do not own — `impl MyTrait for String` is legal — so you extend third-party types without wrappers or extension methods, subject only to the orphan rule. Second, trait methods can have default bodies that call the required methods, so a trait can offer a large derived API over a small required core (`Iterator` requires only `next` and provides seventy-odd adaptors); C# default interface methods arrived late and are rarely used this way. Third, traits are not types: a value's type never *is* `Check`, and you must opt into `dyn Check` to get runtime polymorphism, which means the default is static dispatch with no allocation and no vtable. The design consequence is that Rust traits are used for capability and generic bounds far more than for runtime substitutability.

**A2. Explain monomorphisation, and contrast it with how .NET handles generics.**

The compiler stamps out a separate specialised copy of a generic function for each concrete type it is called with, so `run_static::<NonEmpty>` and `run_static::<MaxLen>` are two distinct functions with the trait calls resolved statically and available for inlining. .NET does something similar but only for value types — each value-type instantiation gets its own JIT-compiled code — while all reference-type instantiations *share* one code path that operates on object references. The Rust model gives uniform zero-overhead abstraction at the cost of code size and compile time; the .NET model keeps the binary small and supports true runtime reflection over generics, which Rust cannot do because the type arguments no longer exist at run time.

**A3. What is a trait object, when do you need one, and what does it cost?**

`dyn Trait` is a trait object: a fat pointer holding the data pointer and a vtable pointer, resolved at run time. You need one whenever the concrete type is not known statically — a heterogeneous `Vec<Box<dyn Check>>`, a plugin registry, or a return type that varies by branch — because monomorphisation requires one concrete type per instantiation. It costs an indirection per call, blocks inlining, and usually costs an allocation (`Box`). It is exactly what a C# `IList<ICheck>` always was, so the cost is not new; the difference is that Rust makes you write `dyn` and `Box`, so the cost is visible rather than the silent default.

**A4. What is object safety, and why can't you make a `dyn` out of every trait?**

A trait is object-safe (now called `dyn`-compatible) only if every method can be dispatched through a vtable. That rules out generic methods, because there is no single compiled body to point at; methods taking or returning `Self` by value, because the size is unknown behind the pointer; and associated constants. `Clone` is the canonical example: `fn clone(&self) -> Self` cannot go in a vtable, which is why `Box<dyn Clone>` does not exist and crates define `trait CloneBox { fn clone_box(&self) -> Box<dyn Trait>; }` instead. When the compiler tells you a trait "cannot be made into an object", it is almost always one of those three causes.

**A5. State the orphan rule and explain what problem it prevents.**

You may implement a trait for a type only if either the trait or the type is local to your crate. It prevents two unrelated crates from both writing `impl Display for Vec<u8>` — if both were allowed and a third crate depended on both, there would be no principled way to choose, and coherence (one impl per trait-type pair, globally) would be lost. C# has no equivalent problem because extension methods are resolved lexically by `using` directives, so two conflicting extensions simply do not conflict unless imported together. The practical workaround in Rust is the newtype: wrap the foreign type in a local tuple struct and implement the foreign trait on that.

**A6. What is a blanket impl, and what does it give you over a C# extension method?**

A blanket impl is `impl<T: Bound> Trait for T`, implementing a trait for *every* type satisfying a bound — for example `impl<T: Display> ToString for T`, which is why every printable type has `.to_string()`. The advantage over an extension method is that it participates in the trait system: the generated impls satisfy generic bounds elsewhere, so a function requiring `T: ToString` accepts any `Display` type without knowing about the blanket. A C# extension method is only syntax — it is invisible to generic constraints, it cannot be dispatched dynamically, and it is not found unless the namespace is imported. The cost is coherence pressure: a blanket impl is very hard to add to an existing trait without breaking downstream impls.

## Part B — worked solution

This is the exact file that was compiled and run to produce this book; every assertion below passed under `cargo test` on the pinned toolchain.

```rust
//! Drill 08 — Traits, generics, static vs dynamic dispatch.

use std::fmt;

/// A trait with a required method and a *default* method built on top of it.
/// The C# analogue is an interface with a default interface method, except that
/// here the default has been idiomatic since 1.0 and carries no diamond rules.
pub trait Check {
    /// The one thing an implementor must supply.
    fn passes(&self, value: &str) -> bool;

    /// Provided for free; override only if you can do better.
    fn name(&self) -> String {
        "unnamed check".to_string()
    }

    fn describe(&self, value: &str) -> String {
        if self.passes(value) {
            format!("{}: pass", self.name())
        } else {
            format!("{}: FAIL ({value:?})", self.name())
        }
    }
}

pub struct NonEmpty;

impl Check for NonEmpty {
    fn passes(&self, value: &str) -> bool {
        !value.trim().is_empty()
    }
    fn name(&self) -> String {
        "non-empty".to_string()
    }
}

pub struct MaxLen(pub usize);

impl Check for MaxLen {
    fn passes(&self, value: &str) -> bool {
        value.chars().count() <= self.0
    }
    fn name(&self) -> String {
        format!("max-len({})", self.0)
    }
}

pub struct Lowercase;

impl Check for Lowercase {
    fn passes(&self, value: &str) -> bool {
        value.chars().all(|c| !c.is_uppercase())
    }
    // deliberately does not override `name`, to exercise the default
}

/// **Static dispatch.** `impl Check` monomorphises: the compiler stamps out a
/// copy per concrete type and can inline through the call. Zero indirection.
pub fn run_static(check: impl Check, value: &str) -> bool {
    check.passes(value)
}

/// Generic with a `where` clause — identical meaning, better shape once bounds
/// pile up. Note this accepts a *slice of one concrete type*.
pub fn count_passing<C>(checks: &[C], value: &str) -> usize
where
    C: Check,
{
    checks.iter().filter(|c| c.passes(value)).count()
}

/// **Dynamic dispatch.** A heterogeneous list needs a trait object: one vtable
/// pointer per element, resolved at runtime. This is what a C# `IList<ICheck>`
/// always was — except in Rust you opt into the indirection explicitly.
pub fn run_all(checks: &[Box<dyn Check>], value: &str) -> Vec<String> {
    checks.iter().map(|c| c.describe(value)).collect()
}

/// A generic newtype with a bound on the *impl block* rather than the struct —
/// the idiom that keeps the type usable even where the bound does not hold.
pub struct Report<T> {
    pub items: Vec<T>,
}

impl<T> Report<T> {
    pub fn new() -> Self {
        Self { items: Vec::new() }
    }
    pub fn push(&mut self, item: T) {
        self.items.push(item);
    }
}

impl<T> Default for Report<T> {
    fn default() -> Self {
        Self::new()
    }
}

impl<T: fmt::Display> Report<T> {
    /// Only available when `T: Display`.
    pub fn render(&self) -> String {
        self.items.iter().map(T::to_string).collect::<Vec<_>>().join("; ")
    }
}

/// A blanket impl: every `Check` gets `Negated` for free. This is the mechanism
/// behind `impl<T: Display> ToString for T`, and it is strictly more powerful
/// than C# extension methods because it participates in generic resolution.
pub trait CheckExt: Check + Sized {
    fn negated(self) -> Negated<Self> {
        Negated(self)
    }
}

impl<T: Check> CheckExt for T {}

pub struct Negated<T>(pub T);

impl<T: Check> Check for Negated<T> {
    fn passes(&self, value: &str) -> bool {
        !self.0.passes(value)
    }
    fn name(&self) -> String {
        format!("not({})", self.0.name())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_methods_are_inherited_unless_overridden() {
        assert_eq!(NonEmpty.name(), "non-empty");
        assert_eq!(Lowercase.name(), "unnamed check");
        assert_eq!(MaxLen(4).name(), "max-len(4)");
    }

    #[test]
    fn static_dispatch_takes_a_concrete_type() {
        assert!(run_static(NonEmpty, "prod"));
        assert!(!run_static(NonEmpty, "   "));
        assert!(run_static(MaxLen(4), "prod"));
        assert!(!run_static(MaxLen(3), "prod"));
    }

    #[test]
    fn a_homogeneous_slice_needs_no_trait_object() {
        let checks = [MaxLen(10), MaxLen(3), MaxLen(1)];
        assert_eq!(count_passing(&checks, "prod"), 1);
    }

    #[test]
    fn dynamic_dispatch_allows_a_heterogeneous_list() {
        let checks: Vec<Box<dyn Check>> =
            vec![Box::new(NonEmpty), Box::new(MaxLen(3)), Box::new(Lowercase)];
        let out = run_all(&checks, "PROD");
        assert_eq!(out[0], "non-empty: pass");
        assert_eq!(out[1], "max-len(3): FAIL (\"PROD\")");
        assert_eq!(out[2], "unnamed check: FAIL (\"PROD\")");
    }

    #[test]
    fn impl_blocks_may_carry_bounds_the_struct_does_not() {
        let mut r: Report<u32> = Report::new();
        r.push(1);
        r.push(2);
        assert_eq!(r.render(), "1; 2");

        // A `Report<NonEmpty>` still constructs fine; it just has no `render`.
        let mut opaque: Report<NonEmpty> = Report::default();
        opaque.push(NonEmpty);
        assert_eq!(opaque.items.len(), 1);
    }

    #[test]
    fn a_blanket_impl_extends_every_implementor() {
        let inverted = NonEmpty.negated();
        assert!(inverted.passes("   "));
        assert!(!inverted.passes("prod"));
        assert_eq!(inverted.name(), "not(non-empty)");
    }
}
```
