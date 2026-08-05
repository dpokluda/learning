//! Drill 04 — `String` vs `&str`, byte indices, and char boundaries.
//!
//! The recurring trap: `&s[..n]` slices by *bytes* and panics if `n` is not a
//! UTF-8 code point boundary. Two tests here exist to catch exactly that.

/// Return the first whitespace-delimited word as a *borrow* of the input.
/// Do not allocate — the signature already ties the result's life to `input`.
pub fn first_word(_input: &str) -> &str {
    todo!("borrow a sub-slice; no String, no to_string()")
}

/// Truncate to at most `max_chars` **characters**, never panicking and never
/// splitting a code point. `char_indices` hands you the byte offset of each.
pub fn truncate_chars(_input: &str, _max_chars: usize) -> &str {
    todo!("count chars, slice on the byte offset you get back")
}

/// Lowercase the scope and guarantee a leading `/`, allocating once.
pub fn normalize_scope(_scope: &str) -> String {
    todo!()
}

/// The longest whitespace-delimited word, measured in characters.
/// The `AsRef<str>` bound is what lets callers pass `&str` *or* `String`.
pub fn longest_word<S: AsRef<str>>(_text: S) -> String {
    todo!("max_by_key over chars().count()")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn first_word_borrows_rather_than_allocates() {
        let owned = String::from("policy assignment scope");
        assert_eq!(first_word(&owned), "policy");
        assert_eq!(first_word("single"), "single");
        assert_eq!(first_word(""), "");
    }

    #[test]
    fn truncate_counts_chars_not_bytes() {
        // "é" is two bytes; a naive &s[..2] would be fine here but &s[..1] panics.
        assert_eq!(truncate_chars("déní", 2), "dé");
        assert_eq!(truncate_chars("déní", 99), "déní");
        assert_eq!(truncate_chars("", 3), "");
    }

    #[test]
    fn truncate_never_splits_a_code_point() {
        let s = "αβγδ"; // 2 bytes each
        for n in 0..=6 {
            // The point of the drill: this must not panic for any n.
            let t = truncate_chars(s, n);
            assert!(s.starts_with(t));
        }
    }

    #[test]
    fn normalize_adds_the_leading_slash_only_when_missing() {
        assert_eq!(normalize_scope("/Subscriptions/A"), "/subscriptions/a");
        assert_eq!(normalize_scope("Subscriptions/A"), "/subscriptions/a");
    }

    #[test]
    fn as_ref_accepts_both_string_and_str() {
        assert_eq!(longest_word("a bb ccc"), "ccc");
        assert_eq!(longest_word(String::from("a bb ccc")), "ccc");
        assert_eq!(longest_word(""), "");
    }
}
