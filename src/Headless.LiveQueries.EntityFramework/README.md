# Headless.LiveQueries.EntityFramework

Tracks an Entity Framework Core context for live queries: the tables a query reads are recorded, and the tables a committed write changed are announced.

## Why use this package

Without it, every live query declares its dependency keys and every writer announces the keys it changed, and a forgotten announcement leaves a screen stale with no error. With `UseEntityFramework<TContext>()`, a query through the context depends on the tables it read, and `SaveChanges`, `ExecuteUpdate`, `ExecuteDelete`, and raw SQL re-run the live queries that read the written tables, after the write is durable: when its EF Core transaction, its Headless unit of work, or its `TransactionScope` commits. Tracking is per table, or per row for filters on key and insert-only columns when `RowLevelMatching` is on, and reaches live queries in the same process only. Writes EF Core does not issue still need `ILiveQueryInvalidator`; read the guide for what is tracked and what is not.

## Install

```bash
dotnet add package Headless.LiveQueries.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Live Queries guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/live-queries.md#headlesslivequeriesentityframework)
