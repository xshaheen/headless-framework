# Headless.AuditLog.Storage.PostgreSql

Raw PostgreSQL storage provider for `Headless.AuditLog`. No Entity Framework dependency — uses Npgsql directly. Creates the audit table at host startup and stores JSON columns as `jsonb` by default.

## Problem Solved

Provides PostgreSQL-native audit log storage without pulling Entity Framework into the dependency graph. Creates and maintains the audit table via self-initializing DDL, stores JSON columns as `jsonb` by default, and can enroll writes atomically in the consumer's active Npgsql transaction.

## Key Features

- No EF Core dependency — depends only on `Npgsql`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog.Core`.
- `PostgreSqlAuditLogStore` — implements `IAuditLogStore`; enrolls in the consumer's ambient Npgsql transaction when available; falls back to its own connection otherwise.
- `PostgreSqlAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging.
- `PostgreSqlReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` via parameterized SQL queries.
- Self-initializing DDL at startup; DDL races across replicas serialized with `pg_advisory_xact_lock`.
- Batched INSERT: up to 500 rows per command (cached per row count).
- `jsonb` by default for JSON columns; override via `AuditLogStorageOptions.JsonColumnType` (`Jsonb` or `Json` accepted; `NvarcharMax` rejected at options validation).
- `PostgreSqlAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s).
- `UsePostgreSql` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<PostgreSqlAuditLogOptions>)`, and `(Action<PostgreSqlAuditLogOptions, IServiceProvider>)`.
- Same index set as `Headless.AuditLog.Storage.EntityFramework`: tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, correlation ID.

## Design Notes

Transaction enrollment is conditional: the store attempts to resolve a `NpgsqlConnection` and `NpgsqlTransaction` from `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the driver differs, it falls back to its own connection — audit rows then commit before `SaveChanges`, and an entity-save failure leaves orphan rows. A deduplicated warning fires once per distinct saving-context type and once per driver mismatch.

DDL initialization uses two separate transactions (schema+table, then indexes) so a concurrent-startup race that aborts the table transaction does not wipe the index DDL.

## Installation

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
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
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

Skip startup DDL when schema is provisioned out-of-band:

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(connectionString);
});
```

Configure provider-specific options:

```csharp
setup.UsePostgreSql(options =>
{
    options.ConnectionString = connectionString;
    options.CommandTimeout = TimeSpan.FromSeconds(60);
});
```

Bind provider options from configuration, or configure with service resolution:

```csharp
setup.UsePostgreSql(builder.Configuration.GetSection("Headless:AuditLog:PostgreSql"));
setup.UsePostgreSql((options, sp) =>
    options.ConnectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("AuditLog")!);
```

## Configuration

`PostgreSqlAuditLogOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | (required) | Npgsql connection string. |
| `CommandTimeout` | `30s` | Timeout for DDL and DML commands. |

`AuditLogStorageOptions.JsonColumnType` for this provider: `Jsonb` (default) or `Json`. `NvarcharMax` is rejected at options validation time.

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.AuditLog.Core`
- `Headless.Serializer`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlAuditLogStorageInitializer` as a hosted service (creates schema + table + indexes at startup).
- Registers `PostgreSqlAuditLogWriter` as singleton.
- Registers `IAuditLogStore` as scoped (`PostgreSqlAuditLogStore`).
- Registers `IAuditLog<TContext>` as singleton (`PostgreSqlAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`PostgreSqlReadAuditLog<TContext>`).
- Registers `IJsonSerializer`, `IClock`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.
