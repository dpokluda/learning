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
