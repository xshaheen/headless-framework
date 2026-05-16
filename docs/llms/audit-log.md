---
domain: Audit Log
packages: AuditLog.Abstractions, AuditLog.EntityFramework
---

# Audit Log

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.AuditLog.Abstractions](#headlessauditlogabstractions)
- [Headless.AuditLog.EntityFramework](#headlessauditlogentityframework)

> Property-level audit logging for entity mutations and explicit business events (PII reveals, cross-tenant access, etc.). EF Core implementation persists audit rows atomically with the originating `SaveChanges`.

## Quick Orientation

Two packages:

- `Headless.AuditLog.Abstractions` — contracts (`IAuditTracked`, `[AuditSensitive]`, `[AuditIgnore]`, `IAuditLog<TContext>`, `IReadAuditLog<TContext>`, `IAuditLogStore`, `AuditLogOptions`).
- `Headless.AuditLog.EntityFramework` — EF Core change capture, persistent storage, and explicit event logging tied to a specific `DbContext`.

Typical setup:

```csharp
services.AddHeadlessAuditLog(o =>
{
    o.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
});
services.AddHeadlessAuditLogEntity<AppDbContext>();
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureAuditLog();
}
```

Mark entities to audit with `IAuditTracked`:

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

Explicit events (reads, PII reveals, failures):

```csharp
await auditLog.LogAsync(
    "pii.revealed",
    entityType: typeof(Patient).FullName,
    entityId: id.ToString()
);
```

Query audit entries through `IReadAuditLog<TContext>` rather than touching the `AuditLogEntry` table directly.

## Agent Instructions

- Mark auditable entities with `IAuditTracked`. To audit every entity by default, set `AuditByDefault = true` on `AuditLogOptions` and use `[AuditIgnore]` to opt out.
- Mark PII/secret fields with `[AuditSensitive]`. Choose the global strategy via `AuditLogOptions.SensitiveDataStrategy`: `Redact` (default), `Exclude`, or `Transform`. When using `Transform`, set `SensitiveValueTransformer` to a pure synchronous function; options validation fails otherwise.
- Use `[AuditIgnore]` on properties (or whole entities) that should not be captured.
- Register the pipeline with `services.AddHeadlessAuditLog(...)` first, then `services.AddHeadlessAuditLogEntity<TContext>()` for each `DbContext` you audit. `AddHeadlessAuditLogEntity` requires `AddHeadlessDbContext<T>` for the same context.
- Call `modelBuilder.ConfigureAuditLog()` inside the `DbContext`'s `OnModelCreating`. Pass `jsonColumnType: "jsonb"` on PostgreSQL for native JSONB storage of `OldValues` / `NewValues` / `ChangedFields`.
- Use `IAuditLog<TContext>` for explicit events (reads, reveals, failures) — do not insert `AuditLogEntry` rows directly. Multi-context applications resolve a distinct logger per owning context via the `TContext` type-arg.
- Use `IReadAuditLog<TContext>` to query audit history. Do not couple callers to `AuditLogEntry` or EF types directly.
- Soft-delete and suspend transitions are detected automatically and emit `entity.soft_deleted` / `entity.restored` / `entity.suspended` / `entity.unsuspended` actions instead of `entity.updated`.
- `EntityFilter` and `PropertyFilter` predicates are cached after first evaluation per `(Type, propertyName)`. Keep them pure and deterministic.
- `IpAddress` and `UserAgent` are not auto-populated by EF change capture — set them explicitly through `IAuditLog.LogAsync` when relevant.
- On SQLite, override the default composite key (`(CreatedAt, Id)`) with a single-column key on `Id` — SQLite cannot autoincrement composite keys.
- When reading `OldValues` / `NewValues` after a provider round-trip, expect `JsonElement` values; use `GetDecimal()`, `GetBoolean()`, etc.

---

# Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

## Problem Solved

Provides a provider-agnostic audit log API for capturing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to any specific storage implementation.

## Key Features

- `IAuditTracked` — marker interface for entities to be audited on `SaveChanges`.
- `[AuditIgnore]` — excludes a property (or entire entity) from change capture.
- `[AuditSensitive]` — marks a property as PII/secret; value is handled per configured strategy.
- `SensitiveDataStrategy` — `Redact` (replace with `"***"`), `Exclude` (omit entirely), or `Transform` (custom function).
- `AuditLogOptions` — master enable/disable, `AuditByDefault` mode, per-entity/property filters, configurable default exclusions, sensitive-value transformer.
- `IAuditLog<TContext>` — explicit logging of non-mutation events bound to a persistence context.
- `IAuditLogStore` — storage abstraction called by the change-tracking pipeline; `Save`/`SaveAsync` take the saving `DbContext` and return one `IAuditLogStoreEntry` per audit row added.
- `IAuditLogStoreEntry` — provider-owned handle; the orchestrator calls `DiscardPendingChanges()` on failure and `ReleaseAfterCommit()` after success. Implementations must be idempotent.
- `IReadAuditLog<TContext>` — query abstraction for reading audit entries without coupling callers to EF.
- `IAuditChangeCapture` — scans ChangeTracker entries and produces `AuditLogEntryData` records.

## Installation

```bash
dotnet add package Headless.AuditLog.Abstractions
```

## Configuration

| Option | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Master switch; `false` disables all capture. |
| `AuditByDefault` | `false` | When `true`, audits every entity unless `[AuditIgnore]` is present. |
| `SensitiveDataStrategy` | `Redact` | Global strategy for `[AuditSensitive]` properties. |
| `SensitiveValueTransformer` | `null` | Required when effective strategy is `Transform`; must be pure and synchronous. |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; result cached per type for the capture service lifetime. |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; result cached per `(Type, propertyName)`. |
| `DefaultExcludedProperties` | Framework-managed | Property names skipped during change capture; consumers can add/remove entries. |

## Important Notes

- `AddHeadlessAuditLog` validates global `SensitiveDataStrategy.Transform` configuration and fails options resolution when no `SensitiveValueTransformer` is configured.
- `[AuditSensitive(SensitiveDataStrategy.Transform)]` on a property throws `OptionsValidationException` on first capture unless `SensitiveValueTransformer` is set.
- `EntityId` is a plain string for single-column keys; composite keys are serialized as a JSON string array.

## Dependencies

- `Headless.Hosting`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`

## Side Effects

None. Abstractions package.

---

# Headless.AuditLog.EntityFramework

EF Core implementation of the audit log subsystem: change capture, persistent storage, and explicit event logging.

## Problem Solved

Wires the audit log pipeline into EF Core's ChangeTracker so entity mutations are captured and persisted atomically with the originating `SaveChanges` — no separate commit, no data loss on rollback.

## Key Features

- `EfAuditChangeCapture` — scans ChangeTracker before save, produces `AuditLogEntryData` per changed entity.
- `EfAuditLogStore` — adds `AuditLogEntry` rows to the same `DbContext` so they commit in the same transaction.
- `EfAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging.
- `EfReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` for filtered read-back.
- `AuditLogEntry` — single-table entity with JSON columns for `OldValues`, `NewValues`, and `ChangedFields`.
- `ConfigureAuditLog()` — `ModelBuilder` extension; supports custom table name, schema, and JSON column type.
- Soft-delete and suspend detection — emits `entity.soft_deleted` / `entity.restored` / `entity.suspended` / `entity.unsuspended` actions on `IsDeleted` / `IsSuspended` transitions.
- Zero overhead when `AuditLogOptions.IsEnabled` is `false`.

## Installation

```bash
dotnet add package Headless.AuditLog.EntityFramework
```

## Configuration

### PostgreSQL JSON columns

```csharp
modelBuilder.ConfigureAuditLog(jsonColumnType: "jsonb");
```

### Custom table and schema

```csharp
modelBuilder.ConfigureAuditLog(tableName: "audit_entries", schema: "audit");
```

### Sensitive data strategies

| Strategy | Behavior |
|---|---|
| `Redact` (default) | Replaces value with `"***"`; property name still appears in `ChangedFields`. |
| `Exclude` | Omits the property entirely from `OldValues`, `NewValues`, and `ChangedFields`. |
| `Transform` | Passes value through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize). |

## Key Behaviors

- **Atomicity** — Audit entries are added to the same `DbContext` and commit in the same transaction as entity changes.
- **Zero overhead** — `CaptureChanges` returns an empty list immediately when `IsEnabled` is `false`.
- **Disabled signal** — When `IsEnabled` is `false`, the first capture attempt logs a warning with remediation guidance.
- **Owned entities** — Inherit auditability from their aggregate owner.
- **Non-fatal capture errors** — If capturing a single entity fails, a warning is logged and the save continues without that entry.
- **JSON round-trip shape** — `OldValues` and `NewValues` deserialize non-string values as `JsonElement`; use `GetDecimal()`, `GetBoolean()`, etc.
- **Composite key encoding** — Single-column `EntityId` values stay plain strings; composite keys are JSON arrays such as `["tenant-a","order,42"]`.
- **Client metadata** — `IpAddress` and `UserAgent` are persisted when explicitly supplied; EF change capture does not populate them automatically.

## SQLite Limitation

The default entity configuration uses a composite primary key `(CreatedAt, Id)` for partition-readiness. SQLite cannot autoincrement composite keys — override with a single-column PK on `Id`:

```csharp
builder.HasKey(e => e.Id); // Override for SQLite
```

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IAuditChangeCapture` as scoped (`EfAuditChangeCapture`).
- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`).
- Registers `IAuditLog<TContext>` as scoped (`EfAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as scoped (`EfReadAuditLog<TContext>`).
