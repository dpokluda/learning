# 25 — Database access with sqlx

You have three habits from .NET, and each maps onto something different here. EF Core gives you an ORM with
change tracking, migrations, LINQ translated to SQL, and a `DbContext` that is the unit of work. Dapper gives
you a thin mapper: you write SQL, it hands back objects. ADO.NET gives you the raw connection, command, and
reader.

Rust's ecosystem does not have an EF Core, and that is a deliberate cultural position rather than a gap
waiting to be filled. The dominant library, **sqlx**, sits roughly where Dapper does — you write SQL, it maps
rows to structs — with one addition that has no equivalent anywhere in .NET: it can **check your SQL against
a real database at compile time**. A typo in a column name fails `cargo build`, not a request in production.
That single feature is the reason to pay attention to this chapter even if you never write Rust against a
database.

> **Prerequisite:** [21 — tokio in practice](21-tokio-in-practice.md), because sqlx is async to the core.

Everything in this module was verified against sqlx 0.9.0 with an in-memory and a file-backed SQLite
database. The snippets are marked `ignore` in the book's test harness because sqlx is not among the harness's
dependencies, but they were compiled and run in a scratch project first.

## Connecting and querying

`polcheck` gains a findings table so that scan results can be persisted and queried later. The setup is a
pool, which you should think of exactly as you think of ADO.NET connection pooling — except that here the
pool is an explicit object you create and pass around rather than an invisible property of the connection
string.

```toml
[dependencies]
sqlx = { version = "0.9", features = ["runtime-tokio", "sqlite", "macros", "migrate", "chrono"] }
tokio = { version = "1", features = ["full"] }
```

Note how much of sqlx is feature-gated. You choose the async runtime (`runtime-tokio`), the database drivers
(`sqlite`, `postgres`, `mysql`), the TLS backend, and the type integrations (`chrono`, `uuid`, `json`)
individually. This is the crate that most rewards reading the feature list before adding it, and it is the
opposite of the .NET model where `Microsoft.Data.SqlClient` brings everything.

```rust,ignore
use sqlx::{sqlite::SqlitePoolOptions, FromRow, Row, SqlitePool};

#[derive(Debug, FromRow, PartialEq)]
struct Finding {
    id: i64,
    resource_id: String,
    rule: String,
    severity: i64,
}

async fn setup() -> Result<SqlitePool, sqlx::Error> {
    let pool = SqlitePoolOptions::new()
        .max_connections(4)
        .connect("sqlite::memory:")
        .await?;

    sqlx::query(
        "CREATE TABLE findings (
            id INTEGER PRIMARY KEY,
            resource_id TEXT NOT NULL,
            rule TEXT NOT NULL,
            severity INTEGER NOT NULL)",
    )
    .execute(&pool)
    .await?;

    Ok(pool)
}
```

`#[derive(FromRow)]` is the Dapper mapping step made explicit: it generates code that reads each field out of
a row by column name. Dapper does the same by reflection at runtime; sqlx does it by codegen at compile time,
which is faster and — more usefully — means a mismatch between struct and query can be caught statically once
you use the checked macros.

Writing and reading looks like this. Note that `?` is SQLite's placeholder; PostgreSQL uses `$1`, `$2`, and
MySQL uses `?` — sqlx does not paper over the difference, which is a mild annoyance in exchange for never
guessing what your driver actually sends.

```rust,ignore
// Parameters are always bound, never interpolated. There is no string-concat
// path here, so SQL injection is essentially designed out.
let res = sqlx::query("INSERT INTO findings (resource_id, rule, severity) VALUES (?, ?, ?)")
    .bind("res-1")
    .bind("require-owner")
    .bind(3i64)
    .execute(&pool)
    .await?;

assert_eq!(res.rows_affected(), 1);
let new_id = res.last_insert_rowid();

// Map straight onto the struct — the Dapper `Query<T>` analogue.
let rows: Vec<Finding> = sqlx::query_as(
    "SELECT id, resource_id, rule, severity FROM findings ORDER BY id",
)
.fetch_all(&pool)
.await?;

// `fetch_optional` is `QuerySingleOrDefault` without the null ambiguity.
let one: Option<Finding> = sqlx::query_as(
    "SELECT id, resource_id, rule, severity FROM findings WHERE resource_id = ?",
)
.bind("res-2")
.fetch_optional(&pool)
.await?;
```

