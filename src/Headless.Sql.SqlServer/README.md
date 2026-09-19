# Headless.Sql.SqlServer

SQL Server connection factory backed by `Microsoft.Data.SqlClient`.

## Why use this package

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQL Server and Azure SQL, wrapping `Microsoft.Data.SqlClient` so repositories can obtain open `SqlConnection` instances through the provider-agnostic interface.

## Install

```bash
dotnet add package Headless.Sql.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SQL guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sql.md#headlesssqlsqlserver)
