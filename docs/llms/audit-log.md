---
domain: Audit Log
packages: AuditLog.Abstractions, AuditLog, AuditLog.Storage.EntityFramework, AuditLog.Storage.PostgreSql, AuditLog.Storage.SqlServer
---

# Audit Log

> Property-level audit logging for entity mutations and explicit business events (PII reveals, cross-tenant access, etc.). EF Core implementation persists audit rows atomically with the originating `SaveChanges`. Raw ADO.NET providers (PostgreSql, SqlServer) create and own the audit table at host startup.

## Orientation

Install `Headless.AuditLog` plus exactly one storage provider:

| Package | Use when |
|---|---|
| `Headless.AuditLog.Abstractions` | Contract package pulled by Core and providers; reference directly only when you need contracts without DI setup. |
| `Headless.AuditLog` | DI setup, options validation, storage options, setup builders, and the exactly-one-provider registration pipeline. |
| `Headless.AuditLog.Storage.EntityFramework` | You already use EF Core and want audit rows to commit atomically in the same `SaveChanges` transaction. |
| `Headless.AuditLog.Storage.PostgreSql` | You want zero EF dependency and are on PostgreSQL. |
| `Headless.AuditLog.Storage.SqlServer` | You want zero EF dependency and are on SQL Server. |

Code against `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, and `IReadAuditLog<TContext>` — never reference provider types directly. To audit authorization denials in an API, call `services.AddHeadlessAuthorizationDenialAudit<TContext>()` from `Headless.Api`.

## Agent Rules

- Configure automatic capture in the EF model: `modelBuilder.Entity<TEntity>().IsAudited()` opts in, `ExcludeFromAudit()` opts an entity or property out, and `IsAuditSensitive(...)` marks a property as sensitive. These methods live in `Headless.EntityFramework.Primitives` (which `Headless.EntityFramework` references); domain entities need no audit marker or attributes.
- Treat entity policy as tri-state. Explicit inclusion or exclusion wins; an unconfigured entity follows `AuditLogOptions.AuditByDefault`. Owned entries inherit eligibility from their root owner, and derived types inherit the nearest configured base policy unless overridden.
- Apply property policy in this order: framework default exclusions, explicit `ExcludeFromAudit()`, and `PropertyFilter` veto before sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides the global `AuditLogOptions.SensitiveDataStrategy`.
- Register the audit log with exactly one `services.AddHeadlessAuditLog(setup => setup.Use...)` call. Put global audit options in `setup.ConfigureOptions(...)` and storage-table options in `setup.ConfigureStorage(...)`.
- For EF storage, call `setup.UseEntityFramework<TContext>()`, register the same context with EF Core, register a singleton `IDbContextFactory<TContext>` (`AddDbContextFactory<TContext>()` or `AddPooledDbContextFactory<TContext>()` for a plain `DbContext`, or `AddHeadlessDbContext<TContext>()` or `AddHeadlessDbContextPool<TContext>()` for a `HeadlessDbContext`; both Headless registrations add the factory) for read-back, and call `modelBuilder.AddHeadlessAuditLog(this)` inside `OnModelCreating`, which reads the storage options from the context's services and names every object in the database's convention (snake_case on PostgreSQL, PascalCase elsewhere). A startup gate validates this at boot and throws if it is missing. The factory must be a singleton (the default lifetime of all four registrations): the EF reader and startup gate are singletons that would capture a scoped or transient factory for the life of the host, so startup refuses one with `InvalidServiceLifetimeException`.
- For raw storage, call `setup.UsePostgreSql(connectionString)` or `setup.UseSqlServer(connectionString)`, or the parameterless `UsePostgreSql()` / `UseSqlServer()` to reuse the connection registered by `AddPostgreSqlSql` / `AddSqlServerSql`; the provider creates the audit table at host startup and writes over its own connection.
- Raw PostgreSQL and SQL Server packages provide storage only. Automatic change capture and the fluent metadata policy are EF-specific; there is no parallel provider-neutral policy registry.
- Use `IAuditLog<TContext>` for explicit events (reads, reveals, failures) — do not insert `AuditLogEntry` rows directly. Multi-context applications resolve a distinct logger per owning context via the `TContext` type parameter.
- Use `IReadAuditLog<TContext>` to query audit history. Do not couple callers to `AuditLogEntry` or EF types directly.
- Page audit history with the continuation token, never with offsets: pass the previous page's `ContinuationPage<AuditLogEntryData>.ContinuationToken` as `AuditLogQuery.ContinuationToken`, with the same filters and `Direction`, until it comes back `null`. The token is opaque; do not parse or build it.
- Use `IAuditLogWriter<TContext>` for an explicit event that must persist even though no `SaveChanges` follows, such as a denied request. Use `IAuditLog<TContext>` when the entry must commit or roll back with the caller's entity changes. On EF storage the writer needs `IDbContextFactory<TContext>`.
- Soft-delete transitions are detected automatically and emit `entity.soft_deleted` / `entity.restored` actions instead of `entity.updated`.
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

`services.AddHeadlessAuthorizationDenialAudit<TContext>()` in `Headless.Api` wraps the registered `IAuthorizationMiddlewareResultHandler` and writes one entry through `IAuditLogWriter<TContext>` each time the authorization middleware challenges or forbids a request. Register the audit log storage too; the writer comes from it.

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

Raw providers (`PostgreSql`, `SqlServer`) contribute the audit table (step `AuditLog/1`) and its indexes (step `AuditLog/2`) to the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), which creates the schema and applies them at host startup. A configured `TableName` is part of the history id (`AuditLog:<table>`). `JsonColumnType` and `CreatedAtColumnType` shape the DDL, so changing either after the table exists fails startup with a checksum mismatch. Set `AuditLogStorageOptions.InitializeOnStartup = false` when the schema is provisioned out-of-band. The runner then skips the feature's steps in `Apply` mode, still includes them in `Verify` mode and `SchemaRunner.ExportScript`, and startup does not block.

## Choosing a Provider

| | EF Core | PostgreSql | SqlServer |
|---|---|---|---|
| **Use when** | Already using EF Core; want atomic commit with entity changes; migrations managed by EF. | Pure PostgreSQL shop; no EF dependency desired; want `jsonb` native columns. | SQL Server shop; no EF dependency desired. |
| **Avoid when** | Not using EF Core; or need to avoid EF dependency in the audit service layer. | Not on PostgreSQL; or need EF-managed migrations. | Not on SQL Server; or need EF-managed migrations. |
| **Atomicity** | Always — same `DbContext`, same transaction. | When consumer opens an explicit transaction that matches the Npgsql driver; otherwise separate connection. | When consumer opens an explicit transaction that matches the SqlClient driver; otherwise separate connection. |
| **Schema management** | EF migrations. | Schema runner steps at startup, or the exported deploy script. | Schema runner steps at startup, or the exported deploy script. |
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
| `DefaultExcludedProperties` | Framework-managed set | Property names skipped during change capture; consumers can add/remove entries. Default set includes `ConcurrencyStamp`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `CreatedById`, `UpdatedById`, `DeletedById`. |
| `CaptureErrorStrategy` | `Continue` | `Continue` logs an error and proceeds; `Throw` aborts the save. |

### Runtime behavior

None. This is an abstractions package and registers no services.

---

## Headless.AuditLog

DI setup package for `Headless.AuditLog`: options validation, setup builders, and the exactly-one-storage-provider registration pipeline.

### API and behavior

- `SetupAuditLog.AddHeadlessAuditLog(setup => setup.Use...)` — the single public DI entry point in the `Headless.AuditLog` namespace (add `using Headless.AuditLog;`); requires exactly one storage provider. The options-only registration is `internal` (a funnel the builder overload uses to register `AuditLogOptions` once), so a store-less audit log cannot be registered by accident.
- `HeadlessAuditLogSetupBuilder` — fluent builder passed to `AddHeadlessAuditLog(setup => ...)`; exposes `ConfigureOptions`, `ConfigureStorage`, and `RegisterExtension`.
- `HeadlessAuditLogBuilder` — returned by `AddHeadlessAuditLog(setup => ...)`; provides access to `IServiceCollection` for chaining.
- `IAuditLogStorageOptionsExtension` — setup-time hook implemented by storage provider packages.
- `AuditLogStorageOptions` — shared storage options: `Schema`, `TableName`, `JsonColumnType`, `CreatedAtColumnType`, `InitializeOnStartup`.
- `RelationalAuditLogOptions` — base of `PostgreSqlAuditLogOptions` and `SqlServerAuditLogOptions` (`ConnectionString`, `CommandTimeout`). Core also holds the one relational audit log, store, writer, and reader both raw providers run, written over the `ISqlDialect` statement kit; each provider package supplies only its options, DDL, and registration.
- `AuditLogJsonColumnType` — provider-validated JSON column type enum: `Jsonb`, `Json`, `NvarcharMax`.
- `AuditLogOptionsValidator` — validates transform-sensitive-data configuration at startup.

### Install

```bash
dotnet add package Headless.AuditLog
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
        options.Schema = "compliance"; // default: "headless"
        options.TableName = "compliance_audit"; // default: audit_log_entries on PostgreSQL, AuditLogEntries elsewhere
    });

    setup.UseEntityFramework<AppDbContext>();
});
```

### Configuration

Configure audit behavior through `setup.ConfigureOptions(...)` and storage shape through `setup.ConfigureStorage(...)`. Then select exactly one storage provider by calling the provider extension, such as `UseEntityFramework<TContext>()`, `UsePostgreSql(...)`, or `UseSqlServer(...)`, from the installed storage package. `ConfigureStorage` also accepts the `Headless:AuditLog:Storage` configuration section.

Storage options (`AuditLogStorageOptions`):

| Option | Default | Description |
|---|---|---|
| `Schema` | `"headless"` | Database schema name, shared with every Headless feature. See [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features). |
| `TableName` | `null` (database convention) | Table name. `null` uses `audit_log_entries` on PostgreSQL and `AuditLogEntries` elsewhere; a configured name is used verbatim, at most 40 characters. The primary key and index names derive from it in the database's casing (`pk_{TableName}` and `ix_{TableName}_tenant_account_time` on PostgreSQL, `PK_{TableName}` and `IX_{TableName}_TenantAccountTime` on SQL Server, and siblings), so two audit tables can share one schema. The longest derived PostgreSQL name must fit the 63-byte identifier limit, so startup validation rejects a longer name on every provider. |
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
- `HeadlessAuditLogModelBuilderExtensions.AddHeadlessAuditLog(DbContext)` — registers and configures the `AuditLogEntry` entity type, resolving `AuditLogStorageOptions` from the context's services and the naming style from `Database.ProviderName`; idempotent. The `(AuditLogStorageOptions, StorageNamingStyle)` overload takes both explicitly; pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` so the mapping matches the raw providers.
- Object names follow the database: on PostgreSQL the table is `audit_log_entries` with snake_case columns (`created_at`, `tenant_id`, …), key `pk_audit_log_entries`, and indexes `ix_audit_log_entries_tenant_time` and siblings; elsewhere the table is `AuditLogEntries` with PascalCase columns, key `PK_AuditLogEntries`, and indexes `IX_AuditLogEntries_TenantTime` and siblings.
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
        options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // for PostgreSQL
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

