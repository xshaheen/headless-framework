---
domain: Audit Log
packages: AuditLog.Abstractions, AuditLog.Core, AuditLog.Storage.EntityFramework, AuditLog.Storage.PostgreSql, AuditLog.Storage.SqlServer
---

# Audit Log

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Two flavors of audit entry](#two-flavors-of-audit-entry)
    - [What gets captured](#what-gets-captured)
    - [Sensitive data handling](#sensitive-data-handling)
    - [Scope and unit-of-work](#scope-and-unit-of-work)
    - [Field length limits](#field-length-limits)
    - [Startup initialization](#startup-initialization)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.AuditLog.Abstractions](#headlessauditlogabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.AuditLog.Core](#headlessauditlogcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.AuditLog.Storage.EntityFramework](#headlessauditlogstorageentityframework)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.AuditLog.Storage.PostgreSql](#headlessauditlogstoragepostgresql)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.AuditLog.Storage.SqlServer](#headlessauditlogstoragesqlserver)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Property-level audit logging for entity mutations and explicit business events (PII reveals, cross-tenant access, etc.). EF Core implementation persists audit rows atomically with the originating `SaveChanges`. Raw ADO.NET providers (PostgreSql, SqlServer) create and own the audit table at host startup.

## Quick Orientation

Install `Headless.AuditLog.Core` plus exactly one storage provider:

| Package | Use when |
|---|---|
| `Headless.AuditLog.Abstractions` | Contract package pulled by Core and providers; reference directly only when you need contracts without DI setup. |
| `Headless.AuditLog.Core` | DI setup, options validation, setup builders, and the exactly-one-provider registration pipeline. |
| `Headless.AuditLog.Storage.EntityFramework` | You already use EF Core and want audit rows to commit atomically in the same `SaveChanges` transaction. |
| `Headless.AuditLog.Storage.PostgreSql` | You want zero EF dependency and are on PostgreSQL. |
| `Headless.AuditLog.Storage.SqlServer` | You want zero EF dependency and are on SQL Server. |

Code against `IAuditLog<TContext>` and `IReadAuditLog<TContext>` — never reference provider types directly.

## Agent Instructions

- Mark auditable entities with `IAuditTracked`. To audit every entity by default, set `AuditByDefault = true` on `AuditLogOptions` and use `[AuditIgnore]` to opt out.
- Mark PII/secret fields with `[AuditSensitive]`. Choose the global strategy via `AuditLogOptions.SensitiveDataStrategy`: `Redact` (default), `Exclude`, or `Transform`. When using `Transform`, set `SensitiveValueTransformer` to a pure synchronous function; options validation fails otherwise.
- Use `[AuditIgnore]` on properties (or entire entities) that must not be captured.
- Register the audit log with exactly one `services.AddHeadlessAuditLog(setup => setup.Use...)` call. Put global audit options in `setup.ConfigureOptions(...)` and storage-table options in `setup.ConfigureStorage(...)`.
- For EF storage, call `setup.UseEntityFramework<TContext>()`, register the same context with EF Core, register `IDbContextFactory<TContext>` for read-back, and call `modelBuilder.AddHeadlessAuditLog(auditLogStorageOptions)` inside `OnModelCreating`. A startup gate validates this at boot and throws if it is missing.
- For raw storage, call `setup.UsePostgreSql(connectionString)` or `setup.UseSqlServer(connectionString)`; the provider creates the audit table at host startup and writes over its own connection.
- Use `IAuditLog<TContext>` for explicit events (reads, reveals, failures) — do not insert `AuditLogEntry` rows directly. Multi-context applications resolve a distinct logger per owning context via the `TContext` type parameter.
- Use `IReadAuditLog<TContext>` to query audit history. Do not couple callers to `AuditLogEntry` or EF types directly.
- Soft-delete and suspend transitions are detected automatically and emit `entity.soft_deleted` / `entity.restored` / `entity.suspended` / `entity.unsuspended` actions instead of `entity.updated`.
- `EntityFilter` and `PropertyFilter` predicates are cached after first evaluation per `(Type, propertyName)`. Keep them pure and deterministic.
- `IpAddress` and `UserAgent` are not auto-populated by EF change capture — set them explicitly through `IAuditLog<TContext>.LogAsync` when relevant.
- On SQLite, override the default composite primary key `(CreatedAt, Id)` with a single-column key on `Id` — SQLite cannot autoincrement composite keys.
- When reading `OldValues` / `NewValues` after a provider round-trip, expect `JsonElement` values; use `GetDecimal()`, `GetBoolean()`, etc. for typed access.
- Raw providers (PostgreSql, SqlServer) attempt to enroll writes in the consumer's ambient EF transaction when the database drivers match. If there is no ambient transaction, audit rows commit on a separate connection before `SaveChanges` — an entity-save failure then leaves orphan audit rows. Use an explicit transaction on the EF side to guarantee atomicity.
- `CaptureErrorStrategy` defaults to `Continue`: a capture failure logs an error and lets `SaveChanges` proceed — a per-entity failure skips only that entity's audit entry, a whole-capture failure skips the batch. Set to `Throw` to abort the save when capture fails.

## Core Concepts

### Two flavors of audit entry

The pipeline produces two kinds of rows:

1. **Automatic property-level entries** — emitted on every `SaveChanges` for entities implementing `IAuditTracked` (or all entities when `AuditByDefault` is `true`). The `EfAuditChangeCapture` service scans `ChangeTracker` entries before the save, records `OldValues`, `NewValues`, and `ChangedFields`, and maps the EF `EntityState` to an `AuditChangeType` (`Created`, `Updated`, `Deleted`).

2. **Explicit business-event entries** — emitted by calling `IAuditLog<TContext>.LogAsync(action, ...)`. Used for events that have no corresponding entity mutation: data reads, PII reveals, cross-tenant access, authorization failures. These entries have no `OldValues`, no `ChangeType`, and the caller controls every field including the `action` string (e.g., `"pii.revealed"`, `"report.downloaded"`).

### What gets captured

Each entry carries:

- **Actor** — `UserId`, `AccountId`, `TenantId` (resolved from `ICurrentUser` / `ICurrentTenant`), `CorrelationId` (from `ICorrelationIdProvider`), and optionally `IpAddress` / `UserAgent` (must be set explicitly — the pipeline does not auto-populate these).
- **Entity identity** — `EntityType` (full CLR type name) and `EntityId` (plain string for single-column keys; JSON array for composite keys).
- **Change data** — `OldValues` and `NewValues` as `Dictionary<string, object?>` (serialized to JSON), and `ChangedFields` as `List<string>`. After a provider round-trip, non-string values deserialize as `JsonElement` — use `GetDecimal()`, `GetBoolean()`, etc.
- **Outcome** — `Success` flag and optional `ErrorCode`.
- **Timestamp** — `CreatedAt` (UTC).

### Sensitive data handling

Properties marked `[AuditSensitive]` are subject to a strategy applied at capture time:

- `Redact` (default) — replaces the value with `"***"`; the property name still appears in `ChangedFields` so you know it changed.
- `Exclude` — omits the property entirely from `OldValues`, `NewValues`, and `ChangedFields`.
- `Transform` — passes the value through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize). The transformer receives a `SensitiveValueContext` carrying `EntityType`, `PropertyName`, `PropertyClrType`, and `Value`. Must be a pure, synchronous function.

Per-property strategy: `[AuditSensitive(SensitiveDataStrategy.Exclude)]` overrides the global default for that property.

### Scope and unit-of-work

For EF storage, audit entries are added to the **same `DbContext` instance** and commit in the **same database transaction** as the entity changes — no separate round-trip and no data loss on rollback. The `IAuditLogStore` receives the `savingContext` parameter on every `Save`/`SaveAsync` call to enforce this in multi-context applications.

For raw ADO.NET providers, atomicity is available but conditional: the store attempts to enroll in the consumer's ambient `DbConnection` / `DbTransaction` via `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the drivers differ, audit rows commit on a separate connection and are not atomic with `SaveChanges`.

### Field length limits

All string fields are silently truncated to column limits before persistence (`AuditLogFieldLimits` is the single source of truth shared by all providers). Key limits: `Action` 256, `EntityType` 512, `EntityId` 256, `UserId`/`AccountId`/`TenantId`/`CorrelationId` 128, `UserAgent` 512, `IpAddress` 45.

### Startup initialization

Raw providers (`PostgreSql`, `SqlServer`) create the audit schema, table, and indexes at host startup via a `HostedInitializer`. Set `AuditLogStorageOptions.InitializeOnStartup = false` to skip DDL when the schema is provisioned out-of-band. The initializer still reports `IsInitialized = true` so dependent services do not block.

## Choosing a Provider

| | EF Core | PostgreSql | SqlServer |
|---|---|---|---|
| **Use when** | Already using EF Core; want atomic commit with entity changes; migrations managed by EF. | Pure PostgreSQL shop; no EF dependency desired; want `jsonb` native columns. | SQL Server shop; no EF dependency desired. |
| **Avoid when** | Not using EF Core; or need to avoid EF dependency in the audit service layer. | Not on PostgreSQL; or need EF-managed migrations. | Not on SQL Server; or need EF-managed migrations. |
| **Atomicity** | Always — same `DbContext`, same transaction. | When consumer opens an explicit transaction that matches the Npgsql driver; otherwise separate connection. | When consumer opens an explicit transaction that matches the SqlClient driver; otherwise separate connection. |
| **Schema management** | EF migrations. | Self-initializing DDL at startup (idempotent; races serialized via `pg_advisory_xact_lock`). | Self-initializing DDL at startup (idempotent; races serialized via `sp_getapplock`). |
| **JSON columns** | String columns by default; opt into native `jsonb`/`json` via `AuditLogJsonColumnType`. | `jsonb` by default (native JSONB type; `Json` or `NvarcharMax` also accepted). | `nvarchar(max)` only. |
| **Extra dependencies** | `Microsoft.EntityFrameworkCore` | `Npgsql` | `Microsoft.Data.SqlClient` |
| **Change capture** | Built-in via `EfAuditChangeCapture` scanning `ChangeTracker`. | Requires pairing with EF (via `AddHeadlessDbContext<TContext>`) or a custom `IAuditChangeCapture`. | Same as PostgreSql. |

---

## Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

### Problem Solved

Provides a provider-agnostic audit log API for capturing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to any specific storage implementation.

### Key Features

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
- `IAuditEntityIdResolver` — patches deferred entity IDs and temporary property values (store-generated keys, FKs to just-added principals) after `SaveChanges` assigns real keys.
- `IAmbientDbTransactionAccessor` — allows raw ADO.NET stores to enroll in the consumer's active `DbConnection`/`DbTransaction` without taking an EF dependency.

### Installation

```bash
dotnet add package Headless.AuditLog.Abstractions
```

### Quick Start

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

### Configuration

| Option | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Master switch; `false` disables all capture. |
| `AuditByDefault` | `false` | When `true`, audits every entity unless `[AuditIgnore]` is present. |
| `SensitiveDataStrategy` | `Redact` | Global strategy for `[AuditSensitive]` properties. |
| `SensitiveValueTransformer` | `null` | Required when effective strategy is `Transform`; must be pure and synchronous. |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; result cached per type. |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; result cached per `(Type, propertyName)`. |
| `DefaultExcludedProperties` | Framework-managed set | Property names skipped during change capture; consumers can add/remove entries. Default set includes `ConcurrencyStamp`, `DateCreated`, `DateUpdated`, `DateDeleted`, `DateSuspended`, `CreatedById`, `UpdatedById`, `DeletedById`, `SuspendedById`. |
| `CaptureErrorStrategy` | `Continue` | `Continue` logs an error and proceeds; `Throw` aborts the save. |

Storage options (`AuditLogStorageOptions`):

| Option | Default | Description |
|---|---|---|
| `Schema` | `"audit"` | Database schema name. |
| `TableName` | `"audit_log"` | Table name. |
| `JsonColumnType` | `null` (provider default) | Override JSON column type: `Jsonb`, `Json`, or `NvarcharMax`. |
| `CreatedAtColumnType` | `null` (provider default) | Override the timestamp column DDL type string. |
| `InitializeOnStartup` | `true` | Set `false` to skip DDL at startup (raw providers only). |

### Dependencies

- `Headless.Extensions`

### Side Effects

None. This is an abstractions package and registers no services.

---

## Headless.AuditLog.Core

DI setup package for `Headless.AuditLog`: options validation, setup builders, and the exactly-one-storage-provider registration pipeline.

### Problem Solved

Keeps audit-log contracts provider-neutral while centralizing the public `AddHeadlessAuditLog(...)` setup API and provider extension hook in one Core package.

### Key Features

- `SetupAuditLog.AddHeadlessAuditLog(setup => setup.Use...)` — the single public DI entry point in the `Headless.AuditLog` namespace (add `using Headless.AuditLog;`); requires exactly one storage provider. The options-only registration is `internal` (a funnel the builder overload uses to register `AuditLogOptions` once), so a store-less audit log cannot be registered by accident.
- `HeadlessAuditLogSetupBuilder` — fluent builder passed to `AddHeadlessAuditLog(setup => ...)`; exposes `ConfigureOptions`, `ConfigureStorage`, and `RegisterExtension`.
- `HeadlessAuditLogBuilder` — returned by `AddHeadlessAuditLog(setup => ...)`; provides access to `IServiceCollection` for chaining.
- `IAuditLogStorageOptionsExtension` — setup-time hook implemented by storage provider packages.
- `AuditLogOptionsValidator` — validates transform-sensitive-data configuration at startup.

### Installation

```bash
dotnet add package Headless.AuditLog.Core
```

### Quick Start

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
    });

    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.TableName = "audit_log";
    });

    setup.UseEntityFramework<AppDbContext>();
});
```

### Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.Checks`
- `Headless.Hosting`
- `FluentValidation`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`

### Side Effects

- Registers `AuditLogOptions` with startup validation.
- Configures `AuditLogStorageOptions`.
- Runs the selected storage provider's setup extension.

---

## Headless.AuditLog.Storage.EntityFramework

EF Core implementation of the audit log subsystem: change capture, persistent storage, and explicit event logging.

### Problem Solved

Wires the audit log pipeline into EF Core's ChangeTracker so entity mutations are captured and persisted atomically with the originating `SaveChanges` — no separate commit, no data loss on rollback.

### Key Features

- `EfAuditChangeCapture` — scans ChangeTracker before save, produces `AuditLogEntryData` per changed entity.
- `EfAuditLogStore` — adds `AuditLogEntry` rows to the same `DbContext` so they commit in the same transaction as entity changes.
- `EfAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging; resolves `ICurrentUser`, `ICurrentTenant`, `ICorrelationIdProvider`, and `IClock` from DI.
- `EfReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` using `IDbContextFactory<TContext>` (no-tracking queries).
- `AuditLogEntry` — EF entity; decorated with `[AuditIgnore]` to prevent recursive capture when `AuditByDefault` is enabled.
- `AuditLogModelBuilderExtensions.AddHeadlessAuditLog(modelBuilder, options)` — registers and configures the `AuditLogEntry` entity type; idempotent.
- Composite primary key `(CreatedAt, Id)` for partition-readiness; index set covers tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, and correlation ID.
- Soft-delete detection: emits `entity.soft_deleted` / `entity.restored` on `IsDeleted` transitions.
- Suspend detection: emits `entity.suspended` / `entity.unsuspended` on `IsSuspended` transitions.
- Zero overhead when `AuditLogOptions.IsEnabled` is `false` — `CaptureChanges` returns an empty list immediately.
- Startup gate (`AuditLogEntityValidationStartupGate`) validates that `AuditLogEntry` is registered in the `DbContext` model and throws with a clear message if `modelBuilder.AddHeadlessAuditLog` was omitted.

