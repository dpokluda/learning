# 12 — Smart pointers and interior mutability

Everything so far has assumed one owner and compile-time-checked borrows. That covers most code, and it is
where you should stay by default. But some shapes genuinely need more: a graph where several nodes point at
the same child, a cache that must be updated through a shared reference, a recursive type whose size the
compiler cannot compute, state shared across threads. Rust's answer is a family of library types — not
language features — that each trade away one guarantee for one capability, at a cost you can see in the
type name.

Coming from C#, where the GC makes every one of these shapes free and invisible, the temptation is to reach
for `Rc<RefCell<T>>` immediately and reconstruct your object graph as-is. Resist it. Most of the time the
right move is to restructure — use indices, use ownership, use a `&mut` parameter — and the shapes that
genuinely need shared mutability are rarer than your instincts suggest.

> **Prerequisite:** [11 — Error handling](11-error-handling.md).

## The lineup

| Type | Gives you | Cost | C# analogue |
|---|---|---|---|
| `Box<T>` | heap allocation, one owner | one indirection | any class instance |
| `Rc<T>` | shared ownership, single thread | non-atomic refcount | a reference |
| `Arc<T>` | shared ownership, across threads | atomic refcount | a reference |
| `Cell<T>` | mutate through `&`, `Copy` types | none | a mutable field |
| `RefCell<T>` | mutate through `&`, runtime-checked | flag check, can panic | a mutable field |
| `Mutex<T>` | exclusive access across threads | lock | `lock` + a field |
| `RwLock<T>` | many readers or one writer | lock | `ReaderWriterLockSlim` |
| `Cow<'_, T>` | borrow until you must own | a branch | (no analogue) |
| `Weak<T>` | non-owning reference, breaks cycles | none | `WeakReference<T>` |

Read that table as a menu of trades. Every row buys a capability by giving something up, and the type name
is the receipt — a reviewer who sees `Arc<Mutex<HashMap<..>>>` knows immediately that this is shared mutable
state across threads and can ask the right questions. In C# the same design is `static Dictionary` plus a
`lock` statement somewhere, and nothing in the type says so.

## `Box<T>`: one owner, on the heap

`Box<T>` is the simplest: it moves a value to the heap and owns it. When the box drops, the value drops.

```rust
fn main() {
    let boxed: Box<i32> = Box::new(5);
    assert_eq!(*boxed, 5);            // deref to read
    assert_eq!(*boxed + 1, 6);
}                                     // heap allocation freed here, deterministically
```

That is a `class` field in C#, except the deallocation is not the GC's decision. Three situations actually
require it.

**Recursive types**, because the compiler must know a type's size and `enum Rule { Not(Rule) }` is
infinite:

```rust,compile_fail
enum Rule {
    RequireTag(String),
    Not(Rule),           // error: recursive type has infinite size
}
```

```rust
enum Rule {
    RequireTag(String),
    Not(Box<Rule>),      // fine: Box is one pointer, size known
}

fn main() {
    let r = Rule::Not(Box::new(Rule::RequireTag("owner".to_owned())));
    assert!(matches!(r, Rule::Not(_)));
}
```

C# never hits this because every class is already a reference; a `class Node { Node Child; }` is one
pointer by construction. Rust makes you say where the indirection is.

**Trait objects**, because `dyn Trait` has no compile-time size:

```rust
trait Reporter { fn render(&self) -> String; }
struct Plain;
impl Reporter for Plain { fn render(&self) -> String { "plain".into() } }

fn main() {
    let r: Box<dyn Reporter> = Box::new(Plain);
    assert_eq!(r.render(), "plain");

    let all: Vec<Box<dyn Reporter>> = vec![Box::new(Plain)];
    assert_eq!(all.len(), 1);
}
```

**Very large values you want to move cheaply**, since moving a `Box` copies eight bytes regardless of what
it points at. This is a real optimisation for big enum variants — clippy's `large_enum_variant` lint will
suggest it.