`UseEntityFramework<TContext>()` requires the same context to be registered with EF Core. Register a singleton `IDbContextFactory<TContext>` too (`AddDbContextFactory` / `AddPooledDbContextFactory` for a plain `DbContext`, `AddHeadlessDbContext` / `AddHeadlessDbContextPool` for a `HeadlessDbContext`): the startup gate and `IReadAuditLog<TContext>` read through it, so host startup fails without one.

#### DbContext setup

```csharp
public sealed class AppDbContext : DbContext
{
    private readonly IOptions<AuditLogStorageOptions> _auditLogStorage;

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
    options.TableName = "audit_entries"; // optional, used verbatim, at most 40 characters
    options.Schema = "compliance"; // default: "headless"
    options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // optional
    options.CreatedAtColumnType = "timestamp with time zone"; // optional explicit override
});
```

`AuditLogJsonColumnType` is an allowlist enum so the column-type string cannot inject SQL identifiers. `CreatedAtColumnType` is a free string override for the timestamp column; provider defaults are `timestamp with time zone` on PostgreSQL and `datetimeoffset(7)` on SQL Server when unset.

Sensitive data strategies:

| Strategy | Behavior |
|---|---|
| `Redact` (default) | Replaces value with `"***"`; property name still appears in `ChangedFields`. |
| `Exclude` | Omits the property entirely from `OldValues`, `NewValues`, and `ChangedFields`. |
| `Transform` | Passes value through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize). |

