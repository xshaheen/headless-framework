# Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

## Problem Solved

Provides a provider-agnostic audit log API for capturing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to any specific storage implementation.

## Key Features

- `IAuditTracked` - Marker interface; entities implementing it are automatically audited on SaveChanges
- `AuditIgnoreAttribute` - Excludes a property (or entire entity) from change capture
- `AuditSensitiveAttribute` - Marks a property as PII/secret; value is handled per configured strategy
- `SensitiveDataStrategy` - `Redact` (replace with `"***"`), `Exclude` (omit entirely), or `Transform` (custom function)
- `AuditLogOptions` - Master enable/disable, `AuditByDefault` mode, per-entity/property filters, configurable default exclusions, sensitive-value transformer
- `IAuditLog` - Explicit logging of non-mutation events (reads, reveals, failures)
- `IAuditLogStore` - Storage abstraction called by the change-tracking pipeline. `Save`/`SaveAsync` return one `IAuditLogStoreEntry` handle per audit row added to the persistence context (empty list = nothing added, orchestrator skips the audit commit step)
- `IAuditLogStoreEntry` - Provider-owned handle for an audit row added to the persistence context; the orchestrator calls `Detach()` on it to roll back the pending row on failure. Implementations must be idempotent
- `IReadAuditLog` - Query abstraction for reading audit entries back without coupling callers to EF types
- `IAuditChangeCapture` - Scans ChangeTracker entries and produces `AuditLogEntryData` records

## Installation

```bash
dotnet add package Headless.AuditLog.Abstractions
```

## Usage

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

### Registration

```csharp
services.AddHeadlessAuditLog(o =>
{
    o.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
});
```

> This package registers options only. Add `Headless.AuditLog.EntityFramework` for the EF Core implementation that wires up storage and change capture.

## Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `IsEnabled` | `true` | Master switch; `false` disables all capture |
| `AuditByDefault` | `false` | When `true`, audits every entity unless `[AuditIgnore]` is present |
| `SensitiveDataStrategy` | `Redact` | Global strategy for `[AuditSensitive]` properties |
| `SensitiveValueTransformer` | `null` | Required whenever the effective strategy is `Transform`; must be a pure, synchronous function |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; first result is cached per type for the capture service lifetime |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; first result is cached per `(Type, propertyName)` for the capture service lifetime |
| `DefaultExcludedProperties` | Framework-managed names | Default property names skipped during change capture; consumers can add/remove entries |

## Important Notes

- `AddHeadlessAuditLog` validates the global `SensitiveDataStrategy.Transform` configuration and fails options resolution when no `SensitiveValueTransformer` is configured.
- If a property explicitly uses `[AuditSensitive(SensitiveDataStrategy.Transform)]`, the first capture attempt throws an `OptionsValidationException` unless `SensitiveValueTransformer` is configured.
- `EntityFilter` and `PropertyFilter` results are cached after first evaluation, so predicates must be pure and deterministic.
- `AuditLogEntryData.OldValues` and `NewValues` may contain `JsonElement` values after provider round-trips; use `JsonElement` APIs such as `GetDecimal()` for typed access.
- `EntityId` stays a plain string for single-column keys; composite keys are serialized as a JSON string array.
- `IpAddress` and `UserAgent` are not auto-populated by the built-in EF change-capture pipeline; set them explicitly through `IAuditLog.LogAsync` or a custom capture implementation.

## Dependencies

- `Headless.Hosting`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`

## Side Effects

None. This is an abstractions package.