### Design Notes

The composite primary key `(CreatedAt, Id)` is a deliberate time-partitioning choice: partitioning the audit table by `CreatedAt` range is a common retention strategy. SQLite does not support `ValueGeneratedOnAdd` on composite keys, so consumers targeting SQLite must override to a single-column PK on `Id`.

`EfAuditLogStore` intentionally does not call `SaveChanges` — audit entries are tracked in the same `DbContext` and commit when the entity save runs. This requires that `AuditLogEntry` is in the same model as the audited entities. If you need to write audit entries to a different database or schema, use the raw ADO.NET providers instead.

JSON columns default to string columns (via value converters), universally portable across all EF-supported databases. Override to a native type via `AuditLogStorageOptions.JsonColumnType = AuditLogJsonColumnType.Jsonb` when targeting PostgreSQL for native `jsonb` semantics.

### Installation

```bash
dotnet add package Headless.AuditLog.Storage.EntityFramework
```

### Quick Start

#### DI setup

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

#### DbContext setup

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

#### Explicit event logging

```csharp
await auditLog.LogAsync("pii.revealed", entityType: typeof(Patient).FullName, entityId: id.ToString());
```

#### Query audit entries

```csharp
var entries = await readAuditLog.QueryAsync(
    action: "entity.updated",
    entityType: typeof(Patient).FullName,
    limit: 50,
    cancellationToken: ct
);
```

