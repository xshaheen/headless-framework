# Headless.AuditLog.Storage.SqlServer

Raw SQL Server storage provider for `Headless.AuditLog`. No Entity Framework dependency — uses `Microsoft.Data.SqlClient` directly. Creates the audit table at host startup and stores JSON payloads as `nvarchar(max)` by default.

## Why use this package

Provides SQL Server-native audit log storage without pulling Entity Framework into the dependency graph. Creates and maintains the audit table via self-initializing DDL, stores JSON payloads as `nvarchar(max)` by default, and can enroll writes atomically in the consumer's active SQL Server transaction.

## Install

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogstoragesqlserver)
