---
domain: SQL
packages: Sql.Abstractions, Sql.Core, Sql.PostgreSql, Sql.SqlServer, Sql.Sqlite
---

# SQL

> Provider-agnostic SQL connection factory for raw SQL / Dapper scenarios with PostgreSQL, SQL Server, and SQLite backends.

## Orientation

Install `Headless.Sql.Abstractions` plus one provider package. Add `Headless.Sql.Core` when you need the default scoped `ISqlCurrentConnection` implementation:

- `Headless.Sql.PostgreSql` — wraps Npgsql; returns `NpgsqlConnection`
- `Headless.Sql.SqlServer` — wraps `Microsoft.Data.SqlClient`; returns `SqlConnection`
- `Headless.Sql.Sqlite` — wraps `Microsoft.Data.Sqlite`; returns `SqliteConnection`

Each provider package ships a single `Add{Provider}Sql` registration extension that wires the connection factory, the connection-string checker, and a scoped `ISqlCurrentConnection` in one call:

```csharp
builder.Services.AddPostgreSqlSql(connectionString);
// or resolve the connection string from the service provider:
builder.Services.AddPostgreSqlSql(sp => sp.GetRequiredService<ISecrets>().SqlConnectionString);
```

Inject `ISqlConnectionFactory` and call `CreateNewConnectionAsync()` to get an already-open `DbConnection`. Pair with Dapper or raw ADO.NET — this layer does not provide query helpers.

## Agent Rules