What `Box` does *not* give you is sharing. One owner, always. If you need two, keep reading.

## `Rc<T>` and `Arc<T>`: shared ownership

`Rc<T>` is a reference-counted pointer: cloning it bumps a counter rather than duplicating the value, and
the value drops when the last `Rc` does. `Arc<T>` is the same with an atomic counter, so it can cross
threads.

```rust
use std::rc::Rc;

#[derive(Debug)]
struct Policy { name: String }

fn main() {
    let policy = Rc::new(Policy { name: "require-owner".to_owned() });
    assert_eq!(Rc::strong_count(&policy), 1);

    let a = Rc::clone(&policy);          // refcount 2 — NOT a deep copy
    let b = policy.clone();              // refcount 3 — same thing, different spelling
    assert_eq!(Rc::strong_count(&policy), 3);

    // All three point at the same allocation.
    assert_eq!(a.name, "require-owner");
    assert!(Rc::ptr_eq(&a, &b));

    drop(a);
    drop(b);
    assert_eq!(Rc::strong_count(&policy), 1);
}
```

`Rc::clone(&x)` and `x.clone()` are identical; the community convention is to write `Rc::clone` at the call
site because it makes "this is a refcount bump, not a deep copy" visible to the reader. Follow it.

This is the closest thing Rust has to a C# reference, and it is worth being precise about the differences.
A C# reference is free to copy, costs nothing to drop, and the GC handles cycles. An `Rc` costs a
non-atomic increment to clone and a decrement plus a branch to drop, and **it leaks on cycles** — there is
no tracing collector to notice that a group of objects points only at each other.

`Arc` is the multithreaded version, and the atomic operations are the reason both exist:

```rust
use std::sync::Arc;
use std::thread;

fn main() {
    let shared = Arc::new(vec![1, 2, 3]);
    let mut handles = Vec::new();

    for i in 0..3 {
        let data = Arc::clone(&shared);        // one clone per thread
        handles.push(thread::spawn(move || data[i] * 10));
    }

    let results: Vec<i32> = handles.into_iter().map(|h| h.join().unwrap()).collect();
    assert_eq!(results, vec![10, 20, 30]);
}
```

Try that with `Rc` and it will not compile: `Rc` is not `Send`, so the closure cannot cross a thread
boundary, and the error message names the type. That single compile error is worth an entire class of .NET
race conditions — in C#, sharing a non-thread-safe object across threads compiles perfectly and fails
occasionally in production.

Critically, `Rc<T>` and `Arc<T>` give **shared, immutable** access. You cannot get a `&mut T` out of one
while other clones exist, because that would be aliasing plus mutation. `Rc::get_mut` returns
`Option<&mut T>` and yields `None` unless the count is exactly one. To mutate shared data you need the next
piece.

## Interior mutability: `Cell` and `RefCell`

The borrowing rules say shared XOR mutable. Interior mutability types keep that guarantee but move the
enforcement from compile time to runtime, which is the only way to express "I have a `&T` and I need to
change it".

**`Cell<T>`** works for `Copy` types by never handing out a reference at all — you get and set whole values:

```rust
use std::cell::Cell;

struct Stats {
    evaluations: Cell<u32>,          // mutable through &self
}

impl Stats {
    fn record(&self) {               // note: &self, not &mut self
        self.evaluations.set(self.evaluations.get() + 1);
    }
}

fn main() {
    let s = Stats { evaluations: Cell::new(0) };
    s.record();
    s.record();
    assert_eq!(s.evaluations.get(), 2);
}
```

No runtime check is needed because no reference ever escapes, so `Cell` cannot panic. It is the right choice
for counters, flags, and small `Copy` state.

**`RefCell<T>`** works for any type by tracking borrow state at runtime:

