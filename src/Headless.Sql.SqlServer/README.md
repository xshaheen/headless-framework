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
builder.Services.AddSingleton<ISqlConnectionFactory>(new SqlServerConnectionFactory(connectionString));

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
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

None (manual registration required).
