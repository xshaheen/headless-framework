---
domain: Sequences
packages: Sequences.Abstractions, Sequences.Core, Sequences.PostgreSql, Sequences.SqlServer
---

# Sequences

> Tenant-scoped, named, monotonic counters in a relational table. The fast mode takes a number in its own short transaction. The gap-free mode takes it inside the caller's unit of work, so a rollback returns the number.

## Orientation

Register once with `AddHeadlessSequences` and exactly one provider:

```csharp
builder.Services.AddPostgreSqlUnitOfWork(); // the gap-free mode needs a unit of work on the same database
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

Formatting stays in the application. The framework returns a `long`. Prefixes (`INV-`), the year text, padding, and check digits are the caller's concern. So is the choice of partition: pass the year for a counter that restarts each year.

## Agent Rules

- Choose the mode per name, and use only that name's entry point. A gap-free name called through `ISequenceGenerator`, or a fast name called through `unit.Sequences`, throws `InvalidOperationException` before any SQL runs. Mixing the two would let a fast caller's rollback leave a gap in a gap-free counter, or make a flow wait on its own row lock.
- Use the gap-free mode only for numbers that must have no gaps. It serializes every writer of one counter until each writer's unit commits or rolls back, so its throughput is bounded by transaction length. Take the number as late in the unit as possible.
- When one unit takes several gap-free counters, take them in a fixed order everywhere. Two units taking the same counters in opposite orders deadlock. The gap-free mode never retries inside the caller's transaction, so the unit's owner receives the deadlock.
- Calls on one unit must be sequential. Do not issue two `unit.Sequences` calls on the same unit concurrently (`Task.WhenAll`): they share one connection, which neither Npgsql nor SqlClient allows.
- Run gap-free units at READ COMMITTED, or at READ COMMITTED SNAPSHOT on SQL Server. Stricter isolation fails when another transaction has updated the counter: PostgreSQL REPEATABLE READ or SERIALIZABLE raises `40001`, and SQL Server SNAPSHOT raises update conflict 3960. The error propagates unwrapped, and the unit's owner handles it; `RunAsync(db, …)` under a retrying EF strategy treats it as transient.
- Gap-free calls require a unit on the same database the provider is configured for, with a transaction from that provider. Any other unit is refused before a statement runs.
- In an observed-mode unit (a gap-free call from a domain-event handler during the EF save pipeline's own save), the call marks the unit non-retryable, as `unit.Outbox` and `unit.Jobs` do. A replay would restore a tracked entity carrying a number that the rolled-back counter hands out again. Inside your own `RunAsync(db, …)` block the unit stays replayable, because the replay takes the number again.
- A fast call cancelled or cut off during its commit may still have consumed a number. Treat that as an ordinary fast-mode gap.
- The tenant is read from `ICurrentTenant` on every call. There is no tenant argument; host code numbering on behalf of a tenant switches with `ICurrentTenant.Change(...)`. With no tenant (`Id` is `null`), calls use the host counter. An empty or whitespace tenant id is refused, so it can never fall into the host counter.
- Names and partitions compare ordinally and case-sensitively: `"INV"` and `"inv"` are two counters. A name may be at most 128 characters, a partition at most 64, and a tenant id at most 128 (`SequenceFieldLimits`). Longer values, a blank name, a whitespace-only partition, and any key part that starts or ends with whitespace throw `ArgumentException` before any SQL runs. The whitespace rule exists because SQL Server ignores trailing spaces when comparing keys, so `"acme"` and `"acme "` would otherwise share one counter there. A `null` or empty partition means no partition.
- The table is created at startup by the provider's initializer. With `InitializeOnStartup = false` the application owns the table. A call against a missing table fails, and on PostgreSQL that failure aborts the caller's unit.
- There is no reset, set, peek, or delete API. Start a new period with a new partition.
- On SQL Server, the first call on a new counter takes a key-range lock on the gap in the primary key that holds the new key. In gap-free mode that lock is held until the unit commits, so first use of any other new counter whose key sorts into the same gap waits, for any tenant and from either mode. Two units that each first-use several new counters can deadlock on those ranges. Keep a unit that takes a counter's first number short. PostgreSQL locks only the conflicting key.

## Core Concepts

### Counter key and policy

The stored key is `(tenant_id, name, partition)`. The host tenant and "no partition" are both stored as `''`. A `SequencePolicy` sets `Start` (default 1), `Step` (greater than 0, default 1) and `Mode` (default `Fast`). `setup.DefaultPolicy(...)` applies to every name without its own `setup.Policy(name, ...)`.

`Start` is read only when a key's row is created, so changing it never moves an existing counter. A changed `Step` applies from the next call, and the existing counter then mixes both steps.

### Ranges

`ReserveAsync(name, count)` atomically advances the counter by `count * step` and returns a `SequenceRange` with `First`, `Count`, `Step` and `Last`. Enumerating it yields `First, First + Step, …, Last` without allocating. `count` must be at least 1. A `count * step` that cannot fit in a `long` throws `OverflowException` before the database is called. Reserve is fast-mode only.

### Overflow

A counter whose stored value would pass `long.MaxValue` fails with the provider's arithmetic error (PostgreSQL `22003`, SQL Server 8115). No typed exhaustion error exists.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Sequences.PostgreSql` | The application's units of work run on PostgreSQL | Units run on SQL Server | One `INSERT … ON CONFLICT DO UPDATE` per call, locking only the counter's key |
| `Headless.Sequences.SqlServer` | The application's units of work run on SQL Server | Many new counters are first used concurrently in long gap-free units | First use of a key takes a key-range lock on its gap in the primary key |

The provider must sit on the database the gap-free units run on. The counters are rows in that database, written inside the unit's transaction.

