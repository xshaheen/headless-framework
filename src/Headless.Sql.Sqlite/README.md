# Headless.Sql.Sqlite

SQLite connection factory backed by `Microsoft.Data.Sqlite`.

## Why use this package

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for SQLite, enabling in-process integration tests with `:memory:` databases and lightweight embedded / edge deployments without a separate database server.

It also runs raw-ADO `SqliteConnection` work as a unit of work (`BeginAsync`, `Enlist`, and `RunAsync` on `IUnitOfWorkFactory`), so rows that Headless stores write inside the transaction, such as a gap-free sequence value or a fenced lease, commit and roll back with it.

## Install

```bash
dotnet add package Headless.Sql.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SQL guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sql.md#headlesssqlsqlite)
