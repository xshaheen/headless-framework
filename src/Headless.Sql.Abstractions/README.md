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
