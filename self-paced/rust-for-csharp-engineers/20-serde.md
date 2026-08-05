# 20 — serde: serialization and deserialization

If you had to name the crate that most defines what modern Rust feels like, it would be serde. It is the
serialization framework the entire ecosystem agreed on, and its influence is so pervasive that "does it
implement `Serialize`?" is a question you will ask about every type you design. Configuration files, HTTP
payloads, message queues, database rows, cache entries — all of them go through serde.

Coming from .NET you have used `System.Text.Json` and probably Newtonsoft before it, so the *concept* needs
no introduction. What is genuinely different is the architecture. `System.Text.Json` is a JSON library:
it knows about JSON, and if you want YAML you get a different library with different attributes and a
different mental model. serde is a *data model* with pluggable formats on both ends. Your type derives
`Serialize` once, and that one impl works for JSON, YAML, TOML, MessagePack, CBOR, and bincode without
change. The derive runs at compile time, so there is no reflection, no `JsonSerializerContext` to opt into
for AOT, and no runtime cost for the abstraction.

For `polcheck`, serde is what turns a rule file on disk into typed Rust values, and typed findings back into
a report.

> **Prerequisite:** [19 — anyhow and thiserror](19-anyhow-and-thiserror.md).

## The two-layer design

Understanding serde's architecture explains almost every API decision it makes, so it is worth thirty
seconds up front.

serde defines a **data model** of 29 types — primitives, strings, sequences, maps, structs, enums, options,
and a few more. Two traits connect your types to it. `Serialize` says "here is how to describe me in terms
of the data model", and `Deserialize` says "here is how to build me from the data model". Format crates
like `serde_json` implement the other half: a `Serializer` that turns data-model calls into JSON bytes, and
a `Deserializer` that turns JSON bytes into data-model calls.

The consequence is an N + M rather than N × M problem. Adding a new format costs one crate and every
existing type works with it; adding a new type costs one derive and every existing format works with it.
That is a genuinely better factoring than the .NET world, where a converter written for
`System.Text.Json` is useless to a YAML library.

The practical setup needs two dependencies, and the `derive` feature is easy to forget:

```toml
[dependencies]
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"
```

## The basics

Derive the traits, call the format crate. That is the whole happy path:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
struct Resource {
    id: String,
    kind: String,
    location: String,
    tags: Vec<String>,
}

fn main() {
    let r = Resource {
        id: "res-1".into(),
        kind: "storage".into(),
        location: "westus2".into(),
        tags: vec!["prod".into(), "team-platform".into()],
    };

    // to_string for compact, to_string_pretty for human-readable.
    let json = serde_json::to_string(&r).unwrap();
    assert_eq!(
        json,
        r#"{"id":"res-1","kind":"storage","location":"westus2","tags":["prod","team-platform"]}"#
    );

    // Round-trips exactly.
    let back: Resource = serde_json::from_str(&json).unwrap();
    assert_eq!(back, r);
}
```

Note `serde_json::from_str::<Resource>` needs to know the target type, which it gets from the annotation on
`back`. Where inference cannot find it you use the turbofish — `serde_json::from_str::<Resource>(&json)` —
the same mechanism as `collect` in module 10.

Errors are ordinary `Result`s carrying line and column information, which makes them far more useful than a
bare `JsonException`:

```rust
use serde::Deserialize;

#[derive(Deserialize, Debug)]
struct Rule {
    name: String,
    severity: u8,
}

