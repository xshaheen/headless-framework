# Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

## Problem Solved

Provides a provider-agnostic audit log API for capturing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to any specific storage implementation.

## Key Features

- `IAuditTracked` — marker interface; entities implementing it are audited automatically on `SaveChanges`.
- `[AuditIgnore]` — excludes a property (on a property) or an entire entity from change capture (on a class, when `AuditByDefault` is enabled).
- `[AuditSensitive]` — marks a property as PII/secret; value is handled per configured strategy. Accepts an optional `SensitiveDataStrategy` parameter to override the global default per-property.
- `SensitiveDataStrategy` — `Redact` (replace with `"***"`), `Exclude` (omit entirely), or `Transform` (custom function).
- `SensitiveValueContext` — passed to `SensitiveValueTransformer`; provides `EntityType`, `PropertyName`, `PropertyClrType`, `Value`.
- `AuditChangeType` — `Created`, `Updated`, `Deleted`.
- `AuditLogOptions` — master enable/disable, `AuditByDefault` mode, per-entity/property filters, `CaptureErrorStrategy`, configurable default exclusions, sensitive-value transformer.
- `AuditLogStorageOptions` — shared storage options: `Schema`, `TableName`, `JsonColumnType`, `CreatedAtColumnType`, `InitializeOnStartup`.
- `AuditLogJsonColumnType` — `Jsonb`, `Json`, `NvarcharMax`.
- `IAuditLog<TContext>` — explicit logging of non-mutation events; `TContext` binds the logger to a specific persistence context for multi-context applications.
- `IReadAuditLog<TContext>` — query abstraction returning `IReadOnlyList<AuditLogEntryData>`; supports filtering by `action`, `entityType`, `entityId`, `userId`, `tenantId`, `from`, `to`, and `limit`.
- `AuditLogEntryData` — immutable record capturing all fields; `OldValues`/`NewValues` are `Dictionary<string, object?>`.
- `IAuditLogStore` — storage abstraction called by the change-tracking pipeline; `Save`/`SaveAsync` take the saving `DbContext` and return `IAuditLogStoreEntry` handles.
- `IAuditLogStoreEntry` — provider handle; orchestrator calls `DiscardPendingChanges()` on failure and `ReleaseAfterCommit()` after success. Both must be idempotent.
- `IAuditChangeCapture` — scans ChangeTracker entries and produces `AuditLogEntryData` records.
- `IAuditEntityIdResolver` — patches deferred entity IDs after `SaveChanges` assigns store-generated keys.
- `IAmbientDbTransactionAccessor` — allows raw ADO.NET stores to enroll in the consumer's active `DbConnection`/`DbTransaction` without taking an EF dependency.
- `HeadlessAuditLogSetupBuilder` — fluent builder passed to `AddHeadlessAuditLog(setup => ...)`; exposes `ConfigureOptions`, `ConfigureStorage`, and `RegisterExtension`.

## Installation

```bash
dotnet add package Headless.AuditLog.Abstractions
```

## Quick Start

Mark entities to audit:

```csharp
public class Patient : AggregateRoot<Guid>, IAuditTracked
{
    public string Name { get; set; } = "";

    [AuditSensitive]
    public string NationalId { get; set; } = "";

    [AuditSensitive(SensitiveDataStrategy.Exclude)]
    public string CreditCardToken { get; set; } = "";

    [AuditIgnore]
    public DateTime LastComputedAt { get; set; }
}
```

Log explicit events:

```csharp
await auditLog.LogAsync(
    "pii.revealed",
    entityType: typeof(Patient).FullName,
    entityId: id.ToString(),
    data: new Dictionary<string, object?> { ["requestedBy"] = currentUser.UserId }
);
```

Query audit history:

```csharp
var entries = await readAuditLog.QueryAsync(
    entityType: typeof(Patient).FullName,
    entityId: patientId.ToString(),
    limit: 50,
    cancellationToken: ct
);
```

> This package registers options only. Add `Headless.AuditLog.Storage.EntityFramework`, `Headless.AuditLog.Storage.PostgreSql`, or `Headless.AuditLog.Storage.SqlServer` for storage.

## Configuration

| Option | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Master switch; `false` disables all capture. |
| `AuditByDefault` | `false` | When `true`, audits every entity unless `[AuditIgnore]` is present. |
| `SensitiveDataStrategy` | `Redact` | Global strategy for `[AuditSensitive]` properties. |
| `SensitiveValueTransformer` | `null` | Required when effective strategy is `Transform`; must be pure and synchronous. |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; result cached per type. |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; result cached per `(Type, propertyName)`. |
| `DefaultExcludedProperties` | Framework-managed set | Property names skipped during change capture; consumers can add/remove entries. |
| `CaptureErrorStrategy` | `Continue` | `Continue` logs an error and proceeds; `Throw` aborts the save. |

Storage options (`AuditLogStorageOptions`):

| Option | Default | Description |
|---|---|---|
| `Schema` | `"audit"` | Database schema name. |
| `TableName` | `"audit_log"` | Table name. |
| `JsonColumnType` | `null` (provider default) | Override JSON column type: `Jsonb`, `Json`, or `NvarcharMax`. |
| `CreatedAtColumnType` | `null` (provider default) | Override the timestamp column DDL type string. |
| `InitializeOnStartup` | `true` | Set `false` to skip DDL at startup (raw providers only). |

## Dependencies

- `Headless.Hosting`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`

## Side Effects

None. This is an abstractions package; it registers `AuditLogOptions` and its validator only when `AddHeadlessAuditLog` is called.
