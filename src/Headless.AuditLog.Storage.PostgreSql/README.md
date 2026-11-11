# Headless.AuditLog.Storage.PostgreSql

Raw PostgreSQL storage provider for `Headless.AuditLog`. No Entity Framework dependency — uses Npgsql directly. Creates the audit table at host startup and stores JSON columns as `jsonb` by default.

## Why use this package

Provides PostgreSQL-native audit log storage without pulling Entity Framework into the dependency graph. Creates and maintains the audit table via self-initializing DDL, stores JSON columns as `jsonb` by default, and can enroll writes atomically in the consumer's active Npgsql transaction. Optionally makes the table append-only, gives the purge to the table's owner, and partitions the table by month with retention that drops whole months.

## Install

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogstoragepostgresql)
