# Headless.Sql.Abstractions

Defines the provider-agnostic interfaces for SQL connection creation and validation.

## Why use this package

Application code that works with raw SQL should not depend on a specific ADO.NET driver. `ISqlConnectionFactory` decouples the connection-string source and connection-creation lifecycle from service code, making it trivial to switch drivers (e.g., PostgreSQL in production, SQLite in tests) without touching repositories.

## Install

```bash
dotnet add package Headless.Sql.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SQL guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sql.md#headlesssqlabstractions)
