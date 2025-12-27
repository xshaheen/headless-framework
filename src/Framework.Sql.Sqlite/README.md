# Framework.Sql.Sqlite

This package provides the [SQLite](https://www.sqlite.org/) implementation of the `Framework.Sql.Abstraction` abstractions.

## Features

-   **Connection Factory**: `SqliteConnectionFactory` implements `ISqlConnectionFactory` to provide managed SQLite connection instances.
-   **Connection String Checker**: `SqliteConnectionStringChecker` implements `IConnectionStringChecker` to validate SQLite connection strings and database existence.

## Usage

This package is typically used by registering the implementations with your dependency injection container, tailored for SQLite environments.

```csharp
// Example registration (pseudocode)
services.AddSingleton<ISqlConnectionFactory>(new SqliteConnectionFactory(connectionString));
services.AddTransient<IConnectionStringChecker, SqliteConnectionStringChecker>();
```
