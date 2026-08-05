//! Drill 09 — The standard traits that make a type feel native.
//!
//! Take a plain struct from "a bag of data" to "a type the rest of the language
//! already knows how to use". Every test below is unlocked by one std trait.

use std::cmp::Ordering;
use std::fmt;
use std::str::FromStr;

/// Add the derives the tests need. Work them out from the assertions rather
/// than deriving everything reflexively: `Copy`, ordering, hashing and
/// `Default` are each load-bearing for exactly one test.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Severity(u8);

impl Severity {
    pub const INFO: Severity = Severity(0);
    pub const WARNING: Severity = Severity(1);
    pub const ERROR: Severity = Severity(2);

    pub fn level(self) -> u8 {
        todo!()
    }
}

/// The user-facing rendering — `"info"` / `"warning"` / `"error"`. Note there
/// is no `impl ToString`: you get `to_string()` free from the blanket impl.
impl fmt::Display for Severity {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}

#[derive(Debug, PartialEq, Eq)]
pub struct ParseSeverityError(pub String);

/// Reads `unknown severity: {0}`.
impl fmt::Display for ParseSeverityError {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}

impl std::error::Error for ParseSeverityError {}

/// This is what powers `"warning".parse::<Severity>()` — the `TryParse`
/// analogue, except the failure is a value rather than an `out` parameter.
/// Accept `info`, `warning`, `warn`, `error`, case-insensitively, ignoring
/// surrounding whitespace.
impl FromStr for Severity {
    type Err = ParseSeverityError;

    fn from_str(_s: &str) -> Result<Self, Self::Err> {
        todo!()
    }
}

/// Implement `From` and you get `Into` free from the blanket impl. Always
/// implement this direction, never `Into` directly.
impl From<Severity> for u8 {
    fn from(_value: Severity) -> Self {
        todo!()
    }
}

/// The fallible direction, which also wires up `try_into()`. Accept 0..=2.
impl TryFrom<u8> for Severity {
    type Error = ParseSeverityError;

    fn try_from(_value: u8) -> Result<Self, Self::Error> {
        todo!()
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Finding {
    pub id: String,
    pub severity: Severity,
}

/// Order by severity **descending**, then by id **ascending**. Deriving `Ord`
/// would give the wrong answer, so this one is hand-written. `Ordering::then_with`
/// is the tie-breaking combinator.
impl Ord for Finding {
    fn cmp(&self, _other: &Self) -> Ordering {
        todo!()
    }
}

/// The contract: when `Ord` is hand-written, `PartialOrd` must agree with it.
/// Delegating is the only correct implementation — write that one line.
impl PartialOrd for Finding {
    fn partial_cmp(&self, _other: &Self) -> Option<Ordering> {
        todo!()
    }
}

/// Renders as `"[{severity}] {id}"`.
impl fmt::Display for Finding {
    fn fmt(&self, _f: &mut fmt::Formatter<'_>) -> fmt::Result {
        todo!()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeSet;

    #[test]
    fn display_is_the_tostring_analogue() {
        assert_eq!(Severity::WARNING.to_string(), "warning");
        assert_eq!(format!("{}", Severity::ERROR), "error");
    }

    #[test]
    fn debug_and_display_are_different_traits_on_purpose() {
        assert_eq!(format!("{}", Severity::INFO), "info");
        assert_eq!(format!("{:?}", Severity::INFO), "Severity(0)");
    }

    #[test]
    fn fromstr_powers_the_parse_method() {
        assert_eq!("warning".parse::<Severity>(), Ok(Severity::WARNING));
        assert_eq!("  WARN ".parse::<Severity>(), Ok(Severity::WARNING));
        assert_eq!(
            "nope".parse::<Severity>().unwrap_err(),
            ParseSeverityError("nope".to_string())
        );
    }

    #[test]
    fn from_gives_into_for_free() {
        let raw: u8 = Severity::ERROR.into();
        assert_eq!(raw, 2);

        let back: Severity = 1u8.try_into().unwrap();
        assert_eq!(back, Severity::WARNING);
        assert!(Severity::try_from(9u8).is_err());
    }

    #[test]
    fn default_is_the_zero_value() {
        assert_eq!(Severity::default(), Severity::INFO);
    }

    #[test]
    fn hash_and_eq_let_the_type_be_a_key() {
        let set: BTreeSet<Severity> = [Severity::ERROR, Severity::INFO, Severity::ERROR].into();
        assert_eq!(set.len(), 2);
        // BTreeSet iterates in `Ord` order, which the derive made numeric.
        assert_eq!(set.iter().next(), Some(&Severity::INFO));
    }

    #[test]
    fn hand_written_ord_drives_sort() {
        let mut findings = [
            Finding { id: "b".into(), severity: Severity::INFO },
            Finding { id: "a".into(), severity: Severity::ERROR },
            Finding { id: "c".into(), severity: Severity::ERROR },
        ];
        findings.sort();
        let rendered: Vec<String> = findings.iter().map(Finding::to_string).collect();
        assert_eq!(rendered, ["[error] a", "[error] c", "[info] b"]);
    }
}
