# Framework.Sql.SqlServer

SQL Server connection factory using Microsoft.Data.SqlClient.

## Problem Solved

Provides SQL Server-specific implementation of the SQL connection factory, enabling efficient connection management with Microsoft.Data.SqlClient.

## Key Features

- `SqlServerConnectionFactory` - ISqlConnectionFactory implementation
- `SqlServerConnectionStringChecker` - Connection string validation
- Returns strongly-typed `SqlConnection` instances

## Installation

```bash
dotnet add package Framework.Sql.SqlServer
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

- `Framework.Sql.Abstractions`
- `Microsoft.Data.SqlClient`

## Side Effects

None (manual registration required).
