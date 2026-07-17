---
domain: SQL
packages: Sql.Abstractions, Sql.Core, Sql.PostgreSql, Sql.SqlServer, Sql.Sqlite
---

# SQL

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Connection factory abstraction](#connection-factory-abstraction)
    - [Ambient connection (`ISqlCurrentConnection`)](#ambient-connection-isqlcurrentconnection)
    - [Connection string checker](#connection-string-checker)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Sql.Abstractions](#headlesssqlabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Sql.Core](#headlesssqlcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Sql.PostgreSql](#headlesssqlpostgresql)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Sql.SqlServer](#headlesssqlsqlserver)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Sql.Sqlite](#headlesssqlsqlite)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Provider-agnostic SQL connection factory for raw SQL / Dapper scenarios with PostgreSQL, SQL Server, and SQLite backends.

## Quick Orientation

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

## Agent Instructions

- These packages are for **raw SQL / Dapper** scenarios only. For EF Core, use the `Headless.EntityFramework` packages instead.
- Always depend on `ISqlConnectionFactory` from `Headless.Sql.Abstractions` in service code. Never reference `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`, or `SqliteConnectionFactory` in application-layer code.
- Do **not** construct connections directly (`new NpgsqlConnection(cs)` / `new SqlConnection(cs)`). Always go through the factory so the connection string is centralized and the factory can be swapped in tests.
- Connections returned by `CreateNewConnectionAsync()` are **already open** — calling `OpenAsync()` on them again throws an `InvalidOperationException`.
- Always dispose connections with `await using` — they are `IAsyncDisposable`. Holding an open connection unnecessarily may exhaust the connection pool.
- `ISqlCurrentConnection` defines an ambient, lazy-open connection for unit-of-work patterns. `Headless.Sql.Core` provides `DefaultSqlCurrentConnection`; the provider `Add{Provider}Sql` extensions register it as scoped for you.
- `IConnectionStringChecker` is for health checks and startup validation; `Add{Provider}Sql` registers the provider implementation, or register it yourself and inject `IConnectionStringChecker`. Note: `SqliteConnectionStringChecker` always returns `DatabaseExists = true` when connected (SQLite creates the file on open).
- For in-process integration tests, call `AddSqliteSql("Data Source=:memory:")` — it needs no external server.
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

## Choosing a Provider

| Provider | Package | ADO.NET driver | Use when | Avoid when |
|---|---|---|---|---|
| PostgreSQL | `Headless.Sql.PostgreSql` | Npgsql | Production PostgreSQL; need `NpgsqlConnection`-specific features (COPY, LISTEN/NOTIFY, type mapping) | Vendor lock-in is unacceptable in code that must stay abstract |
| SQL Server | `Headless.Sql.SqlServer` | `Microsoft.Data.SqlClient` | Production SQL Server / Azure SQL; need `SqlConnection`-specific features (bulk copy, SQL auth) | PostgreSQL or SQLite environment |
| SQLite | `Headless.Sql.Sqlite` | `Microsoft.Data.Sqlite` | In-process testing with `:memory:`; embedded / edge / single-file deployments | High concurrency production workloads (SQLite write lock serializes writes) |

---
## Headless.Sql.Abstractions

Defines the provider-agnostic interfaces for SQL connection creation and validation.

### Problem Solved

Application code that works with raw SQL should not depend on a specific ADO.NET driver. `ISqlConnectionFactory` decouples the connection-string source and connection-creation lifecycle from service code, making it trivial to switch drivers (e.g., PostgreSQL in production, SQLite in tests) without touching repositories.

### Key Features

- `ISqlConnectionFactory` — create and manage database connections; `GetConnectionString()` retrieves the configured string; `CreateNewConnectionAsync()` returns an already-open `DbConnection`
- `ISqlCurrentConnection` — ambient connection for unit-of-work scopes; lazy-opens on first call, re-opens on drop
- `IConnectionStringChecker` — validate server reachability and database existence; `CheckAsync(connectionString, cancellationToken)` returns a `ConnectionCheckResult` record struct (`Connected`, `DatabaseExists`)

### Installation

```bash
dotnet add package Headless.Sql.Abstractions
```

### Quick Start

```csharp
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

### Dependencies

None.

### Side Effects

None. This is an abstractions package — it registers no services.

---

## Headless.Sql.Core

Default implementation package for provider-agnostic SQL helpers.

### Problem Solved

Keeps `Headless.Sql.Abstractions` limited to interfaces while providing a reusable scoped ambient connection implementation for unit-of-work patterns.

### Key Features

- `DefaultSqlCurrentConnection` — concrete thread-safe implementation of `ISqlCurrentConnection` backed by `AsyncLock`.
- Lazily opens one connection per scope and reuses it until disposal.
- Reopens the underlying connection if it is observed closed.

### Installation

```bash
dotnet add package Headless.Sql.Core
```

### Quick Start

```csharp
builder.Services.AddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();
```

### Configuration

None. Register `DefaultSqlCurrentConnection` explicitly as a scoped `ISqlCurrentConnection`, and register one provider-specific `ISqlConnectionFactory` from `Headless.Sql.PostgreSql`, `Headless.Sql.SqlServer`, or `Headless.Sql.Sqlite`.

### Dependencies

- `Headless.Sql.Abstractions`
- `Nito.AsyncEx`

### Side Effects

None. Register services explicitly.

---

## Headless.Sql.PostgreSql

PostgreSQL connection factory backed by Npgsql.

### Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for PostgreSQL, wrapping Npgsql so repositories can obtain open `NpgsqlConnection` instances through the provider-agnostic interface.

### Key Features

- `NpgsqlConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `NpgsqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `NpgsqlConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `postgres` first, then calling `ChangeDatabaseAsync` to the target
- `SetupPostgreSqlSql.AddPostgreSqlSql(string connectionString)` / `AddPostgreSqlSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection

### Design Notes

`NpgsqlConnectionFactory.CreateNewConnectionAsync()` is declared `public async ValueTask<NpgsqlConnection>` — a covariant return relative to the explicit `ValueTask<DbConnection>` implementation on `ISqlConnectionFactory`. Callers that inject `NpgsqlConnectionFactory` directly (e.g., provider-aware infrastructure code) can use `NpgsqlConnection`-specific APIs (COPY, LISTEN/NOTIFY, Npgsql type mapping) without a cast. Callers that inject `ISqlConnectionFactory` see only `DbConnection`.

`NpgsqlConnectionStringChecker` uses a 1-second connect timeout (`Timeout = 1`) to fail fast in health checks. If the target database cannot be selected, `DatabaseExists` is `false` but `Connected` is `true`.

### Installation

```bash
dotnet add package Headless.Sql.PostgreSql
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Registers ISqlConnectionFactory, IConnectionStringChecker, and a scoped ISqlCurrentConnection.
builder.Services.AddPostgreSqlSql(connectionString);
```

Use in a repository (always inject `ISqlConnectionFactory`, not the concrete type):

```csharp
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

### Dependencies

- `Headless.Checks`
- `Headless.Sql.Abstractions`
- `Headless.Sql.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Npgsql`

### Side Effects

`AddPostgreSqlSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped.

---
## Headless.Sql.SqlServer

SQL Server connection factory backed by `Microsoft.Data.SqlClient`.

### Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQL Server and Azure SQL, wrapping `Microsoft.Data.SqlClient` so repositories can obtain open `SqlConnection` instances through the provider-agnostic interface.

### Key Features

- `SqlServerConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqlServerConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `master` first, then calling `ChangeDatabaseAsync` to the target
- `SetupSqlServerSql.AddSqlServerSql(string connectionString)` / `AddSqlServerSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection

### Installation

```bash
dotnet add package Headless.Sql.SqlServer
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;

// Registers ISqlConnectionFactory, IConnectionStringChecker, and a scoped ISqlCurrentConnection.
builder.Services.AddSqlServerSql(connectionString);
```

Use in a repository:

```csharp
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

### Dependencies

- `Headless.Checks`
- `Headless.Sql.Abstractions`
- `Headless.Sql.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

`AddSqlServerSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped.

---
## Headless.Sql.Sqlite

SQLite connection factory backed by `Microsoft.Data.Sqlite`.

### Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQLite, enabling in-process integration tests with `:memory:` databases and lightweight embedded / edge deployments without a separate database server.

### Key Features

- `SqliteConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqliteConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqliteConnectionStringChecker` — `IConnectionStringChecker` that opens the SQLite database and reports both `Connected` and `DatabaseExists` as `true` on success (SQLite creates the file on open, so the two flags are always identical)
- `SetupSqliteSql.AddSqliteSql(string connectionString)` / `AddSqliteSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection

### Design Notes

`SqliteConnectionStringChecker` differs from the PostgreSQL and SQL Server implementations: because SQLite creates the database file when the connection opens, there is no meaningful distinction between "server reachable" and "database exists". Both `ConnectionCheckResult` fields are set to `true` together on a successful open, or both remain `false` on failure.

For in-process testing, prefer `"Data Source=:memory:"` — the database is private to the connection and disappears when the connection closes.

### Installation

```bash
dotnet add package Headless.Sql.Sqlite
```

### Quick Start

```csharp
// In-process tests (no server required):
services.AddSqliteSql("Data Source=:memory:");

// File-based embedded database:
services.AddSqliteSql("Data Source=app.db");
```

Use in a repository:

```csharp
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

### Dependencies

- `Headless.Checks`
- `Headless.Sql.Abstractions`
- `Headless.Sql.Core`
- `Microsoft.Data.Sqlite`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

`AddSqliteSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped. For file-based databases, SQLite creates the `.db` file on the first connection open if it does not exist.