### Configuration

```csharp
setup.ConfigureStorage(options =>
{
    options.TableName = "audit_entries";
    options.Schema = "audit";
    options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // optional
    options.CreatedAtColumnType = "timestamp with time zone"; // optional explicit override
});
```

`AuditLogJsonColumnType` is an allowlist enum so the column-type string cannot inject SQL identifiers. `CreatedAtColumnType` is a free string override for the timestamp column; provider defaults are `timestamp with time zone` on PostgreSQL and `datetime2` on SQL Server when unset.

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

### Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.AuditLog.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Registers `IAuditChangeCapture` as scoped (`EfAuditChangeCapture`).
- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`).
- Registers `IAuditLog<TContext>` as scoped (`EfAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`EfReadAuditLog<TContext>`).
- Registers `AuditLogEntityValidationStartupGate<TContext>` as a hosted service (validates model at startup).

---

## Headless.AuditLog.Storage.PostgreSql

Raw PostgreSQL storage provider for audit rows. No Entity Framework dependency — uses Npgsql directly.

### Problem Solved

Provides PostgreSQL-native audit log storage without pulling Entity Framework into the dependency graph. Creates and maintains the audit table via self-initializing DDL, stores JSON columns as `jsonb` by default, and can enroll writes atomically in the consumer's active Npgsql transaction.

