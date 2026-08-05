# 11 — Error handling

This is the module where the largest single piece of your C# muscle memory has to go. In .NET, failure is
a control-flow event: a method that fails throws, the stack unwinds until someone catches, and the
signature tells you nothing about what might go wrong. In Rust, failure is a **value**: a function that can
fail returns `Result<T, E>`, the caller must do something with it, and the signature is a complete contract.
There is no `throws` clause because there is no throwing — the error type is right there in the return type,
checked by the compiler, impossible to ignore by accident.

That sounds heavier than exceptions and, written badly, it is. Written idiomatically it is lighter, because
the `?` operator collapses propagation to one character and the type system does the work that `catch
(SpecificException)` blocks do by hand.

> **Prerequisite:** [10 — Collections and iterators](10-collections-and-iterators.md).

## Two axes: absence and failure

Rust separates two things C# conflates into `null` and exceptions respectively.

**`Option<T>` models absence.** There is no `null`, so a value that might not be there says so in its type.
This is C# 8's nullable reference types taken to their logical conclusion: not a warning-level annotation
bolted onto a runtime that still permits `null`, but an actual different type that you cannot dereference
without handling the empty case.

**`Result<T, E>` models failure.** An operation that can go wrong returns the success value or an error
value, and both are ordinary data.

```rust,ignore
enum Option<T> { None, Some(T) }
enum Result<T, E> { Ok(T), Err(E) }
```

Both are plain enums from module 07 with no compiler magic. Both are `#[must_use]`, so ignoring one is a
warning — the single most valuable safety property in the whole design, because the C# equivalent (calling
a method and dropping a returned error code) is silent.

The optimiser makes `Option<T>` free in the common cases through *niche optimisation*: since a `&T` can
never be null, `Option<&T>` uses the all-zeros bit pattern for `None` and occupies exactly one pointer.
`Option<Box<T>>`, `Option<NonZeroU32>`, and `Option<String>` are all the same size as their payload.

```rust
use std::mem::size_of;

fn main() {
    assert_eq!(size_of::<Option<&u8>>(), size_of::<&u8>());
    assert_eq!(size_of::<Option<String>>(), size_of::<String>());
    assert_eq!(size_of::<Option<u8>>(), 2);   // no niche in u8, so a discriminant byte is added
}
```

## Working with `Option<T>`

The combinator vocabulary is large; these are the ones that carry the weight.

```rust
use std::collections::HashMap;

fn main() {
    let tags: HashMap<&str, &str> = HashMap::from([("owner", "platform/alice")]);

    // map: transform the value if present.
    let upper: Option<String> = tags.get("owner").map(|s| s.to_uppercase());
    assert_eq!(upper.as_deref(), Some("PLATFORM/ALICE"));

    // and_then: chain another Option-returning operation (LINQ's SelectMany).
    let team: Option<&str> = tags.get("owner").and_then(|s| s.split_once('/')).map(|(t, _)| t);
    assert_eq!(team, Some("platform"));

    // unwrap_or / unwrap_or_else / unwrap_or_default: supply a fallback (?? in C#).
    assert_eq!(tags.get("env").copied().unwrap_or("unset"), "unset");
    assert_eq!(tags.get("env").copied().unwrap_or_default(), "");

    // filter: keep the value only if it passes.
    assert_eq!(tags.get("owner").filter(|s| s.len() > 100), None);

    // ok_or: turn absence into an error, which is how Option feeds into Result.
    let r: Result<&&str, &str> = tags.get("env").ok_or("no env tag");
    assert_eq!(r, Err("no env tag"));

    // is_some_and: predicate without unwrapping.
    assert!(tags.get("owner").is_some_and(|s| s.starts_with("platform")));
}
```

Map the vocabulary onto what you know: `map` is `?.`, `unwrap_or` is `??`, `and_then` is `?.` followed by
another nullable-returning call, `filter` has no C# analogue, and `ok_or` is the bridge from "missing" to
"failed". The crucial difference from `?.` is that the compiler *forces* the final handling — you cannot
forget the `??` and get a `NullReferenceException` three frames later.

`unwrap()` and `expect("message")` extract the value and **panic** if there is none. They are not the enemy
— they are the right tool when absence is genuinely impossible — but they need discipline, covered below.

## `Result<T, E>` and the `?` operator

`Result` has the same combinator surface plus an error side:

