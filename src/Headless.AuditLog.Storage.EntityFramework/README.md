# Headless.AuditLog.Storage.EntityFramework

EF Core storage provider for automatic audit entries and explicit event logging.

## Problem Solved

Persists audit entries through the application's EF Core `DbContext` so they commit atomically with the originating `SaveChanges` — no separate connection or commit.

## Key Features

- `EfAuditLogStore` — adds `AuditLogEntry` rows to the same `DbContext` so they commit in the same transaction as entity changes.
- `EfAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging; resolves `ICurrentUser`, `ICurrentTenant`, `ICorrelationIdProvider`, and `TimeProvider` from DI.
- `EfReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` using `IDbContextFactory<TContext>` (no-tracking queries).
- `AuditLogEntry` — EF entity excluded from automatic capture through EF model metadata, preventing recursion when `AuditByDefault` is enabled.
- `AuditLogModelBuilderExtensions.AddHeadlessAuditLog(modelBuilder, options)` — registers and configures the `AuditLogEntry` entity type; idempotent.
- Composite primary key `(CreatedAt, Id)` for partition-readiness; index set covers tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, and correlation ID.
- Startup gate validates that `AuditLogEntry` was fully configured through `modelBuilder.AddHeadlessAuditLog` and throws with a clear message if the call was omitted, even when the entity was pre-registered.

## Design Notes

The composite primary key `(CreatedAt, Id)` is a deliberate time-partitioning choice for audit retention strategies. SQLite does not support `ValueGeneratedOnAdd` on composite keys — consumers targeting SQLite must override to a single-column PK on `Id`.

`AddHeadlessAuditLog` applies `ExcludeFromAudit()` to `AuditLogEntry`, including when the entity was pre-registered. Calling `IsAudited()` for `AuditLogEntry` later overrides that policy deterministically, but this is unsupported because it can recursively create audit rows.

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

    modelBuilder.Entity<Patient>(patient =>
    {
        patient.IsAudited();
        patient.Property(x => x.NationalId).IsAuditSensitive();
        patient.Property(x => x.CreditCardToken).IsAuditSensitive(SensitiveDataStrategy.Exclude);
        patient.Property(x => x.LastComputedAt).ExcludeFromAudit();
    });
}
```

The fluent audit policy is supplied by `Headless.EntityFramework`. Owned entries inherit eligibility from their root owner; derived types inherit the nearest configured base policy unless overridden.

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

Entity policy is tri-state: `IsAudited()` and `ExcludeFromAudit()` override `AuditByDefault`; an unconfigured entity follows the option. Default property exclusions, explicit `ExcludeFromAudit()`, and `PropertyFilter` veto before sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides the global strategy.

SQLite key override (required when targeting SQLite):

```csharp
builder.HasKey(e => e.Id); // single-column PK for SQLite
```

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.AuditLog.Core`
- `Headless.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`).
- Registers `IAuditLog<TContext>` as scoped (`EfAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`EfReadAuditLog<TContext>`).
- Registers `AuditLogEntityValidationStartupGate<TContext>` as a hosted service (validates model at startup).
- Automatic `ChangeTracker` capture and the fluent model policy are supplied by `Headless.EntityFramework`; this package only selects EF-backed audit storage.