### Key Features

- No EF Core dependency — depends only on `Npgsql`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog.Core`.
- `PostgreSqlAuditLogStore` — implements `IAuditLogStore`; enrolls in the consumer's ambient Npgsql transaction when available; falls back to its own connection otherwise.
- `PostgreSqlAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging.
- `PostgreSqlReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` via parameterized SQL queries.
- `PostgreSqlAuditLogStorageInitializer` — creates schema, table, and indexes at host startup; DDL races across replicas serialized with `pg_advisory_xact_lock`.
- Batched INSERT: up to 500 rows per command (cached per row count to avoid repeated string building).
- `jsonb` by default for `OldValues`, `NewValues`, and `ChangedFields`; override via `AuditLogStorageOptions.JsonColumnType` (`Jsonb` or `Json` accepted; `NvarcharMax` rejected at options validation time).
- `PostgreSqlAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s).
- `UsePostgreSql` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<PostgreSqlAuditLogOptions>)`, and `(Action<PostgreSqlAuditLogOptions, IServiceProvider>)`.
- Same index set as the EF provider: tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, correlation ID.

### Design Notes

Transaction enrollment is conditional: the store attempts to resolve a `NpgsqlConnection` and `NpgsqlTransaction` from the registered `IAmbientDbTransactionAccessor`. If no ambient transaction exists — or if the connection is a different driver type — it falls back to opening its own connection. In the fallback path, audit rows commit before `SaveChanges` completes; an entity-save failure leaves orphan audit rows. A deduplicated warning is logged once per distinct saving-context type (and once per distinct driver mismatch type) to flag this.

