---
domain: SQL
packages: Sql.Abstractions, Sql.PostgreSql, Sql.SqlServer, Sql.Sqlite
---

# SQL

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Sql.Abstractions](#headlesssqlabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Sql.PostgreSql](#headlesssqlpostgresql)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Usage](#usage-1)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Sql.SqlServer](#headlesssqlsqlserver)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Usage](#usage-2)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Sql.Sqlite](#headlesssqlsqlite)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-2)
  - [Usage](#usage-3)
  - [Configuration](#configuration-3)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)

> Provider-agnostic SQL connection factory for raw SQL / Dapper scenarios with PostgreSQL, SQL Server, and SQLite backends.

## Quick Orientation

Install `Headless.Sql.Abstractions` + one provider:
- `Sql.PostgreSql` -- `NpgsqlConnectionFactory` (Npgsql)
- `Sql.SqlServer` -- `SqlServerConnectionFactory` (Microsoft.Data.SqlClient)
- `Sql.Sqlite` -- `SqliteConnectionFactory` (Microsoft.Data.Sqlite)

Register manually in DI:
```csharp
builder.Services.AddSingleton<ISqlConnectionFactory>(
    new NpgsqlConnectionFactory(connectionString)
);
```

Then inject `ISqlConnectionFactory` and call `CreateNewConnectionAsync()` to get an open `DbConnection`. Pair with Dapper or raw ADO.NET for queries.

## Agent Instructions

- These packages are for **raw SQL / Dapper** scenarios. For EF Core, use the `Orm.EntityFramework` packages instead.
- Use `ISqlConnectionFactory` from Abstractions as the primary interface. Never depend on provider-specific types in application code.
- Connections from `CreateNewConnectionAsync()` are already opened -- do not call `OpenAsync()` again.
- `ISqlCurrentConnection` provides ambient connection access if needed (e.g., inside a unit-of-work scope).
- `IConnectionStringChecker` is available for health checks and connection validation.
- Registration is manual (`AddSingleton<ISqlConnectionFactory>(...)`) -- there is no `AddSql*()` convenience method.
- For testing, use `Sql.Sqlite` with `"Data Source=:memory:"` connection string.
- Always dispose connections with `await using` -- they are `IAsyncDisposable`.

---
# Headless.Sql.Abstractions

Defines interfaces for SQL database connection management.

## Problem Solved

Provides provider-agnostic interfaces for SQL connection creation and validation, enabling consistent database access patterns across different SQL providers (PostgreSQL, SQL Server, SQLite).

## Key Features

- `ISqlConnectionFactory` - Create and manage database connections
- `ISqlCurrentConnection` - Access current ambient connection
- `IConnectionStringChecker` - Validate connection strings and database existence

## Installation

```bash
dotnet add package Headless.Sql.Abstractions
```

## Usage

```csharp
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

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
---
# Headless.Sql.PostgreSql

PostgreSQL connection factory using Npgsql.

## Problem Solved

Provides PostgreSQL-specific implementation of the SQL connection factory, enabling efficient connection management with Npgsql.

## Key Features

- `NpgsqlConnectionFactory` - ISqlConnectionFactory implementation
- `NpgsqlConnectionStringChecker` - Connection string validation
- Returns strongly-typed `NpgsqlConnection` instances

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
```

## Usage

```csharp
public sealed class ReportService(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Report>> GetReportsAsync(CancellationToken ct)
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

Connection string via constructor:

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

SQL Server connection factory using Microsoft.Data.SqlClient.

## Problem Solved

Provides SQL Server-specific implementation of the SQL connection factory, enabling efficient connection management with Microsoft.Data.SqlClient.

## Key Features

- `SqlServerConnectionFactory` - ISqlConnectionFactory implementation
- `SqlServerConnectionStringChecker` - Connection string validation
- Returns strongly-typed `SqlConnection` instances

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
```

## Usage

```csharp
public sealed class ReportService(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Report>> GetReportsAsync(CancellationToken ct)
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

Connection string via constructor:

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

SQLite connection factory using Microsoft.Data.Sqlite.

## Problem Solved

Provides SQLite-specific implementation of the SQL connection factory, ideal for development, testing, and embedded database scenarios.

## Key Features

- `SqliteConnectionFactory` - ISqlConnectionFactory implementation
- `SqliteConnectionStringChecker` - Connection string validation
- Returns strongly-typed `SqliteConnection` instances
- Lightweight, file-based database support

## Installation

```bash
dotnet add package Headless.Sql.Sqlite
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = "Data Source=app.db";
builder.Services.AddSingleton<ISqlConnectionFactory>(
    new SqliteConnectionFactory(connectionString)
);
```

## Usage

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

```csharp
// In-memory database (for testing)
services.AddSingleton<ISqlConnectionFactory>(
    new SqliteConnectionFactory("Data Source=:memory:")
);

// File-based database
services.AddSingleton<ISqlConnectionFactory>(
    new SqliteConnectionFactory("Data Source=app.db")
);
```

## Dependencies

- `Headless.Sql.Abstractions`
- `Microsoft.Data.Sqlite`

## Side Effects

None (manual registration required).
