# Headless.Sql.PostgreSql

PostgreSQL connection factory backed by Npgsql.

## Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for PostgreSQL, wrapping Npgsql so repositories can obtain open `NpgsqlConnection` instances through the provider-agnostic interface.

## Key Features

- `NpgsqlConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `NpgsqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `NpgsqlConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `postgres` first, then calling `ChangeDatabaseAsync` to the target
- `SetupPostgreSqlSql.AddPostgreSqlSql(string connectionString)` / `AddPostgreSqlSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection

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

## Configuration

Resolve the connection string from the service provider with the factory overload:

```csharp
services.AddPostgreSqlSql(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetConnectionString("Postgres")!;
});
```

## Dependencies

- `Headless.Checks`
- `Headless.Sql.Abstractions`
- `Headless.Sql.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Npgsql`

## Side Effects

`AddPostgreSqlSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped.