DDL initialization uses two separate transactions — one for schema+table, one for indexes — so a concurrent-startup race that aborts the table transaction does not wipe the index DDL as a side effect.

### Installation

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
```

### Quick Start

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.TableName = "audit_log";
    });
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

Skip startup DDL when schema is provisioned out-of-band:

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(connectionString);
});
```

Configure provider-specific options:

```csharp
setup.UsePostgreSql(options =>
{
    options.ConnectionString = connectionString;
    options.CommandTimeout = TimeSpan.FromSeconds(60);
});
```

Bind provider options from configuration, or configure with service resolution:

```csharp
setup.UsePostgreSql(builder.Configuration.GetSection("Headless:AuditLog:PostgreSql"));
setup.UsePostgreSql((options, sp) =>
    options.ConnectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("AuditLog")!);
```

### Configuration

`PostgreSqlAuditLogOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | (required) | Npgsql connection string. |
| `CommandTimeout` | `30s` | Timeout for DDL and DML commands. |

`AuditLogStorageOptions.JsonColumnType` for this provider: `Jsonb` (default) or `Json`. `NvarcharMax` is rejected at options validation time.

### Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.AuditLog.Core`
- `Headless.Serializer`
- `Npgsql`

### Side Effects

- Registers `PostgreSqlAuditLogStorageInitializer` as a hosted service (creates schema + table + indexes at startup).
- Registers `PostgreSqlAuditLogWriter` as singleton.
- Registers `IAuditLogStore` as scoped (`PostgreSqlAuditLogStore`).
- Registers `IAuditLog<TContext>` as singleton (`PostgreSqlAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`PostgreSqlReadAuditLog<TContext>`).
- Registers `IJsonSerializer`, `IClock`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.

---

## Headless.AuditLog.Storage.SqlServer

Raw SQL Server storage provider for audit rows. No Entity Framework dependency — uses `Microsoft.Data.SqlClient` directly.

### Problem Solved

Provides SQL Server-native audit log storage without pulling Entity Framework into the dependency graph. Creates and maintains the audit table via self-initializing DDL, stores JSON payloads as `nvarchar(max)` by default, and can enroll writes atomically in the consumer's active SQL Server transaction.

### Key Features

- No EF Core dependency — depends only on `Microsoft.Data.SqlClient`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog.Core`.
- `SqlServerAuditLogStore` — implements `IAuditLogStore`; enrolls in the consumer's ambient `SqlTransaction` when available; falls back to its own connection otherwise.
- `SqlServerAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging.
- `SqlServerReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` via parameterized SQL queries using `TOP(@Limit)`.
- `SqlServerAuditLogStorageInitializer` — creates schema, table, and indexes at host startup; DDL races serialized with `sp_getapplock`; wrapped in `BEGIN TRAN`/`COMMIT TRAN` with a `TRY`/`CATCH`/`ROLLBACK` guard.
- Batched INSERT: up to 100 rows per command (SQL Server parameter limit is lower than PostgreSQL's).
- `nvarchar(max)` by default for JSON columns; `NvarcharMax` is the only accepted `AuditLogJsonColumnType` (PostgreSQL-specific types are rejected at options validation time).
- `SqlServerAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s).
- `UseSqlServer` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<SqlServerAuditLogOptions>)`, and `(Action<SqlServerAuditLogOptions, IServiceProvider>)`.
- Same index set as the EF provider: tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, correlation ID.

### Design Notes

Transaction enrollment mirrors the PostgreSQL provider: the store resolves the ambient `SqlConnection`/`SqlTransaction` via `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the driver is not `SqlClient`, it falls back to its own connection. In the fallback path, audit rows commit before `SaveChanges` — an entity-save failure leaves orphan rows. A deduplicated warning is logged once per distinct saving-context type and once per driver mismatch.