```rust
fn parse_port(s: &str) -> Result<u16, String> {
    s.parse::<u16>().map_err(|e| format!("bad port '{s}': {e}"))
}

fn main() {
    assert_eq!(parse_port("8080"), Ok(8080));
    assert!(parse_port("99999").unwrap_err().starts_with("bad port '99999'"));

    // The full toolkit.
    let r: Result<i32, String> = Ok(2);
    assert_eq!(r.clone().map(|n| n * 10), Ok(20));
    assert_eq!(r.clone().and_then(|n| if n > 0 { Ok(n) } else { Err("neg".to_owned()) }), Ok(2));
    assert_eq!(r.clone().unwrap_or(0), 2);
    assert_eq!(r.clone().ok(), Some(2));               // discard the error, get an Option
    assert!(r.is_ok());
}
```

But you will rarely write chains of those, because `?` exists. It is the whole ergonomic story:

```rust
use std::num::ParseIntError;

fn sum_of_parts(input: &str) -> Result<i64, ParseIntError> {
    let mut total = 0i64;
    for part in input.split(',') {
        let n: i64 = part.trim().parse()?;    // on Err, return early with the error
        total += n;
    }
    Ok(total)
}

fn main() {
    assert_eq!(sum_of_parts("1, 2, 3"), Ok(6));
    assert!(sum_of_parts("1, x, 3").is_err());
}
```

`expr?` expands to roughly "if `Ok(v)`, evaluate to `v`; if `Err(e)`, `return Err(From::from(e))`". Three
things follow from that expansion, and all three matter.

**It returns from the enclosing function**, so `?` only works in a function returning `Result` (or
`Option`, or anything implementing `Try`). This is not exception propagation — nothing unwinds, no stack is
captured, and the cost is a branch.

**It applies `From::from` to the error.** That is the hook from module 09 that makes heterogeneous errors
compose: if your function returns `Result<T, MyError>` and you `?` an `io::Error`, the conversion happens
automatically as long as `impl From<io::Error> for MyError` exists. Module 19's `thiserror` derives those
impls, which is why real code is so clean.

**It works on `Option` too**, propagating `None`:

```rust
fn first_word_len(s: &str) -> Option<usize> {
    let word = s.split_whitespace().next()?;   // None short-circuits the function
    Some(word.len())
}

fn main() {
    assert_eq!(first_word_len("hello world"), Some(5));
    assert_eq!(first_word_len("   "), None);
}
```

You cannot mix them in one function — `?` on an `Option` inside a `Result`-returning function is a type
error. The bridges are `.ok_or(err)?` (Option → Result) and `.ok()?` (Result → Option), and you will write
both constantly.

### Side by side

The comparison is worth seeing in full, because the shapes are so close that the difference is easy to
undersell.

```csharp
// C#
public static int ReadCount(string path)
{
    var text = File.ReadAllText(path);      // may throw IOException, UnauthorizedAccess...
    return int.Parse(text.Trim());          // may throw FormatException, OverflowException
}

// Caller has no idea what can go wrong. The compiler is silent.
try { var n = ReadCount("count.txt"); }
catch (Exception ex) { /* which ones? read the docs, or the source */ }
```

```rust
use std::fs;
use std::path::Path;

#[derive(Debug)]
enum ReadCountError {
    Io(std::io::Error),
    Parse(std::num::ParseIntError),
}

impl From<std::io::Error> for ReadCountError {
    fn from(e: std::io::Error) -> Self { ReadCountError::Io(e) }
}
impl From<std::num::ParseIntError> for ReadCountError {
    fn from(e: std::num::ParseIntError) -> Self { ReadCountError::Parse(e) }
}

fn read_count(path: &Path) -> Result<i32, ReadCountError> {
    let text = fs::read_to_string(path)?;    // io::Error -> ReadCountError via From
    let n = text.trim().parse::<i32>()?;     // ParseIntError -> ReadCountError via From
    Ok(n)
}

fn main() {
    // The signature is the documentation: exactly two things can go wrong.
    match read_count(Path::new("definitely-missing.txt")) {
        Ok(n) => println!("{n}"),
        Err(ReadCountError::Io(e)) => println!("io: {e}"),
        Err(ReadCountError::Parse(e)) => println!("parse: {e}"),
    }
}
```

The Rust version is longer — until you replace those two `From` impls with `#[derive(Error)]` and
`#[from]`, at which point it is shorter *and* the caller gets an exhaustive `match` the compiler will
maintain. That is the trade: a little more ceremony at the definition, a lot more certainty at every call
site.

## Panics: the other kind of failure

Rust does have a mechanism that unwinds the stack, and it is deliberately not the error-handling story.

A **panic** signals a bug — a violated invariant, an impossible state, a contract the caller broke. It is
raised by `panic!`, by `unwrap` on `None`/`Err`, by array indexing out of range, by integer division by
zero, by `assert!` failing. By default it unwinds the stack running `Drop` for every live value, prints a
message and (with `RUST_BACKTRACE=1`) a backtrace, and terminates the thread. If the panicking thread is
`main`, the process exits with a non-zero code.

The distinction to hold onto is this. **`Result` is for failures the caller could reasonably anticipate and
handle**: the file was missing, the JSON was malformed, the network timed out. **Panic is for failures that
mean the program's assumptions are wrong**: an index that was proven in range is not, a `HashMap` invariant
was violated, a `match` reached an arm the author documented as unreachable. The C# instinct that maps
closest is the distinction between a `FileNotFoundException` you catch and a `NullReferenceException` you
fix.

You *can* catch a panic with `std::panic::catch_unwind`, and there is exactly one good reason to: stopping
a panic from crossing an FFI boundary or taking down a server's worker thread. Using it as a general
`try/catch` is an anti-pattern — it does not work if the binary is built with `panic = "abort"`, it
requires the closure to be `UnwindSafe`, and it silently converts a bug into a swallowed error.

```rust
fn main() {
    let result = std::panic::catch_unwind(|| {
        let v: Vec<i32> = vec![];
        v[0]                                  // panics
    });
    assert!(result.is_err());
    // Legitimate use: a thread-pool worker that must survive a bad task.
}
```

Release builds can be configured to abort instead of unwinding, which produces smaller and slightly faster
binaries at the cost of destructors not running:

```toml
[profile.release]
panic = "abort"
```

### `unwrap` discipline

`unwrap()` and `expect()` panic on the empty/error case. The community norm, which is worth adopting
wholesale, is this. In **tests, examples, and prototypes**, unwrap freely — a panic is a test failure, which
is exactly what you want. In **library code**, essentially never: return the error and let the caller
decide. In **application code**, use `expect` with a message explaining *why the author believed it could
not fail*, not what went wrong:

```rust
fn main() {
    // Bad: tells you nothing you didn't know from the panic location.
    // let n: i32 = "42".parse().expect("parse failed");

    // Good: documents the invariant.
    let n: i32 = "42".parse().expect("literal is a valid i32");
    assert_eq!(n, 42);

    // Better still where possible: make the invariant unnecessary.
    let m: i32 = "42".parse().unwrap_or(0);
    assert_eq!(m, 42);
}
```

Clippy's `unwrap_used` and `expect_used` lints can enforce this per crate, which is the closest thing to a
Roslyn analyzer rule and is worth turning on in library crates.

## Custom error types

For a library, you want a concrete error enum: it gives callers something to `match` on, it is
`#[non_exhaustive]`-able so you can add variants without a breaking change, and it carries structured data
rather than a string. Written by hand it looks like this:

```rust
use std::error::Error;
use std::fmt;

#[derive(Debug)]
pub enum PolcheckError {
    Io(std::io::Error),
    Json { line: usize, message: String },
    UnknownRule(String),
}

impl fmt::Display for PolcheckError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            PolcheckError::Io(e) => write!(f, "i/o error: {e}"),
            PolcheckError::Json { line, message } => write!(f, "invalid JSON at line {line}: {message}"),
            PolcheckError::UnknownRule(name) => write!(f, "unknown rule '{name}'"),
        }
    }
}

impl Error for PolcheckError {
    fn source(&self) -> Option<&(dyn Error + 'static)> {
        match self {
            PolcheckError::Io(e) => Some(e),
            _ => None,
        }
    }
}

impl From<std::io::Error> for PolcheckError {
    fn from(e: std::io::Error) -> Self { PolcheckError::Io(e) }
}

fn load(path: &str) -> Result<String, PolcheckError> {
    Ok(std::fs::read_to_string(path)?)
}

fn main() {
    let e = load("nope.json").unwrap_err();
    assert!(e.to_string().starts_with("i/o error:"));
    assert!(e.source().is_some());

    let e2 = PolcheckError::UnknownRule("frobnicate".to_owned());
    assert_eq!(e2.to_string(), "unknown rule 'frobnicate'");
    assert!(e2.source().is_none());
}
```

That is fifty lines of boilerplate for three variants, and nobody writes it by hand any more —
`thiserror` (module 19) derives all of it from attributes. But knowing what is being generated matters,
because the pieces are exactly the `Exception` contract you know: `Display` is `Message`, `source()` is
`InnerException`, `Debug` is what the panic handler prints, and `From` is what `?` calls.

### `Box<dyn Error>` for when you do not care

In a binary, or at a boundary where the caller will only log and exit, an enum is over-engineering. The
standard-library shortcut is a boxed trait object:

```rust
use std::error::Error;

fn run() -> Result<i64, Box<dyn Error>> {
    let text = "17";
    let n: i64 = text.parse()?;                 // ParseIntError boxes automatically
    let _ = std::fs::metadata(".")?;            // io::Error boxes too
    Ok(n)
}

fn main() -> Result<(), Box<dyn Error>> {
    assert_eq!(run()?, 17);
    Ok(())
}
```

Two things there. `Box<dyn Error>` works with `?` because `impl<E: Error + 'static> From<E> for Box<dyn
Error>` exists, so any error type converts. And **`main` can return `Result`** — if it returns `Err`, the
runtime prints the `Debug` representation and exits with code 1, which is why binaries often have a
one-line `main` that calls a `run() -> Result<(), Box<dyn Error>>`.

You lose the ability to `match` on specific errors, though `downcast_ref` gives some of it back:

```rust
use std::error::Error;

fn main() {
    let e: Box<dyn Error> = Box::new("17x".parse::<i64>().unwrap_err());
    assert!(e.downcast_ref::<std::num::ParseIntError>().is_some());
    assert!(e.downcast_ref::<std::io::Error>().is_none());
}
```

That is `catch (SpecificException)` reconstructed by hand, and it is a good illustration of what the
type-erased path costs you.

The rule of thumb the ecosystem has settled on, which module 19 makes concrete: **`thiserror` for
libraries, `anyhow` for binaries.** A library owes its callers a typed error they can match on; a binary
owes its user a good message and a stack of context.

## Adding context

The one thing exceptions genuinely do better out of the box is accumulate a stack trace. Rust's answer is
explicit context, added as the error propagates:

```rust
use std::fmt;

#[derive(Debug)]
struct ContextError {
    context: String,
    source: Box<dyn std::error::Error + Send + Sync>,
}

impl fmt::Display for ContextError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.context)
    }
}

impl std::error::Error for ContextError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        Some(self.source.as_ref())
    }
}

fn load(path: &str) -> Result<String, ContextError> {
    std::fs::read_to_string(path).map_err(|e| ContextError {
        context: format!("failed to read rules from '{path}'"),
        source: Box::new(e),
    })
}

/// Walk the chain, exactly like unwrapping InnerException.
fn chain(e: &dyn std::error::Error) -> Vec<String> {
    let mut out = vec![e.to_string()];
    let mut cur = e.source();
    while let Some(s) = cur {
        out.push(s.to_string());
        cur = s.source();
    }
    out
}

fn main() {
    let e = load("nope.json").unwrap_err();
    let c = chain(&e);
    assert_eq!(c[0], "failed to read rules from 'nope.json'");
    assert!(c.len() >= 2);          // the underlying io::Error is the cause
}
```

The output the user sees is a causal chain — "failed to read rules from 'rules.json'" caused by "The system
cannot find the file specified" — which is far more useful than a stack trace through fifteen frames of
framework code. `anyhow`'s `.context("...")` reduces the whole thing above to one method call, and this is
precisely why the ecosystem loves it.

## `polcheck`: errors done properly

Pulling it together for the running example, with a typed library error and a `main` that reports the
chain.