Entity policy is tri-state: `IsAudited()` and `ExcludeFromAudit()` override `AuditByDefault`; an unconfigured entity follows the option. Default property exclusions, explicit `ExcludeFromAudit()`, and `PropertyFilter` veto before sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides the global strategy.

SQLite key override (required when targeting SQLite):

```csharp
// After AddHeadlessAuditLog, so it replaces the default composite key.
modelBuilder.Entity<AuditLogEntry>().HasKey(e => e.Id); // single-column PK for SQLite
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

- No EF Core dependency — depends only on `Npgsql`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog`.
- `IAuditLogStore` — enrolls in the consumer's ambient Npgsql transaction when available; falls back to its own connection otherwise.
- `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` — explicit event logging; both write over the provider's own connection.
- `IReadAuditLog<TContext>` — parameterized keyset queries over `(created_at, id)`.
- The audit table and indexes are two schema steps (`AuditLog/1`, `AuditLog/2`) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts).
- Batched INSERT: up to 100 rows per command, the size SQL Server's parameter limit allows, so both providers share one writer (statement text cached per row count).
- `jsonb` by default for `OldValues`, `NewValues`, and `ChangedFields`; override via `AuditLogStorageOptions.JsonColumnType` (`Jsonb` or `Json` accepted; `NvarcharMax` rejected at options validation time).
- `PostgreSqlAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s, applied to every write and read), inherited from `RelationalAuditLogOptions`.
- `UsePostgreSql` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<PostgreSqlAuditLogOptions>)`, and `(Action<PostgreSqlAuditLogOptions, IServiceProvider>)`, plus a parameterless `UsePostgreSql()` that reads the connection registered by `AddPostgreSqlSql`.
- Creates table `audit_log_entries` (unless `TableName` is set) with snake_case columns (`created_at`, `tenant_id`, `old_values`, …), key `pk_{table}`, and the same index set as the EF provider, named `ix_{table}_tenant_time`, `ix_{table}_tenant_action_time`, `ix_{table}_tenant_entity_time`, `ix_{table}_tenant_actor_time`, `ix_{table}_tenant_account_time`, and `ix_{table}_correlation`, each ending in `(created_at, id)`.

