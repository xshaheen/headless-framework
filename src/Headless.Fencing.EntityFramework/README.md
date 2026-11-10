# Headless.Fencing.EntityFramework

Maps a lease kept as five columns on an entity's own row in Entity Framework Core, and runs the fenced row transitions on the context's connection and transaction.

## Why use this package

Lets an EF Core entity carry its lease as a `RowLeaseSnapshot` complex property, builds the `FencedRowTable<TKey>` descriptor from the model's table and column names, and selects lease candidates in LINQ by the database clock, while every grant, renewal, and settlement stays a `FencedRows` transition that re-checks the lease after its row lock.

## Install

```bash
dotnet add package Headless.Fencing.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingentityframework)
