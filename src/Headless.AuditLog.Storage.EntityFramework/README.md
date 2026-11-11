# Headless.AuditLog.Storage.EntityFramework

EF Core storage provider for automatic audit entries and explicit event logging.

## Why use this package

Persists audit entries through the application's EF Core `DbContext` so they commit atomically with the originating `SaveChanges` — no separate connection or commit. On PostgreSQL or SQL Server, install `Headless.AuditLog.Storage.PostgreSql.EntityFramework` or `Headless.AuditLog.Storage.SqlServer.EntityFramework` instead: each builds on this package and adds that database's migration helpers and retention purge.

## Install

```bash
dotnet add package Headless.AuditLog.Storage.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogstorageentityframework)