```rust
use std::collections::HashMap;
use std::error::Error;
use std::fmt;

#[derive(Debug)]
pub enum RuleError {
    UnknownOperator(String),
    MissingField { rule: String, field: &'static str },
    TooDeep { depth: usize, max: usize },
}

impl fmt::Display for RuleError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            RuleError::UnknownOperator(op) => write!(f, "unknown operator '{op}'"),
            RuleError::MissingField { rule, field } => {
                write!(f, "rule '{rule}' is missing required field '{field}'")
            }
            RuleError::TooDeep { depth, max } => {
                write!(f, "rule nesting depth {depth} exceeds maximum {max}")
            }
        }
    }
}

impl Error for RuleError {}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Rule {
    RequireTag { key: String },
    Not(Box<Rule>),
}

const MAX_DEPTH: usize = 8;

/// Build a rule from a (operator, argument) pair, validating as we go.
pub fn parse_rule(op: &str, arg: &str, depth: usize) -> Result<Rule, RuleError> {
    if depth > MAX_DEPTH {
        return Err(RuleError::TooDeep { depth, max: MAX_DEPTH });
    }
    match op {
        "require-tag" => {
            if arg.is_empty() {
                return Err(RuleError::MissingField { rule: op.to_owned(), field: "key" });
            }
            Ok(Rule::RequireTag { key: arg.to_owned() })
        }
        "not" => {
            let (inner_op, inner_arg) = arg
                .split_once(':')
                .ok_or_else(|| RuleError::MissingField { rule: op.to_owned(), field: "inner" })?;
            Ok(Rule::Not(Box::new(parse_rule(inner_op, inner_arg, depth + 1)?)))
        }
        other => Err(RuleError::UnknownOperator(other.to_owned())),
    }
}

pub fn evaluate(rule: &Rule, tags: &HashMap<String, String>) -> bool {
    match rule {
        Rule::RequireTag { key } => tags.contains_key(key),
        Rule::Not(inner) => !evaluate(inner, tags),
    }
}

fn main() {
    let tags = HashMap::from([("owner".to_owned(), "platform".to_owned())]);

    let r = parse_rule("require-tag", "owner", 0).unwrap();
    assert!(evaluate(&r, &tags));

    let n = parse_rule("not", "require-tag:env", 0).unwrap();
    assert!(evaluate(&n, &tags));

    // Errors are values you can inspect and match on.
    let e = parse_rule("frobnicate", "x", 0).unwrap_err();
    assert_eq!(e.to_string(), "unknown operator 'frobnicate'");
    assert!(matches!(e, RuleError::UnknownOperator(_)));

    let e = parse_rule("require-tag", "", 0).unwrap_err();
    assert!(matches!(e, RuleError::MissingField { field: "key", .. }));

    let e = parse_rule("require-tag", "x", 99).unwrap_err();
    assert!(matches!(e, RuleError::TooDeep { max: MAX_DEPTH, .. }));
}
```

Notice `ok_or_else` bridging an `Option` into the `Result` chain so `?` can carry it, and the structured
variants carrying data (`depth`, `max`, `field`) rather than pre-formatted strings — which is what lets a
caller *react* to `TooDeep` rather than just print it.

## Before you move on

The shift is from failure-as-control-flow to failure-as-value. `Option<T>` replaces `null` with a type the
compiler makes you open, and niche optimisation means it usually costs nothing. `Result<T, E>` replaces
exceptions with a return value, so a function's signature becomes a complete statement of how it can fail
— the `throws` clause C# never had, enforced rather than documented.

The `?` operator is what makes that practical: it early-returns on error and applies `From::from` on the
way out, which is the hook that lets a dozen different underlying error types funnel into one crate-level
error enum with no conversion code at the call sites. It works on `Option` too, and `.ok_or(...)?` and
`.ok()?` bridge between the two worlds.

Panics are a separate mechanism for a separate purpose: bugs, not anticipated failures. Catching them is
almost always wrong, `unwrap` belongs in tests and in places where you can articulate the invariant, and
`expect` messages should say why you believed it could not fail.

For error types, the ecosystem's settled answer is a typed enum implementing `Error` in libraries (so
callers can match) and a boxed, context-carrying error in binaries (so users get a good message). Both
reduce to a derive macro and a method call once you reach modules 19; what you should carry forward is
*why* the split exists.

If you can explain what `?` desugars to, why `Box<dyn Error>` is fine in `main` but poor in a library API,
and when a panic is more correct than a `Result`, you have the model. Next: what to do when a single owner
is not enough.

Next: [12 — Smart pointers and interior mutability](12-smart-pointers.md).

### Sources

- *The Book*, ch. 9 "Error Handling". <https://doc.rust-lang.org/book/ch09-00-error-handling.html> — panic vs `Result`, the `?` operator, and guidelines for choosing.
- `std::option::Option`. <https://doc.rust-lang.org/std/option/enum.Option.html> — the full combinator list and the niche-optimisation guarantee.
- `std::result::Result`. <https://doc.rust-lang.org/std/result/enum.Result.html> — combinators and the `FromIterator` impls.
- *The Rust Reference*, "The question mark operator". <https://doc.rust-lang.org/reference/expressions/operator-expr.html#the-question-mark-operator> — the normative desugaring, including the `From` conversion.
- `std::error::Error`. <https://doc.rust-lang.org/std/error/trait.Error.html> — `source()`, and the blanket `From<E> for Box<dyn Error>`.
- `std::panic::catch_unwind`. <https://doc.rust-lang.org/std/panic/fn.catch_unwind.html> — the documented caveats about `UnwindSafe` and `panic = "abort"`.
- *Rust API Guidelines*, "Necessities" (C-GOOD-ERR). <https://rust-lang.github.io/api-guidelines/interoperability.html#error-types-are-meaningful-and-well-behaved-c-good-err> — what a well-behaved library error type must do.
