---
domain: Audit Log
packages: AuditLog.Abstractions, AuditLog.Core, AuditLog.Storage.EntityFramework, AuditLog.Storage.PostgreSql, AuditLog.Storage.SqlServer
---

# Audit Log

> Property-level audit logging for entity mutations and explicit business events (PII reveals, cross-tenant access, etc.). EF Core implementation persists audit rows atomically with the originating `SaveChanges`. Raw ADO.NET providers (PostgreSql, SqlServer) create and own the audit table at host startup.

## Orientation

Install `Headless.AuditLog.Core` plus exactly one storage provider:

| Package | Use when |
|---|---|
| `Headless.AuditLog.Abstractions` | Contract package pulled by Core and providers; reference directly only when you need contracts without DI setup. |
| `Headless.AuditLog.Core` | DI setup, options validation, storage options, setup builders, and the exactly-one-provider registration pipeline. |
| `Headless.AuditLog.Storage.EntityFramework` | You already use EF Core and want audit rows to commit atomically in the same `SaveChanges` transaction. |
| `Headless.AuditLog.Storage.PostgreSql` | You want zero EF dependency and are on PostgreSQL. |
| `Headless.AuditLog.Storage.SqlServer` | You want zero EF dependency and are on SQL Server. |

Code against `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, and `IReadAuditLog<TContext>` — never reference provider types directly. To audit authorization denials in an API, call `services.AddHeadlessAuthorizationDenialAudit<TContext>()` from `Headless.Api.Core`.

## Agent Rules

- Configure automatic capture in the EF model: `modelBuilder.Entity<TEntity>().IsAudited()` opts in, `ExcludeFromAudit()` opts an entity or property out, and `IsAuditSensitive(...)` marks a property as sensitive. These methods live in `Headless.EntityFramework`; domain entities need no audit marker or attributes.
- Treat entity policy as tri-state. Explicit inclusion or exclusion wins; an unconfigured entity follows `AuditLogOptions.AuditByDefault`. Owned entries inherit eligibility from their root owner, and derived types inherit the nearest configured base policy unless overridden.
- Apply property policy in this order: framework default exclusions, explicit `ExcludeFromAudit()`, and `PropertyFilter` veto before sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides the global `AuditLogOptions.SensitiveDataStrategy`.
- Register the audit log with exactly one `services.AddHeadlessAuditLog(setup => setup.Use...)` call. Put global audit options in `setup.ConfigureOptions(...)` and storage-table options in `setup.ConfigureStorage(...)`.
- For EF storage, call `setup.UseEntityFramework<TContext>()`, register the same context with EF Core, register `IDbContextFactory<TContext>` for read-back, and call `modelBuilder.AddHeadlessAuditLog(auditLogStorageOptions)` inside `OnModelCreating`. A startup gate validates this at boot and throws if it is missing.
- For raw storage, call `setup.UsePostgreSql(connectionString)` or `setup.UseSqlServer(connectionString)`; the provider creates the audit table at host startup and writes over its own connection.
- Raw PostgreSQL and SQL Server packages provide storage only. Automatic change capture and the fluent metadata policy are EF-specific; there is no parallel provider-neutral policy registry.
- Use `IAuditLog<TContext>` for explicit events (reads, reveals, failures) — do not insert `AuditLogEntry` rows directly. Multi-context applications resolve a distinct logger per owning context via the `TContext` type parameter.
- Use `IReadAuditLog<TContext>` to query audit history. Do not couple callers to `AuditLogEntry` or EF types directly.
- Page audit history with the continuation token, never with offsets: pass the previous page's `ContinuationPage<AuditLogEntryData>.ContinuationToken` as `AuditLogQuery.ContinuationToken`, with the same filters and `Direction`, until it comes back `null`. The token is opaque; do not parse or build it.
- Use `IAuditLogWriter<TContext>` for an explicit event that must persist even though no `SaveChanges` follows, such as a denied request. Use `IAuditLog<TContext>` when the entry must commit or roll back with the caller's entity changes. On EF storage the writer needs `IDbContextFactory<TContext>`.
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

1. **Automatic property-level entries** — emitted on `SaveChanges` for entities included by the finalized EF model policy, or for unconfigured entities when `AuditByDefault` is `true`. The `EfAuditChangeCapture` service scans `ChangeTracker` entries before the save, records `OldValues`, `NewValues`, and `ChangedFields`, and maps the EF `EntityState` to an `AuditChangeType` (`Created`, `Updated`, `Deleted`).

2. **Explicit business-event entries** — emitted by calling `IAuditLog<TContext>.LogAsync(request)`. Used for events that have no corresponding entity mutation: data reads, PII reveals, cross-tenant access, authorization failures. These entries have no `OldValues`, no `ChangeType`, and the caller controls every field through `AuditLogWriteRequest`, including the `action` string (e.g., `"pii.revealed"`, `"report.downloaded"`). `IAuditLogWriter<TContext>.WriteAsync(request)` records the same kind of entry but commits it immediately in its own transaction; see [Standalone writes](#standalone-writes).

### What gets captured

Each entry carries:

- **Actor** — `UserId`, `AccountId`, `TenantId` (resolved from `ICurrentUser` / `ICurrentTenant`), `CorrelationId` (from `ICorrelationIdProvider`), and optionally `IpAddress` / `UserAgent` (must be set explicitly — the pipeline does not auto-populate these).
- **Entity identity** — `EntityType` (full CLR type name) and `EntityId` (plain string for single-column keys; JSON array for composite keys).
- **Change data** — `OldValues` and `NewValues` as `Dictionary<string, object?>` (serialized to JSON), and `ChangedFields` as `List<string>`. After a provider round-trip, non-string values deserialize as `JsonElement` — use `GetDecimal()`, `GetBoolean()`, etc.
- **Outcome** — `Success` flag and optional `ErrorCode`.
- **Timestamp** — `CreatedAt` (UTC).

### Sensitive data handling

Properties configured with `IsAuditSensitive()` are subject to a strategy applied at capture time:

- `Redact` (default) — replaces the value with `"***"`; the property name still appears in `ChangedFields` so you know it changed.
- `Exclude` — omits the property entirely from `OldValues`, `NewValues`, and `ChangedFields`.
- `Transform` — passes the value through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize). The transformer receives a `SensitiveValueContext` carrying `EntityType`, `PropertyName`, `PropertyClrType`, and `Value`. Must be a pure, synchronous function.

A strategy passed to `IsAuditSensitive(SensitiveDataStrategy.Exclude)` overrides the global default for that property. Explicit property exclusion and `PropertyFilter` vetoes run first, so an excluded property is never processed as sensitive.

### Scope and unit-of-work

For EF storage, audit entries are added to the **same `DbContext` instance** and commit in the **same database transaction** as the entity changes — no separate round-trip and no data loss on rollback. The `IAuditLogStore` receives the `savingContext` parameter on every `Save`/`SaveAsync` call to enforce this in multi-context applications.

For raw ADO.NET providers, atomicity is available but conditional: the store attempts to enroll in the consumer's ambient `DbConnection` / `DbTransaction` via `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the drivers differ, audit rows commit on a separate connection and are not atomic with `SaveChanges`.

