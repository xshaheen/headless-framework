# Headless.Messaging.Storage.SqlServer.EntityFramework

Connects `Headless.Messaging.Storage.SqlServer` to an EF Core `DbContext` and enables the transactional outbox.

## Why use this package

Use it with the SQL Server storage package when messaging must share an EF Core connection and commit application state, fenced inbox outcomes, and captured durable messages in one transaction. The adapter does not make handler entry or external effects exactly once.

## Install

```bash
dotnet add package Headless.Messaging.Storage.SqlServer.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingstoragesqlserverentityframework)
