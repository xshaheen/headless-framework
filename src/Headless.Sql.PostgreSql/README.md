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
builder.Services.AddSingleton<ISqlConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

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

- `Headless.Checks`
- `Headless.Sql.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Npgsql`

## Side Effects

None (manual registration required).
