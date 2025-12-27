# Framework.Sql.PostgreSql

This package provides the [PostgreSQL](https://www.postgresql.org/) implementation of the `Framework.Sql.Abstraction` abstractions, built on top of [Npgsql](https://www.npgsql.org/).

## Features

-   **Connection Factory**: `NpgsqlConnectionFactory` implements `ISqlConnectionFactory` to provide managed `NpgsqlConnection` instances.
-   **Connection String Checker**: `NpgsqlConnectionStringChecker` implements `IConnectionStringChecker` to validate PostgreSQL connection strings and database existence.

## Usage

This package is typically used by registering the implementations with your dependency injection container, often matching the `ISqlConnectionFactory` interface.

```csharp
// Example registration (pseudocode)
services.AddSingleton<ISqlConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
services.AddTransient<IConnectionStringChecker, NpgsqlConnectionStringChecker>();
```