The four terminal methods are worth memorising because they encode the cardinality you expect, and the
compiler then holds you to it. `fetch_one` returns `T` and errors if there is no row; `fetch_optional`
returns `Option<T>`; `fetch_all` returns `Vec<T>`; and `fetch` returns a `Stream` you can consume row by row
without buffering. That last one is the analogue of `DbDataReader` and matters when a result set is larger
than memory:

```rust,ignore
use futures_util::StreamExt;
use sqlx::Executor;

let mut total = 0i64;
let mut stream = pool.fetch("SELECT severity FROM findings");
while let Some(row) = stream.next().await {
    total += row?.get::<i64, _>("severity");
}
```

Compare that with EF Core's `AsAsyncEnumerable()`. The shapes are similar; the difference is that here
streaming is the default capability and buffering is what you opt into, whereas EF materialises by default.

When you do not have a struct — an aggregate, an ad-hoc projection — you can read columns off the row
dynamically, which is the `IDataRecord` layer:

```rust,ignore
let row = sqlx::query("SELECT COUNT(*) AS n, MAX(severity) AS m FROM findings")
    .fetch_one(&pool)
    .await?;

let n: i64 = row.get("n");          // panics on a wrong name or type
let m: i64 = row.try_get("m")?;     // returns Result instead
```

`get` versus `try_get` is the same `unwrap`-versus-`?` discipline from module 09, applied to column access.
Prefer `try_get` anywhere the schema might drift.

## Transactions

Transactions are where Rust's ownership model quietly earns its keep.

```rust,ignore
let mut tx = pool.begin().await?;

sqlx::query("INSERT INTO findings (resource_id, rule, severity) VALUES (?,?,?)")
    .bind("res-3")
    .bind("require-tag")
    .bind(9i64)
    .execute(&mut *tx)      // note: reborrow the transaction as an executor
    .await?;

tx.commit().await?;         // or tx.rollback().await?;
```

The `&mut *tx` is unusual enough to explain: `Transaction` derefs to a connection, and the executor methods
want `&mut` access to that connection, so you dereference and re-borrow. You will write this many times and
it stops looking strange quickly.

The genuinely interesting part is what happens when you *don't* commit. `Transaction` implements `Drop`, and
dropping it rolls back. Because Rust guarantees `Drop` runs when the value goes out of scope — including on
an early `?` return — a transaction you forget to commit cannot silently leak. This is the same guarantee
`using (var tx = conn.BeginTransaction())` gives you in C#, except that in C# you have to remember the
`using`. Here the guarantee is unconditional. I verified the rollback path: inserting inside a transaction
and then rolling back leaves the row count unchanged.

The one caveat, which module 12 already warned you about, is that `Drop` cannot be async. The rollback on
drop is therefore a best-effort synchronous operation, and the sqlx documentation is explicit that you should
prefer an explicit `commit` or `rollback` when you care about the outcome. This is the async-`Drop` problem
that .NET solved with `IAsyncDisposable` and `await using`, and Rust has not yet.

## Compile-time checked queries

Now the headline. Swap `sqlx::query` for the `sqlx::query!` **macro** and sqlx connects to a real database
*during compilation*, prepares your statement, and verifies it:

```rust,ignore
let min = 1i64;

let rows = sqlx::query_as!(
    Finding,
    "SELECT id, resource_id, rule, severity FROM findings WHERE severity >= ?",
    min
)
.fetch_all(&pool)
.await?;

// `query!` without a struct generates an anonymous record with typed fields.
let rec = sqlx::query!("SELECT COUNT(*) AS n FROM findings")
    .fetch_one(&pool)
    .await?;
let count: i64 = rec.n;
```

