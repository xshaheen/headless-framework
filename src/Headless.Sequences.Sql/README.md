# Headless.Sequences.Sql

Provides the one relational counter store the PostgreSQL, SQL Server, and SQLite sequences providers share.

## Why use this package

The counter store is written once against the Headless SQL dialect kit, so every relational provider numbers the same way while each keeps its own naming convention and DDL. Each relational provider package references it, so an application installs the provider, not this package.

## Install

```bash
dotnet add package Headless.Sequences.Sql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Sequences guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sequences.md#headlesssequencessql)
