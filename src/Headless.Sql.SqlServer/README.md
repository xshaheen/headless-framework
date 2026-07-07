# Headless.Sql.SqlServer

SQL Server connection factory backed by `Microsoft.Data.SqlClient`.

## Problem Solved

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQL Server and Azure SQL, wrapping `Microsoft.Data.SqlClient` so repositories can obtain open `SqlConnection` instances through the provider-agnostic interface.

## Key Features

- `SqlServerConnectionFactory` — `ISqlConnectionFactory` implementation; `CreateNewConnectionAsync()` returns a strongly-typed `SqlConnection` (already open); `GetConnectionString()` retrieves the configured string
- `SqlServerConnectionStringChecker` — `IConnectionStringChecker` that verifies server reachability and database existence by connecting to `master` first, then calling `ChangeDatabaseAsync` to the target
- `SetupSqlServerSql.AddSqlServerSql(string connectionString)` / `AddSqlServerSql(Func<IServiceProvider, string>)` — one-call registration of the factory, checker, and scoped ambient connection

## Installation

```bash
dotnet add package Headless.Sql.SqlServer
```

## Quick Start

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

## Configuration

Resolve the connection string from the service provider with the factory overload:

```csharp
services.AddSqlServerSql(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetConnectionString("SqlServer")!;
});
```

## Dependencies

- `Headless.Checks`
- `Headless.Sql.Abstractions`
- `Headless.Sql.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

`AddSqlServerSql` registers `ISqlConnectionFactory` and `IConnectionStringChecker` as singletons and `ISqlCurrentConnection` (`DefaultSqlCurrentConnection`) as scoped.