What that buys you is best shown by breaking it. Misspell a column and this is what `cargo build` prints —
this is real output, not a paraphrase:

```text
error: error returned from database: (code: 1) no such column: resource_idd
 --> src/bin/badcol.rs:5:13
  |
5 |     let r = sqlx::query!("SELECT resource_idd FROM findings").fetch_all(&pool).await?;
  |             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
```

A database error, delivered by the compiler, with a span pointing at the offending SQL. Sit with that for a
moment, because nothing in the .NET ecosystem does it. EF Core catches schema mismatches at compile time only
because LINQ is typed — but LINQ cannot express everything SQL can, and the moment you drop to
`FromSqlRaw` you are back to runtime errors. Dapper is *always* runtime. sqlx gives you raw SQL and static
checking at the same time, which is a combination that genuinely did not exist before.

The mechanism is what the macro needs from you: a `DATABASE_URL` environment variable pointing at a database
with the right schema, available at build time. Omit it and the build fails with a message that tells you
your two options:

```text
error: set `DATABASE_URL` to use query macros online, or run `cargo sqlx prepare` to update the query cache
```

That second option is how this works in CI and in offline builds. Running `cargo sqlx prepare` (from the
`sqlx-cli` tool) queries the database once and writes the results into a `.sqlx/` directory that you commit
to source control. Subsequent builds read the cached metadata instead of connecting, so CI needs no database
at all — and a schema change that invalidates a query is caught the moment someone regenerates the cache.

The honest tradeoffs are real and you should weigh them. Compile times increase, because every macro invokes
a database round-trip or a cache lookup. Your build acquires a dependency on either a live database or a
committed `.sqlx/` directory that can go stale. Dynamic SQL — a `WHERE` clause assembled from optional
filters — cannot be checked, because the macro needs a literal string; for that you fall back to the
unchecked `query_as` functions, or use `QueryBuilder` for safe dynamic construction. And the checking is only
as good as the database you point it at, so a dev database that has drifted from production will happily
certify a query that fails in production.

My recommendation is to use the macros for the queries you have, keep `.sqlx/` committed, and accept the
unchecked functions where dynamism demands it. The compile-time guarantee is worth more than the
inconvenience.

## Migrations

sqlx ships migrations, and they are refreshingly boring: numbered `.sql` files in a `migrations/`
directory, applied in order, tracked in a `_sqlx_migrations` table.

```text
migrations/
  20250101000000_create_findings.sql
  20250102000000_add_scanned_at.sql
```

```rust,ignore
// Embeds the migration files into the binary at compile time and runs them.
sqlx::migrate!("./migrations").run(&pool).await?;
```

The contrast with EF Core is stark, and which side you prefer says something about your taste. EF Core
generates migrations from model diffs — you change the C# class, run `Add-Migration`, and get generated
`Up`/`Down` methods. sqlx does none of that: you write the SQL. There is no model to diff against, because
sqlx has no model; your structs are query results, not entity definitions.

For a team that lives in EF Core's scaffolding this feels like a step backwards. For a team that has ever
fought a generated migration that dropped and recreated a column, hand-written SQL feels like relief. Neither
view is wrong; know which one you hold before choosing.

## Errors

`sqlx::Error` is a typed enum, so you can distinguish "no rows" from "connection died" from "unique
constraint violated" by matching rather than by parsing a message:

```rust,ignore
match sqlx::query_as::<_, Finding>("SELECT ... WHERE id = ?")
    .bind(id)
    .fetch_one(&pool)
    .await
{
    Ok(f) => Ok(f),
    Err(sqlx::Error::RowNotFound) => Err(AppError::NotFound(id)),
    Err(e) if e.as_database_error().map_or(false, |d| d.is_unique_violation()) => {
        Err(AppError::Duplicate)
    }
    Err(e) => Err(AppError::Db(e)),
}
```

`as_database_error()` gives you the driver-specific detail — SQLSTATE code, constraint name — behind a
common trait, with helpers like `is_unique_violation()` so you rarely need the raw code. Compare this with
catching `SqlException` and switching on `ex.Number`, and the difference is that here the exhaustiveness is
checked and the "I forgot a case" path is a compiler warning rather than a production incident.

