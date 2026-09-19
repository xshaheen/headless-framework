# Headless.Permissions.Storage.PostgreSql

PostgreSQL raw-DDL storage for permission management.

## Why use this package

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework. All schema is created idempotently at host startup via raw ADO.NET.

## Install

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Permissions guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/permissions.md#headlesspermissionsstoragepostgresql)
