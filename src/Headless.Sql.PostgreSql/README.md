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