```rust
use std::cell::RefCell;

fn main() {
    let cache: RefCell<Vec<String>> = RefCell::new(Vec::new());

    cache.borrow_mut().push("a".to_owned());     // runtime-checked &mut
    cache.borrow_mut().push("b".to_owned());
    assert_eq!(cache.borrow().len(), 2);         // runtime-checked &

    // Many shared borrows are fine.
    let r1 = cache.borrow();
    let r2 = cache.borrow();
    assert_eq!(r1.len(), r2.len());
    drop(r1);
    drop(r2);

    // try_borrow_mut lets you check instead of panicking.
    let held = cache.borrow();
    assert!(cache.try_borrow_mut().is_err());
    drop(held);
    assert!(cache.try_borrow_mut().is_ok());
}
```

The rule is unchanged — many readers or one writer — but violating it now **panics at runtime** rather than
failing to compile:

```rust,should_panic
use std::cell::RefCell;

fn main() {
    let c = RefCell::new(5);
    let _a = c.borrow();
    let _b = c.borrow_mut();      // panics: already borrowed
}
```

That is the trade in one snippet, and it is why `RefCell` is a tool of last resort rather than a
convenience. You have taken a class of error the compiler catches and converted it into a class of error
your users find. Reach for it when the sharing is genuinely dynamic — a tree where a node must update its
parent, a memoisation cache behind an `&self` method — and restructure when it is not.

The guards are worth understanding as `Drop` in action: `borrow()` returns a `Ref<T>` and `borrow_mut()`
returns a `RefMut<T>`, each of which releases the flag when dropped. Holding one across a long block or an
`.await` is how you cause the panic above; keeping the borrow scope tight is the discipline.

## `Rc<RefCell<T>>`: the shape, and why to avoid it

Combine shared ownership with interior mutability and you get the pattern that C# gives you by default:

```rust
use std::cell::RefCell;
use std::rc::Rc;

#[derive(Debug)]
struct Counter { hits: u32 }

fn main() {
    let shared = Rc::new(RefCell::new(Counter { hits: 0 }));

    let a = Rc::clone(&shared);
    let b = Rc::clone(&shared);

    a.borrow_mut().hits += 1;
    b.borrow_mut().hits += 10;

    assert_eq!(shared.borrow().hits, 11);
}
```

This works, and it is occasionally right. But `Rc<RefCell<T>>` is the single most over-used pattern among
C# and Java developers learning Rust, because it recreates the mutable object graph they are used to. Before
you write it, try these in order.

**Restructure to single ownership.** Very often the graph you drew has a natural tree, and the "shared"
node can be owned by its parent with the other referents holding a `&`.

**Use indices instead of pointers.** An arena — `Vec<Node>` plus `usize` indices — is the standard Rust
answer to graph structures. It is faster (contiguous memory, no refcount), simpler (no cycles to worry
about), and serialises trivially:

```rust
#[derive(Debug)]
struct Node { value: String, children: Vec<usize> }

#[derive(Debug, Default)]
struct Tree { nodes: Vec<Node> }

impl Tree {
    fn add(&mut self, value: &str, parent: Option<usize>) -> usize {
        let id = self.nodes.len();
        self.nodes.push(Node { value: value.to_owned(), children: Vec::new() });
        if let Some(p) = parent {
            self.nodes[p].children.push(id);
        }
        id
    }

    fn descendants(&self, id: usize) -> Vec<&str> {
        let mut out = Vec::new();
        let mut stack = vec![id];
        while let Some(n) = stack.pop() {
            out.push(self.nodes[n].value.as_str());
            stack.extend(self.nodes[n].children.iter().copied());
        }
        out
    }
}

fn main() {
    let mut t = Tree::default();
    let root = t.add("all", None);
    let a = t.add("require-owner", Some(root));
    let _b = t.add("require-env", Some(root));
    let _c = t.add("not", Some(a));

    assert_eq!(t.descendants(root).len(), 4);
    assert_eq!(t.nodes[root].children.len(), 2);
}
```

That is how `polcheck`'s rule tree would be stored if it needed back-references, and it is how most
production Rust represents graphs. Note that mutation is ordinary `&mut self`, checked at compile time.

