# Headless.AuditLog.Storage.EntityFramework

EF Core implementation of the audit log subsystem: change capture, persistent storage, and explicit event logging.

## Problem Solved

Wires the audit log pipeline into EF Core's ChangeTracker so that entity mutations are captured and persisted atomically with the originating `SaveChanges` call — no separate commit, no data loss on rollback.

## Key Features

- `EfAuditChangeCapture` - Scans ChangeTracker before save; produces `AuditLogEntryData` per changed entity
- `EfAuditLogStore` - Adds `AuditLogEntry` rows to the same DbContext so they commit in the same transaction
- `EfAuditLog<TContext>` - Implements `IAuditLog<TContext>` for explicit event logging (reads, PII reveals, failures)
- `EfReadAuditLog<TContext>` - Implements `IReadAuditLog<TContext>` for filtered read-back without leaking EF entities
- `AuditLogEntry` - Single-table entity with JSON columns for `OldValues`, `NewValues`, and `ChangedFields`
- `AddHeadlessAuditLog()` - ModelBuilder extension; uses `AuditLogStorageOptions` for table name, schema, and JSON column type
- Soft-delete detection: automatically emits `entity.soft_deleted` / `entity.restored` actions when `IsDeleted` transitions
- Suspend detection: emits `entity.suspended` / `entity.unsuspended` when `IsSuspended` transitions
- `EntityFilter` and `PropertyFilter` results are cached after first evaluation for the capture service lifetime
- Zero overhead when `AuditLogOptions.IsEnabled` is `false`

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
        options.JsonColumnType = AuditLogJsonColumnType.Jsonb;
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

`UseEntityFramework<TContext>()` requires the same context to be registered with EF Core. Register `IDbContextFactory<TContext>` too if you resolve `IReadAuditLog<TContext>`.

### DbContext setup

```csharp
public AppDbContext(
    DbContextOptions<AppDbContext> options,
    IOptions<AuditLogStorageOptions> auditLogStorage)
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

### Storage Options

Set `JsonColumnType` to store `OldValues`, `NewValues`, and `ChangedFields` as native JSONB on PostgreSQL. The property is an allowlist enum (`AuditLogJsonColumnType`) — `Jsonb`, `Json`, or `NvarcharMax` — to prevent SQL identifier injection through the column-type string. `CreatedAtColumnType` is a free string override for the timestamp column (defaults: `timestamp with time zone` on Postgres, `datetime2` on SQL Server):

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options =>
    {
        options.TableName = "audit_entries";
        options.Schema = "audit";
        options.JsonColumnType = AuditLogJsonColumnType.Jsonb;
        options.CreatedAtColumnType = "timestamp with time zone"; // optional explicit override
    });
    setup.UseEntityFramework<AppDbContext>();
});
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
- Registers `IReadAuditLog<TContext>` as singleton (`EfReadAuditLog<TContext>`)
