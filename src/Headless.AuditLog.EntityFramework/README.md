# Headless.AuditLog.EntityFramework

EF Core implementation of the audit log subsystem: change capture, persistent storage, and explicit event logging.

## Problem Solved

Wires the audit log pipeline into EF Core's ChangeTracker so that entity mutations are captured and persisted atomically with the originating `SaveChanges` call — no separate commit, no data loss on rollback.

## Key Features

- `EfAuditChangeCapture` - Scans ChangeTracker before save; produces `AuditLogEntryData` per changed entity
- `EfAuditLogStore` - Adds `AuditLogEntry` rows to the same DbContext so they commit in the same transaction
- `EfAuditLog` - Implements `IAuditLog` for explicit event logging (reads, PII reveals, failures)
- `AuditLogEntry` - Single-table entity with JSON columns for `OldValues`, `NewValues`, and `ChangedFields`
- `ConfigureAuditLog()` - ModelBuilder extension; supports custom table name, schema, and JSON column type
- Soft-delete detection: automatically emits `entity.soft_deleted` / `entity.restored` actions when `IsDeleted` transitions
- Suspend detection: emits `entity.suspended` / `entity.unsuspended` when `IsSuspended` transitions
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

services.AddHeadlessAuditLogEntityFramework();
```

`AddHeadlessAuditLogEntityFramework` requires `AddHeadlessAuditLog` (from `Headless.AuditLog.Abstractions`) to be called first for options registration, and `AddHeadlessDbContext<T>` for the DbContext registration.

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

Per-property strategy override:

```csharp
[AuditSensitive(SensitiveDataStrategy.Exclude)]
public string CreditCardToken { get; set; } = "";
```

## Key Behaviors

- **Atomicity** - Audit entries are added to the same DbContext and committed in the same transaction as entity changes
- **Zero overhead** - When `IsEnabled` is `false`, `CaptureChanges` returns an empty list immediately
- **Soft-delete detection** - Monitors `IsDeleted` and `IsSuspended` property transitions; emits semantic action names instead of generic `entity.updated`
- **Owned entities** - Inherit auditability from their aggregate owner
- **Audit capture errors are non-fatal** - If capturing a single entity fails, a warning is logged and the save continues without that entry

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IAuditChangeCapture` as scoped (`EfAuditChangeCapture`)
- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`)
- Registers `IAuditLog` as scoped (`EfAuditLog`)