**Pass `&mut` down instead of storing a shared handle.** A visitor that needs to accumulate results takes
`&mut Vec<Finding>` as a parameter rather than owning a shared cell.

If none of those work, `Rc<RefCell<T>>` is there and it is not shameful. Just make it a decision rather than
a reflex.

## Cycles and `Weak<T>`

Reference counting cannot reclaim a cycle, so `Rc` leaks where a GC would not. This is the one place where
.NET is unambiguously more forgiving, and Rust's answer is the same as .NET's for a different problem:
a weak reference.

```rust
use std::cell::RefCell;
use std::rc::{Rc, Weak};

struct Node {
    name: String,
    parent: RefCell<Weak<Node>>,      // weak: does not keep the parent alive
    children: RefCell<Vec<Rc<Node>>>, // strong: parent owns children
}

fn main() {
    let root = Rc::new(Node {
        name: "root".to_owned(),
        parent: RefCell::new(Weak::new()),
        children: RefCell::new(Vec::new()),
    });

    let child = Rc::new(Node {
        name: "child".to_owned(),
        parent: RefCell::new(Rc::downgrade(&root)),   // Rc -> Weak
        children: RefCell::new(Vec::new()),
    });
    root.children.borrow_mut().push(Rc::clone(&child));

    // Follow the weak link back up: upgrade returns Option<Rc<T>>.
    let parent = child.parent.borrow().upgrade().expect("root is alive");
    assert_eq!(parent.name, "root");
    // `parent` is a real strong handle, so the count is temporarily 2.
    assert_eq!(Rc::strong_count(&root), 2);
    drop(parent);

    assert_eq!(Rc::strong_count(&root), 1);   // the child's weak link doesn't count
    assert_eq!(Rc::weak_count(&root), 1);

    drop(root);
    // Now the weak link dangles, and upgrade() says so instead of crashing.
    assert!(child.parent.borrow().upgrade().is_none());
}
```

The discipline is simple and worth memorising: **strong references point down the ownership tree, weak
references point back up.** `Rc::downgrade` makes a `Weak`, and `upgrade()` returns `Option<Rc<T>>` —
`None` if the target is gone, which is exactly `WeakReference<T>.TryGetTarget` with a better type.

The important difference from .NET is *why* you use it. In C#, `WeakReference` is for caches you want the
GC to be able to reclaim; cycles are handled automatically and you never think about them. In Rust, `Weak`
is primarily for breaking cycles, and forgetting it is a memory leak — safe Rust, note, since leaking is
not unsound, just wasteful.

## `Mutex<T>` and `RwLock<T>`

The multithreaded members of the family, covered properly in module 15 but belonging structurally here.
Their design is the thing to notice: **the lock owns the data**, so you cannot access the data without
holding the lock.

```rust
use std::sync::{Arc, Mutex, RwLock};
use std::thread;

fn main() {
    let counter = Arc::new(Mutex::new(0u32));
    let mut handles = Vec::new();

    for _ in 0..8 {
        let c = Arc::clone(&counter);
        handles.push(thread::spawn(move || {
            let mut guard = c.lock().unwrap();     // blocks; guard derefs to &mut u32
            *guard += 1;
        }));                                        // guard drops -> unlock
    }
    for h in handles { h.join().unwrap(); }
    assert_eq!(*counter.lock().unwrap(), 8);

    // RwLock: many readers or one writer.
    let cfg = Arc::new(RwLock::new(vec!["westus2".to_owned()]));
    {
        let r1 = cfg.read().unwrap();
        let r2 = cfg.read().unwrap();               // concurrent reads are fine
        assert_eq!(r1.len(), r2.len());
    }
    cfg.write().unwrap().push("eastus".to_owned());
    assert_eq!(cfg.read().unwrap().len(), 2);
}
```