### Paging audit history

`IReadAuditLog<TContext>.QueryAsync` returns one `ContinuationPage<AuditLogEntryData>` (from `Headless.Primitives`). Entries are ordered by `CreatedAt`, then by the row `Id`, in `AuditLogQuery.Direction`: `NewestFirst` (default) or `OldestFirst`. The `Id` tie-break gives entries that share a timestamp a stable order, so no entry is skipped or repeated at a page boundary.

Paging is keyset-based. The page's `ContinuationToken` encodes the `(CreatedAt, Id)` of its last entry; the next query returns entries strictly after that position. The token is `null` when no further entries match. Each provider fetches `Size + 1` rows to decide this, so there is no count query.

```csharp
string? token = null;

do
{
    var page = await readAuditLog.QueryAsync(
        new AuditLogQuery
        {
            TenantId = tenantId,
            Action = "authorization.forbidden",
            Size = 100,
            ContinuationToken = token,
        },
        ct
    );

    Render(page.Items);
    token = page.ContinuationToken;
} while (token is not null);
```

- Send a token with the same filters and `Direction` that produced it. The token carries only a position, so a changed filter still returns entries after that position under the new filter, which is rarely what a UI wants.
- Entries written after the first page was read appear only when they sort after the current position: with `OldestFirst` they show up at the end of the walk, and with `NewestFirst` they appear only when you start again from the first page.
- A token stays valid across providers and process restarts.
- `QueryAsync` throws `ArgumentException` for a token it did not issue or an undefined `Direction`, and `ArgumentOutOfRangeException` when `Size` is less than one or equal to int.MaxValue.
- Filters: `Action`, `EntityType`, `EntityId`, `UserId` (the actor), `AccountId`, `TenantId`, `CorrelationId`, `From` (inclusive), and `To` (exclusive). Every provider ships an index for each common filter that ends in `(CreatedAt, Id)`, so a filtered page seeks to its position instead of scanning.