- These packages are for **raw SQL / Dapper** scenarios only. For EF Core, use the `Headless.EntityFramework` packages instead.
- Always depend on `ISqlConnectionFactory` from `Headless.Sql.Abstractions` in service code. Never reference `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`, or `SqliteConnectionFactory` in application-layer code.
- Do **not** construct connections directly (`new NpgsqlConnection(cs)` / `new SqlConnection(cs)`). Always go through the factory so the connection string is centralized and the factory can be swapped in tests.
- Connections returned by `CreateNewConnectionAsync()` are **already open** — calling `OpenAsync()` on them again throws an `InvalidOperationException`.
- Always dispose connections with `await using` — they are `IAsyncDisposable`. Holding an open connection unnecessarily may exhaust the connection pool.
- `ISqlCurrentConnection` defines an ambient, lazy-open connection for unit-of-work patterns. `Headless.Sql.Core` provides `DefaultSqlCurrentConnection`; the provider `Add{Provider}Sql` extensions register it as scoped for you.
- `IConnectionStringChecker` is for health checks and startup validation; `Add{Provider}Sql` registers the provider implementation, or register it yourself and inject `IConnectionStringChecker`. Note: `SqliteConnectionStringChecker` always returns `DatabaseExists = true` when connected (SQLite creates the file on open).
- For in-process integration tests, call `AddSqliteSql("Data Source=:memory:")` — it needs no external server.
- To give every Headless storage feature the same database, register the connection once with `AddPostgreSqlSql` or `AddSqlServerSql` and call the feature's parameterless `UsePostgreSql()` or `UseSqlServer()`. Do not repeat the connection string per feature. See [Shared connection and schema for storage features](#shared-connection-and-schema-for-storage-features).
- Storage features create their tables through one schema runner that records applied steps in `headless_schema_history`. For hosts that must not run DDL, set `SchemaRunnerMode.Verify` and deploy the script from `SchemaRunner.ExportScript`. Never drop a feature table by hand without also deleting its history rows. See [Schema runner](#schema-runner-apply-verify-and-deploy-time-scripts).
- Each provider package ships `Add{Provider}Sql(string connectionString)` and `Add{Provider}Sql(Func<IServiceProvider, string>)` on `IServiceCollection` (e.g. `AddPostgreSqlSql`, `AddSqlServerSql`, `AddSqliteSql`). Each registers `ISqlConnectionFactory` (singleton), `IConnectionStringChecker` (singleton), and `ISqlCurrentConnection` → `DefaultSqlCurrentConnection` (scoped). The factory and checker use the same connection string.

## Core Concepts

### Connection factory abstraction

`ISqlConnectionFactory` answers two questions: what is the connection string, and how do I get an open connection? The interface has exactly two members:

```csharp
public interface ISqlConnectionFactory
{
    string GetConnectionString();
    ValueTask<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default);
}
```

Each provider implementation's public `CreateNewConnectionAsync` returns a covariant, strongly-typed connection (e.g. `NpgsqlConnection`, `SqlConnection`, `SqliteConnection`) so provider-aware code can access driver-specific APIs without an extra cast. The `ISqlConnectionFactory` explicit implementation returns `DbConnection` for abstraction consumers.

### Ambient connection (`ISqlCurrentConnection`)

`ISqlCurrentConnection` is for unit-of-work scenarios where multiple repositories must share the same underlying connection within a request:

```csharp
public interface ISqlCurrentConnection : IAsyncDisposable
{
    ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default);
}
```

`Headless.Sql.Core` provides `DefaultSqlCurrentConnection`, which lazily opens one connection per instance, protects concurrent callers with an `AsyncLock`, and re-opens if the connection drops. Register it as **scoped** — one instance per request/scope — so it is disposed at the end of each unit of work.

### Connection string checker

`IConnectionStringChecker.CheckAsync` returns a `ConnectionCheckResult` readonly record struct (`Connected`, `DatabaseExists`) and accepts an optional `CancellationToken`. The `Connected` flag indicates whether the server is reachable; `DatabaseExists` indicates whether the target database exists. A cancelled token throws `OperationCanceledException`; all other connection errors are logged and surfaced through the result. Use this in health checks or startup validation. Behavior differs by provider:

- **PostgreSQL**: connects to `postgres` system database first, then calls `ChangeDatabaseAsync` to verify the target database.
- **SQL Server**: connects to `master`, then calls `ChangeDatabaseAsync` to verify the target database.
- **SQLite**: `Connected` and `DatabaseExists` are always set together — SQLite creates the file on `OpenAsync`, so there is no distinction.

### Shared connection and schema for storage features

The raw PostgreSQL and SQL Server providers of Messaging storage, Coordination, DistributedLocks, AuditLog, Sequences, Features, Permissions, Settings, Fencing, and Idempotency each have a parameterless `UsePostgreSql()` and `UseSqlServer()`, and the SQLite providers of Sequences, Idempotency, Fencing, and Coordination a parameterless `UseSqlite()`. These overloads read the connection string that `AddPostgreSqlSql`, `AddSqlServerSql`, or `AddSqliteSql` registered, so one registration serves every feature:

```csharp
builder.Services.AddPostgreSqlSql(builder.Configuration.GetConnectionString("Default")!);

builder.Services.AddHeadlessFencing(setup => setup.UsePostgreSql());
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql());
builder.Services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());
```

Every relational feature creates its tables in the `headless` schema (`HeadlessStorageDefaults.Schema` in `Headless.Hosting.Initialization`) unless you configure another one. Each feature prefixes its object names with the feature, for example `fencing_leases` and `idempotency_records` on PostgreSQL or `FencingLeases` and `IdempotencyRecords` on SQL Server, so all features coexist in the one schema.

Names follow the database's convention: snake_case on PostgreSQL, PascalCase on SQL Server. A feature whose EF Core mapping takes a `StorageNamingStyle` expects the style of the database it targets; `HeadlessStorageNaming.ForProvider(Database.ProviderName)` (same namespace) returns it, so the mapping produces the same objects as the raw provider.

Precedence, per feature:

- **Connection.** An explicit overload (`UsePostgreSql(connectionString)`, `UsePostgreSql(IConfiguration)`, or an options callback that sets `ConnectionString`) wins for that feature. It does not read the shared registration. The parameterless overload uses the shared connection and nothing else.
- **Schema.** `ConfigureStorage(storage => storage.Schema = "…")` on the feature's setup builder moves that feature's objects only. Sequences has no `ConfigureStorage`; set `Schema` on the provider options (`PostgreSqlSequencesOptions`, `SqlServerSequencesOptions`) instead.

The parameterless overloads resolve the connection string when the feature's options are first resolved, not at registration, so `AddPostgreSqlSql` can come before or after the feature. Resolution throws `InvalidOperationException` when no `ISqlConnectionFactory` is registered, or when the registered factory belongs to the other provider, for example `UsePostgreSql()` with `AddSqlServerSql`.

Custom provider code reads the same connection through `IServiceProvider.GetPostgreSqlConnectionString()` or `IServiceProvider.GetSqlServerConnectionString()` (namespace `Headless.Sql`), which throw the same `InvalidOperationException`.

The EF Core storage variants and Jobs take their connection from the `DbContext` and do not have these overloads.

### Schema runner: apply, verify, and deploy-time scripts

Every raw PostgreSQL, SQL Server, and SQLite storage feature contributes its DDL to one schema runner (`SchemaRunner`, namespace `Headless.Hosting.Initialization.Schema`) instead of running its own initializer. Registering a feature is enough; the feature adds its contribution and the runner's hosted initializer. The runner:

- Applies every missing step of every feature that reaches one database in a single pass at host start, under one session lock per database (`pg_try_advisory_lock` or `sp_getapplock`, polled; on SQLite a leased row in `headless_schema_lock`), before any hosted service can use the tables.
- Records each applied step, with a SHA-256 checksum of its SQL, in `headless_schema_history` in the feature's schema. A recorded step is never run again, so a warm start is one history read per schema and takes no lock.
- Absorbs a creator outside the lock, such as your own EF migration committing the same schema or table first: the failed step is re-run once in a fresh transaction.

Choose the startup mode with `services.AddHeadlessSchemaRunner(options => options.Mode = …)`:

| Mode | Writes DDL | Fails startup when |
|---|---|---|
| `SchemaRunnerMode.Apply` (default) | Yes, missing steps only | A recorded step's checksum no longer matches the code |
| `SchemaRunnerMode.Verify` | Never | A step is missing, or a recorded checksum no longer matches |

The same options set `CommandTimeout` (default 10 minutes), which applies to every runner statement, DDL included, instead of each feature's OLTP `CommandTimeout`. They also set `LockTimeout` (default 2 minutes), how long a replica waits while another replica applies steps.

Use `Verify` when DDL may not run from the application. Generate the reviewable script from the same registrations the application uses, and apply it with `psql` or `sqlcmd` in the deployment:

```csharp
// For example in a small console entry point or a test that CI runs: register the features exactly as the
// application does, then export. Export performs no database access; the connection string is never opened.
var services = new ServiceCollection();
services.AddHeadlessSequences(setup => setup.UsePostgreSql(connectionString));
services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString));

await using var provider = services.BuildServiceProvider();
var script = provider.GetRequiredService<SchemaRunner>().ExportScript(PostgreSqlSchemaDialect.Instance);
await File.WriteAllTextAsync("headless-schema.postgresql.sql", script);
```

The script is deterministic for a given set of registrations, so it can be committed and diffed. Every statement is guarded, so running it twice changes nothing. It inserts the same history rows the runner writes, so a host in `Verify` mode accepts the database afterwards. The SQL Server script separates batches with `GO`.

Rules that change how you operate a database:

- **The runner trusts its history.** Dropping a feature's table by hand does not make the next start recreate it. Delete that feature's rows from `headless_schema_history` too, or drop the whole schema.
- **Never edit a released step.** A changed step fails startup with a checksum mismatch naming the feature and version. Features evolve a released schema by adding a new step. Until a release ships the step, change it in place: no database runs it, so an upgrade step would be migration code for a schema nobody has.
- **Configuration that shapes DDL is part of the checksum.** Changing AuditLog's `JsonColumnType` after its table exists fails startup instead of being silently ignored.
- **Features with configurable object names keep one history per name.** `Sequences` with the default table records `Sequences/1`; with `TableName = "counters"` it records `Sequences:counters/1`, so two hosts naming the table differently in one schema never collide.
- **`InitializeOnStartup = false`** on a feature keeps its steps out of `Apply` mode. `Verify` mode and `ExportScript` still include them.
- History rows of features a host does not register are ignored, so hosts with different feature sets can share one schema. A row for a registered feature whose step this host does not know, such as a newer replica's step during a rolling deploy, is logged, not fatal.

### Store statement kit (for provider authors)

A relational store is written once against `ISqlDialect` (`Headless.Sql`), with `PostgreSqlDialect.Instance`, `SqlServerDialect.Instance`, and `SqliteDialect.Instance` as the engines. Application code does not call it; a feature's store does. The dialect renders a small set of statement shapes, and each shape keeps the same locking and clock rules on every engine:

| Shape | Does | Result |
| --- | --- | --- |
| `SqlLockedRead` | Reads one row by key under an update-intent lock (a key-range lock when absent) | The row, or none |
| `SqlFencedTransition` | One conditional `UPDATE` whose `WHERE` is the fence; run it after a locked read of the same key | Applied flag, then the written columns |
| `SqlInsertIfAbsent` | Inserts the keyed row only when no row has the key, never raising a duplicate-key error | Inserted flag, then the columns |
| `SqlUpsert` | Inserts the keyed row, or updates it when an optional guard allows | `SqlUpsertOutcome` (`Refused`, `Inserted`, `Updated`), then the columns |
| `SqlClaimNext` | Claims the first row, or the first `@BatchSizeParameter` rows, matching a filter, skipping locked rows | One row per claimed row |
| `SqlDeleteBatch` | Deletes up to `@BatchSizeParameter` matching rows, skipping locked rows | Row count |
| `SqlClockedStatement` | Any one statement that reads the database clock without waiting on a lock first | The statement's own |

- **Tokens.** Fragments use `SqlDialectTokens.Now` (`{now}`) for the database clock, never the application clock, and `SqlDialectTokens.Stored` (`{stored}`) for the stored row in an upsert's `Set` and `Guard`. SQL Server reads the clock after the statement's lock wait; PostgreSQL reads `clock_timestamp()` once per statement, never `now()`.
- **Results.** `SqlFencedCommand.ExecuteAsync` and `SqlUpsertCommand.ExecuteAsync` (`Headless.Sql.Core`) return `SqlFenced<TRow, TAccepted>` and `SqlUpserted<T>`, whose written values are reachable only through `Match`, so a store cannot use a result without handling the refusal.
- **Lists.** `InList(expression, parameter, elementType)` with `AddListParameter` binds a whole list as one parameter: `= ANY(@p)` over an array on PostgreSQL, `OPENJSON` with a typed `WITH` clause on SQL Server. No table type has to exist, and an empty list matches nothing. Binary and JSON lists are refused. `InTuples` with `CreateTupleListParameters` matches a list of rows as whole tuples (`unnest` over one array per column on PostgreSQL, `OPENJSON` with positional columns and `EXISTS` on SQL Server), so pairs such as (id, expected version) are never matched across rows.
- **Parameters.** `AddParameter` types each value by `SqlColumnType` (`KeyText`, `Text`, `Int16`, `Int32`, `Int64`, `Timestamp`, `Binary`, `Guid`, `Boolean`, `Json`); text is sized to its column so the comparison keeps the column's collation. `AddDuration` and `ShiftByDuration` add a `TimeSpan` to an instant without the SQL Server `DATEADD` int overflow.
- **Errors.** `Classify` maps a driver exception to `SqlErrorKind`: `UniqueViolation`, `Deadlock`, `SerializationConflict`, `DuplicateObject`, `LockTimeout`, and `TransactionAborted` for a statement that ran on a transaction an earlier error ended or doomed (PostgreSQL `25P02`, SQL Server `3930`, and on SQLite the driver's `InvalidOperationException` for a transaction SQLite rolled back). That transaction is lost; roll the unit back rather than retrying inside it.
- **Autonomous calls.** `SqlAutonomousTransaction.RunAsync` runs one store call on its own READ COMMITTED transaction, at most 3 attempts, each on a fresh connection. It retries a fault only when `RelationalTransientFaults.IsTransient` (`Headless.UnitOfWork`) calls it transient and it was raised before the commit started: by the transaction begin or the store's statements. That set is the unit of work's: a deadlock, a serialization conflict, a lock timeout, a dropped connection, a capacity fault, on SQL Server EF Core's `EnableRetryOnFailure` error numbers minus the client command timeout, and on SQLite `SQLITE_BUSY` and `SQLITE_LOCKED`. A fault from the commit is never retried, whatever its classification, because the commit may have landed on the server before it failed on the wire; it surfaces unchanged. Nor is a fault observed after the caller's token was cancelled. `Classify` takes no part in the retry. The call never retries inside a caller's transaction.
- **Custom autonomous loops.** `SqlAutonomousTransaction.RetryAsync` applies the same rule to an attempt that owns its own connection and transaction, such as an EF claim scope. The attempt receives a `SqlAutonomousAttempt` and calls `MarkCommitStarted()` immediately before it commits. An optional `onRetry` callback receives the fault and the number of the attempt about to run.
- **Portable values.** `SqlPortable.Truncate` truncates a duration to the microsecond every engine keeps. Key text is checked with `Argument.IsPortableKey` (`Headless.Checks`).
- **Enlistment.** `RelationalEnlistment.RequireLive` and `RequireSameDatabase` (`Headless.UnitOfWork`) are the checks a store runs before writing inside a caller's unit of work.

#### SQLite in the kit

SQLite has no row locks, no `SKIP LOCKED`, no advisory locks, no schemas, and no server clock. `SqliteDialect` keeps each shape's contract with these substitutes:

- **One write lock instead of row locks.** `Microsoft.Data.Sqlite` begins every transaction with `BEGIN IMMEDIATE` unless you pass `deferred: true`, so a store's transaction, autonomous or yours, holds the database write lock from its first statement and every statement in it runs after any other writer finished. The locking read opens with a no-op write, so a deferred caller transaction takes the lock there. Claims and batch deletes skip nothing because no other writer can hold a row. Writers are serialized per database file: do not use SQLite for write-heavy concurrent workloads.
- **Synchronous waits.** `Microsoft.Data.Sqlite` waits for the write lock on the calling thread. An autonomous call (and a SQLite unit's begin, and the schema runner) yields first, so the caller gets a pending task, but the call still holds a thread-pool thread for up to the busy timeout while it waits.
- **Busy, not deadlock.** A writer that waits longer than the connection's `Default Timeout` (30 seconds) fails with `SQLITE_BUSY`, which `Classify` reports as `LockTimeout` (`SerializationConflict` for `SQLITE_BUSY_SNAPSHOT`) and `RelationalTransientFaults` treats as transient. A deferred transaction that read before writing can fail this way while another writer is active; begin transactions `IMMEDIATE`, the driver's default.
- **Instants as text.** A `Timestamp` column is `TEXT` holding `yyyy-MM-dd HH:mm:ss.ffffff+00:00`: UTC, always six fractional digits, so text order is time order. A bound instant is converted to UTC and truncated to the microsecond (`TimestampPrecision` is 1 µs). `Microsoft.Data.Sqlite` reads it back as `DateTimeOffset`.
- **The clock.** `{now}` is SQLite's `'now'`, millisecond resolution, fixed for the whole statement and read inside it, so after the write lock was taken. It is the clock of the host that runs the statement. Every process that opens one SQLite file must run on that file's host (SQLite locking does not work over a network file system), so all of them read one clock.
- **Other storage forms.** `Guid` is upper-case text, the form EF Core's SQLite provider writes. `Boolean` is `0`/`1`. `Json` is text. List and tuple parameters are one JSON array read with `json_each`.
- **Schemas are name prefixes.** `Qualify("headless", "fencing_leases")` is `"headless_fencing_leases"`; `SqliteDialect.QualifiedName` returns the unquoted form for index names, which SQLite scopes to the whole file.
- **No generations inside a caller's transaction.** A table-backed sequence rolls back with the transaction that drew from it, so a store must not draw a generation or fencing token inside a caller's unit: a rolled-back value would be drawn again. The SQLite Idempotency and Fencing providers refuse those enlisted calls and draw only in transactions they commit themselves.
- **Sequences are tables.** `NextSequenceValue` reads one past the `value` of a one-row table (`id` 1). Reading does not advance it: the feature's DDL adds triggers on the table that stores the drawn value, so the draw and the advance happen in one statement under the write lock. One statement draws one value, however many rows it writes.
- **Upsert outcome.** SQLite's `RETURNING` cannot tell an insert from an update, so the upsert renders one statement per branch and `SqlUpsertCommand` reads the first row of any result set.

**Recommended: WAL journal mode.** Run `PRAGMA journal_mode=WAL;` once on the database file. The setting is stored in the file, so later connections inherit it. In WAL mode, readers read the last committed state while a writer holds the write lock and is committing. The default rollback journal blocks them during each commit. That helps every read outside a store transaction, such as the Idempotency peek, which begins a deferred transaction so it never queues behind a writer. Writers are still serialized: WAL does not add concurrent writers. Trade-offs:

- SQLite keeps two more files next to the database, `<file>-wal` and `<file>-shm`. Back up and move all three together, or checkpoint first (`PRAGMA wal_checkpoint(TRUNCATE);`).
- WAL relies on shared memory, so every process must run on the host that holds the file. This adds nothing new: SQLite locking already requires it.
- The Headless providers do not set the journal mode. It belongs to the database, not to one feature that shares it, so the application sets it once, for example right after creating the file.

## Choosing a Provider

| Provider | Package | ADO.NET driver | Use when | Avoid when |
|---|---|---|---|---|
| PostgreSQL | `Headless.Sql.PostgreSql` | Npgsql | Production PostgreSQL; need `NpgsqlConnection`-specific features (COPY, LISTEN/NOTIFY, type mapping) | Vendor lock-in is unacceptable in code that must stay abstract |
| SQL Server | `Headless.Sql.SqlServer` | `Microsoft.Data.SqlClient` | Production SQL Server / Azure SQL; need `SqlConnection`-specific features (bulk copy, SQL auth) | PostgreSQL or SQLite environment |
| SQLite | `Headless.Sql.Sqlite` | `Microsoft.Data.Sqlite` | In-process testing with `:memory:`; embedded / edge / single-file deployments | High concurrency production workloads (SQLite write lock serializes writes) |

---
## Headless.Sql.Abstractions

Defines the provider-agnostic interfaces for SQL connection creation and validation.

### API and behavior

- `ISqlConnectionFactory` — create and manage database connections; `GetConnectionString()` retrieves the configured string; `CreateNewConnectionAsync()` returns an already-open `DbConnection`
- `ISqlCurrentConnection` — ambient connection for unit-of-work scopes; lazy-opens on first call, re-opens on drop
- `IConnectionStringChecker` — validate server reachability and database existence; `CheckAsync(connectionString, cancellationToken)` returns a `ConnectionCheckResult` record struct (`Connected`, `DatabaseExists`)

### Install

```bash
dotnet add package Headless.Sql.Abstractions
```

### Setup and use

```csharp
using Dapper;

// Register a concrete factory (provider package required):
builder.Services.AddSingleton<ISqlConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

// Inject and use in a repository:
public sealed class OrderRepository(ISqlConnectionFactory connectionFactory)
{
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.CreateNewConnectionAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<Order>(
            "SELECT * FROM orders WHERE id = @Id",
            new { Id = id }
        );
    }
}
```

Add `Headless.Sql.Core` when you need the default scoped `ISqlCurrentConnection` implementation.

### Configuration

None. This is an abstractions-only package.

### Runtime behavior

None. This is an abstractions package — it registers no services.

---

## Headless.Sql.Core

Default implementation package for provider-agnostic SQL helpers.

### API and behavior

- `DefaultSqlCurrentConnection` — concrete thread-safe implementation of `ISqlCurrentConnection` backed by `AsyncLock`.
- Lazily opens one connection per scope and reuses it until disposal.
- Reopens the underlying connection if it is observed closed.
- `SqlAutonomousTransaction` and `SqlAutonomousAttempt` — the store kit's autonomous-call retry (see [Store statement kit](#store-statement-kit-for-provider-authors)). They classify faults through `RelationalTransientFaults`, so the package depends on `Headless.UnitOfWork`.

### Install

```bash
dotnet add package Headless.Sql.Core
```

### Setup and use

```csharp
builder.Services.AddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();
```

### Configuration

None. Register `DefaultSqlCurrentConnection` explicitly as a scoped `ISqlCurrentConnection`, and register one provider-specific `ISqlConnectionFactory` from `Headless.Sql.PostgreSql`, `Headless.Sql.SqlServer`, or `Headless.Sql.Sqlite`.

### Runtime behavior

None. Register services explicitly.

---

## Headless.Sql.PostgreSql

PostgreSQL connection factory backed by Npgsql.

### API and behavior

- `NpgsqlConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `NpgsqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `NpgsqlConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `postgres` first, then calling `ChangeDatabaseAsync` to the target
- `SetupPostgreSqlSql.AddPostgreSqlSql(string connectionString)` / `AddPostgreSqlSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection
- `IServiceProvider.GetPostgreSqlConnectionString()` (`HeadlessPostgreSqlSharedConnectionExtensions`) — returns the registered `NpgsqlConnectionFactory` connection string; the storage features' parameterless `UsePostgreSql()` calls it
- `PostgreSqlSchemaDialect.Instance` — the schema runner's PostgreSQL dialect: polled session advisory lock per database, `headless_schema_history` DDL, and `42P06`/`42P07`/`42710`/`23505` classified as an absorbed race. Pass it to `SchemaRunner.ExportScript` for a `psql` script
- `PostgreSqlSchemaInitLock.AcquireStatement(schema)` — the schema-wide transaction lock; the dialect takes it before `CREATE SCHEMA`

### Design constraints

`NpgsqlConnectionFactory.CreateNewConnectionAsync()` is declared `public async ValueTask<NpgsqlConnection>` — a covariant return relative to the explicit `ValueTask<DbConnection>` implementation on `ISqlConnectionFactory`. Callers that inject `NpgsqlConnectionFactory` directly (e.g., provider-aware infrastructure code) can use `NpgsqlConnection`-specific APIs (COPY, LISTEN/NOTIFY, Npgsql type mapping) without a cast. Callers that inject `ISqlConnectionFactory` see only `DbConnection`.

`NpgsqlConnectionStringChecker` uses a 1-second connect timeout (`Timeout = 1`) to fail fast in health checks. If the target database cannot be selected, `DatabaseExists` is `false` but `Connected` is `true`.

### Install

```bash
dotnet add package Headless.Sql.PostgreSql
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Registers ISqlConnectionFactory, IConnectionStringChecker, and a scoped ISqlCurrentConnection.
builder.Services.AddPostgreSqlSql(connectionString);
```

Use in a repository (always inject `ISqlConnectionFactory`, not the concrete type):

```csharp
using Dapper;

public sealed class ReportRepository(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Report>> GetRecentAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.CreateNewConnectionAsync(ct);

        return await connection.QueryAsync<Report>(
            "SELECT * FROM reports WHERE created_at > @Date",
            new { Date = DateTime.UtcNow.AddDays(-30) }
        );
    }
}
```

### Configuration

Resolve the connection string from the service provider with the factory overload:

```csharp
services.AddPostgreSqlSql(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetConnectionString("Postgres")!;
});
```

### Runtime behavior

`AddPostgreSqlSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped.

---
## Headless.Sql.SqlServer

SQL Server connection factory backed by `Microsoft.Data.SqlClient`.

### API and behavior

- `SqlServerConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqlServerConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `master` first, then calling `ChangeDatabaseAsync` to the target
- `SetupSqlServerSql.AddSqlServerSql(string connectionString)` / `AddSqlServerSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection
- `IServiceProvider.GetSqlServerConnectionString()` (`HeadlessSqlServerSharedConnectionExtensions`) — returns the registered `SqlServerConnectionFactory` connection string; the storage features' parameterless `UseSqlServer()` calls it
- `SqlServerSchemaDialect.Instance` — the schema runner's SQL Server dialect: polled session `sp_getapplock` per database, `headless_schema_history` DDL, and errors 2714/1913/2759 classified as an absorbed race. Pass it to `SchemaRunner.ExportScript` for a `sqlcmd` script with `GO` batch separators

### Install

```bash
dotnet add package Headless.Sql.SqlServer
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Registers ISqlConnectionFactory, IConnectionStringChecker, and a scoped ISqlCurrentConnection.
builder.Services.AddSqlServerSql(connectionString);
```

Use in a repository:

```csharp
using Dapper;

public sealed class ReportRepository(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Report>> GetRecentAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.CreateNewConnectionAsync(ct);

        return await connection.QueryAsync<Report>(
            "SELECT * FROM Reports WHERE CreatedAt > @Date",
            new { Date = DateTime.UtcNow.AddDays(-30) }
        );
    }
}
```

### Configuration

Resolve the connection string from the service provider with the factory overload:

```csharp
services.AddSqlServerSql(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetConnectionString("SqlServer")!;
});
```

### Runtime behavior

`AddSqlServerSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped.

---
## Headless.Sql.Sqlite

SQLite connection factory backed by `Microsoft.Data.Sqlite`.

### API and behavior

- `SqliteConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqliteConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqliteConnectionStringChecker` — `IConnectionStringChecker` that opens the SQLite database and reports both `Connected` and `DatabaseExists` as `true` on success (SQLite creates the file on open, so the two flags are always identical)
- `SetupSqliteSql.AddSqliteSql(string connectionString)` / `AddSqliteSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection
- `IServiceProvider.GetSqliteConnectionString()` (`HeadlessSqliteSharedConnectionExtensions`) — returns the registered `SqliteConnectionFactory` connection string; the storage features' parameterless `UseSqlite()` calls it
- `SqliteDialect.Instance` — the store kit's SQLite dialect; see [SQLite in the kit](#sqlite-in-the-kit)
- `SqliteSchemaDialect.Instance` — the schema runner's SQLite dialect: a leased lock row in `headless_schema_lock` (lease `SqliteSchemaDialect.LockLease`, one minute) in place of a session lock, and the history table named `<schema>_headless_schema_history`. Pass it to `SchemaRunner.ExportScript` for a script the `sqlite3` shell runs as one file

### Design constraints

`SqliteConnectionStringChecker` differs from the PostgreSQL and SQL Server implementations: because SQLite creates the database file when the connection opens, there is no meaningful distinction between "server reachable" and "database exists". Both `ConnectionCheckResult` fields are set to `true` together on a successful open, or both remain `false` on failure.

For in-process testing, prefer `"Data Source=:memory:"` — the database is private to the connection and disappears when the connection closes. Storage features are the exception: the schema runner and every store call open their own connections, so give them a database file.

### Install

```bash
dotnet add package Headless.Sql.Sqlite
```

### Setup and use

```csharp
// In-process tests (no server required):
services.AddSqliteSql("Data Source=:memory:");

// File-based embedded database:
services.AddSqliteSql("Data Source=app.db");
```

Use in a repository:

```csharp
using Dapper;

public sealed class CacheRepository(ISqlConnectionFactory connectionFactory)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var connection = await connectionFactory.CreateNewConnectionAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT value FROM cache WHERE key = @Key",
            new { Key = key }
        );
    }
}
```

### Configuration

Pass the connection string to `AddSqliteSql`. SQLite connection strings use `Data Source=<path>` or `Data Source=:memory:`.

### Runtime behavior

`AddSqliteSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped. For file-based databases, SQLite creates the `.db` file on the first connection open if it does not exist.
