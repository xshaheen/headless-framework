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

- `Haedless.Sql.Abstractions`
- `Microsoft.Data.Sqlite`

## Side Effects

None (manual registration required).
