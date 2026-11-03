# Headless.Fencing.Sql

Provides the one relational lease store the PostgreSQL, SQL Server, and SQLite fencing providers share.

## Why use this package

The lease store is written once against the Headless SQL dialect kit, so every relational provider fences, renews, and sweeps leases the same way while each keeps its own naming convention and DDL. Each relational provider package references it, so an application installs the provider, not this package.

## Install

```bash
dotnet add package Headless.Fencing.Sql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingsql)
