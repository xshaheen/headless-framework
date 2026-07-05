# Headless.AuditLog.Storage.EntityFramework

EF Core implementation of the audit log subsystem: change capture, persistent storage, and explicit event logging.

## Problem Solved

Wires the audit log pipeline into EF Core's ChangeTracker so entity mutations are captured and persisted atomically with the originating `SaveChanges` — no separate commit, no data loss on rollback.

## Key Features

- `EfAuditChangeCapture` — scans ChangeTracker before save, produces `AuditLogEntryData` per changed entity.
- `EfAuditLogStore` — adds `AuditLogEntry` rows to the same `DbContext` so they commit in the same transaction as entity changes.
- `EfAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging; resolves `ICurrentUser`, `ICurrentTenant`, `ICorrelationIdProvider`, and `IClock` from DI.
- `EfReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` using `IDbContextFactory<TContext>` (no-tracking queries).
- `AuditLogEntry` — EF entity; decorated with `[AuditIgnore]` to prevent recursive capture when `AuditByDefault` is enabled.
- `AuditLogModelBuilderExtensions.AddHeadlessAuditLog(modelBuilder, options)` — registers and configures the `AuditLogEntry` entity type; idempotent.
- Composite primary key `(CreatedAt, Id)` for partition-readiness; index set covers tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, and correlation ID.
- Soft-delete detection: emits `entity.soft_deleted` / `entity.restored` on `IsDeleted` transitions.
- Suspend detection: emits `entity.suspended` / `entity.unsuspended` on `IsSuspended` transitions.
- Zero overhead when `AuditLogOptions.IsEnabled` is `false`.
- Startup gate validates that `AuditLogEntry` is registered in the `DbContext` model and throws with a clear message if `modelBuilder.AddHeadlessAuditLog` was omitted.

## Design Notes

The composite primary key `(CreatedAt, Id)` is a deliberate time-partitioning choice for audit retention strategies. SQLite does not support `ValueGeneratedOnAdd` on composite keys — consumers targeting SQLite must override to a single-column PK on `Id`.

JSON columns default to string columns via value converters (universally portable). Override to native `jsonb` via `AuditLogStorageOptions.JsonColumnType = AuditLogJsonColumnType.Jsonb` when targeting PostgreSQL.

## Installation

```bash
dotnet add package Headless.AuditLog.Storage.EntityFramework
```

## Quick Start

### DI setup

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureOptions(o =>
    {
        o.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
    });
    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // for PostgreSQL
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

`UseEntityFramework<TContext>()` requires the same context to be registered with EF Core. Register `IDbContextFactory<TContext>` too if you resolve `IReadAuditLog<TContext>`.

### DbContext setup

```csharp
public AppDbContext(DbContextOptions<AppDbContext> options, IOptions<AuditLogStorageOptions> auditLogStorage)
    : base(options)
{
    _auditLogStorage = auditLogStorage;
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddHeadlessAuditLog(_auditLogStorage.Value);
}
```

### Explicit event logging

```csharp
await auditLog.LogAsync("pii.revealed", entityType: typeof(Patient).FullName, entityId: id.ToString());
```

### Query audit entries

```csharp
var entries = await readAuditLog.QueryAsync(
    action: "entity.updated",
    entityType: typeof(Patient).FullName,
    limit: 50,
    cancellationToken: ct
);
```

## Configuration

```csharp
setup.ConfigureStorage(options =>
{
    options.TableName = "audit_entries";
    options.Schema = "audit";
    options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // optional
    options.CreatedAtColumnType = "timestamp with time zone"; // optional explicit override
});
```

Sensitive data strategies:

| Strategy | Behavior |
|---|---|
| `Redact` (default) | Replaces value with `"***"`; property name still appears in `ChangedFields`. |
| `Exclude` | Omits the property entirely from `OldValues`, `NewValues`, and `ChangedFields`. |
| `Transform` | Passes value through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize). |

SQLite key override (required when targeting SQLite):

```csharp
builder.HasKey(e => e.Id); // single-column PK for SQLite
```

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.AuditLog.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IAuditChangeCapture` as scoped (`EfAuditChangeCapture`).
- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`).
- Registers `IAuditLog<TContext>` as scoped (`EfAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`EfReadAuditLog<TContext>`).
- Registers `AuditLogEntityValidationStartupGate<TContext>` as a hosted service (validates model at startup).