Compare with C#, where `lock (someObject) { ... }` protects a *region of code* and the association with the
data it guards is a convention living in a comment. Forgetting to take the lock on one code path is a
classic .NET bug that no tool catches. In Rust, the data is inside the `Mutex`, so there is no path to it
that skips the lock. The guard's `Drop` releases it, so there is no `finally` to forget and no early return
that leaks the lock.

Two Rust-specific wrinkles. `lock()` returns a `Result` because a mutex becomes **poisoned** if a thread
panics while holding it — the `unwrap()` you see everywhere is propagating that. And `RwLock`'s exact
fairness depends on the OS primitive; `parking_lot` (module 26) offers faster, non-poisoning, smaller
alternatives and is a very common dependency.

## `Cow<'_, T>`: borrow until you must own

Clone-on-write is a type with no C# equivalent, because in C# strings are immutable references and the
question does not arise. It represents "either a borrow or an owned value" and only allocates when
something actually needs to change:

```rust
use std::borrow::Cow;

/// Redact secrets. Most inputs need no change, so most calls allocate nothing.
fn redact(input: &str) -> Cow<'_, str> {
    if input.contains("secret") {
        Cow::Owned(input.replace("secret", "[REDACTED]"))
    } else {
        Cow::Borrowed(input)
    }
}

fn main() {
    let clean = redact("location=westus2");
    assert!(matches!(clean, Cow::Borrowed(_)));      // zero allocations
    assert_eq!(clean, "location=westus2");

    let dirty = redact("token=secret123");
    assert!(matches!(dirty, Cow::Owned(_)));
    assert_eq!(dirty, "token=[REDACTED]123");

    // Cow derefs to the borrowed form, so it reads like a &str.
    assert_eq!(clean.len(), 16);

    // into_owned forces ownership when you need to store it.
    let stored: String = redact("plain").into_owned();
    assert_eq!(stored, "plain");
}
```

The payoff is in hot paths where the common case is "no change needed": a normaliser, an escaper, a
config-expansion step. `serde` uses `Cow<'a, str>` extensively for zero-copy deserialisation, which module
20 revisits. In C# the same optimisation exists but is spelled by returning the input reference unchanged
and relying on immutability — which works precisely because C# has no mutation to worry about.

## `polcheck`: a shared, thread-safe rule cache

Bringing the module together on something realistic: `polcheck` compiles rules once and evaluates them from
several worker threads, keeping a hit counter.

```rust
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::thread;

#[derive(Debug, Clone)]
pub struct Rule {
    pub name: String,
    pub required_tag: String,
}

#[derive(Debug, Clone)]
pub struct Resource {
    pub id: String,
    pub tags: HashMap<String, String>,
}

pub struct Engine {
    /// Immutable after construction: shared with Arc, no lock needed.
    rules: Arc<Vec<Rule>>,
    /// Mutable and shared: Arc for ownership, Mutex for access.
    hits: Arc<Mutex<HashMap<String, u32>>>,
}

impl Engine {
    pub fn new(rules: Vec<Rule>) -> Self {
        Self {
            rules: Arc::new(rules),
            hits: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    pub fn scan(&self, resources: Vec<Resource>) -> usize {
        let chunk = resources.len().div_ceil(4).max(1);
        let mut handles = Vec::new();

        for batch in resources.chunks(chunk) {
            let rules = Arc::clone(&self.rules);          // cheap: refcount bump
            let hits = Arc::clone(&self.hits);
            let batch: Vec<Resource> = batch.to_vec();

            handles.push(thread::spawn(move || {
                let mut local = 0usize;
                for r in &batch {
                    for rule in rules.iter() {
                        if !r.tags.contains_key(&rule.required_tag) {
                            local += 1;
                            // Lock scope kept as small as possible.
                            *hits.lock().unwrap().entry(rule.name.clone()).or_insert(0) += 1;
                        }
                    }
                }
                local
            }));
        }

        handles.into_iter().map(|h| h.join().unwrap()).sum()
    }

    pub fn hits_for(&self, rule: &str) -> u32 {
        self.hits.lock().unwrap().get(rule).copied().unwrap_or(0)
    }
}

fn main() {
    let engine = Engine::new(vec![
        Rule { name: "require-owner".into(), required_tag: "owner".into() },
        Rule { name: "require-env".into(), required_tag: "env".into() },
    ]);

    let resources: Vec<Resource> = (0..8)
        .map(|i| Resource {
            id: format!("res-{i}"),
            tags: if i % 2 == 0 {
                HashMap::from([("owner".to_owned(), "platform".to_owned())])
            } else {
                HashMap::new()
            },
        })
        .collect();

    let total = engine.scan(resources);
    assert_eq!(total, 12);                       // 4 even miss env; 4 odd miss both
    assert_eq!(engine.hits_for("require-env"), 8);
    assert_eq!(engine.hits_for("require-owner"), 4);
}
```

