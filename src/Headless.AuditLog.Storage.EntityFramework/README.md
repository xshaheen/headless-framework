# Headless.AuditLog.Storage.EntityFramework

EF Core storage provider for automatic audit entries and explicit event logging.

## Why use this package

Persists audit entries through the application's EF Core `DbContext` so they commit atomically with the originating `SaveChanges` — no separate connection or commit. Its migration helpers make the audit table append-only (PostgreSQL and SQL Server) or partitioned by month (PostgreSQL) with the same SQL the raw providers use, so the trigger is never copied into a migration by hand.

## Install

```bash
dotnet add package Headless.AuditLog.Storage.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogstorageentityframework)
