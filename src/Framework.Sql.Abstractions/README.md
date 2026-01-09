# Framework.Sql.Abstractions

Defines interfaces for SQL database connection management.

## Problem Solved

Provides provider-agnostic interfaces for SQL connection creation and validation, enabling consistent database access patterns across different SQL providers (PostgreSQL, SQL Server, SQLite).

## Key Features

- `ISqlConnectionFactory` - Create and manage database connections
- `ISqlCurrentConnection` - Access current ambient connection
- `IConnectionStringChecker` - Validate connection strings and database existence

## Installation

```bash
dotnet add package Framework.Sql.Abstractions
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
