# Headless.AuditLog.EntityFramework

EF Core implementation of the audit log subsystem: change capture, persistent storage, and explicit event logging.

## Problem Solved

Wires the audit log pipeline into EF Core's ChangeTracker so that entity mutations are captured and persisted atomically with the originating `SaveChanges` call — no separate commit, no data loss on rollback.

## Key Features

- `EfAuditChangeCapture` - Scans ChangeTracker before save; produces `AuditLogEntryData` per changed entity
- `EfAuditLogStore` - Adds `AuditLogEntry` rows to the same DbContext so they commit in the same transaction
- `EfAuditLog<TContext>` - Implements `IAuditLog<TContext>` for explicit event logging (reads, PII reveals, failures)
- `EfReadAuditLog<TContext>` - Implements `IReadAuditLog<TContext>` for filtered read-back without leaking EF entities
- `AuditLogEntry` - Single-table entity with JSON columns for `OldValues`, `NewValues`, and `ChangedFields`
- `ConfigureAuditLog()` - ModelBuilder extension; supports custom table name, schema, and JSON column type
- Soft-delete detection: automatically emits `entity.soft_deleted` / `entity.restored` actions when `IsDeleted` transitions
- Suspend detection: emits `entity.suspended` / `entity.unsuspended` when `IsSuspended` transitions
- `EntityFilter` and `PropertyFilter` results are cached after first evaluation for the capture service lifetime
- Zero overhead when `AuditLogOptions.IsEnabled` is `false`

## Installation

```bash
dotnet add package Headless.AuditLog.EntityFramework
```

## Quick Start

### DI setup

```csharp
services.AddHeadlessAuditLog(o =>
{
    o.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
});

services.AddAuditLogEntityFramework<AppDbContext>();
```

`AddAuditLogEntityFramework` requires `AddHeadlessAuditLog` (from `Headless.AuditLog.Abstractions`) to be called first for options registration, and `AddHeadlessDbContext<T>` for the DbContext registration.

### DbContext setup

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureAuditLog();
}
```

### Entity opt-in

```csharp
public class Patient : AggregateRoot<Guid>, IAuditTracked
{
    public string Name { get; set; } = "";

    [AuditSensitive]
    public string NationalId { get; set; } = "";

    [AuditIgnore]
    public DateTime LastComputedAt { get; set; }
}
```

### Explicit event logging

```csharp
await auditLog.LogAsync(
    "pii.revealed",
    entityType: typeof(Patient).FullName,
    entityId: id.ToString()
);
```

### Query audit entries

```csharp
var entries = await readAuditLog.QueryAsync(
    action: "entity.updated",
    entityType: typeof(Patient).FullName,
    limit: 50,
    cancellationToken: cancellationToken
);
```

## Configuration

### PostgreSQL JSON columns

Pass `jsonColumnType: "jsonb"` to store `OldValues`, `NewValues`, and `ChangedFields` as native JSONB instead of serialized strings:

```csharp
modelBuilder.ConfigureAuditLog(jsonColumnType: "jsonb");
```

### Custom table and schema

```csharp
modelBuilder.ConfigureAuditLog(tableName: "audit_entries", schema: "audit");
```

### Sensitive data strategies

| Strategy | Behavior |
|----------|----------|
| `Redact` (default) | Replaces value with `"***"`; property name still appears in `ChangedFields` |
| `Exclude` | Omits the property entirely from `OldValues`, `NewValues`, and `ChangedFields` |
| `Transform` | Passes value through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize) |

`SensitiveValueTransformer` must be configured whenever the effective strategy is `Transform`. Global misconfiguration fails options resolution; per-property `[AuditSensitive(SensitiveDataStrategy.Transform)]` without a transformer throws an `OptionsValidationException` during capture instead of silently redacting.

Per-property strategy override:

```csharp
[AuditSensitive(SensitiveDataStrategy.Exclude)]
public string CreditCardToken { get; set; } = "";
```

## Key Behaviors

- **Atomicity** - Audit entries are added to the same DbContext and committed in the same transaction as entity changes
- **Zero overhead** - When `IsEnabled` is `false`, `CaptureChanges` returns an empty list immediately
- **Disabled auditing signal** - When `IsEnabled` is `false`, the first capture attempt logs a warning with remediation guidance
- **Soft-delete detection** - Monitors `IsDeleted` and `IsSuspended` property transitions; emits semantic action names instead of generic `entity.updated`
- **Owned entities** - Inherit auditability from their aggregate owner
- **Audit capture errors are non-fatal** - If capturing a single entity fails, a warning is logged and the save continues without that entry
- **JSON round-trip shape** - `OldValues` and `NewValues` deserialize non-string values as `JsonElement`; use `GetDecimal()`, `GetBoolean()`, and similar APIs when reading them back
- **Composite key encoding** - Single-column `EntityId` values remain plain strings; composite keys are serialized as JSON string arrays such as `["tenant-a","order,42"]`
- **Client metadata** - `IpAddress` and `UserAgent` are persisted when explicitly supplied, but automatic EF change capture does not populate them

## SQLite Limitation

The default entity configuration uses a composite primary key `(CreatedAt, Id)` for partition-readiness. SQLite does not support `ValueGeneratedOnAdd` (autoincrement) on composite keys. Consumers targeting SQLite must override the key configuration — for example, using a single-column PK on `Id`:

```csharp
builder.HasKey(e => e.Id); // Override for SQLite
```

## Migration Note

Composite-key `EntityId` values are now serialized as JSON arrays instead of comma-joined strings. Existing stored audit rows using the old comma-joined format remain unchanged; downstream parsers should handle both shapes during transition.

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IAuditChangeCapture` as scoped (`EfAuditChangeCapture`)
- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`)
- Registers `IAuditLog<TContext>` as scoped (`EfAuditLog<TContext>`)
- Registers `IReadAuditLog<TContext>` as scoped (`EfReadAuditLog<TContext>`)
