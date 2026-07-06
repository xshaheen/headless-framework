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
services.AddSingleton<ISqlConnectionFactory>(new SqliteConnectionFactory("Data Source=:memory:"));

// File-based embedded database:
services.AddSingleton<ISqlConnectionFactory>(new SqliteConnectionFactory("Data Source=app.db"));

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
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

None (manual registration required). For file-based databases, SQLite creates the `.db` file on the first connection open if it does not exist.
