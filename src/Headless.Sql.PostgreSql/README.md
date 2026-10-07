# Headless.Sql.PostgreSql

PostgreSQL connection factory backed by Npgsql.

## Why use this package

Provides the `ISqlConnectionFactory` and `IConnectionStringChecker` implementations for PostgreSQL, wrapping Npgsql so repositories can obtain open `NpgsqlConnection` instances through the provider-agnostic interface.

It also runs raw-ADO `NpgsqlConnection` work as a unit of work (`BeginAsync`, `Enlist`, and `RunAsync` on `IUnitOfWorkFactory`), so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

## Install

```bash
dotnet add package Headless.Sql.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SQL guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sql.md#headlesssqlpostgresql)