### Standalone writes

`IAuditLogWriter<TContext>` records an explicit event and commits it before `WriteAsync` returns, in a transaction of its own. Use it when the request ends without a `SaveChanges` that would carry an `IAuditLog<TContext>` entry, for example an authorization denial or a read-only request that must leave a trail. A standalone entry survives a later rollback of the caller's work.

- EF storage writes through a new context from `IDbContextFactory<TContext>`, so the caller's scoped context and its pending changes are untouched.
- PostgreSQL and SQL Server storage write through their own connection, exactly like their `IAuditLog<TContext>`.
- `WriteAsync` does nothing when `AuditLogOptions.IsEnabled` is `false`.

### Authorization denial entries

`services.AddHeadlessAuthorizationDenialAudit<TContext>()` in `Headless.Api.Core` wraps the registered `IAuthorizationMiddlewareResultHandler` and writes one entry through `IAuditLogWriter<TContext>` each time the authorization middleware challenges or forbids a request. Register the audit log storage too; the writer comes from it.

| Field | Value |
|---|---|
| `Action` | `authorization.challenged` (no or rejected authentication, usually 401) or `authorization.forbidden` (authenticated, a requirement failed, usually 403). |
| `Success` | `false` |
| `UserId`, `AccountId`, `TenantId`, `CorrelationId` | Stamped by the writer from `ICurrentUser`, `ICurrentTenant`, and `ICorrelationIdProvider`. `UserId` is empty when the caller is unauthenticated. |
| `NewValues` | `method` (HTTP method), `route` (the endpoint's route template, such as `/orders/{id}`, never the request path), and `policies` (the named policies on the endpoint; empty for the default policy). |

The request body, query string, headers, and raw path are never recorded. The entry commits before the challenge or forbid response is written, and the write is not cancelled when the client disconnects, so resetting the connection does not erase the record. A failed audit write is logged as an error (`AuthorizationDenialAuditWriteFailed`) and the denial response proceeds unchanged. Call the registration after any custom `IAuthorizationMiddlewareResultHandler`, which it wraps; a handler registered after it replaces it. Every audited denial costs one database insert and commit, including denials of anonymous traffic, so put rate limiting ahead of authorization on endpoints that attract scanners or credential stuffing. Denials raised outside the authorization middleware, such as a `Results.Forbid()` returned by an endpoint, are not audited.

Query denials with `new AuditLogQuery { TenantId = tenantId, Action = "authorization.forbidden" }`.

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
| **Change capture** | Built-in via `EfAuditChangeCapture` scanning `ChangeTracker` and reading EF model policy. | Storage only; pair with `Headless.EntityFramework` for built-in automatic capture, or log explicit events. | Same as PostgreSql. |

---

## Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

### API and behavior

- `SensitiveDataStrategy` — `Redact` (replace with `"***"`), `Exclude` (omit entirely), or `Transform` (custom function).
- `SensitiveValueContext` — passed to `SensitiveValueTransformer`; provides `EntityType`, `PropertyName`, `PropertyClrType`, `Value`.
- `AuditChangeType` — `Created`, `Updated`, `Deleted`.
- `AuditLogOptions` — master enable/disable, `AuditByDefault` mode, per-entity/property filters, `CaptureErrorStrategy`, configurable default exclusions, sensitive-value transformer.
- `IAuditLog<TContext>` — explicit logging of non-mutation events; `TContext` binds the logger to a specific persistence context for multi-context applications.
- `AuditLogWriteRequest` — explicit event data with a required `Action` initializer and optional entity, payload, success, and error metadata.
- `IAuditLogWriter<TContext>` — explicit logging that commits each entry immediately in its own transaction; see [Standalone writes](#standalone-writes).
- `IReadAuditLog<TContext>` — keyset-paged query abstraction returning `ContinuationPage<AuditLogEntryData>`; see [Paging audit history](#paging-audit-history).
- `AuditLogQuery` — implements `IContinuationPageRequest`: filters (`Action`, `EntityType`, `EntityId`, `UserId`, `AccountId`, `TenantId`, `CorrelationId`, `From`, `To`), `Direction`, `Size` (default 100), and `ContinuationToken`.
- `AuditLogSortDirection` — `NewestFirst` (default) or `OldestFirst`.
- `AuditLogEntryData` — immutable record capturing all fields; `OldValues`/`NewValues` are `Dictionary<string, object?>`.
- `IAuditLogStore` — storage abstraction called by the change-tracking pipeline; `Save`/`SaveAsync` take the saving `DbContext` and return `IAuditLogStoreEntry` handles.
- `IAuditLogStoreEntry` — provider handle; orchestrator calls `DiscardPendingChanges()` on failure and `ReleaseAfterCommit()` after success. Both must be idempotent.
- `IAuditChangeCapture` — scans ChangeTracker entries and produces `AuditLogEntryData` records.
- `IAuditEntityIdResolver` — patches deferred entity IDs and temporary property values (store-generated keys, FKs to just-added principals) after `SaveChanges` assigns real keys.
- `IAmbientDbTransactionAccessor` — allows raw ADO.NET stores to enroll in the consumer's active `DbConnection`/`DbTransaction` without taking an EF dependency.

### Install

```bash
dotnet add package Headless.AuditLog.Abstractions
```

### Setup and use

Automatic capture policy is configured by `Headless.EntityFramework`; see the EF storage provider Quick Start below. This abstractions package stays EF-free and provides the contracts for explicit event logging and audit-history queries.

Log explicit events:

```csharp
await auditLog.LogAsync(
    new AuditLogWriteRequest
    {
        Action = "pii.revealed",
        EntityType = typeof(Patient).FullName,
        EntityId = id.ToString(),
        Data = new Dictionary<string, object?> { ["requestedBy"] = currentUser.UserId },
    }
);
```

Query audit history:

```csharp
var page = await readAuditLog.QueryAsync(
    new AuditLogQuery
    {
        EntityType = typeof(Patient).FullName,
        EntityId = patientId.ToString(),
        Size = 50,
    },
    ct
);
// page.Items holds the entries; pass page.ContinuationToken to fetch the next page.
```

### Configuration

| Option | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Master switch; `false` disables all capture. |
| `AuditByDefault` | `false` | Controls entities without explicit EF model policy; explicit `IsAudited()` or `ExcludeFromAudit()` takes precedence. |
| `SensitiveDataStrategy` | `Redact` | Global strategy for properties configured with `IsAuditSensitive()`. |
| `SensitiveValueTransformer` | `null` | Required when effective strategy is `Transform`; must be pure and synchronous. |
| `EntityFilter` | `null` | Predicate returning `true` to exclude a type; result cached per type. |
| `PropertyFilter` | `null` | Predicate returning `true` to exclude a property; result cached per `(Type, propertyName)`. |
| `DefaultExcludedProperties` | Framework-managed set | Property names skipped during change capture; consumers can add/remove entries. Default set includes `ConcurrencyStamp`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `SuspendedAt`, `CreatedById`, `UpdatedById`, `DeletedById`, `SuspendedById`. |
| `CaptureErrorStrategy` | `Continue` | `Continue` logs an error and proceeds; `Throw` aborts the save. |

### Runtime behavior

None. This is an abstractions package and registers no services.

---

## Headless.AuditLog.Core

DI setup package for `Headless.AuditLog`: options validation, setup builders, and the exactly-one-storage-provider registration pipeline.

### API and behavior

- `SetupAuditLog.AddHeadlessAuditLog(setup => setup.Use...)` — the single public DI entry point in the `Headless.AuditLog` namespace (add `using Headless.AuditLog;`); requires exactly one storage provider. The options-only registration is `internal` (a funnel the builder overload uses to register `AuditLogOptions` once), so a store-less audit log cannot be registered by accident.
- `HeadlessAuditLogSetupBuilder` — fluent builder passed to `AddHeadlessAuditLog(setup => ...)`; exposes `ConfigureOptions`, `ConfigureStorage`, and `RegisterExtension`.
- `HeadlessAuditLogBuilder` — returned by `AddHeadlessAuditLog(setup => ...)`; provides access to `IServiceCollection` for chaining.
- `IAuditLogStorageOptionsExtension` — setup-time hook implemented by storage provider packages.
- `AuditLogStorageOptions` — shared storage options: `Schema`, `TableName`, `JsonColumnType`, `CreatedAtColumnType`, `InitializeOnStartup`.
- `AuditLogJsonColumnType` — provider-validated JSON column type enum: `Jsonb`, `Json`, `NvarcharMax`.
- `AuditLogOptionsValidator` — validates transform-sensitive-data configuration at startup.

### Install

```bash
dotnet add package Headless.AuditLog.Core
```

### Setup and use

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

### Configuration

Configure audit behavior through `setup.ConfigureOptions(...)` and storage shape through `setup.ConfigureStorage(...)`. Then select exactly one storage provider by calling the provider extension, such as `UseEntityFramework<TContext>()`, `UsePostgreSql(...)`, or `UseSqlServer(...)`, from the installed storage package. `ConfigureStorage` also accepts the `Headless:AuditLog:Storage` configuration section.

Storage options (`AuditLogStorageOptions`):

| Option | Default | Description |
|---|---|---|
| `Schema` | `"audit"` | Database schema name. |
| `TableName` | `"audit_log"` | Table name. |
| `JsonColumnType` | `null` (provider default) | Override JSON column type: `Jsonb`, `Json`, or `NvarcharMax`. |
| `CreatedAtColumnType` | `null` (provider default) | Override the timestamp column DDL type string. |
| `InitializeOnStartup` | `true` | Set `false` to skip DDL at startup (raw providers only). |

### Runtime behavior

- Registers `AuditLogOptions` with startup validation.
- Configures `AuditLogStorageOptions`.
- Runs the selected storage provider's setup extension.

---

## Headless.AuditLog.Storage.EntityFramework

EF Core storage provider for automatic audit entries and explicit event logging.

### API and behavior

- `EfAuditLogStore` — adds `AuditLogEntry` rows to the same `DbContext` so they commit in the same transaction as entity changes.
- `EfAuditLog<TContext>` — implements `IAuditLog<TContext>` for explicit event logging; resolves `ICurrentUser`, `ICurrentTenant`, `ICorrelationIdProvider`, and `TimeProvider` from DI.
- `EfAuditLogWriter<TContext>` — implements `IAuditLogWriter<TContext>`; saves each entry through a new context from `IDbContextFactory<TContext>`.
- `EfReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` using `IDbContextFactory<TContext>` (no-tracking queries).
- `AuditLogEntry` — EF entity excluded from automatic capture through EF model metadata, preventing recursion when `AuditByDefault` is enabled.
- `AuditLogModelBuilderExtensions.AddHeadlessAuditLog(modelBuilder, options)` — registers and configures the `AuditLogEntry` entity type; idempotent.
- Composite primary key `(CreatedAt, Id)` for partition-readiness; index set covers tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, tenant+account+time, and correlation ID, each ending in `(CreatedAt, Id)` for keyset paging.
- A startup validator (`AuditLogEntityStartupValidator`) checks that `AuditLogEntry` was fully configured through `modelBuilder.AddHeadlessAuditLog` and throws with a clear message if the call was omitted, even when the entity was pre-registered.

### Design constraints

The composite primary key `(CreatedAt, Id)` is a deliberate time-partitioning choice: partitioning the audit table by `CreatedAt` range is a common retention strategy. SQLite does not support `ValueGeneratedOnAdd` on composite keys, so consumers targeting SQLite must override to a single-column PK on `Id`.

`EfAuditLogStore` intentionally does not call `SaveChanges` — audit entries are tracked in the same `DbContext` and commit when the entity save runs. This requires that `AuditLogEntry` is in the same model as the audited entities. If you need to write audit entries to a different database or schema, use the raw ADO.NET providers instead.

`AddHeadlessAuditLog` applies `ExcludeFromAudit()` to `AuditLogEntry`, including when the entity was pre-registered. Calling `IsAudited()` for `AuditLogEntry` later overrides that policy deterministically, but this is unsupported because it can recursively create audit rows.

JSON columns default to string columns (via value converters), universally portable across all EF-supported databases. Override to a native type via `AuditLogStorageOptions.JsonColumnType = AuditLogJsonColumnType.Jsonb` when targeting PostgreSQL for native `jsonb` semantics.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.EntityFramework
```

### Setup and use

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

`UseEntityFramework<TContext>()` requires the same context to be registered with EF Core. Register `IDbContextFactory<TContext>` too if you resolve `IReadAuditLog<TContext>` or `IAuditLogWriter<TContext>`.

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

#### Explicit event logging

```csharp
await auditLog.LogAsync(new AuditLogWriteRequest
{
    Action = "pii.revealed",
    EntityType = typeof(Patient).FullName,
    EntityId = id.ToString(),
});
```

#### Query audit entries

```csharp
var page = await readAuditLog.QueryAsync(
    new AuditLogQuery
    {
        Action = "entity.updated",
        EntityType = typeof(Patient).FullName,
        Size = 50,
    },
    ct
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

Entity policy is tri-state: `IsAudited()` and `ExcludeFromAudit()` override `AuditByDefault`; an unconfigured entity follows the option. Default property exclusions, explicit `ExcludeFromAudit()`, and `PropertyFilter` veto before sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides the global strategy.

SQLite key override (required when targeting SQLite):

```csharp
builder.HasKey(e => e.Id); // single-column PK for SQLite
```

### Runtime behavior

- Registers `IAuditLogStore` as scoped (`EfAuditLogStore`).
- Registers `IAuditLog<TContext>` as scoped (`EfAuditLog<TContext>`).
- Registers `IAuditLogWriter<TContext>` as scoped (`EfAuditLogWriter<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`EfReadAuditLog<TContext>`).
- Registers `AuditLogEntityStartupValidator<TContext>` as an `IHeadlessStartupValidator` (validates the model at startup).
- Automatic `ChangeTracker` capture and the fluent model policy are supplied by `Headless.EntityFramework`; this package only selects EF-backed audit storage.

---

## Headless.AuditLog.Storage.PostgreSql

Raw PostgreSQL storage provider for audit rows. No Entity Framework dependency — uses Npgsql directly.

### API and behavior

- No EF Core dependency — depends only on `Npgsql`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog.Core`.
- `PostgreSqlAuditLogStore` — implements `IAuditLogStore`; enrolls in the consumer's ambient Npgsql transaction when available; falls back to its own connection otherwise.
- `PostgreSqlAuditLog<TContext>` — implements `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` for explicit event logging; both write over the provider's own connection.
- `PostgreSqlReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` via parameterized SQL queries.
- `PostgreSqlAuditLogStorageInitializer` — creates schema, table, and indexes at host startup; DDL races across replicas serialized with `pg_advisory_xact_lock`.
- Batched INSERT: up to 500 rows per command (cached per row count to avoid repeated string building).
- `jsonb` by default for `OldValues`, `NewValues`, and `ChangedFields`; override via `AuditLogStorageOptions.JsonColumnType` (`Jsonb` or `Json` accepted; `NvarcharMax` rejected at options validation time).
- `PostgreSqlAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s).
- `UsePostgreSql` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<PostgreSqlAuditLogOptions>)`, and `(Action<PostgreSqlAuditLogOptions, IServiceProvider>)`.
- Same index set as the EF provider: tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, tenant+account+time, correlation ID, each ending in `(CreatedAt, Id)`.

### Design constraints

Transaction enrollment is conditional: the store attempts to resolve a `NpgsqlConnection` and `NpgsqlTransaction` from the registered `IAmbientDbTransactionAccessor`. If no ambient transaction exists — or if the connection is a different driver type — it falls back to opening its own connection. In the fallback path, audit rows commit before `SaveChanges` completes; an entity-save failure leaves orphan audit rows. A deduplicated warning is logged once per distinct saving-context type (and once per distinct driver mismatch type) to flag this.

DDL initialization uses two separate transactions — one for schema+table, one for indexes — so a concurrent-startup race that aborts the table transaction does not wipe the index DDL as a side effect.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
```

### Setup and use

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

### Runtime behavior

- Registers `PostgreSqlAuditLogStorageInitializer` as a hosted service (creates schema + table + indexes at startup).
- Registers `PostgreSqlAuditLogWriter` as singleton.
- Registers `IAuditLogStore` as scoped (`PostgreSqlAuditLogStore`).
- Registers `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` as singletons (`PostgreSqlAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`PostgreSqlReadAuditLog<TContext>`).
- Registers `IJsonSerializer`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.

---

## Headless.AuditLog.Storage.SqlServer

Raw SQL Server storage provider for audit rows. No Entity Framework dependency — uses `Microsoft.Data.SqlClient` directly.

### API and behavior

- No EF Core dependency — depends only on `Microsoft.Data.SqlClient`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog.Core`.
- `SqlServerAuditLogStore` — implements `IAuditLogStore`; enrolls in the consumer's ambient `SqlTransaction` when available; falls back to its own connection otherwise.
- `SqlServerAuditLog<TContext>` — implements `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` for explicit event logging; both write over the provider's own connection.
- `SqlServerReadAuditLog<TContext>` — implements `IReadAuditLog<TContext>` via parameterized SQL queries using `TOP(@Limit)`. The writer and reader bind timestamps as `datetime2`, so stored values, range bounds, and continuation positions keep full precision.
- `SqlServerAuditLogStorageInitializer` — creates schema, table, and indexes at host startup; DDL races serialized with `sp_getapplock`; wrapped in `BEGIN TRAN`/`COMMIT TRAN` with a `TRY`/`CATCH`/`ROLLBACK` guard.
- Batched INSERT: up to 100 rows per command (SQL Server parameter limit is lower than PostgreSQL's).
- `nvarchar(max)` by default for JSON columns; `NvarcharMax` is the only accepted `AuditLogJsonColumnType` (PostgreSQL-specific types are rejected at options validation time).
- `SqlServerAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s).
- `UseSqlServer` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<SqlServerAuditLogOptions>)`, and `(Action<SqlServerAuditLogOptions, IServiceProvider>)`.
- Same index set as the EF provider: tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, tenant+account+time, correlation ID, each ending in `(CreatedAt, Id)`.

### Design constraints

Transaction enrollment mirrors the PostgreSQL provider: the store resolves the ambient `SqlConnection`/`SqlTransaction` via `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the driver is not `SqlClient`, it falls back to its own connection. In the fallback path, audit rows commit before `SaveChanges` — an entity-save failure leaves orphan rows. A deduplicated warning is logged once per distinct saving-context type and once per driver mismatch.

DDL initialization uses `sp_getapplock` (`Session` scope, 30 s timeout) to serialize concurrent multi-replica startups without deadlocking. The entire DDL body runs inside a single `BEGIN TRAN`/`COMMIT TRAN` block; the applock is released explicitly before `COMMIT` and defensively in the `CATCH` block to ensure the session-scoped lock is freed before the connection returns to the pool.

Batch size is capped at 100 rows (vs. 500 for PostgreSQL) because SQL Server's parameter limit per batch is lower.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
```

### Setup and use

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

### Runtime behavior

- Registers `SqlServerAuditLogStorageInitializer` as a hosted service (creates schema + table + indexes at startup).
- Registers `SqlServerAuditLogWriter` as singleton.
- Registers `IAuditLogStore` as scoped (`SqlServerAuditLogStore`).
- Registers `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` as singletons (`SqlServerAuditLog<TContext>`).
- Registers `IReadAuditLog<TContext>` as singleton (`SqlServerReadAuditLog<TContext>`).
- Registers `IJsonSerializer`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.
