---
domain: SQL
packages: Sql.Abstractions, Sql.PostgreSql, Sql.SqlServer, Sql.Sqlite
---

# SQL

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Sql.Abstractions](#headlesssqlabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Sql.PostgreSql](#headlesssqlpostgresql)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Design Notes](#design-notes)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Sql.SqlServer](#headlesssqlsqlserver)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-2)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Sql.Sqlite](#headlesssqlsqlite)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Design Notes](#design-notes-1)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-3)
  - [Configuration](#configuration-3)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)

> Provider-agnostic SQL connection factory for raw SQL / Dapper scenarios with PostgreSQL, SQL Server, and SQLite backends.

## Quick Orientation

Install `Headless.Sql.Abstractions` plus one provider package:

- `Headless.Sql.PostgreSql` — wraps Npgsql; returns `NpgsqlConnection`
- `Headless.Sql.SqlServer` — wraps `Microsoft.Data.SqlClient`; returns `SqlConnection`
- `Headless.Sql.Sqlite` — wraps `Microsoft.Data.Sqlite`; returns `SqliteConnection`

There are no `AddSql*()` convenience methods. Register the factory manually:

```csharp
builder.Services.AddSingleton<ISqlConnectionFactory>(
    new NpgsqlConnectionFactory(connectionString)
);
```

Inject `ISqlConnectionFactory` and call `CreateNewConnectionAsync()` to get an already-open `DbConnection`. Pair with Dapper or raw ADO.NET — this layer does not provide query helpers.

## Agent Instructions

- These packages are for **raw SQL / Dapper** scenarios only. For EF Core, use the `Headless.Orm.EntityFramework` packages instead.
- Always depend on `ISqlConnectionFactory` from `Headless.Sql.Abstractions` in service code. Never reference `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`, or `SqliteConnectionFactory` in application-layer code.
- Do **not** construct connections directly (`new NpgsqlConnection(cs)` / `new SqlConnection(cs)`). Always go through the factory so the connection string is centralized and the factory can be swapped in tests.
- Connections returned by `CreateNewConnectionAsync()` are **already open** — calling `OpenAsync()` on them again throws an `InvalidOperationException`.
- Always dispose connections with `await using` — they are `IAsyncDisposable`. Holding an open connection unnecessarily may exhaust the connection pool.
- `ISqlCurrentConnection` / `DefaultSqlCurrentConnection` provide an ambient, lazy-open connection for unit-of-work patterns. Inject as scoped, not singleton.
- `IConnectionStringChecker` is for health checks and startup validation; register the provider implementation (e.g., `NpgsqlConnectionStringChecker`) and inject `IConnectionStringChecker`. Note: `SqliteConnectionStringChecker` always returns `DatabaseExists = true` when connected (SQLite creates the file on open).
- For in-process integration tests, register `SqliteConnectionFactory` with `"Data Source=:memory:"` — it needs no external server.
- There is no per-package `AddSql*()` extension. Manual `AddSingleton<ISqlConnectionFactory>(...)` registration is the only pattern.

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

`DefaultSqlCurrentConnection` lazily opens one connection per instance, protects concurrent callers with an `AsyncLock`, and re-opens if the connection drops. Register it as **scoped** — one instance per request/scope — so it is disposed at the end of each unit of work.

### Connection string checker

`IConnectionStringChecker` returns `(bool Connected, bool DatabaseExists)`. The `Connected` flag indicates whether the server is reachable; `DatabaseExists` indicates whether the target database exists. Use this in health checks or startup validation. Behavior differs by provider:

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
# Headless.Sql.Abstractions

Defines the provider-agnostic interfaces for SQL connection creation and validation.

## Problem Solved

Application code that works with raw SQL should not depend on a specific ADO.NET driver. `ISqlConnectionFactory` decouples the connection-string source and connection-creation lifecycle from service code, making it trivial to switch drivers (e.g., PostgreSQL in production, SQLite in tests) without touching repositories.

## Key Features

- `ISqlConnectionFactory` — create and manage database connections; `GetConnectionString()` retrieves the configured string; `CreateNewConnectionAsync()` returns an already-open `DbConnection`
- `ISqlCurrentConnection` — ambient connection for unit-of-work scopes; lazy-opens on first call, re-opens on drop
- `DefaultSqlCurrentConnection` — concrete thread-safe implementation of `ISqlCurrentConnection` backed by `AsyncLock`
- `IConnectionStringChecker` — validate server reachability and database existence; returns `(bool Connected, bool DatabaseExists)`

## Installation

```bash
dotnet add package Headless.Sql.Abstractions
```

## Quick Start

```csharp
// Register a concrete factory (provider package required):
builder.Services.AddSingleton<ISqlConnectionFactory>(
    new NpgsqlConnectionFactory(connectionString)
);

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

Register `DefaultSqlCurrentConnection` as scoped when you need a shared ambient connection within a unit of work:

```csharp
builder.Services.AddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

- `Headless.Hosting`
- `Nito.AsyncEx` (transitively, via `DefaultSqlCurrentConnection`)

## Side Effects

None. This is an abstractions package — it registers no services.

---
# Headless.Sql.PostgreSql

PostgreSQL connection factory backed by Npgsql.

## Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for PostgreSQL, wrapping Npgsql so repositories can obtain open `NpgsqlConnection` instances through the provider-agnostic interface.

## Key Features

- `NpgsqlConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `NpgsqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `NpgsqlConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `postgres` first, then calling `ChangeDatabaseAsync` to the target

## Design Notes

`NpgsqlConnectionFactory.CreateNewConnectionAsync()` is declared `public async ValueTask<NpgsqlConnection>` — a covariant return relative to the explicit `ValueTask<DbConnection>` implementation on `ISqlConnectionFactory`. Callers that inject `NpgsqlConnectionFactory` directly (e.g., provider-aware infrastructure code) can use `NpgsqlConnection`-specific APIs (COPY, LISTEN/NOTIFY, Npgsql type mapping) without a cast. Callers that inject `ISqlConnectionFactory` see only `DbConnection`.

`NpgsqlConnectionStringChecker` uses a 1-second connect timeout (`Timeout = 1`) to fail fast in health checks. If the target database cannot be selected, `DatabaseExists` is `false` but `Connected` is `true`.

## Installation

```bash
dotnet add package Headless.Sql.PostgreSql
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddSingleton<ISqlConnectionFactory>(
    new NpgsqlConnectionFactory(connectionString)
);

// Optional: register the health-check helper
builder.Services.AddSingleton<IConnectionStringChecker, NpgsqlConnectionStringChecker>();
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

## Configuration

Pass the connection string directly to the constructor:

```csharp
services.AddSingleton<ISqlConnectionFactory>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new NpgsqlConnectionFactory(config.GetConnectionString("Postgres")!);
});
```

## Dependencies

- `Headless.Sql.Abstractions`
- `Npgsql`

## Side Effects

None (manual registration required).

---
# Headless.Sql.SqlServer

SQL Server connection factory backed by `Microsoft.Data.SqlClient`.

## Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQL Server and Azure SQL, wrapping `Microsoft.Data.SqlClient` so repositories can obtain open `SqlConnection` instances through the provider-agnostic interface.

## Key Features

- `SqlServerConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqlServerConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `master` first, then calling `ChangeDatabaseAsync` to the target

## Installation

```bash
dotnet add package Headless.Sql.SqlServer
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddSingleton<ISqlConnectionFactory>(
    new SqlServerConnectionFactory(connectionString)
);

// Optional: register the health-check helper
builder.Services.AddSingleton<IConnectionStringChecker, SqlServerConnectionStringChecker>();
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

## Configuration

Pass the connection string directly to the constructor:

```csharp
services.AddSingleton<ISqlConnectionFactory>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new SqlServerConnectionFactory(config.GetConnectionString("SqlServer")!);
});
```

## Dependencies

- `Headless.Sql.Abstractions`
- `Microsoft.Data.SqlClient`

## Side Effects

None (manual registration required).

---
# Headless.Sql.Sqlite

SQLite connection factory backed by `Microsoft.Data.Sqlite`.

## Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQLite, enabling in-process integration tests with `:memory:` databases and lightweight embedded / edge deployments without a separate database server.

## Key Features

- `SqliteConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqliteConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqliteConnectionStringChecker` — `IConnectionStringChecker` that opens the SQLite database and reports both `Connected` and `DatabaseExists` as `true` on success (SQLite creates the file on open, so the two flags are always identical)

## Design Notes

`SqliteConnectionStringChecker` differs from the PostgreSQL and SQL Server implementations: because SQLite creates the database file when the connection opens, there is no meaningful distinction between "server reachable" and "database exists". Both tuple fields are set to `true` together on a successful open, or both remain `false` on failure.

For in-process testing, prefer `"Data Source=:memory:"` — the database is private to the connection and disappears when the connection closes.

## Installation

```bash
dotnet add package Headless.Sql.Sqlite
```

## Quick Start

```csharp
// In-process tests (no server required):
services.AddSingleton<ISqlConnectionFactory>(
    new SqliteConnectionFactory("Data Source=:memory:")
);

// File-based embedded database:
services.AddSingleton<ISqlConnectionFactory>(
    new SqliteConnectionFactory("Data Source=app.db")
);

// Optional: register the health-check helper
services.AddSingleton<IConnectionStringChecker, SqliteConnectionStringChecker>();
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

## Configuration

Pass the connection string directly to the constructor. SQLite connection strings use `Data Source=<path>` or `Data Source=:memory:`.

## Dependencies

- `Headless.Sql.Abstractions`
- `Microsoft.Data.Sqlite`

## Side Effects

None (manual registration required). For file-based databases, SQLite creates the `.db` file on the first connection open if it does not exist.
