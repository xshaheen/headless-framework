# Headless.AuditLog.Storage.PostgreSql.EntityFramework

Entity Framework audit log storage for PostgreSQL.

## Why use this package

Use it when audit entries commit through the application's EF Core `DbContext` and the database is PostgreSQL. It adds what only PostgreSQL has to `Headless.AuditLog.Storage.EntityFramework`: migration helpers that make the audit table append-only and add monthly partitions with the same SQL the raw PostgreSQL provider uses, a retention purge that passes the append-only trigger or runs through the table owner's function, and the service that keeps the partitions.

## Install

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogstoragepostgresqlentityframework)
