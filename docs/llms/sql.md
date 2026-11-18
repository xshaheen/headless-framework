---
domain: SQL
packages: Sql.Abstractions, Sql, Sql.PostgreSql, Sql.SqlServer, Sql.Sqlite
---

# SQL

> An open-connection factory for raw SQL and Dapper on PostgreSQL, SQL Server, and SQLite, plus the shared connection and schema runner that Headless storage features use.

## Orientation

Install one provider package. It brings `Headless.Sql.Abstractions` and `Headless.Sql` with it:

- `Headless.Sql.PostgreSql` wraps Npgsql and returns `NpgsqlConnection`.
- `Headless.Sql.SqlServer` wraps `Microsoft.Data.SqlClient` and returns `SqlConnection`.
- `Headless.Sql.Sqlite` wraps `Microsoft.Data.Sqlite` and returns `SqliteConnection`.

One `Add{Provider}Sql` call registers the connection factory, the connection-string checker, and a scoped `ISqlCurrentConnection`:

```csharp
builder.Services.AddPostgreSqlSql(connectionString);
// or resolve the connection string from the service provider:
builder.Services.AddPostgreSqlSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")!);
```

Inject `ISqlConnectionFactory` and call `CreateNewConnectionAsync()` to get an open `DbConnection`. Query it with Dapper or ADO.NET. This layer has no query helpers.