Read the type signatures as documentation. `Arc<Vec<Rule>>` says "shared, never mutated" — no lock, so no
contention. `Arc<Mutex<HashMap<..>>>` says "shared and mutated" — and the reviewer immediately knows to
check the lock scope. In the C# version both would be plain fields and the distinction would live in your
head.

## Before you move on

The family is a menu of trades, each spelled out in the type. `Box` buys heap allocation and a known size
for one owner, which is what makes recursive enums and trait objects possible. `Rc` and `Arc` buy shared
ownership at the cost of a refcount and the possibility of leaking cycles, with `Arc`'s atomics being the
price of crossing threads and `Rc`'s lack of `Send` being the compile error that saves you. `Cell` and
`RefCell` buy mutation through a shared reference by moving the aliasing check from compile time to
runtime, which means `RefCell` can panic where the borrow checker would merely have refused to build.
`Mutex` and `RwLock` do the same across threads, with the crucial design difference from C# that the lock
owns the data rather than guarding a region of code. `Cow` avoids allocating until something actually
changes. `Weak` breaks cycles, pointing back up the ownership tree.

The most valuable habit to build here is resistance. `Rc<RefCell<T>>` faithfully reproduces the C# object
graph and is almost always the wrong first answer; try single ownership, then an index-based arena, then
passing `&mut` down the call chain, and only then reach for shared mutability. When you do reach for it,
you will find the type name has documented the decision for whoever reads the code next.

If you can explain why `Rc<T>` cannot give you a `&mut T`, what makes `RefCell` a runtime rather than
compile-time tool, and why `Mutex<T>` owning its data is better than `lock(obj)` guarding a block, you are
ready for real concurrency.

Next: [13 — Modules, crates, and workspaces](13-modules-and-crates.md).

### Sources

- *The Book*, ch. 15 "Smart Pointers". <https://doc.rust-lang.org/book/ch15-00-smart-pointers.html> — `Box`, `Deref`, `Drop`, `Rc`, `RefCell`, and the reference-cycle discussion.
- `std::rc::Rc`. <https://doc.rust-lang.org/std/rc/struct.Rc.html> — `strong_count`, `downgrade`, `get_mut`, and the explicit warning about cycles leaking.
- `std::sync::Arc`. <https://doc.rust-lang.org/std/sync/struct.Arc.html> — the atomic variant and when the extra cost is warranted.
- `std::cell` module docs. <https://doc.rust-lang.org/std/cell/> — the interior-mutability rationale and the `Cell` / `RefCell` / `OnceCell` comparison.
- `std::sync::Mutex`. <https://doc.rust-lang.org/std/sync/struct.Mutex.html> — guard semantics and lock poisoning.
- `std::borrow::Cow`. <https://doc.rust-lang.org/std/borrow/enum.Cow.html> — clone-on-write and `into_owned`.
- *The Rustonomicon*, "Leaking". <https://doc.rust-lang.org/nomicon/leaking.html> — why leaking is safe, and what that means for `Rc` cycles.
