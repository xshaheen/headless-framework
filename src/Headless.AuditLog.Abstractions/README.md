# Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

## Problem Solved

Provides a provider-agnostic audit log API for capturing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to any specific storage implementation.

## Key Features

- `IAuditTracked` - Marker interface; entities implementing it are automatically audited on SaveChanges
- `AuditIgnoreAttribute` - Excludes a property (or entire entity) from change capture
- `AuditSensitiveAttribute` - Marks a property as PII/secret; value is handled per configured strategy
- `SensitiveDataStrategy` - `Redact` (replace with `"***"`), `Exclude` (omit entirely), or `Transform` (custom function)
- `AuditLogOptions` - Master enable/disable, `AuditAllEntities` mode, per-entity/property filters, sensitive-value transformer
- `IAuditLog` - Explicit logging of non-mutation events (reads, reveals, failures)
- `IAuditLogStore` - Storage abstraction called by the change-tracking pipeline
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
| `AuditAllEntities` | `false` | When `true`, audits every entity unless `[AuditIgnore]` is present |
| `SensitiveDataStrategy` | `Redact` | Global strategy for `[AuditSensitive]` properties |
| `SensitiveValueTransformer` | `null` | Required when strategy is `Transform`; must be a pure, synchronous function |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; result is cached per type |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; result is cached |

## Dependencies

- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`

## Side Effects

None. This is an abstractions package.
