# Headless.UnitOfWork.PostgreSql

Npgsql provider for Headless units of work.

## Why use this package

Runs raw-ADO `NpgsqlConnection` work as a unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

## Install

```bash
dotnet add package Headless.UnitOfWork.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Unit of Work guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#headlessunitofworkpostgresql)
