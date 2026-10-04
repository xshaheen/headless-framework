# Headless.Coordination.Database

Provides the one relational membership store the PostgreSQL and SQL Server coordination providers share.

## Why use this package

The membership store is written once against the Headless SQL dialect kit, so PostgreSQL and SQL Server behave the same while each keeps its own naming convention and DDL.

## Install

```bash
dotnet add package Headless.Coordination.Database
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Coordination guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/coordination.md#headlesscoordinationcoredatabase)