---

## Headless.Sequences.Abstractions

Consumer contracts: `ISequenceGenerator`, `SequenceRange`, `SequenceMode`, and the `unit.Sequences` accessor on `IUnitOfWork`.

### Setup

```bash
dotnet add package Headless.Sequences.Abstractions
```

Reference it from code that takes numbers. Registration lives in the Core and provider packages.

### Design and runtime behavior

- `ISequenceGenerator.NextAsync(name, partition, ct)` returns the counter's next value. `ReserveAsync(name, count, partition, ct)` returns a consecutive `SequenceRange`. Both are fast-mode only.
- `unit.Sequences` (namespace `Headless.UnitOfWork`) returns a facade bound to the unit, and repeated reads on one unit return the same instance. Its `NextAsync(name, partition, ct)` is gap-free only.
- `unit.Sequences` throws `InvalidOperationException` naming `AddHeadlessSequences` when no provider registered the feature.

---

## Headless.Sequences.Core

Registration, policies, key resolution, and the provider seam.

### Setup

```bash
dotnet add package Headless.Sequences.Core
```

Applications reach it through a provider package; call `AddHeadlessSequences` as shown in [Orientation](#orientation).

### Configuration

| Builder member | Effect |
| --- | --- |
| `Policy(name, SequencePolicy)` | Sets one name's start, step, and mode |
| `DefaultPolicy(SequencePolicy)` | Sets the policy of every unregistered name (default: fast, start 1, step 1) |
| `ConfigureOptions(Action<SequencesOptions>)` | Mutates `SequencesOptions` (`DefaultPolicy`, `Policies`) directly |

A policy with `Step` of 0 or less fails options validation at startup.

### Design and runtime behavior

- `AddHeadlessSequences` requires exactly one `Use…` provider call and throws when there are none or several, or when it is called twice.
- It registers `ISequenceGenerator` and the gap-free unit-of-work feature as singletons. It also registers the async-local `CurrentTenant` as the `ICurrentTenant` fallback, so `ICurrentTenant.Change(...)` works with no other tenancy registration. A real tenancy registration wins.
- The gap-free call checks, in order: the arguments, the name's mode, the unit's state and relational resource, then the provider's transaction type and database. Only after every check passes does it mark an observed unit non-retryable and run the increment. A refused call leaves the unit retryable and runs no statement.
- `ISequenceStore` is the provider seam. Applications do not call it.

---

## Headless.Sequences.PostgreSql

PostgreSQL storage for both modes.

### Setup

```bash
dotnet add package Headless.Sequences.PostgreSql
```

```csharp
builder.Services.AddHeadlessSequences(setup => setup.UsePostgreSql(connectionString));
// or bind options: setup.UsePostgreSql(builder.Configuration.GetSection("Sequences"));
// or: setup.UsePostgreSql(options => { options.ConnectionString = cs; options.Schema = "numbering"; });
```

Gap-free units begin over a PostgreSQL connection or an EF `DbContext` (`AddPostgreSqlUnitOfWork()` or the EF unit-of-work package).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the counters and that gap-free units must run on |
| `CommandTimeout` | 30 seconds | Also bounds how long a gap-free call waits for another unit's row lock |
| `Schema` / `TableName` | `sequences` / `sequences` | Validated as PostgreSQL identifiers |
| `InitializeOnStartup` | `true` | When `false`, the application creates the table |

### Design and runtime behavior

- Each call is one `INSERT … ON CONFLICT (tenant_id, name, partition) DO UPDATE … RETURNING`, so concurrent first calls on a new key never collide. Key columns use `COLLATE "C"`.
- The fast path opens its own connection, runs in an explicit READ COMMITTED transaction, and retries a deadlock (`40P01`) up to 3 attempts.
- The gap-free path runs on the unit's own connection and transaction, with no retry. A failed or cancelled statement aborts the caller's PostgreSQL transaction, so the unit can then only roll back.
- The initializer serializes concurrent hosts with an advisory lock and creates the schema and table idempotently.

---

## Headless.Sequences.SqlServer

SQL Server storage for both modes.

### Setup

```bash
dotnet add package Headless.Sequences.SqlServer
```

```csharp
builder.Services.AddHeadlessSequences(setup => setup.UseSqlServer(connectionString));
```

Gap-free units begin over a SQL Server connection or an EF `DbContext` (`AddSqlServerUnitOfWork()` or the EF unit-of-work package).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the counters and that gap-free units must run on |
| `CommandTimeout` | 30 seconds | Also bounds how long a gap-free call waits for another unit's locks |
| `Schema` / `TableName` | `sequences` / `sequences` | Validated as SQL Server identifiers |
| `InitializeOnStartup` | `true` | When `false`, the application creates the table |

### Design and runtime behavior

- Each call is one batch: `UPDATE … WITH (UPDLOCK, HOLDLOCK)`, then an `INSERT` in the same transaction when the key is new, with both results collected into a table variable and read back once. The batch has no `TRY/CATCH`, so a caller transaction running `SET XACT_ABORT ON` is never doomed by a caught duplicate key.
- Key columns use a binary (`_BIN2`) collation, so names compare ordinally, as on PostgreSQL. The clustered primary key is exactly `(tenant_id, name, partition)`, and it is what makes the range lock above serialize first use.
- The fast path opens its own connection, runs in an explicit READ COMMITTED transaction, and retries a deadlock (1205) up to 3 attempts.
- The gap-free path runs on the unit's own connection and transaction with no retry. With `XACT_ABORT ON`, a timeout or cancellation rolls back the caller's transaction.
- The initializer serializes concurrent hosts with `sp_getapplock` and creates the schema, table, and key idempotently.