The same registration feeds every Headless storage feature that you configure with a parameterless `UsePostgreSql()`, `UseSqlServer()`, or `UseSqlite()`. Those features create their tables through one schema runner at deploy time. See [Shared connection and schema for storage features](#shared-connection-and-schema-for-storage-features) and [Schema runner](#schema-runner-apply-verify-and-deploy-time-scripts).

## Agent Rules

- Use these packages for raw SQL and Dapper. For EF Core, use the [ORM](orm.md) packages.
- Inject `ISqlConnectionFactory` in service code. Inject `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`, or `SqliteConnectionFactory` only where you need a driver-specific API, such as Npgsql `COPY` or SQL Server bulk copy.
- Never construct a connection with `new NpgsqlConnection(cs)` or `new SqlConnection(cs)`. Go through the factory, so the connection string stays in one place and tests can swap the factory.
- Never call `OpenAsync()` on a connection from `CreateNewConnectionAsync()`. It is already open, and a second open throws `InvalidOperationException`.
- Dispose every connection with `await using`. A connection held open longer than needed drains the pool.
- To give every Headless storage feature the same database, register the connection once with `AddPostgreSqlSql`, `AddSqlServerSql`, or `AddSqliteSql`, and call each feature's parameterless `UsePostgreSql()`, `UseSqlServer()`, or `UseSqlite()`. Do not repeat the connection string per feature.
- Create storage-feature tables at deploy time with `dotnet MyApp.dll resources provision` (or `deploy`), or with the script from `SchemaRunner.ExportScript`. A host start only verifies them and fails when a step is missing. In a development or test host, call `AddDeployStepsOnStartup()`.
- Never drop a feature table by hand. Use `resources delete -n {dialect}/{schema}/{feature}`, which also deletes the feature's history rows. A hand-dropped table is not recreated while its history rows remain.
- For an in-process test of your own SQL, call `AddSqliteSql("Data Source=:memory:")`. Give storage features a SQLite file instead, because they open connections of their own.

## Core Concepts

### Connection factory abstraction

`ISqlConnectionFactory` has two members:

```csharp
public interface ISqlConnectionFactory
{
    string GetConnectionString();
    ValueTask<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default);
}
```

Each provider's public `CreateNewConnectionAsync` returns the driver type (`NpgsqlConnection`, `SqlConnection`, `SqliteConnection`), so code that injects the concrete factory uses driver APIs without a cast. Code that injects `ISqlConnectionFactory` sees `DbConnection`.

`factory.PingAsync(cancellationToken)` (`Headless.Sql`) opens a new connection and runs `SELECT 1`. `PingAsync(commandText, cancellationToken)` runs your own query instead. Both throw the driver's `DbException` when the server is unreachable or rejects the query. The ping sends a query because opening a pooled connection can return an idle one without a round trip. The same `PingAsync` pair on `DbConnection` (`HeadlessDbConnectionPingExtensions`, namespace `System.Data.Common`) runs the query on a connection you already opened. It neither opens nor disposes that connection, and a closed connection fails with the driver's `InvalidOperationException`.

### Ambient connection (`ISqlCurrentConnection`)

`ISqlCurrentConnection` lets several repositories in one scope share one connection:

```csharp
public interface ISqlCurrentConnection : IAsyncDisposable
{
    ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default);
}
```

The provider registrations bind it to `DefaultSqlCurrentConnection` (`Headless.Sql`) as scoped. It opens one connection on first use, is safe for concurrent callers, and reopens the connection when it finds it closed. Disposing the scope disposes the connection in any state, including one that broke or closed during the scope.

### Connection string checker

`IConnectionStringChecker.CheckAsync(connectionString, cancellationToken)` returns a `ConnectionCheckResult` (`Connected`, `DatabaseExists`). `Connected` says the server is reachable. `DatabaseExists` says the target database exists. A cancelled token throws `OperationCanceledException`. Every other connection error is logged and reported through the result. Use it for startup validation or a custom health check.

| Provider | How it checks |
| --- | --- |
| PostgreSQL | Connects to the `postgres` database with a 1-second connect timeout, then switches to the target. If the switch fails, `Connected` is `true` and `DatabaseExists` is `false`. |
| SQL Server | Connects to `master`, then switches to the target. |
| SQLite | Opens the file, which SQLite creates when missing. Both flags are `true` on success and `false` on failure. |

### Shared connection and schema for storage features

The raw PostgreSQL and SQL Server providers of Messaging storage, Coordination, DistributedLocks, AuditLog, Sequences, Features, Permissions, Settings, Fencing, and Idempotency each have a parameterless `UsePostgreSql()` and `UseSqlServer()`. The SQLite providers of Sequences, Idempotency, Fencing, and Coordination have a parameterless `UseSqlite()`. These overloads read the connection string that `AddPostgreSqlSql`, `AddSqlServerSql`, or `AddSqliteSql` registered, so one registration serves every feature:

```csharp
builder.Services.AddPostgreSqlSql(builder.Configuration.GetConnectionString("Default")!);

builder.Services.AddHeadlessFencing(setup => setup.UsePostgreSql());
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql());
builder.Services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());
```

Every relational feature creates its tables in the `headless` schema (`HeadlessStorageDefaults.Schema` in `Headless.Hosting`) unless you configure another. Each feature prefixes its object names with the feature name, such as `fencing_leases` and `idempotency_records` on PostgreSQL or `FencingLeases` and `IdempotencyRecords` on SQL Server, so all features fit in one schema.

Names follow the database's convention: snake_case on PostgreSQL, PascalCase on SQL Server. A feature whose EF Core mapping takes a `StorageNamingStyle` expects the style of its target database. `HeadlessStorageNaming.ForProvider(Database.ProviderName)` returns that style, so the EF Core mapping produces the same objects as the raw provider.

Precedence, per feature:

- **Connection.** An explicit overload (`UsePostgreSql(connectionString)`, `UsePostgreSql(IConfiguration)`, or an options callback that sets `ConnectionString`) wins for that feature and ignores the shared registration. The parameterless overload uses only the shared connection.
- **Schema.** `ConfigureStorage(storage => storage.Schema = "…")` on a feature's setup builder moves that feature's objects only. Sequences has no `ConfigureStorage`. Set `Schema` on `PostgreSqlSequencesOptions` or `SqlServerSequencesOptions` instead.

The parameterless overloads read the connection string when the feature's options are first resolved, so `AddPostgreSqlSql` can come before or after the feature. Resolution throws `InvalidOperationException` when no `ISqlConnectionFactory` is registered, or when the registered factory belongs to another provider, such as `UsePostgreSql()` with `AddSqlServerSql`.

Your own code reads the shared connection string with `IServiceProvider.GetPostgreSqlConnectionString()`, `GetSqlServerConnectionString()`, or `GetSqliteConnectionString()`, which throw the same `InvalidOperationException`.

The EF Core storage variants and Jobs take their connection from the `DbContext` and have no parameterless overload.

### Schema runner: apply, verify, and deploy-time scripts

Every raw PostgreSQL, SQL Server, and SQLite storage feature contributes its DDL to one `SchemaRunner` (namespace `Headless.Hosting.Schema`). Each contribution is a `Schema` [resource](utilities.md#resource-provisioning) named `{dialect}/{schema}/{feature}`, such as `PostgreSql/headless/Fencing`, with startup check `FailStart`.

| Who | Writes DDL | How |
|---|---|---|
| A host start | Never | Verifies every contribution in one history read per schema. Fails the start on a missing step or a changed checksum, naming the fix. |
| `dotnet MyApp.dll resources provision` | Missing steps only | Applies every contribution, export-only ones included, one output line per feature. |
| `AddDeployStepsOnStartup()` | Missing steps only | Provisions every resource as the host starts, before the check. For development and test hosts. `HeadlessTestServer` calls it by default. |
| The exported script | Guarded statements | Applied with `psql`, `sqlcmd`, or the `sqlite3` shell by the deployment. |

How the runner applies steps:

- It applies every missing step for one database in one pass, under one lock per database: `pg_try_advisory_lock` on PostgreSQL, `sp_getapplock` on SQL Server, and a leased row in `headless_schema_lock` on SQLite. Other replicas wait up to `LockTimeout`.
- It records each applied step with a SHA-256 checksum of its SQL in `headless_schema_history`, in the feature's schema. A recorded step never runs again. A warm start reads the history once per schema and takes no lock.
- When something outside the lock creates the same schema or table first, such as your own EF Core migration, the runner re-runs the failed step once in a fresh transaction. A table a migration tool already created is fine, because every step is idempotent.
- A custom `IResourceProvisioner` that must run after every schema step, as EF Core migrations do, returns the provisioners whose kind is `SchemaRunner.ResourceKind` from its `DependsOn`.
- `resources provision` treats each feature as its own resource, so it opens a connection and takes the lock once per feature. That cost is at deploy time, not at startup.

`services.AddHeadlessSchemaRunner(options => …)` sets:

| Option | Default | Effect |
|---|---|---|
| `CommandTimeout` | 10 minutes | Timeout for every runner statement, DDL included. Each feature's own `CommandTimeout` does not apply. |
| `LockTimeout` | 2 minutes | How long a replica waits while another replica applies steps. |

To deploy with a reviewable script, export it from the same registrations the application uses, and apply it with `psql` or `sqlcmd`:

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

The script is deterministic for a set of registrations, so you can commit and diff it. Every statement is guarded, so running it twice changes nothing. It inserts the same history rows the runner writes, so a host start verifies the database afterwards. Pass `SqlServerSchemaDialect.Instance` for a SQL Server script, which separates batches with `GO`, or `SqliteSchemaDialect.Instance` for a script the `sqlite3` shell runs as one file. When an EF Core migration depends on these tables, apply this script before the EF Core migration script or bundle.

**Verify:** `dotnet MyApp.dll resources verify -k Schema` reports `Ok` for every feature and exits `0`. Before provisioning, the host start fails, naming each missing feature and `resources provision`.

Rules that change how you operate the database:

- **The runner trusts its history.** A hand-dropped table is not recreated while its history rows remain. Remove a feature with `resources delete -n {dialect}/{schema}/{feature}`, which drops its objects and deletes its history rows, so the next `resources provision` creates them again. After a hand drop, delete that feature's rows from `headless_schema_history`, or drop the whole schema.
- **Purge and delete stay inside one feature.** `resources purge` deletes the rows of the feature's tables in one transaction, and keeps its host-state tables, sequences, and history. `resources delete` drops the feature's tables and sequences, then deletes its history rows. Neither touches another feature in the same schema. Outside the destructive environments, both need `--force` (see [Resource provisioning](utilities.md#resource-provisioning)). A feature that declares no objects, such as Arabic search, reports both as `Unsupported`.
- **A missing history table is checked against the catalog.** When a history read reports the table missing, the runner runs `ISchemaDialect.HistoryTableExistsSql(schema)` and, if the table exists, reads the history again. A pooled SQLite connection can report a table another replica just created as missing. If the history still cannot be read, verify fails with `SchemaRunnerException`; it never reports applied steps as missing.
- **A changed checksum fails the start.** The error names the feature and the step version.
- **Configuration that shapes DDL is part of the checksum.** Changing AuditLog's `JsonColumnType` after its table exists fails the start instead of being ignored.
- **A configurable object name keeps its own history.** Sequences with the default table records `Sequences/1`. With `TableName = "counters"`, it records `Sequences:counters/1`, so two hosts that name the table differently in one schema never collide.
- **Export-only contributions** (`SchemaContribution.ExportOnly`) appear in `ExportScript` and in `resources provision`, but the host never verifies them. A Messaging additional outbox on its own database uses one, so the deploy script still creates its objects.
- **Host-state tables** are tables whose rows a feature writes at startup or keeps live, not application data: the Features, Permissions, and Settings definition tables on PostgreSQL and SQL Server, and the Coordination membership tables. Test data resets keep their rows. See [Tables a reset preserves](testing.md#tables-a-reset-preserves).
- **History table name.** `headless_schema_history` on PostgreSQL and SQL Server. On SQLite, where a schema is a table-name prefix, `<schema>_headless_schema_history`.
- **Hosts with different feature sets can share one schema.** History rows of features a host does not register are ignored. A row for a registered feature whose step this host does not know, such as a newer replica's step during a rolling deploy, is logged as `SchemaRunnerUnknownStep` and is not fatal.

#### Schema runner observability

The runner emits traces and metrics under `Headless.SchemaRunner` (`SchemaRunnerDiagnostics.SourceName`, for both the `ActivitySource` and the `Meter`). Subscribe with `tracing.AddSchemaRunnerInstrumentation()` and `metrics.AddSchemaRunnerInstrumentation()` (namespaces `OpenTelemetry.Trace` and `OpenTelemetry.Metrics`), or with `AddSource` and `AddMeter` and the constant. `builder.AddHeadless()` with `OpenTelemetry.Enabled` already subscribes to every `Headless.*` source and meter. Nothing is recorded until something subscribes.

Spans: one `schema_runner.apply` or `schema_runner.verify` span per pass, including the startup verification. It has a `schema_runner.lock_wait` child per database that takes the lock and a `schema_runner.step` child per applied step. The driver's DDL spans nest under the step span.

| Instrument | Kind | Unit | Attributes | Meaning |
| --- | --- | --- | --- | --- |
| `headless.schema_runner.duration` | Histogram | `ms` | `headless.schema_runner.mode` (`apply`, `verify`), `headless.schema_runner.outcome` (`success`, `failure`), `error.type` on failure | One pass, every database it reaches included. A start refused for a checksum or a missing step is a `failure` with `error.type` `Headless.Hosting.Schema.SchemaRunnerException`. |
| `headless.schema_runner.lock.wait.duration` | Histogram | `ms` | `headless.schema_runner.dialect`, `headless.schema_runner.lock.outcome` (`acquired`, `timed_out`, `failed`) | Time a pass waited for another replica's lock. `failed` means the lock query failed or the wait was cancelled. A warm database takes no lock and records nothing. |
| `headless.schema_runner.steps` | Counter | `{step}` | `headless.schema_runner.dialect`, `headless.schema_runner.feature`, `headless.schema_runner.step.outcome` (`applied`, `skipped`) | Steps an apply pass ran, or skipped because the history records them. Verify passes record none. |
| `headless.schema_runner.mismatches` | Counter | `{mismatch}` | `headless.schema_runner.mode`, `headless.schema_runner.dialect`, `headless.schema_runner.feature`, `headless.schema_runner.mismatch.kind` (`missing`, `unknown`, `checksum`) | History disagreements a pass found. Apply passes never report `missing`. |
| `headless.schema_runner.absorbed_races` | Counter | `{race}` | `headless.schema_runner.dialect` | Steps re-run because a creator outside the runner committed the same object first. |

`headless.schema_runner.feature` is the feature name alone (`Sequences`, never `Sequences:counters`). No signal carries a schema name, SQL text, the database identity, or connection details. Span statuses carry no description, because a driver message can quote object names.

### Store statement kit (for provider authors)

Read this section only when you write your own relational store. Application code that uses the built-in features never calls the kit. A store is written once against `ISqlDialect` (`Headless.Sql`), with `PostgreSqlDialect.Instance`, `SqlServerDialect.Instance`, and `SqliteDialect.Instance` as the engines. The dialect renders a fixed set of statement shapes, each with the same locking and clock rules on every engine:

| Shape | Does | Result |
| --- | --- | --- |
| `SqlLockedRead` | Reads one row by key under an update-intent lock (a key-range lock when absent). | The row, or none |
| `SqlFencedTransition` | One conditional `UPDATE` whose `WHERE` is the fence. Run it after a locked read of the same key. | Applied flag, then the written columns |
| `SqlInsertIfAbsent` | Inserts the keyed row only when no row has the key, never raising a duplicate-key error. | Inserted flag, then the columns |
| `SqlUpsert` | Inserts the keyed row, or updates it when an optional guard allows. | `SqlUpsertOutcome` (`Refused`, `Inserted`, `Updated`), then the columns |
| `SqlClaimNext` | Claims the first row, or the first `@BatchSizeParameter` rows, matching a filter, skipping locked rows. | One row per claimed row |
| `SqlDeleteBatch` | Deletes up to `@BatchSizeParameter` matching rows, skipping locked rows. | Row count |
| `SqlLockBatch` | Locks the first `@BatchSizeParameter` matching rows, in order, skipping locked rows, for later statements of the same transaction. | One row per locked row |
| `SqlInsert` | Inserts one row. A duplicate key still raises. | The written columns |
| `SqlTransactionLock` | Takes an exclusive lock on each named resource until the transaction ends, in list order, in one command, waiting while another transaction holds one (an advisory lock on PostgreSQL, `sp_getapplock` on SQL Server). List overlapping names in one agreed order, such as ascending, or callers can deadlock. | None |
| `SqlClockedStatement` | Any one statement that reads the database clock without waiting on a lock first. Set `ReadsOneRow = true` on a read of at most one row, such as a primary-key lookup, so PostgreSQL reads the clock inline when the statement names `{now}` once. | The statement's own |

Rules:

- **Clock.** Write `SqlDialectTokens.Now` (`{now}`) for the database clock, never the application clock, and `SqlDialectTokens.Stored` (`{stored}`) for the stored row in an upsert's `Set` and `Guard`. PostgreSQL reads `clock_timestamp()` once per statement. A locked read, fenced transition, claim, and batch lock read the clock after their rows are locked. `SqlUpsert` and `SqlInsertIfAbsent` may read it before waiting on a concurrent writer of the same key, so decide a lease or a due time with a locked read and a fenced transition, not with an upsert guard that compares against `{now}`.
- **Partial keys.** `SqlLockedRead` and `SqlInsertIfAbsent` take an optional `KeyPredicate`: the filter of the partial unique index the key belongs to, written with the same literals as the index, so PostgreSQL infers the index as the conflict target and SQL Server matches its filtered index.
- **Expressions.** `BooleanLiteral` renders a literal, so a filtered index stays usable. `NewGuid` draws a random identifier per row. `ShiftBySeconds` adds an integer column of seconds to an instant. `NextChangeMarker` gives successive writes of one row strictly increasing markers. `Limit` is the clause after `ORDER BY` that keeps a page. `LikeIgnoringCase` is `ILIKE` on PostgreSQL and `LIKE` under the column's collation on SQL Server, with a backslash as the escape character. `ReadWithoutWaiting` is a table reference for a dashboard read that never waits on a locked row: PostgreSQL returns the row as last committed, and SQL Server skips it.
- **Results.** `SqlFencedCommand.ExecuteAsync` and `SqlUpsertCommand.ExecuteAsync` return `SqlFenced<TRow, TAccepted>` and `SqlUpserted<T>`. Their written values are reachable only through `Match`, so a store must handle the refusal.
- **Lists.** `InList(expression, parameter, elementType)` with `AddListParameter` binds a whole list as one parameter, and `InTuples` with `CreateTupleListParameters` matches a list of rows as whole tuples. See [List and tuple predicates](#list-and-tuple-predicates).
- **Parameters.** `AddParameter` types each value by `SqlColumnType` (`KeyText`, `Text`, `Int16`, `Int32`, `Int64`, `Timestamp`, `Binary`, `FixedBinary`, `Guid`, `Boolean`, `Json`). Text is sized to its column, so comparisons keep the column's collation. For a lookup value, use `SqlColumnType.LookupText(maxLength, value)` (or its list overload): it binds unsized when the value is longer than the column, because a sized SQL Server parameter would truncate it and match rows that store its prefix. `FixedBinary(n)` binds SQL Server `binary(n)`, so a seek on a fixed-length key keeps its index. A `ReadOnlyMemory<byte>` value can be bound without a copy, so leave its bytes unchanged until the command has run. `AddDuration` and `ShiftByDuration` add a `TimeSpan` to an instant without the SQL Server `DATEADD` int overflow.
- **Errors.** `Classify` maps a driver exception to `SqlErrorKind`: `UniqueViolation`, `Deadlock`, `SerializationConflict`, `DuplicateObject`, `LockTimeout`, or `TransactionAborted`. `TransactionAborted` means the statement ran on a transaction an earlier error ended or doomed (PostgreSQL `25P02`, SQL Server `3930`, and on SQLite the driver's `InvalidOperationException` for a transaction SQLite rolled back). That transaction is lost, so roll the unit back instead of retrying inside it.
- **Autonomous calls.** `SqlAutonomousTransaction.RunAsync(operation, createConnection, body, timeProvider, cancellationToken)` runs one store call on its own READ COMMITTED transaction, at most 3 attempts, each on a fresh connection. It retries a fault only when `RelationalTransientFaults.IsTransient` (`Headless.UnitOfWork`) calls it transient and it was raised before the commit started. Transient means a deadlock, a serialization conflict, a lock timeout, a dropped connection, a capacity fault, on SQL Server EF Core's `EnableRetryOnFailure` error numbers minus the client command timeout, and on SQLite `SQLITE_BUSY` and `SQLITE_LOCKED`. A fault from the commit is never retried, because the commit may have landed before it failed on the wire. It surfaces unchanged. A fault observed after the caller's token was cancelled is not retried either. The call never retries inside a caller's transaction.
- **Autonomous reads.** `SqlAutonomousRead.RunAsync(operation, createConnection, body, timeProvider, cancellationToken)` runs a one-statement read on its own connection with no transaction, and retries every transient fault, up to the same 3 attempts. Prefer it for a single-statement read, because it skips the `BEGIN` and `COMMIT` round trips, and on SQLite it never waits behind a writer. A read of two statements whose results must agree uses `SqlAutonomousTransaction.RunAsync`.
- **Custom autonomous loops.** `SqlAutonomousTransaction.RetryAsync` applies the same rule to an attempt that owns its own connection and transaction. The attempt receives a `SqlAutonomousAttempt` and calls `MarkCommitStarted()` immediately before it commits. An optional `onRetry` callback receives the fault and the number of the attempt about to run.
- **Operation names.** Pass `operation` as a constant in `feature.verb` form, such as `fencing.grant`. It is recorded as `headless.sql.operation`, and each distinct value is a separate metric series, so a value built from a key, tenant, or message name multiplies the series without bound. A blank value throws `ArgumentException` before any connection opens. Share one constant between providers of the same store.
- **Portable values.** `SqlPortable.Truncate` truncates a duration to the microsecond every engine keeps. Check key text with `Argument.IsPortableKey` (`Headless.Checks`).
- **Enlistment.** Before writing inside a caller's unit of work, run the checks in [For store authors](unit-of-work.md#for-store-authors).

#### List and tuple predicates

A custom `ISqlDialect` implements these members, and a store calls them:

| Member | Contract |
| --- | --- |
| `InList(expression, parameter, elementType)` | A predicate that `expression` equals one element of the list parameter. An empty list matches nothing. |
| `AddListParameter<T>(command, parameter, elementType, values)` and `CreateListParameter<T>(parameter, elementType, values)` | Bind the list `InList` reads as one parameter. Throw `ArgumentException` for a `Binary` or `Json` element type. |
| `InTuples(expressions, parameter, elementTypes)` | A predicate that the tuple of `expressions` equals one row of the list as a whole, so pairs such as (id, expected version) never match across rows. An empty list matches nothing. Throws `ArgumentException` when the counts differ, when there are fewer than two expressions, or for a `Binary` or `Json` element type. |
| `CreateTupleListParameters(parameter, elementTypes, rows)` | The parameters `InTuples` reads: one per element on an engine that binds a list per column, or one for the whole list. Every row holds one non-null value per element type, in order. |

Keep the statement text independent of the list length, so the engine reuses one cached plan, and require no table type created ahead of time. The built-in dialects render:

| Engine | `InList` | `InTuples` |
| --- | --- | --- |
| PostgreSQL | `expr = ANY(@p)` over one array parameter | `(a, b) IN (SELECT * FROM unnest(@p_0, @p_1))`, one array per column, zipped into rows |
| SQL Server | `expr IN (SELECT [value] FROM OPENJSON(@p) WITH ([value] <type> '$'))`, one `nvarchar(max)` JSON parameter | `EXISTS (SELECT 1 FROM OPENJSON(@p) WITH ([c0] <type> '$[0]', …) AS t WHERE t.[c0] = a AND …)`, because T-SQL has no row-value `IN` |
| SQLite | `expr IN (SELECT value FROM json_each(@p))` | `(a, b) IN (SELECT value ->> 0, value ->> 1 FROM json_each(@p))` |

#### Contribute schema steps

A store that owns tables registers its DDL with the schema runner, so `resources provision`, the startup check, and `ExportScript` cover it like the built-in features:

```csharp
using Headless.Hosting.Schema;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Options;

builder.Services.AddHeadlessSchemaContribution(sp =>
{
    var options = sp.GetRequiredService<IOptions<WidgetStoreOptions>>().Value;

    return new SchemaContribution(
        feature: SchemaContribution.FeatureId("Widgets", (options.TableName, "widgets")),
        dialect: PostgreSqlSchemaDialect.Instance,
        createConnection: () => PostgreSqlDialect.Instance.CreateConnection(options.ConnectionString),
        schema: options.Schema,
        steps:
        [
            new SchemaStep(
                "1",
                "Create the widgets table.",
                $"CREATE TABLE IF NOT EXISTS \"{options.Schema}\".\"{options.TableName}\" (id uuid PRIMARY KEY, name text NOT NULL);"
            ),
        ],
        objects: [SchemaObject.Table(options.TableName)]
    );
});
```

`WidgetStoreOptions` is your store's own options class.

| Member | Contract |
| --- | --- |
| `SchemaStep(version, description, sql)` | One idempotent DDL batch: `IF NOT EXISTS` on every create and catalog checks around every alter, because the runner re-runs a step once after a race and the exported script must be safe to run twice. A step never creates the schema. The runner creates it with the history table. Versions are unique within the feature. |
| `SchemaContribution(feature, dialect, createConnection, schema, steps, exportOnly, hostStateTables, objects)` | The ordered steps of one feature in one schema of one database. `createConnection` returns an unopened connection. The runner opens and disposes it. `feature` is recorded in the history table, so renaming it orphans the history. |
| `SchemaContribution.FeatureId(feature, (configured, default)…)` | The history identity of a feature whose object names are configurable: `feature` alone when every name is its default, else `feature:name`, so two hosts that name the objects differently in one schema keep separate histories. Put every configurable object name in it. |
| `objects:` with `SchemaObject.Table(name)` and `SchemaObject.Sequence(name)` | The tables and sequences the steps create, in creation order, named from the same options as the steps. `resources purge` deletes the tables' rows and `resources delete` drops the objects, both in reverse order, so a foreign key never blocks them. A list out of creation order can fail on a foreign key. Indexes, constraints, and triggers go with their table. Purge keeps sequences, so a generation never repeats. Omit `objects` only when the contribution cannot be cleared or torn down. Purge and delete then report it as `Unsupported`. |
| `hostStateTables:` | The unquoted names of tables whose rows the store writes at startup from code or keeps live while the host runs, not application data. A test data reset keeps their rows, because the running host never writes them again. On SQLite, the name carries the schema prefix. |
| `exportOnly: true` | The contribution appears in `ExportScript` and `resources provision`, and the host never verifies it. Use it when the store applies that database's steps with a runner of its own. |

**Never edit a released step.** The history records a checksum of each applied step's SQL, so a changed step fails every start that already applied it, naming the feature and version. To change a released schema, add a step with a new version. A step that no released version has shipped can still change in place, because no database has recorded it.

**Verify:** `dotnet MyApp.dll resources list` shows `PostgreSql/<schema>/Widgets`, and `resources provision` followed by `resources verify -n PostgreSql/<schema>/Widgets` reports `Ok`.

#### Implement a schema dialect

`ISchemaDialect` (`Headless.Hosting.Schema`) is the database-specific half of the runner. The built-in dialects are `PostgreSqlSchemaDialect`, `SqlServerSchemaDialect`, and `SqliteSchemaDialect`. A custom dialect implements:

| Member | Contract |
| --- | --- |
| `Name` | A stable name. Contributions group by it together with the database identity, and it starts the resource name. |
| `ScriptBatchSeparator` | The line between batches in the exported script, such as `GO`, or `null` when the client runs one file as one script. |
| `DatabaseIdentity(connection)` | A stable identity for the database an unopened connection targets. Two connection strings that reach one database must return the same value, because the lock and the single pass are keyed on it. |
| `CreateStringParameter(name, value)` | A string command parameter. |
| `TryAcquireLockSql` | A statement that tries once, without waiting, to take the session lock named by `@LockResource`, returning a scalar that is true or non-zero on success. The runner polls it. A blocking acquire would hold a snapshot open while it waits. |
| `ReleaseLockSql` | Releases the lock named by `@LockResource`. It must not throw when the lock is not held. |
| `HistoryTableSql(schema)` | Idempotent DDL that creates the schema and its history table. |
| `HistoryTableName(schema)` | The unquoted name the history table is stored under: `SchemaRunner.HistoryTableName` (`headless_schema_history`), or that name prefixed with the schema on a database without schemas. Test data resets keep the table by this name. |
| `HistoryTableExistsSql(schema)` | A query whose single integer result is non-zero when the history table exists. It must read the database catalog when it runs, never a cached copy. The runner calls it only after `ReadHistorySql` reports the table missing. |
| `ReadHistorySql(schema)` | Reads the history, projecting feature, step version, checksum, and description, in that order. |
| `InsertHistorySql(schema)` | Records one step from `@Feature`, `@StepVersion`, `@Description`, and `@Checksum`, and does nothing when the row exists. |
| `IsAlreadyCreatedRace(exception)` | `true` when another creator committed the same object first. The runner then re-runs the step once in a fresh transaction. |
| `IsObjectNotFound(exception)` | `true` when the history table or its schema does not exist. |
| `DeleteRowsSql(schema, table)` | Deletes every row of a declared table. |
| `DropObjectSql(schema, schemaObject)` | Drops a declared object, doing nothing when it does not exist. |
| `DeleteFeatureHistorySql(schema)` | Deletes every history row of the feature named by `@Feature`, so a later run applies its steps again. |

#### Store kit observability

`SqlAutonomousTransaction` and `SqlAutonomousRead` emit traces and metrics under `Headless.Sql` (`SqlDiagnostics.SourceName`, for both the `ActivitySource` and the `Meter`). The built-in stores use them, so these signals cover Coordination, Fencing, Idempotency, Sequences, Messaging storage, and the Jobs claim scopes. Subscribe with `tracing.AddSqlInstrumentation()` and `metrics.AddSqlInstrumentation()` (namespaces `OpenTelemetry.Trace` and `OpenTelemetry.Metrics`), or with `AddSource` and `AddMeter` and the constant. `builder.AddHeadless()` with `OpenTelemetry.Enabled` already subscribes to every `Headless.*` source and meter. With nothing subscribed, a call records nothing.

Each call is one `sql.autonomous_transaction` span carrying `headless.sql.operation`. The attempts' driver spans nest under it. Each retry adds a `headless.sql.retry` span event carrying `headless.sql.attempt` (the attempt about to run), `error.type`, and `db.response.status_code`. The span ends with `headless.sql.outcome` and `headless.sql.attempts`, and with an error status and `error.type` when the call fails.

| Instrument | Kind | Unit | Attributes | Meaning |
| --- | --- | --- | --- | --- |
| `headless.sql.autonomous.duration` | Histogram | `ms` | `headless.sql.operation`, `headless.sql.outcome`, `error.type` on failure | One call, every attempt and retry delay included. |
| `headless.sql.autonomous.attempts` | Histogram | `{attempt}` | `headless.sql.operation`, `headless.sql.outcome`, `error.type` on failure | Attempts the call made, the first included: 1 to 3. |
| `headless.sql.autonomous.retries` | Counter | `{retry}` | `headless.sql.operation`, `error.type`, `db.response.status_code` when the driver reports one | One per retried attempt: a transient fault raised before the commit started. |

The built-in stores name their calls by feature: `coordination.*` (membership), `fencing.*` (leases), `idempotency.*`, `sequences.increment`, `messaging.*` (one name per storage method, such as `messaging.lease_publish` or `messaging.claim_delayed_messages`), and `jobs.claim_*` for the Jobs claim scopes.

`headless.sql.outcome` says why a call ended:

| Value | Meaning |
| --- | --- |
| `success` | An attempt returned. |
| `retries_exhausted` | The last attempt failed with a transient fault before its commit. |
| `non_transient` | An attempt failed before its commit with a fault that is not transient. |
| `commit_fault` | An attempt failed after its commit started. The fault surfaced unchanged and was not retried. Alert on it: the write may or may not have landed. |
| `canceled` | The caller cancelled before the commit started. |

`error.type` is the full type name of the outermost `DbException` in the fault, else of the thrown exception. `db.response.status_code` is the SQLSTATE (`40P01`, `40001`) or, on SQL Server, the error number (`1205`). No signal records SQL text, parameters, keys, or connection details, and span statuses carry no description, because a driver message can quote key values. Jobs also logs `JobsClaimTransientRetry` on a claim retry.

#### SQLite in the kit

SQLite has no row locks, no `SKIP LOCKED`, no advisory locks, no schemas, and no server clock. Storage features on SQLite behave as follows:

- **One write lock instead of row locks.** `Microsoft.Data.Sqlite` begins every transaction with `BEGIN IMMEDIATE` unless you pass `deferred: true`, so a store transaction holds the database write lock from its first statement. A deferred caller transaction takes the lock at the store's first locking read. Claims and batch deletes skip nothing, because no other writer can hold a row. Writers are serialized per database file, so do not use SQLite for write-heavy concurrent workloads.
- **Waits block a thread.** `Microsoft.Data.Sqlite` waits for the write lock on the calling thread. An autonomous call, a SQLite unit of work's begin, and the schema runner return a pending task first, but the call still holds a thread-pool thread for up to the busy timeout.
- **Busy, not deadlock.** A writer that waits longer than the connection's `Default Timeout` (30 seconds) fails with `SQLITE_BUSY`. `Classify` reports it as `LockTimeout` (`SerializationConflict` for `SQLITE_BUSY_SNAPSHOT`), and the retry treats it as transient. A deferred transaction that read before writing can fail this way while another writer is active, so begin transactions `IMMEDIATE`, the driver's default. SQLite never picks a deadlock victim.
- **Instants as text.** A `Timestamp` column is `TEXT` holding `yyyy-MM-dd HH:mm:ss.ffffff+00:00`: UTC with six fractional digits, so text order is time order. A bound instant is converted to UTC and truncated to the microsecond. `Microsoft.Data.Sqlite` reads it back as `DateTimeOffset`.
- **The clock.** `{now}` is SQLite's `'now'`: millisecond resolution, fixed for the whole statement, and read after the write lock is taken. It is the clock of the host that runs the statement. Every process that opens one SQLite file must run on the file's host, because SQLite locking does not work over a network file system, so all of them read one clock.
- **Other storage forms.** `Guid` is upper-case text, the form EF Core's SQLite provider writes. `Boolean` is `0` or `1`. `Json` is text.
- **Schemas are name prefixes.** Schema `headless` and table `fencing_leases` become the table `headless_fencing_leases`. Index names are scoped to the whole file.
- **No generations inside a caller's transaction.** A table-backed sequence rolls back with the transaction that drew from it, so a rolled-back value would be drawn again. The SQLite Idempotency and Fencing providers refuse enlisted calls that draw a generation or fencing token, and draw only in transactions they commit themselves.
- **Named locks.** `SqlTransactionLock` takes no extra lock. A transaction begun `IMMEDIATE` already holds the database write lock, which serializes every holder of every name. A deferred transaction gets no lock from it.
- **Case-insensitive match.** `LikeIgnoringCase` ignores case for ASCII letters only, so `É` does not match `é`.
- **Reads.** A plain read takes no write lock and returns the last committed rows. Under the default rollback journal, it can wait for the moment a writer commits.

**Use WAL journal mode.** Run `PRAGMA journal_mode=WAL;` once on the database file. The setting is stored in the file, so later connections inherit it. In WAL mode, readers read the last committed state while a writer commits, instead of blocking during each commit. That helps every read outside a store transaction, such as the Idempotency peek. Writers stay serialized. Trade-offs:

- SQLite keeps two more files beside the database, `<file>-wal` and `<file>-shm`. Back up and move all three together, or checkpoint first with `PRAGMA wal_checkpoint(TRUNCATE);`.
- WAL uses shared memory, so every process must run on the host that holds the file. SQLite locking already requires that.
- The Headless providers never set the journal mode, because it belongs to the database, not to one feature. Set it once in the application, for example right after creating the file.

## Choosing a Provider

| Provider | Package | ADO.NET driver | Use when | Avoid when |
|---|---|---|---|---|
| PostgreSQL | `Headless.Sql.PostgreSql` | Npgsql | Production PostgreSQL, or you need Npgsql features (`COPY`, `LISTEN`/`NOTIFY`, type mapping). | The code must stay provider-neutral and would inject the concrete factory. |
| SQL Server | `Headless.Sql.SqlServer` | `Microsoft.Data.SqlClient` | Production SQL Server or Azure SQL, or you need `SqlConnection` features such as bulk copy. | The database is PostgreSQL or SQLite. |
| SQLite | `Headless.Sql.Sqlite` | `Microsoft.Data.Sqlite` | In-process tests with `:memory:`, and embedded, edge, or single-file deployments. | Concurrent write-heavy production workloads, because the write lock serializes writers. |

---

## Headless.Sql.Abstractions

The provider-neutral contracts: `ISqlConnectionFactory`, `ISqlCurrentConnection`, `IConnectionStringChecker`, and `ConnectionCheckResult`. It registers no services. Reference it from a library that must not depend on a provider.

### Setup

```bash
dotnet add package Headless.Sql.Abstractions
```

Depend on the interface and let the application register a provider:

```csharp
using Dapper;

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

**Verify:** no startup signal from this package. The provider registration supplies the proof: a `sql-postgresql` or `sql-sqlserver` health check, or a test that calls the repository.

---

## Headless.Sql

The default `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`), the `PingAsync` extensions, and the store kit's autonomous calls and telemetry (`SqlAutonomousTransaction`, `SqlAutonomousRead`, `SqlDiagnostics`, `AddSqlInstrumentation()`). Every provider package references it, and every `Add{Provider}Sql` call registers `DefaultSqlCurrentConnection` as scoped.

### Setup

```bash
dotnet add package Headless.Sql
```

Register it yourself only when you register a provider factory by hand instead of through `Add{Provider}Sql`:

```csharp
builder.Services.AddSingleton<ISqlConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();
```

**Verify:** in a test, `await factory.PingAsync(ct)` completes, and it throws the driver's `DbException` when the server is unreachable.

---

## Headless.Sql.PostgreSql

The PostgreSQL connection factory (`NpgsqlConnectionFactory`) and checker (`NpgsqlConnectionStringChecker`), backed by Npgsql.

### Setup

```bash
dotnet add package Headless.Sql.PostgreSql
```

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Registers ISqlConnectionFactory, IConnectionStringChecker, and a scoped ISqlCurrentConnection.
builder.Services.AddPostgreSqlSql(connectionString);
```

Use it in a repository through `ISqlConnectionFactory`:

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

**Verify:** the `sql-postgresql` readiness health check (tags `ready`, `headless`, `database`) reports `Healthy`. See [Health checks](utilities.md#health-checks).

### Configuration

Resolve the connection string from the service provider with the factory overload:

```csharp
services.AddPostgreSqlSql(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetConnectionString("Postgres")!;
});
```

### Design and runtime behavior

- `AddPostgreSqlSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` as scoped.
- It adds the `sql-postgresql` readiness health check, which runs `PingAsync` on a new connection each time health is checked. Storage features that reuse the shared connection are covered by it. Remove it with `services.RemoveHealthChecks(...)`.
- `IServiceProvider.GetPostgreSqlConnectionString()` returns the registered connection string. The storage features' parameterless `UsePostgreSql()` calls it.
- `UnitOfWorkFactoryPostgreSqlExtensions` (namespace `Headless.UnitOfWork`) adds raw-ADO unit-of-work overloads on `IUnitOfWorkFactory`: `BeginAsync(NpgsqlConnection, …)`, `Enlist(NpgsqlConnection, transaction)`, `RunAsync(NpgsqlConnection, …)`, and the replaying `RunAsync(NpgsqlDataSource, …)`. Register the factory with `AddUnitOfWork()`. See [Raw ADO on PostgreSQL](unit-of-work.md#raw-ado-on-postgresql).
- `PostgreSqlSchemaDialect.Instance` is the schema runner's PostgreSQL dialect. Pass it to `SchemaRunner.ExportScript` for a `psql` script.

---

## Headless.Sql.SqlServer

The SQL Server connection factory (`SqlServerConnectionFactory`) and checker (`SqlServerConnectionStringChecker`), backed by `Microsoft.Data.SqlClient`.

### Setup

```bash
dotnet add package Headless.Sql.SqlServer
```

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Registers ISqlConnectionFactory, IConnectionStringChecker, and a scoped ISqlCurrentConnection.
builder.Services.AddSqlServerSql(connectionString);
```

Use it in a repository through `ISqlConnectionFactory`:

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

**Verify:** the `sql-sqlserver` readiness health check (tags `ready`, `headless`, `database`) reports `Healthy`. See [Health checks](utilities.md#health-checks).

### Configuration

Resolve the connection string from the service provider with the factory overload:

```csharp
services.AddSqlServerSql(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetConnectionString("SqlServer")!;
});
```

### Design and runtime behavior

- `AddSqlServerSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` as scoped.
- It adds the `sql-sqlserver` readiness health check, which runs `PingAsync` on a new connection each time health is checked. Remove it with `services.RemoveHealthChecks(...)`.
- `IServiceProvider.GetSqlServerConnectionString()` returns the registered connection string. The storage features' parameterless `UseSqlServer()` calls it.
- `SqlConnection.GetReusableConnectionString(string? configuredConnectionString = null)` (`HeadlessSqlConnectionExtensions`, namespace `Microsoft.Data.SqlClient`) returns a connection string for code that opens connections of its own from an existing connection. Pass the string the connection was configured from, such as EF Core's `Database.GetConnectionString()`, because an opened connection's own string has lost its password. It throws `InvalidOperationException` when a new connection from that string would authenticate differently: the connection holds an `AccessToken`, an `AccessTokenCallback`, or a `Credential`, or the string names a SQL login with no password keyword. An explicit empty password (`Password=;`) is accepted. A token that an interceptor attaches while the connection opens is invisible to it. Jobs' and Messaging's EF Core SQL Server setups call it.
- `UnitOfWorkFactorySqlServerExtensions` (namespace `Headless.UnitOfWork`) adds raw-ADO unit-of-work overloads on `IUnitOfWorkFactory`: `BeginAsync(SqlConnection, …)`, `Enlist(SqlConnection, transaction)`, `RunAsync(SqlConnection, …)`, and the replaying `RunAsync(connectionFactory, …)`. Register the factory with `AddUnitOfWork()`. See [Raw ADO on SQL Server](unit-of-work.md#raw-ado-on-sql-server).
- `SqlServerSchemaDialect.Instance` is the schema runner's SQL Server dialect. Pass it to `SchemaRunner.ExportScript` for a `sqlcmd` script with `GO` batch separators.

---

## Headless.Sql.Sqlite

The SQLite connection factory (`SqliteConnectionFactory`) and checker (`SqliteConnectionStringChecker`), backed by `Microsoft.Data.Sqlite`.

### Setup

```bash
dotnet add package Headless.Sql.Sqlite
```

```csharp
// In-process tests (no server required):
services.AddSqliteSql("Data Source=:memory:");

// File-based embedded database:
services.AddSqliteSql("Data Source=app.db");
```

Use it in a repository through `ISqlConnectionFactory`:

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

**Verify:** `AddSqliteSql` adds no health check. In a test, `await factory.PingAsync(ct)` completes, or `IConnectionStringChecker.CheckAsync` returns `Connected` and `DatabaseExists` both `true`.

### Configuration

Pass `Data Source=<path>` or `Data Source=:memory:` to `AddSqliteSql`. A `:memory:` database is private to one connection and disappears when that connection closes. Storage features need a database file, because the schema runner and every store call open their own connections.

### Design and runtime behavior

- `AddSqliteSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` as scoped.
- SQLite creates the database file on the first open when it does not exist.
- `IServiceProvider.GetSqliteConnectionString()` returns the registered connection string. The storage features' parameterless `UseSqlite()` calls it.
- `UnitOfWorkFactorySqliteExtensions` (namespace `Headless.UnitOfWork`) adds raw-ADO unit-of-work overloads on `IUnitOfWorkFactory`: `BeginAsync(SqliteConnection, …)`, `Enlist(SqliteConnection, transaction)`, `RunAsync(SqliteConnection, …)`, and the replaying `RunAsync(connectionFactory, …)`. Register the factory with `AddUnitOfWork()`. See [Raw ADO on SQLite](unit-of-work.md#raw-ado-on-sqlite).
- `SqliteDialect.Instance` is the store kit's SQLite dialect. See [SQLite in the kit](#sqlite-in-the-kit).
- `SqliteSchemaDialect.Instance` is the schema runner's SQLite dialect. It locks with a leased row in `headless_schema_lock` (lease `SqliteSchemaDialect.LockLease`, one minute) and names the history table `<schema>_headless_schema_history`. Pass it to `SchemaRunner.ExportScript` for a script the `sqlite3` shell runs as one file.
