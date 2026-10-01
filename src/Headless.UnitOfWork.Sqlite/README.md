# Headless.UnitOfWork.Sqlite

Microsoft.Data.Sqlite provider for Headless units of work.

## Why use this package

Runs raw-ADO `SqliteConnection` work as a unit of work, so rows that Headless stores write inside the transaction, such as a gap-free sequence value or a fenced lease, commit and roll back with it.

## Install

```bash
dotnet add package Headless.UnitOfWork.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Unit of Work guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#headlessunitofworksqlite)
