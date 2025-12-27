# Framework.Sql.SqlServer

This package provides the [Microsoft SQL Server](https://www.microsoft.com/sql-server) implementation of the `Framework.Sql.Abstraction` abstractions.

## Features

-   **Connection Factory**: `SqlServerConnectionFactory` implements `ISqlConnectionFactory` to provide managed SQL Server connection instances.
-   **Connection String Checker**: `SqlServerConnectionStringChecker` implements `IConnectionStringChecker` to validate SQL Server connection strings and verify database existence.

## Usage

This package is typically used by registering the implementations with your dependency injection container for applications using SQL Server.

```csharp
// Example registration (pseudocode)
services.AddSingleton<ISqlConnectionFactory>(new SqlServerConnectionFactory(connectionString));
services.AddTransient<IConnectionStringChecker, SqlServerConnectionStringChecker>();
```
