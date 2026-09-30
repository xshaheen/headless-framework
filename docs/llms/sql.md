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

The raw PostgreSQL and SQL Server providers of Messaging storage, Coordination, DistributedLocks, AuditLog, Sequences, Features, Permissions, Settings, Fencing, and Idempotency each have a parameterless `UsePostgreSql()` and `UseSqlServer()`. These overloads read the connection string that `AddPostgreSqlSql` or `AddSqlServerSql` registered, so one registration serves every feature:

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

Every raw PostgreSQL and SQL Server storage feature contributes its DDL to one schema runner (`SchemaRunner`, namespace `Headless.Hosting.Initialization.Schema`) instead of running its own initializer. Registering a feature is enough; the feature adds its contribution and the runner's hosted initializer. The runner:

- Applies every missing step of every feature that reaches one database in a single pass at host start, under one session lock per database (`pg_try_advisory_lock` or `sp_getapplock`, polled), before any hosted service can use the tables.
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
- **Never edit a shipped step.** A changed step fails startup with a checksum mismatch naming the feature and version. Features evolve their schema by adding a new step.
- **Configuration that shapes DDL is part of the checksum.** Changing AuditLog's `JsonColumnType` after its table exists fails startup instead of being silently ignored.
- **Features with configurable object names keep one history per name.** `Sequences` with the default table records `Sequences/1`; with `TableName = "counters"` it records `Sequences:counters/1`, so two hosts naming the table differently in one schema never collide.
- **`InitializeOnStartup = false`** on a feature keeps its steps out of `Apply` mode. `Verify` mode and `ExportScript` still include them.
- History rows of features a host does not register are ignored, so hosts with different feature sets can share one schema. A row for a registered feature whose step this host does not know, such as a newer replica's step during a rolling deploy, is logged, not fatal.

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

### Design constraints

`SqliteConnectionStringChecker` differs from the PostgreSQL and SQL Server implementations: because SQLite creates the database file when the connection opens, there is no meaningful distinction between "server reachable" and "database exists". Both `ConnectionCheckResult` fields are set to `true` together on a successful open, or both remain `false` on failure.

For in-process testing, prefer `"Data Source=:memory:"` — the database is private to the connection and disappears when the connection closes.

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