DDL initialization uses `sp_getapplock` (`Session` scope, 30 s timeout) to serialize concurrent multi-replica startups without deadlocking. The entire DDL body runs inside a single `BEGIN TRAN`/`COMMIT TRAN` block; the applock is released explicitly before `COMMIT` and defensively in the `CATCH` block to ensure the session-scoped lock is freed before the connection returns to the pool.

Batch size is capped at 100 rows (vs. 500 for PostgreSQL) because SQL Server's parameter limit per batch is lower.

### Installation

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
```

### Quick Start

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.TableName = "audit_log";
    });
    setup.UseSqlServer(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

Skip startup DDL when schema is provisioned out-of-band:

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UseSqlServer(connectionString);
});
```

Configure provider-specific options:

```csharp
setup.UseSqlServer(options =>
{
    options.ConnectionString = connectionString;
    options.CommandTimeout = TimeSpan.FromSeconds(60);
});
```

Bind provider options from configuration, or configure with service resolution:

```csharp
setup.UseSqlServer(builder.Configuration.GetSection("Headless:AuditLog:SqlServer"));
setup.UseSqlServer((options, sp) =>
    options.ConnectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("AuditLog")!);
```

### Configuration

`SqlServerAuditLogOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | (required) | SQL Server connection string. |
| `CommandTimeout` | `30s` | Timeout for DDL and DML commands. |

`AuditLogStorageOptions.JsonColumnType` for this provider: `NvarcharMax` only. `Jsonb` and `Json` are rejected at options validation time.

### Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.AuditLog.Core`
- `Headless.Serializer`
- `Microsoft.Data.SqlClient`

### Side Effects

- Registers `SqlServerAuditLogStorageInitializer` as a hosted service (creates schema + table + indexes at startup).
- Registers `SqlServerAuditLogWriter` as singleton.
- Registers `IAuditLogStore` as scoped (`SqlServerAuditLogStore`).
- Registers `IAuditLog<TContext>` as singleton (`SqlServerAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`SqlServerReadAuditLog<TContext>`).
- Registers `IJsonSerializer`, `IClock`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.
