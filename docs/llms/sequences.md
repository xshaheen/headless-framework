---
domain: Sequences
packages: Sequences.Abstractions, Sequences, Sequences.PostgreSql, Sequences.SqlServer, Sequences.Sqlite
---

# Sequences

> Tenant-scoped, named, monotonic counters in a relational table. The fast mode takes a number in its own short transaction. The gap-free mode takes it inside the caller's unit of work, so a rollback returns the number. Document numbers (`REC-2026-000042`) add a template and a yearly, monthly, daily, or fiscal-year reset. The reported mode follows numbers a device sends, as a replay guard.

## Orientation

Register once with `AddHeadlessSequences` and exactly one provider:

```csharp
builder.Services.AddUnitOfWork(); // the gap-free mode needs a unit of work on the same database
builder.Services.AddHeadlessSequences(setup =>
{
    setup.UsePostgreSql(connectionString); // or setup.UseSqlServer(connectionString)
    setup.Policy("invoice", new SequencePolicy { Mode = SequenceMode.GapFree });
    setup.Policy("batch", new SequencePolicy { Start = 1000, Step = 10 });
});
```

A counter is identified by the current tenant (`ICurrentTenant.Id`), a name, and an optional partition. Each counter name has one mode:

- **Fast** (the default): inject `ISequenceGenerator` and call `NextAsync` or `ReserveAsync`. The number is committed in its own transaction before it returns. If the caller's own transaction later rolls back, that number is simply never used, which leaves a gap.
- **Gap-free**: register the name with `Mode = SequenceMode.GapFree` and call `unit.Sequences.NextAsync` on the unit of work that writes the number. The increment runs in the unit's transaction and holds the counter's row lock until the unit ends. A rolled-back unit returns its number to the next caller.
- **Reported**: register the name with `Mode = SequenceMode.Reported` and call `unit.Sequences.AdvanceToAsync(name, value, partition)` with the number a device stamped on its request. The counter keeps the highest value accepted and reports each new one as `Next`, `Skipped` (a gap), or `Stale` (a replay). See [Reported counters](#reported-counters).

```csharp
// Fast: receipts, batch ids, anything where a skipped number is acceptable.
long receipt = await sequences.NextAsync("receipt", partition: "2026", ct);
SequenceRange block = await sequences.ReserveAsync("batch", count: 50, cancellationToken: ct);
foreach (var id in block) { /* block.First .. block.Last, step block.Step */ }

// Gap-free: numbers a regulator audits. Take the number late in the unit.
await unitOfWorkFactory.RunAsync(
    db, // a DbContext through Headless.UnitOfWork.EntityFramework, or a DbConnection
    async (unit, ct) =>
    {
        // ... business writes ...
        invoice.Number = await unit.Sequences.NextAsync("invoice", partition: "2026", ct);
    },
    cancellationToken: ct
);
```

`NextAsync` returns a `long`, and the caller chooses the partition (pass the year for a counter that restarts each year). For a document number, give the policy a `Format` and a `Reset` and call `NextNumberAsync` instead: the period picks the partition and the template formats the text. See [Document numbers](#document-numbers).

```csharp
setup.Policy("receipt", new SequencePolicy
{
    Mode = SequenceMode.GapFree,
    Format = "REC-{yyyy}-{seq:D6}",
    Reset = SequenceReset.Year,
    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"),
});

SequenceNumber receipt = await unit.Sequences.NextNumberAsync("receipt", ct); // receipt.Text == "REC-2026-000042"
```

## Agent Rules

- Choose the mode per name, and use only that name's entry point. A gap-free name called through `ISequenceGenerator`, or a fast name called through `unit.Sequences`, throws `InvalidOperationException` before any SQL runs. Mixing the two would let a fast caller's rollback leave a gap in a gap-free counter, or make a flow wait on its own row lock.
- No `Headless.UnitOfWork.Analyzers` rule covers sequences. The name's configured mode, not the call site, decides which entry point is right, and a call through the wrong one already throws on its first run, so a call-site rule could only report correct code.
- Use the gap-free mode only for numbers that must have no gaps. It serializes every writer of one counter until each writer's unit commits or rolls back, so its throughput is bounded by transaction length. Take the number as late in the unit as possible.
- When one unit takes several gap-free counters, take them in a fixed order everywhere. Two units taking the same counters in opposite orders deadlock. The gap-free mode never retries inside the caller's transaction, so the unit's owner receives the deadlock.
- Calls on one unit must be sequential. Do not issue two `unit.Sequences` calls on the same unit concurrently (`Task.WhenAll`): they share one connection, which neither Npgsql nor SqlClient allows.
- Run gap-free units at READ COMMITTED, or at READ COMMITTED SNAPSHOT on SQL Server. Stricter isolation fails when another transaction has updated the counter: PostgreSQL REPEATABLE READ or SERIALIZABLE raises `40001`, and SQL Server SNAPSHOT raises update conflict 3960. The error propagates unwrapped, and the unit's owner handles it; `RunAsync(db, …)` under a retrying EF strategy treats it as transient.
- Gap-free calls require a unit on the same database the provider is configured for, with a transaction from that provider. Any other unit is refused before a statement runs.
- In an observed-mode unit (a gap-free call from a domain-event handler during the EF save pipeline's own save), the call marks the unit non-retryable, as `unit.Outbox` and `unit.Jobs` do. A replay would restore a tracked entity carrying a number that the rolled-back counter hands out again. Inside your own `RunAsync(db, …)` block the unit stays replayable, because the replay takes the number again.
- A fast call cancelled or cut off during its commit may still have consumed a number. Treat that as an ordinary fast-mode gap.
- The tenant is read from `ICurrentTenant` on every call. There is no tenant argument; host code numbering on behalf of a tenant switches with `ICurrentTenant.Change(...)`. With no tenant (`Id` is `null`), calls use the host counter. An empty or whitespace tenant id is refused, so it can never fall into the host counter.
- Names and partitions compare ordinally and case-sensitively: `"INV"` and `"inv"` are two counters. A name may be at most 128 characters, a partition at most 64, and a tenant id at most 128 (`SequenceFieldLimits`). Longer values, a blank name, a whitespace-only partition, and any key part that starts or ends with whitespace throw `ArgumentException` before any SQL runs. The whitespace rule exists because SQL Server ignores trailing spaces when comparing keys, so `"acme"` and `"acme "` would otherwise share one counter there. A key part with a NUL character or an unpaired UTF-16 surrogate throws the same way: PostgreSQL cannot store NUL, and SqlClient sends a lone surrogate as U+FFFD, so SQL Server would share one counter between keys that differ only in which lone surrogate they carry (`Argument.IsPortableKey`). A `null` or empty partition means no partition.
- The table is created at startup by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). With `InitializeOnStartup = false` the application owns the table. A call against a missing table fails, and on PostgreSQL that failure aborts the caller's unit.
- There is no reset, set, peek, or delete API. Start a new period with a new partition.
- On SQL Server, the first call on a new counter takes a key-range lock on the gap in the primary key that holds the new key. In gap-free mode that lock is held until the unit commits, so first use of any other new counter whose key sorts into the same gap waits, for any tenant and from either mode. Two units that each first-use several new counters can deadlock on those ranges. Keep a unit that takes a counter's first number short. PostgreSQL locks only the conflicting key.

## Core Concepts

### Counter key and policy

The stored key is (tenant id, name, partition). The host tenant and "no partition" are both stored as `''`. A `SequencePolicy` sets `Start` (default 1), `Step` (greater than 0, default 1) and `Mode` (default `Fast`). `setup.DefaultPolicy(...)` applies to every name without its own `setup.Policy(name, ...)`.

`Start` is read only when a key's row is created, so changing it never moves an existing counter. A changed `Step` applies from the next call, and the existing counter then mixes both steps.

### Document numbers

`ISequenceGenerator.NextNumberAsync(name)` (fast) and `unit.Sequences.NextNumberAsync(name)` (gap-free) return a `SequenceNumber(Value, Partition, IssuedOn, Text)`, whose `ToString()` is `Text`. The policy decides the rest:

| Policy member | Default | Effect |
| --- | --- | --- |
| `Format` | `null` (the bare value) | Template. `{seq}` is the value and `{seq:D6}` pads it to six digits (widths 1 to 19). `{yyyy}`, `{yy}`, `{MM}`, `{dd}` are the issue date. `{fy}` is the year the fiscal year starts in. `{{` and `}}` write a brace. An unknown token fails options validation at startup. |
| `Reset` | `Never` | `Year`, `Month`, `Day`, or `FiscalYear`. Each period is its own partition (`2026`, `2026-10`, `2026-10-08`, `FY2026`), so the counter starts again from `Start` in each one. |
| `FiscalYearStartMonth` | 1 | The month (1 to 12) the fiscal year starts in, for `Reset = FiscalYear` and `{fy}`. With 7, 8 October 2026 is in `FY2026` and 8 March 2027 too. |
| `TimeZone` | UTC | The zone the issue date and period are decided in, so a daily counter starts again at the business's local midnight. |

The date comes from the registered `TimeProvider`, converted into the policy's zone. In the gap-free mode, a retry whose first attempt rolled back takes the same number again. A retry that replays a stored response through `Headless.Api.Idempotency` returns the number the first attempt committed. Voiding a document is the application's record: the number stays used.

### Reported counters

A device that numbers its own requests, such as a payment terminal's transaction counter, gives a second line of defense behind an idempotency key. When the terminal restarts and loses its keys, it sends a fresh key for a transaction it already sent, and only the device number shows the replay. Register the name with `Mode = SequenceMode.Reported`, and call `unit.Sequences.AdvanceToAsync(name, value, partition: terminalId)` in the unit that records the transaction:

```csharp
var advance = await unit.Sequences.AdvanceToAsync("pos-terminal", request.SequenceNumber, request.TerminalId, ct);

if (advance.Status == SequenceAdvanceStatus.Stale)
{
    return Results.Conflict(); // the same physical transaction may already be recorded
}

if (advance.Status == SequenceAdvanceStatus.Skipped)
{
    logger.LogWarning("Terminal {Terminal} skipped from {Previous} to {Value}", request.TerminalId, advance.Previous, request.SequenceNumber);
    // or throw here to refuse the gap and roll the unit back
}
```

The counter stores the highest value accepted, starting one `Step` below `Start`, so the first expected value is `Start`. `Next` means the value was the next one expected. `Skipped` means it jumped ahead: the counter moves to it, and the caller decides whether a gap only alerts or refuses the request. `Stale` means it was at or below the stored value: nothing changes. `Previous` is the value accepted before, or `null` for a counter that had accepted none. The counter's row stays locked until the unit ends, so concurrent requests from one device run one at a time, and a rollback leaves the counter where it was, so the device can send the same value again. A reported name refuses `NextAsync`, `NextNumberAsync`, and `ReserveAsync`, and a non-reported name refuses `AdvanceToAsync`.

### Ranges

`ReserveAsync(name, count)` atomically advances the counter by `count * step` and returns a `SequenceRange` with `First`, `Count`, `Step` and `Last`. Enumerating it yields `First, First + Step, …, Last` without allocating. `count` must be at least 1. A `count * step` that cannot fit in a `long` throws `OverflowException` before the database is called. Reserve is fast-mode only.

### Overflow

A counter whose stored value would pass `long.MaxValue` fails with the provider's arithmetic error (PostgreSQL `22003`, SQL Server 8115). No typed exhaustion error exists.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Sequences.PostgreSql` | The application's units of work run on PostgreSQL | Units run on SQL Server | One `INSERT … ON CONFLICT DO UPDATE` per call, locking only the counter's key |
| `Headless.Sequences.SqlServer` | The application's units of work run on SQL Server | Many new counters are first used concurrently in long gap-free units | First use of a key takes a key-range lock on its gap in the primary key |
| `Headless.Sequences.Sqlite` | Embedded or single-host applications whose units run on a SQLite file | Many processes or threads number concurrently | Every call, fast or gap-free, holds the database write lock for its whole transaction, so all writers to the file serialize |

The provider must sit on the database the gap-free units run on. The counters are rows in that database, written inside the unit's transaction.

---

## Headless.Sequences.Abstractions

Consumer contracts: `ISequenceGenerator`, `SequenceRange`, `SequenceNumber`, `SequenceAdvance`, `SequenceAdvanceStatus`, `SequenceMode`, and the `unit.Sequences` accessor on `IUnitOfWork`.

### Setup

```bash
dotnet add package Headless.Sequences.Abstractions
```

Reference it from code that takes numbers. Registration lives in the Core and provider packages.

### Design and runtime behavior

- `ISequenceGenerator.NextAsync(name, partition, ct)` returns the counter's next value. `ReserveAsync(name, count, partition, ct)` returns a consecutive `SequenceRange`. `NextNumberAsync(name, ct)` returns a `SequenceNumber`. All three are fast-mode only.
- `unit.Sequences` (namespace `Headless.UnitOfWork`) returns a facade bound to the unit, and repeated reads on one unit return the same instance. Its `NextAsync(name, partition, ct)` and `NextNumberAsync(name, ct)` are gap-free only. Its `AdvanceToAsync(name, value, partition, ct)` → `SequenceAdvance(Status, Previous)` is reported-only.
- `unit.Sequences` throws `InvalidOperationException` naming `AddHeadlessSequences` when no provider registered the feature.

---

## Headless.Sequences

Registration, policies, key resolution, and the provider seam.

### Setup

```bash
dotnet add package Headless.Sequences
```

Applications reach it through a provider package; call `AddHeadlessSequences` as shown in [Orientation](#orientation).

### Configuration

| Builder member | Effect |
| --- | --- |
| `Policy(name, SequencePolicy)` | Sets one name's start, step, mode, and document-number format, reset, fiscal-year start month, and time zone |
| `DefaultPolicy(SequencePolicy)` | Sets the policy of every unregistered name (default: fast, start 1, step 1) |
| `ConfigureOptions(Action<SequencesOptions>)` | Mutates `SequencesOptions` (`DefaultPolicy`, `Policies`) directly |

A policy with `Step` of 0 or less, an unknown `Format` token or unbalanced brace, or a `FiscalYearStartMonth` outside 1 to 12 fails options validation at startup.

### Design and runtime behavior

- `AddHeadlessSequences` requires exactly one `Use…` provider call and throws when there are none or several, or when it is called twice.
- It registers `ISequenceGenerator` and the gap-free unit-of-work feature as singletons, and `TimeProvider.System` unless the host registered a `TimeProvider`. It also registers the async-local `CurrentTenant` as the `ICurrentTenant` fallback, so `ICurrentTenant.Change(...)` works with no other tenancy registration. A real tenancy registration wins.
- The gap-free call checks, in order: the arguments, the name's mode, the unit's state and relational resource, then the provider's transaction type and database. Only after every check passes does it mark an observed unit non-retryable and run the increment. A refused call leaves the unit retryable and runs no statement.
- `ISequenceStore` is the provider seam. Applications do not call it. A reported advance creates the counter row if it is absent, then locks it and moves it forward with a fenced update, both in the caller's transaction, so the value read as `Previous` is the one the lock protects.

---

## Headless.Sequences.PostgreSql

PostgreSQL storage for both modes.

### Setup

```bash
dotnet add package Headless.Sequences.PostgreSql
```

```csharp
builder.Services.AddHeadlessSequences(setup => setup.UsePostgreSql(connectionString));
// or reuse the connection from services.AddPostgreSqlSql(connectionString): setup.UsePostgreSql();
// or bind options: setup.UsePostgreSql(builder.Configuration.GetSection("Sequences"));
// or: setup.UsePostgreSql(options => { options.ConnectionString = cs; options.Schema = "numbering"; });
```

The parameterless overloads and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

Gap-free units begin over a PostgreSQL connection or an EF `DbContext` (`AddUnitOfWork()` or the EF unit-of-work package).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the counters and that gap-free units must run on |
| `CommandTimeout` | 30 seconds | Also bounds how long a gap-free call waits for another unit's row lock |
| `Schema` / `TableName` | `headless` / `sequences` | Validated as PostgreSQL identifiers. Sequences has no `ConfigureStorage`; the schema is a provider option. `headless` is the schema every Headless feature shares |
| `InitializeOnStartup` | `true` | When `false`, the application creates the table |

### Design and runtime behavior

- Each call is one `INSERT … ON CONFLICT (tenant_id, name, partition) DO UPDATE … RETURNING`, so concurrent first calls on a new key never collide. Key columns use `COLLATE "C"`.
- The fast path opens its own connection, runs in an explicit READ COMMITTED transaction, and retries a transient fault raised before the commit (a deadlock, `40P01`, a serialization conflict, `40001`, or anything Npgsql reports as transient; never a fault from the commit) in a fresh transaction up to 3 attempts, waiting a jittered delay (`n × 10–50 ms` before retry `n`, on the registered `TimeProvider`) between them.
- The gap-free path runs on the unit's own connection and transaction, with no retry. A failed or cancelled statement aborts the caller's PostgreSQL transaction, so the unit can then only roll back.
- The table is a step the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies at startup, under one advisory lock per database shared with every other Headless feature, and records in `headless_schema_history` as `Sequences/1` (`Sequences:<table>/1` for a configured table name).

---

## Headless.Sequences.SqlServer

SQL Server storage for both modes.

### Setup

```bash
dotnet add package Headless.Sequences.SqlServer
```

```csharp
builder.Services.AddHeadlessSequences(setup => setup.UseSqlServer(connectionString));
// or reuse the connection from services.AddSqlServerSql(connectionString): setup.UseSqlServer();
```

The parameterless overloads and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

Gap-free units begin over a SQL Server connection or an EF `DbContext` (`AddUnitOfWork()` or the EF unit-of-work package).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the counters and that gap-free units must run on |
| `CommandTimeout` | 30 seconds | Also bounds how long a gap-free call waits for another unit's locks |
| `Schema` / `TableName` | `headless` / `Sequences` | Validated as SQL Server identifiers. Sequences has no `ConfigureStorage`; the schema is a provider option. `headless` is the schema every Headless feature shares |
| `InitializeOnStartup` | `true` | When `false`, the application creates the table |

### Design and runtime behavior

- Each call is one batch: `UPDATE … WITH (UPDLOCK, HOLDLOCK)`, then an `INSERT` in the same transaction when the key is new, with both results collected into a table variable and read back once. The batch has no `TRY/CATCH`, so a caller transaction running `SET XACT_ABORT ON` is never doomed by a caught duplicate key.
- Key columns use a binary (`_BIN2`) collation, so names compare ordinally, as on PostgreSQL. The clustered primary key is exactly `(TenantId, Name, Partition)`, and it is what makes the range lock above serialize first use.
- The fast path opens its own connection, runs in an explicit READ COMMITTED transaction, and retries a transient fault raised before the commit (a deadlock, 1205, a snapshot update conflict, 3960, a lock timeout, 1222, or a connection fault EF Core's SQL Server retry set covers; never a fault from the commit) in a fresh transaction up to 3 attempts, waiting a jittered delay (`n × 10–50 ms` before retry `n`, on the registered `TimeProvider`) between them.
- The gap-free path runs on the unit's own connection and transaction with no retry. With `XACT_ABORT ON`, a timeout or cancellation rolls back the caller's transaction.
- The table and its clustered key are a step the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies at startup, under one `sp_getapplock` per database shared with every other Headless feature, and records in `headless_schema_history` as `Sequences/1` (`Sequences:<table>/1` for a configured table name).

---

## Headless.Sequences.Sqlite

SQLite storage for both modes.

### Setup

```bash
dotnet add package Headless.Sequences.Sqlite
```

```csharp
builder.Services.AddHeadlessSequences(setup => setup.UseSqlite("Data Source=app.db"));
// or reuse the connection from services.AddSqliteSql(connectionString): setup.UseSqlite();
```

Give it a database file: every call and the schema runner open their own connections, so a private `:memory:` database would vanish between them.

Gap-free units begin over a SQLite connection (`AddUnitOfWork()`).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database file that holds the counters and that gap-free units must run on |
| `CommandTimeout` | 30 seconds | Also bounds how long a call waits for another writer's lock |
| `Schema` / `TableName` | `headless` / `sequences` | Validated as PostgreSQL-style identifiers. SQLite has no schemas, so the table is named `<schema>_<table>` (`headless_sequences`) |
| `InitializeOnStartup` | `true` | When `false`, the application creates the table |

### Design and runtime behavior

- Each call is an `UPDATE … RETURNING` of an existing counter, else an `INSERT … RETURNING` of a new one, in one batch. Both run under the database write lock, which the transaction takes when it begins (`BEGIN IMMEDIATE`), so concurrent first calls on a new key never collide. Key columns are `TEXT` with SQLite's binary collation, so names compare ordinally.
- A gap-free unit holds the write lock from its begin to its commit, so a second unit waits at its own begin, not only at the counter row, and every other writer to the file waits too. Keep gap-free units short.
- The fast path opens its own connection and retries `SQLITE_BUSY` and `SQLITE_LOCKED` raised before the commit in a fresh transaction, up to 3 attempts. The driver already waited out its busy timeout (`Default Timeout`, 30 seconds) before raising either.
- The table is a step the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies at startup and records in `<schema>_headless_schema_history` as `Sequences/1` (`Sequences:<table>/1` for a configured table name).

