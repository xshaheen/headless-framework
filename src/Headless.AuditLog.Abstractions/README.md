# Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

## Problem Solved

Provides a provider-agnostic audit log API for representing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to a capture engine or storage implementation.

## Key Features

- `SensitiveDataStrategy` — `Redact` (replace with `"***"`), `Exclude` (omit entirely), or `Transform` (custom function).
- `SensitiveValueContext` — passed to `SensitiveValueTransformer`; provides `EntityType`, `PropertyName`, `PropertyClrType`, `Value`.
- `AuditChangeType` — `Created`, `Updated`, `Deleted`.
- `AuditLogOptions` — master enable/disable, `AuditByDefault` mode, per-entity/property filters, `CaptureErrorStrategy`, configurable default exclusions, sensitive-value transformer.
- `IAuditLog<TContext>` — explicit logging of non-mutation events; `TContext` binds the logger to a specific persistence context for multi-context applications.
- `IReadAuditLog<TContext>` — query abstraction returning `IReadOnlyList<AuditLogEntryData>`; supports filtering by `action`, `entityType`, `entityId`, `userId`, `tenantId`, `from`, `to`, and `limit`.
- `AuditLogEntryData` — immutable record capturing all fields; `OldValues`/`NewValues` are `Dictionary<string, object?>`.
- `IAuditLogStore` — storage abstraction called by the change-tracking pipeline; `Save`/`SaveAsync` take the saving `DbContext` and return `IAuditLogStoreEntry` handles.
- `IAuditLogStoreEntry` — provider handle; orchestrator calls `DiscardPendingChanges()` on failure and `ReleaseAfterCommit()` after success. Both must be idempotent.
- `IAuditChangeCapture` — scans ChangeTracker entries and produces `AuditLogEntryData` records.
- `IAuditEntityIdResolver` — patches deferred entity IDs and temporary property values (store-generated keys, FKs to just-added principals) after `SaveChanges` assigns real keys.
- `IAmbientDbTransactionAccessor` — allows raw ADO.NET stores to enroll in the consumer's active `DbConnection`/`DbTransaction` without taking an EF dependency.

## Installation

```bash
dotnet add package Headless.AuditLog.Abstractions
```

## Quick Start

Automatic capture policy is configured by `Headless.EntityFramework`; see that package's Quick Start. This abstractions package stays EF-free and provides the contracts for explicit event logging and audit-history queries.

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

> This package defines contracts only. Add `Headless.AuditLog.Core` plus exactly one storage provider for DI setup.

## Configuration

| Option | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Master switch; `false` disables all capture. |
| `AuditByDefault` | `false` | Controls entities without explicit EF model policy; explicit `IsAudited()` or `ExcludeFromAudit()` takes precedence. |
| `SensitiveDataStrategy` | `Redact` | Global strategy for properties configured with `IsAuditSensitive()`. |
| `SensitiveValueTransformer` | `null` | Required when effective strategy is `Transform`; must be pure and synchronous. |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; result cached per type. |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; result cached per `(Type, propertyName)`. |
| `DefaultExcludedProperties` | Framework-managed set | Property names skipped during change capture; consumers can add/remove entries. |
| `CaptureErrorStrategy` | `Continue` | `Continue` logs an error and proceeds; `Throw` aborts the save. |

## Dependencies

- `Headless.Extensions`

## Side Effects

None. This is an abstractions package and registers no services.
