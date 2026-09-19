# Headless.Settings.Storage.SqlServer

SQL Server raw-DDL storage for settings management.

## Why use this package

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Install

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Settings guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/settings.md#headlesssettingsstoragesqlserver)
