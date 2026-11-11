# Headless.AuditLog.Storage.SqlServer.EntityFramework

Entity Framework audit log storage for SQL Server.

## Why use this package

Use it when audit entries commit through the application's EF Core `DbContext` and the database is SQL Server. It adds what only SQL Server has to `Headless.AuditLog.Storage.EntityFramework`: migration helpers that make the audit table append-only with the same trigger the raw SQL Server provider uses, and a retention purge that passes that trigger.

## Install

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogstoragesqlserverentityframework)
