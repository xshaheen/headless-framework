# Headless.Sql.Sqlite

SQLite connection factory backed by `Microsoft.Data.Sqlite`.

## Why use this package

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQLite, enabling in-process integration tests with `:memory:` databases and lightweight embedded / edge deployments without a separate database server.

## Install

```bash
dotnet add package Headless.Sql.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SQL guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sql.md#headlesssqlsqlite)
