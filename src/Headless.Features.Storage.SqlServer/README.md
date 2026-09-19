# Headless.Features.Storage.SqlServer

SQL Server raw-DDL storage for feature management.

## Why use this package

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Install

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Feature Management guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/features.md#headlessfeaturesstoragesqlserver)
