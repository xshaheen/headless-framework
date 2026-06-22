# Headless.AuditLog.Storage.SqlServer

Raw SQL Server storage provider for `Headless.AuditLog`. No Entity Framework dependency — uses `Microsoft.Data.SqlClient` directly. Creates the audit table at host startup and stores JSON payloads as `nvarchar(max)` by default.

## Problem Solved

Provides SQL Server-native audit log storage without pulling Entity Framework into the dependency graph. Creates and maintains the audit table via self-initializing DDL, stores JSON payloads as `nvarchar(max)` by default, and can enroll writes atomically in the consumer's active SQL Server transaction.

## Key Features

- No EF Core dependency — depends only on `Microsoft.Data.SqlClient` and `Headless.AuditLog.Abstractions`.
- `SqlServerAuditLogStore` — implements `IAuditLogStore`; enrolls in the consumer's ambient `SqlTransaction` when available; falls back to its own connection otherwise.
- `SqlServerAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging.
- `SqlServerReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` via parameterized SQL queries using `TOP(@Limit)`.
- Self-initializing DDL at startup; DDL races serialized with `sp_getapplock`; wrapped in `BEGIN TRAN`/`COMMIT TRAN` with `TRY`/`CATCH`/`ROLLBACK` guard.
- Batched INSERT: up to 100 rows per command (SQL Server parameter limit is lower than PostgreSQL's).
- `nvarchar(max)` by default for JSON columns; `NvarcharMax` is the only accepted `AuditLogJsonColumnType` (`Jsonb` and `Json` are rejected at options validation).
- `SqlServerAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s).
- Same index set as `Headless.AuditLog.Storage.EntityFramework`: tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, correlation ID.

## Design Notes

Transaction enrollment mirrors the PostgreSQL provider: the store resolves the ambient `SqlConnection`/`SqlTransaction` via `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the driver is not `SqlClient`, it falls back to its own connection — audit rows commit before `SaveChanges`, and an entity-save failure leaves orphan rows. A deduplicated warning fires once per distinct saving-context type and once per driver mismatch.

DDL uses `sp_getapplock` (`Session` scope, 30 s timeout) to serialize concurrent multi-replica startups. The applock is released before `COMMIT` and defensively in the `CATCH` block to prevent session-scoped lock leaks. Batch size is capped at 100 rows (vs. 500 for the PostgreSQL provider) due to SQL Server's lower parameter-per-batch limit.

## Installation

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
```

## Quick Start

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.TableName = "audit_log";
    });
    setup.UseSqlServer(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

Skip startup DDL when schema is provisioned out-of-band:

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UseSqlServer(connectionString);
});
```

Configure provider-specific options:

```csharp
setup.UseSqlServer(options =>
{
    options.ConnectionString = connectionString;
    options.CommandTimeout = TimeSpan.FromSeconds(60);
});
```

## Configuration

`SqlServerAuditLogOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | (required) | SQL Server connection string. |
| `CommandTimeout` | `30s` | Timeout for DDL and DML commands. |

`AuditLogStorageOptions.JsonColumnType` for this provider: `NvarcharMax` only. `Jsonb` and `Json` are rejected at options validation time.

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.Hosting`
- `Headless.Serializer`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerAuditLogStorageInitializer` as a hosted service (creates schema + table + indexes at startup).
- Registers `SqlServerAuditLogWriter` as singleton.
- Registers `IAuditLogStore` as scoped (`SqlServerAuditLogStore`).
- Registers `IAuditLog<TContext>` as singleton (`SqlServerAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`SqlServerReadAuditLog<TContext>`).
- Registers `IJsonSerializer`, `IClock`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.