### Design constraints

Transaction enrollment is conditional: the store attempts to resolve a `NpgsqlConnection` and `NpgsqlTransaction` from the registered `IAmbientDbTransactionAccessor`. If no ambient transaction exists — or if the connection is a different driver type — it falls back to opening its own connection. In the fallback path, audit rows commit before `SaveChanges` completes; an entity-save failure leaves orphan audit rows. A deduplicated warning is logged once per distinct saving-context type (and once per distinct driver mismatch type) to flag this.

The table and the indexes are separate steps, so each commits and is recorded on its own.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
```

### Setup and use

```csharp
// Reuse the connection every Headless feature shares:
services.AddPostgreSqlSql(builder.Configuration.GetConnectionString("Default")!);
services.AddHeadlessAuditLog(setup => setup.UsePostgreSql());

// Or give the audit log its own database and schema:
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options => options.Schema = "compliance");
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

The parameterless overloads and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

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

Bind provider options from configuration:

```csharp
setup.UsePostgreSql(builder.Configuration.GetSection("Headless:AuditLog:PostgreSql"));
```

Or configure with service resolution:

```csharp
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

- Registers the audit-log schema contribution; the one schema runner creates the table and indexes at startup.
- Registers the shared relational writer from `Headless.AuditLog`, over the PostgreSQL dialect, as singleton.
- Registers `IAuditLogStore` as scoped, and `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, and `IReadAuditLog<TContext>` as singletons, all from `Headless.AuditLog`.
- Registers `IJsonSerializer`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.