## The wider landscape

sqlx is not the only option, and the alternatives correspond to positions you already recognise.
**SeaORM** is the closest thing to EF Core — entities, relations, a query builder, generated migrations —
built on top of sqlx. **Diesel** takes the opposite approach: a fully typed DSL with a schema generated into
Rust, so queries are checked by the type system with no database needed at build time, at the cost of a
steeper learning curve and a DSL that is not SQL. Diesel is synchronous by design, though `diesel-async`
exists.

| | sqlx | Diesel | SeaORM | .NET analogue |
|---|---|---|---|---|
| You write | SQL | a typed DSL | entities + builder | Dapper / LINQ / EF |
| Checking | compile-time, via a real DB | compile-time, via types | runtime | runtime / compile-time |
| Async | native | via `diesel-async` | native | native |
| Migrations | hand-written SQL | hand-written SQL | generated + hand | generated |
| Learning curve | low | high | medium | — |

For most services, and for `polcheck`, sqlx is the right default: it is the least magic, the SQL is the SQL
you would have written anyway, and the compile-time checking removes the main reason people reach for an ORM
in the first place.

## Before you move on

The ecosystem's centre of gravity is Dapper-shaped, not EF-shaped: you write SQL, `#[derive(FromRow)]` maps
rows onto structs, and there is no change tracking, no unit of work, and no LINQ provider. `query_as` plus
`fetch_one`/`fetch_optional`/`fetch_all`/`fetch` covers the cardinalities, with `fetch` streaming rather than
buffering. Parameters are always bound, so injection is designed out rather than guarded against.

Transactions come from `pool.begin()`, take `&mut *tx` as their executor, and roll back on `Drop` — a
stronger guarantee than `using` because you cannot forget it — though `Drop` cannot be async, so an explicit
`commit` or `rollback` is still the right habit.

The feature with no .NET equivalent is the `query!` family, which validates your SQL against a real database
during compilation and reports a bad column as a build error with a span. It needs `DATABASE_URL` at build
time, or a committed `.sqlx/` cache produced by `cargo sqlx prepare` for offline and CI builds. The price is
build-time coupling and the inability to check dynamically-assembled SQL, and for those cases you drop back
to the unchecked functions or `QueryBuilder`.

Migrations are hand-written numbered `.sql` files run by `sqlx::migrate!`, with none of EF Core's model
diffing — a loss if you like scaffolding and a relief if you have been burned by it. Errors are a typed enum,
so `RowNotFound` and `is_unique_violation()` are matched rather than parsed out of a message.

If you can explain what `cargo sqlx prepare` writes and why CI needs it, and why a `Transaction` that is
dropped without `commit` is safer than a C# transaction without a `using`, you have the shape of this crate.

Next: [26 — A field guide to the crates worth knowing](26-crate-field-guide.md).

### Sources

- `sqlx`. <https://docs.rs/sqlx/0.9/sqlx/> — pools, `query`/`query_as`, `FromRow`, executors, transactions. Behaviour verified against 0.9.0 with SQLite.
- `sqlx::query!`. <https://docs.rs/sqlx/0.9/sqlx/macro.query.html> — compile-time verification, `DATABASE_URL`, and the offline cache.
- sqlx README and offline mode. <https://github.com/launchbadge/sqlx#compile-time-verification> — `cargo sqlx prepare` and the `.sqlx` directory.
- `sqlx::Error` and `DatabaseError`. <https://docs.rs/sqlx/0.9/sqlx/enum.Error.html> — `RowNotFound`, `as_database_error`, `is_unique_violation`.
- Diesel. <https://diesel.rs/> — the typed-DSL alternative.
- SeaORM. <https://www.sea-ql.org/SeaORM/> — the EF-Core-shaped alternative built on sqlx.
- Microsoft Learn, EF Core. <https://learn.microsoft.com/ef/core/> — the comparison point for migrations and change tracking.