fn main() {
    let err = serde_json::from_str::<Rule>(r#"{"name":"x","severity":"high"}"#).unwrap_err();
    let msg = err.to_string();
    assert!(msg.contains("invalid type"));
    assert!(msg.contains("line 1"));

    // Missing fields are errors too — there is no silent default.
    let err = serde_json::from_str::<Rule>(r#"{"name":"x"}"#).unwrap_err();
    assert!(err.to_string().contains("missing field `severity`"));
}
```

That last case is a real philosophical difference. `System.Text.Json` leaves a missing property at its CLR
default — `null` for a reference type, `0` for an `int` — and you discover the problem later. serde treats a
missing field as an error unless you explicitly say otherwise. Combined with `Option<T>` for genuinely
optional fields, this means a successfully deserialized value is *complete*, which is the same
"parse, don't validate" payoff you have seen throughout the book.

## Field and container attributes

The `#[serde(...)]` attribute is where the customisation lives. It goes on the container (struct or enum) or
on individual fields, and the vocabulary is small enough to learn in one sitting.

**Renaming** is the most common need, because Rust wants `snake_case` and most JSON APIs want `camelCase`.
`rename_all` on the container handles the whole type at once, and `rename` overrides individual fields:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
struct PolicyAssignment {
    display_name: String,
    policy_definition_id: String,
    #[serde(rename = "id")]
    assignment_id: String,
}

fn main() {
    let a = PolicyAssignment {
        display_name: "Require owner tag".into(),
        policy_definition_id: "/providers/.../require-tag".into(),
        assignment_id: "/subscriptions/.../assignments/a1".into(),
    };

    let json = serde_json::to_string(&a).unwrap();
    assert!(json.contains(r#""displayName":"Require owner tag""#));
    assert!(json.contains(r#""policyDefinitionId""#));
    assert!(json.contains(r#""id":"/subscriptions/.../assignments/a1""#));

    let back: PolicyAssignment = serde_json::from_str(&json).unwrap();
    assert_eq!(back, a);
}
```

`rename_all` accepts `camelCase`, `PascalCase`, `snake_case`, `SCREAMING_SNAKE_CASE`, `kebab-case`, and a
few more. This is `JsonNamingPolicy` except that it is declared on the type rather than configured globally,
which matters when one program talks to two APIs with different conventions.

**Optional and defaulted fields** are where serde's model is noticeably more precise than .NET's:

```rust
use serde::{Deserialize, Serialize};

fn default_severity() -> u8 { 3 }

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
struct Rule {
    name: String,

    /// Absent in the input => None. Serialized as null unless skipped.
    #[serde(skip_serializing_if = "Option::is_none")]
    description: Option<String>,

    /// Absent => Default::default() (0 for u8, empty for Vec/String).
    #[serde(default)]
    tags: Vec<String>,

    /// Absent => the named function's return value.
    #[serde(default = "default_severity")]
    severity: u8,

    /// Never serialized, and not required when deserializing.
    #[serde(skip)]
    source_path: String,
}

fn main() {
    let r: Rule = serde_json::from_str(r#"{"name":"require-owner"}"#).unwrap();
    assert_eq!(r.name, "require-owner");
    assert_eq!(r.description, None);
    assert_eq!(r.tags, Vec::<String>::new());
    assert_eq!(r.severity, 3);            // the default fn ran
    assert_eq!(r.source_path, "");        // skipped fields get Default

    // skip_serializing_if keeps the output clean.
    let json = serde_json::to_string(&r).unwrap();
    assert_eq!(json, r#"{"name":"require-owner","tags":[],"severity":3}"#);
    assert!(!json.contains("description"));
    assert!(!json.contains("sourcePath"));
}
```

The distinction between `Option<T>` and `#[serde(default)]` is worth stating explicitly because it is a
genuine modelling choice, not a stylistic one. `Option<String>` preserves the difference between "absent"
and "present but empty"; `#[serde(default)] String` collapses them. When round-tripping a document you did
not author — a config file a user edits, an API response you forward — preserving the distinction matters.
`JsonIgnoreCondition.WhenWritingNull` is the closest .NET analogue to `skip_serializing_if`, but it cannot
express "this specific field, with this specific predicate".

**Strictness.** `deny_unknown_fields` turns an unexpected key into an error rather than silently dropping
it, which is exactly what you want for configuration files where a typo would otherwise be ignored:

```rust
use serde::Deserialize;

#[derive(Deserialize, Debug)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
struct Config {
    max_depth: usize,
}

fn main() {
    assert!(serde_json::from_str::<Config>(r#"{"maxDepth":8}"#).is_ok());

    // A typo is caught instead of silently ignored.
    let err = serde_json::from_str::<Config>(r#"{"maxDepht":8}"#).unwrap_err();
    assert!(err.to_string().contains("unknown field"));
}
```

I would turn this on for every user-authored config file and leave it off for API responses, where a server
adding a field should not break your client.

**Flattening** inlines one struct's fields into another, and doubles as the way to capture unknown keys:

```rust
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

#[derive(Serialize, Deserialize, Debug)]
struct Common {
    id: String,
    version: u32,
}

#[derive(Serialize, Deserialize, Debug)]
struct Document {
    #[serde(flatten)]
    common: Common,

    title: String,

    /// Every key not matched above lands here.
    #[serde(flatten)]
    extra: HashMap<String, serde_json::Value>,
}

fn main() {
    let json = r#"{"id":"doc-1","version":2,"title":"Policy","author":"david","draft":true}"#;
    let d: Document = serde_json::from_str(json).unwrap();

    assert_eq!(d.common.id, "doc-1");
    assert_eq!(d.common.version, 2);
    assert_eq!(d.title, "Policy");
    assert_eq!(d.extra["author"], serde_json::json!("david"));
    assert_eq!(d.extra["draft"], serde_json::json!(true));
}
```

That trailing `HashMap` is serde's answer to `[JsonExtensionData]`, and it is the standard way to
round-trip a document without losing fields you do not model. Note that `flatten` and `deny_unknown_fields`
are mutually incompatible — the flattened map would consume the very keys the check wants to reject.

## Enums: the four representations

This is serde's most powerful feature and the one with no real .NET counterpart, because .NET has no
discriminated unions. Rust enums carry data, so serde has to decide how to encode *which* variant. It offers
four representations, and choosing between them is a genuine design decision.

Start with the type. A `polcheck` rule condition is naturally a sum type:

```rust
use serde::{Deserialize, Serialize};

/// Externally tagged — the default. The variant name becomes the key.
#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
enum Condition {
    Equals { field: String, value: String },
    GreaterThan { field: String, value: i64 },
    Always,
}

fn main() {
    let c = Condition::Equals { field: "location".into(), value: "westus2".into() };
    let json = serde_json::to_string(&c).unwrap();
    assert_eq!(json, r#"{"equals":{"field":"location","value":"westus2"}}"#);

    // A unit variant is just its name as a string.
    assert_eq!(serde_json::to_string(&Condition::Always).unwrap(), r#""always""#);

    let back: Condition = serde_json::from_str(&json).unwrap();
    assert_eq!(back, c);
}
```

External tagging is unambiguous and self-describing, but it nests a level deeper than most hand-written JSON
schemas do. **Internal tagging** puts the discriminant alongside the fields, which is what most real-world
APIs look like:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(tag = "type", rename_all = "camelCase")]
enum Condition {
    Equals { field: String, value: String },
    GreaterThan { field: String, value: i64 },
}

fn main() {
    let c = Condition::Equals { field: "location".into(), value: "westus2".into() };
    let json = serde_json::to_string(&c).unwrap();
    assert_eq!(json, r#"{"type":"equals","field":"location","value":"westus2"}"#);

    let back: Condition = serde_json::from_str(&json).unwrap();
    assert_eq!(back, c);
}
```

This is the shape you would produce by hand, and it is the one to reach for when designing a new format.
Its limitation is structural: because the tag shares the object with the fields, internally tagged enums
only work with struct-like and unit variants, never newtype variants wrapping a non-struct.

**Adjacent tagging** puts the discriminant and the payload in separate, named keys, which lifts that
restriction:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(tag = "op", content = "arg", rename_all = "camelCase")]
enum Action {
    Deny,
    Audit(String),
    Modify { field: String, to: String },
}

fn main() {
    assert_eq!(serde_json::to_string(&Action::Deny).unwrap(), r#"{"op":"deny"}"#);
    assert_eq!(
        serde_json::to_string(&Action::Audit("log-only".into())).unwrap(),
        r#"{"op":"audit","arg":"log-only"}"#
    );

    let m = Action::Modify { field: "tags.owner".into(), to: "platform".into() };
    assert_eq!(
        serde_json::to_string(&m).unwrap(),
        r#"{"op":"modify","arg":{"field":"tags.owner","to":"platform"}}"#
    );
    assert_eq!(serde_json::from_str::<Action>(r#"{"op":"deny"}"#).unwrap(), Action::Deny);
}
```

**Untagged** has no discriminant at all; serde tries each variant in declaration order and takes the first
that deserializes successfully. It is how you model a field that is "either a string or an object", which
hand-written JSON schemas do constantly:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(untagged)]
enum Target {
    /// Shorthand: just a resource id.
    Id(String),
    /// Long form: a selector object.
    Selector { kind: String, location: Option<String> },
}

fn main() {
    let a: Target = serde_json::from_str(r#""res-1""#).unwrap();
    assert_eq!(a, Target::Id("res-1".into()));

    let b: Target = serde_json::from_str(r#"{"kind":"storage","location":"westus2"}"#).unwrap();
    assert_eq!(b, Target::Selector { kind: "storage".into(), location: Some("westus2".into()) });

    // Serialization emits the payload with no wrapper.
    assert_eq!(serde_json::to_string(&a).unwrap(), r#""res-1""#);
}
```

Untagged is the most ergonomic for hand-written input and the worst for error messages: when nothing
matches, serde can only say "data did not match any variant", because it does not know which variant you
*meant*. Order matters too — put the more specific variants first. Use it for genuinely polymorphic input,
not as a default.

Here is the whole decision in one table:

| Representation | Attribute | JSON for `Equals { field, value }` | Use when |
|---|---|---|---|
| External (default) | — | `{"equals":{"field":..,"value":..}}` | internal formats; you control both ends |
| Internal | `tag = "type"` | `{"type":"equals","field":..,"value":..}` | designing a public API; most natural |
| Adjacent | `tag`, `content` | `{"type":"equals","content":{..}}` | need internal tagging with non-struct variants |
| Untagged | `untagged` | `{"field":..,"value":..}` | shorthand/long-form unions in hand-written input |

Compare this with .NET, where polymorphic JSON needs `[JsonDerivedType]` with a type discriminator, a class
hierarchy, and a runtime type check on the way out. serde's version is a closed set the compiler can check
exhaustively, and switching representations is a one-line attribute change rather than a redesign.

C-like enums without payloads are simpler still — they serialize as strings, and `rename_all` applies:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq, Clone, Copy)]
#[serde(rename_all = "SCREAMING_SNAKE_CASE")]
enum Severity {
    Low,
    Medium,
    High,
}

fn main() {
    assert_eq!(serde_json::to_string(&Severity::High).unwrap(), r#""HIGH""#);
    assert_eq!(serde_json::from_str::<Severity>(r#""LOW""#).unwrap(), Severity::Low);

    // Unknown values are errors, and the message lists what was expected.
    let err = serde_json::from_str::<Severity>(r#""CRITICAL""#).unwrap_err();
    assert!(err.to_string().contains("unknown variant"));
}
```

## Custom conversion

When the wire format does not match your Rust type, `with`, `serialize_with`, and `deserialize_with` let you
supply the conversion. The most common case is a type you do not own, or a representation you do not
control — a number that should be a string, a timestamp in a specific format.

The cleanest approach is a small module exposing `serialize` and `deserialize` functions, referenced by
`#[serde(with = "...")]`:

```rust
use serde::{Deserialize, Deserializer, Serialize, Serializer};

/// Serializes a u64 as a JSON string, because some APIs send big ids as text.
mod string_u64 {
    use super::*;

    pub fn serialize<S: Serializer>(value: &u64, s: S) -> Result<S::Ok, S::Error> {
        s.serialize_str(&value.to_string())
    }

    pub fn deserialize<'de, D: Deserializer<'de>>(d: D) -> Result<u64, D::Error> {
        let text = String::deserialize(d)?;
        text.parse().map_err(serde::de::Error::custom)
    }
}

#[derive(Serialize, Deserialize, Debug, PartialEq)]
struct Event {
    #[serde(with = "string_u64")]
    sequence: u64,
    message: String,
}

fn main() {
    let e = Event { sequence: 9_007_199_254_740_993, message: "scan complete".into() };
    let json = serde_json::to_string(&e).unwrap();
    assert_eq!(json, r#"{"sequence":"9007199254740993","message":"scan complete"}"#);

    let back: Event = serde_json::from_str(&json).unwrap();
    assert_eq!(back, e);
}
```

`serde::de::Error::custom` is how you turn any error into the deserializer's error type — the escape hatch
you will reach for in every custom `deserialize`. This whole pattern is `JsonConverter<T>`, with the
important difference that it is resolved at compile time and attaches to the *field* rather than being
registered globally.

For a type you own, implementing the conversion through `From`/`TryFrom` is usually tidier than a custom
serializer. `#[serde(try_from = "...")]` deserializes into an intermediate type and then converts,
which gives you validation for free:

```rust
use serde::Deserialize;

/// A severity that is always within range, enforced at deserialization time.
/// `try_from` is a *container* attribute: serde deserializes a `u8`, then calls
/// `Severity::try_from` and reports any error through the deserializer.
#[derive(Deserialize, Debug, PartialEq)]
#[serde(try_from = "u8")]
struct Severity(u8);

impl TryFrom<u8> for Severity {
    type Error = String;
    fn try_from(v: u8) -> Result<Self, Self::Error> {
        if (1..=5).contains(&v) {
            Ok(Severity(v))
        } else {
            Err(format!("severity must be 1..=5, got {v}"))
        }
    }
}

#[derive(Deserialize, Debug, PartialEq)]
struct Rule {
    name: String,
    severity: Severity,
}

fn main() {
    let ok: Rule = serde_json::from_str(r#"{"name":"r1","severity":3}"#).unwrap();
    assert_eq!(ok.severity, Severity(3));

    // Out-of-range input fails at the boundary, with your own message.
    let err = serde_json::from_str::<Rule>(r#"{"name":"r1","severity":9}"#).unwrap_err();
    assert!(err.to_string().contains("severity must be 1..=5"));
}
```

Validation now happens at the boundary, once, and every `Severity` in the program is known-good — the
newtype discipline again, this time enforced by the deserializer.

## Zero-copy deserialization

Here is something `System.Text.Json` cannot do at all, and it is one of the reasons serde is fast.

If your struct's fields are `&str` rather than `String`, serde can borrow directly from the input buffer
instead of allocating a new string per field. The lifetime in the struct ties the parsed value to the
source text, and the borrow checker guarantees the buffer outlives it:

```rust
use serde::Deserialize;

/// Every field borrows from the input; deserializing allocates nothing.
#[derive(Deserialize, Debug, PartialEq)]
struct LogLine<'a> {
    level: &'a str,
    message: &'a str,
}

fn main() {
    let text = r#"{"level":"warn","message":"tag missing"}"#;
    let parsed: LogLine<'_> = serde_json::from_str(text).unwrap();

    assert_eq!(parsed.level, "warn");
    assert_eq!(parsed.message, "tag missing");

    // The parsed value genuinely points into `text` — no copies were made.
    assert!(std::ptr::eq(
        parsed.level.as_ptr(),
        text[10..].as_ptr()
    ));
}
```

The constraint is that borrowed fields cannot outlive the buffer, so this works for
parse-process-discard pipelines and not for values you want to store. It also fails when the JSON string
contains escape sequences, since unescaping requires a new allocation — `Cow<'a, str>` is the type that
handles both cases, borrowing when it can and allocating when it must, which is exactly the use case
module 12 introduced.

In .NET the closest equivalent is `Utf8JsonReader` with `ReadOnlySpan<byte>`, but you must hand-write the
reader loop; you cannot get it from a derive.

## Other formats

The payoff for serde's architecture is that everything above transfers unchanged. Swap the format crate and
your types keep working:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "kebab-case")]
struct Settings {
    max_depth: usize,
    fail_fast: bool,
    rule_files: Vec<String>,
}

fn main() {
    let s = Settings {
        max_depth: 8,
        fail_fast: true,
        rule_files: vec!["base.toml".into(), "prod.toml".into()],
    };

    // Same type, different format, zero extra code.
    let as_toml = toml::to_string(&s).unwrap();
    assert!(as_toml.contains("max-depth = 8"));
    assert!(as_toml.contains("fail-fast = true"));

    let back: Settings = toml::from_str(&as_toml).unwrap();
    assert_eq!(back, s);

    let as_json = serde_json::to_string(&s).unwrap();
    let back2: Settings = serde_json::from_str(&as_json).unwrap();
    assert_eq!(back2, s);
}
```

The formats worth knowing: **serde_json** for JSON and the de-facto default; **toml** for configuration and
`Cargo.toml` itself; **serde_yaml** — which you should *not* reach for, as it was deprecated and
unmaintained as of 0.9.34, with `serde_yaml_ng` or `serde_yml` as community continuations; **bincode** for
a compact binary encoding, where version 2 uses `encode_to_vec`/`decode_from_slice` with a `config::standard()`
and makes serde support opt-in; **rmp-serde** for MessagePack; and **ciborium** for CBOR.

A word of caution on the binary formats, since it is a mistake I have watched people make: bincode's
encoding is not self-describing and not versioned, so adding a field breaks every previously written
buffer. It is excellent for a cache you can discard and a poor choice for anything persisted.

## Dynamic JSON

Sometimes you genuinely do not know the shape. `serde_json::Value` is the `JsonNode`/`JObject` equivalent —
an enum over the JSON data model that you can index into and pattern-match:

```rust
use serde_json::{json, Value};

fn main() {
    // The json! macro builds a Value from literal syntax.
    let doc: Value = json!({
        "id": "res-1",
        "tags": { "owner": "platform" },
        "ports": [80, 443]
    });

    // Indexing returns Value::Null for a missing key rather than panicking.
    assert_eq!(doc["id"], json!("res-1"));
    assert_eq!(doc["nope"], Value::Null);

    // Typed accessors return Option, so the failure path is explicit.
    assert_eq!(doc["id"].as_str(), Some("res-1"));
    assert_eq!(doc["ports"][1].as_u64(), Some(443));
    assert_eq!(doc["id"].as_u64(), None);          // wrong type => None

    // Pattern-match the enum when you need to branch on shape.
    let described = match &doc["tags"] {
        Value::Object(map) => format!("object with {} keys", map.len()),
        Value::Array(a) => format!("array of {}", a.len()),
        Value::String(s) => format!("string {s}"),
        Value::Null => "null".to_string(),
        other => format!("scalar {other}"),
    };
    assert_eq!(described, "object with 1 keys");
}
```

`Value` is the right tool for a genuinely dynamic document, a passthrough proxy, or the `flatten` catch-all
you saw earlier. It is the wrong tool for a known schema — if you find yourself writing
`doc["a"]["b"].as_str().unwrap()`, define the struct instead and let serde produce a real error message with
a line number.

## `polcheck`: the rule file

Bringing it together. Here is the rule format for the running example, exercising nearly everything above —
internal tagging for conditions, `rename_all` for a camelCase wire format, defaults for optional settings,
and a recursive enum for nested boolean logic:

```rust
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(tag = "type", rename_all = "camelCase")]
enum Condition {
    /// A field must equal a value.
    Equals { field: String, value: String },
    /// A tag must be present.
    HasTag { name: String },
    /// All nested conditions must hold. Box breaks the infinite size.
    All { conditions: Vec<Condition> },
    /// At least one nested condition must hold.
    Any { conditions: Vec<Condition> },
    /// Inverts a nested condition.
    Not { condition: Box<Condition> },
}

#[derive(Serialize, Deserialize, Debug, PartialEq, Clone, Copy)]
#[serde(rename_all = "lowercase")]
enum Severity { Low, Medium, High }

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Rule {
    name: String,
    #[serde(default = "default_severity")]
    severity: Severity,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    description: Option<String>,
    condition: Condition,
}

fn default_severity() -> Severity { Severity::Medium }

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RuleSet {
    #[serde(default)]
    rules: Vec<Rule>,
}

fn main() {
    let input = r#"
    {
      "rules": [
        {
          "name": "prod-storage-must-be-tagged",
          "severity": "high",
          "condition": {
            "type": "all",
            "conditions": [
              { "type": "equals", "field": "kind", "value": "storage" },
              { "type": "hasTag", "name": "owner" },
              { "type": "not",
                "condition": { "type": "equals", "field": "location", "value": "eastus" } }
            ]
          }
        },
        {
          "name": "needs-env-tag",
          "condition": { "type": "hasTag", "name": "env" }
        }
      ]
    }
    "#;

    let set: RuleSet = serde_json::from_str(input).unwrap();
    assert_eq!(set.rules.len(), 2);

    // The second rule picked up the default severity.
    assert_eq!(set.rules[1].severity, Severity::Medium);
    assert_eq!(set.rules[1].description, None);

    // The nested structure came through as a real, matchable tree.
    match &set.rules[0].condition {
        Condition::All { conditions } => {
            assert_eq!(conditions.len(), 3);
            assert!(matches!(conditions[1], Condition::HasTag { .. }));
            assert!(matches!(conditions[2], Condition::Not { .. }));
        }
        other => panic!("expected All, got {other:?}"),
    }

    // And it round-trips.
    let json = serde_json::to_string(&set).unwrap();
    let back: RuleSet = serde_json::from_str(&json).unwrap();
    assert_eq!(back, set);
}
```

Look at what that bought. Roughly forty lines of type declarations produced a validating parser for a
recursive, polymorphic configuration format, with precise error messages, defaults, strict unknown-field
checking, and a guaranteed round-trip — and the result is a tree the evaluator can `match` on exhaustively.
The `Box<Condition>` in the `Not` variant is the same trick from module 12: recursion needs indirection to
have a finite size, and serde handles boxed fields transparently.

The equivalent in `System.Text.Json` would need a polymorphic type hierarchy with `[JsonDerivedType]`
attributes, a base class, and a runtime type check when evaluating — and the compiler would not tell you
when you forgot to handle a case.

## Before you move on

serde is a data model with pluggable formats, not a JSON library, and that architecture is why one
`#[derive(Serialize, Deserialize)]` works across JSON, TOML, MessagePack, and everything else. The derive
generates code at compile time, so there is no reflection and no AOT story to opt into.

The attributes to know are small in number and large in effect: `rename_all` and `rename` for naming,
`default` and `skip_serializing_if` for optionality, `skip` to exclude a field entirely, `flatten` to inline
a struct or capture unknown keys the way `[JsonExtensionData]` does, and `deny_unknown_fields` to catch
typos in files humans edit. Remember that serde treats a missing field as an error by default — a
deserialized value is complete, which is a real improvement over silently defaulted properties.

Enums are the feature with no .NET equivalent, and the four representations are a genuine design decision:
externally tagged by default, internally tagged (`tag = "..."`) for the most natural-looking public formats,
adjacently tagged when you need a tag with non-struct variants, and untagged for shorthand/long-form unions
at the cost of good error messages. For custom conversions, `with`/`serialize_with`/`deserialize_with`
handle a field-level format mismatch and `try_from` gives you validation at the boundary, both replacing
`JsonConverter<T>` at compile time. Borrowed fields (`&'a str`) give you zero-copy deserialization that
`System.Text.Json` cannot match from a derive, at the cost of tying the value's lifetime to the buffer.

If you can explain why `Option<String>` and `#[serde(default)] String` are different modelling choices, and
why an internally tagged enum cannot have newtype variants, you have the parts you will use daily.

Next: [21 — tokio in practice](21-tokio-in-practice.md).

### Sources

- *Serde* documentation. <https://serde.rs/> — the data model, derive attributes, and format list.
- *Serde field and container attributes*. <https://serde.rs/attributes.html> — the authoritative attribute reference.
- *Serde enum representations*. <https://serde.rs/enum-representations.html> — external, internal, adjacent, and untagged tagging.
- *Serde: understanding deserializer lifetimes*. <https://serde.rs/lifetimes.html> — zero-copy deserialization and the `'de` lifetime.
- `serde_json`. <https://docs.rs/serde_json/1.0/serde_json/> — `Value`, `json!`, and the JSON-specific API.
- `toml`. <https://docs.rs/toml/> — TOML support built on serde.
- `serde_yaml` 0.9.34 deprecation notice. <https://docs.rs/serde_yaml/latest/serde_yaml/> — the crate is unmaintained; prefer a maintained fork.
- Microsoft Learn, "Polymorphic serialization in System.Text.Json". <https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/polymorphism> — the .NET comparison point for tagged unions.