---

## Headless.AuditLog.Storage.SqlServer

Raw SQL Server storage provider for audit rows. No Entity Framework dependency — uses `Microsoft.Data.SqlClient` directly.

### API and behavior

- No EF Core dependency — depends only on `Microsoft.Data.SqlClient`, `Headless.AuditLog.Abstractions`, and `Headless.AuditLog`.
- `IAuditLogStore` — enrolls in the consumer's ambient `SqlTransaction` when available; falls back to its own connection otherwise.
- `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` — explicit event logging; both write over the provider's own connection.
- `IReadAuditLog<TContext>` — parameterized keyset queries over `(CreatedAt, Id)`, limited with `OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY`. The writer and reader bind timestamps typed like the `CreatedAt` column (`datetimeoffset` by default, `datetime2` when `CreatedAtColumnType` says so), so stored values, range bounds, and continuation positions keep full precision and the column is never converted in a comparison.
- The audit table and indexes are two schema steps (`AuditLog/1`, `AuditLog/2`) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts).
- Batched INSERT: up to 100 rows per command, within SQL Server's 2,100-parameter limit.
- `nvarchar(max)` by default for JSON columns; `NvarcharMax` is the only accepted `AuditLogJsonColumnType` (PostgreSQL-specific types are rejected at options validation time).
- `SqlServerAuditLogOptions` — `ConnectionString` (required) and `CommandTimeout` (default 30 s, applied to every write and read), inherited from `RelationalAuditLogOptions`.
- `UseSqlServer` ships the full provider overload trio: `(string connectionString)`, `(IConfiguration configuration)`, `(Action<SqlServerAuditLogOptions>)`, and `(Action<SqlServerAuditLogOptions, IServiceProvider>)`, plus a parameterless `UseSqlServer()` that reads the connection registered by `AddSqlServerSql`.
- Creates table `AuditLogEntries` (unless `TableName` is set) with PascalCase columns, key `PK_{table}`, and the same index set as the EF provider, named `IX_{table}_TenantTime`, `IX_{table}_TenantActionTime`, `IX_{table}_TenantEntityTime`, `IX_{table}_TenantActorTime`, `IX_{table}_TenantAccountTime`, and `IX_{table}_Correlation`, each ending in `(CreatedAt, Id)`.

### Design constraints

Transaction enrollment mirrors the PostgreSQL provider: the store resolves the ambient `SqlConnection`/`SqlTransaction` via `IAmbientDbTransactionAccessor`. If no ambient transaction exists or the driver is not `SqlClient`, it falls back to its own connection. In the fallback path, audit rows commit before `SaveChanges` — an entity-save failure leaves orphan rows. A deduplicated warning is logged once per distinct saving-context type and once per driver mismatch.

The table and the indexes are separate steps, so each commits and is recorded on its own. Replicas serialize on the runner's one `sp_getapplock` per database.

Batch size is capped at 100 rows, on both providers, because SQL Server allows 2,100 parameters per statement.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
```

### Setup and use

```csharp
// Reuse the connection every Headless feature shares:
services.AddSqlServerSql(builder.Configuration.GetConnectionString("Default")!);
services.AddHeadlessAuditLog(setup => setup.UseSqlServer());

// Or give the audit log its own database and schema:
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options => options.Schema = "compliance");
    setup.UseSqlServer(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

The parameterless overloads and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

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

Bind provider options from configuration:

```csharp
setup.UseSqlServer(builder.Configuration.GetSection("Headless:AuditLog:SqlServer"));
```

Or configure with service resolution:

```csharp
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

- Registers the audit-log schema contribution; the one schema runner creates the table and indexes at startup.
- Registers the shared relational writer from `Headless.AuditLog`, over the SQL Server dialect, as singleton.
- Registers `IAuditLogStore` as scoped, and `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, and `IReadAuditLog<TContext>` as singletons, all from `Headless.AuditLog`.
- Registers `IJsonSerializer`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider` as singletons if not already registered.
