---
domain: ORM
packages: Orm.EntityFramework, Orm.EntityFramework.Messaging, Orm.Couchbase, MultiTenancy
---

# ORM

> ORM domain includes three packages: `Headless.Orm.EntityFramework`, its messaging bridge `Headless.Orm.EntityFramework.Messaging`, and `Headless.Orm.Couchbase`.

## Table of Contents

- [Quick Orientation](#quick-orientation)
    - [The two-tier event model](#the-two-tier-event-model)
- [Package Validation Snapshot](#package-validation-snapshot)
- [Agent Instructions](#agent-instructions)
- [Headless.Orm.EntityFramework](#headlessormentityframework)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Pooling](#pooling)
    - [Transaction Pattern](#transaction-pattern)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
    - [Validation Checklist](#validation-checklist)
- [Headless.Orm.EntityFramework.Messaging](#headlessormentityframeworkmessaging)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
    - [Validation Checklist](#validation-checklist-1)
- [Headless.Orm.Couchbase](#headlessormcouchbase)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Transaction Pattern](#transaction-pattern-1)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
    - [Validation Checklist](#validation-checklist-2)

## Quick Orientation

Choose package by storage model:

- `Headless.Orm.EntityFramework`: relational databases via EF Core (`DbContext` model, global filters, auditing, two-tier event dispatch)
- `Headless.Orm.EntityFramework.Messaging`: bridge that supplies the real `IHeadlessOutboxDispatcher`; add it only when EF entities emit integration events that must reach the messaging outbox
- `Headless.Orm.Couchbase`: document database via Couchbase (`BucketContext`, document sets, KV + query + transaction helpers)

Use these packages when you want ORM-level persistence primitives. For raw SQL connection factories and Dapper-style access, use SQL packages instead.

### The two-tier event model

`HeadlessDbContext` collects two kinds of events during `SaveChanges` and dispatches each through its own seam:

- **Domain events** (`IDomainEvent`, emitted by `IDomainEventEmitter` entities) — in-process and in-transaction, published through `ILocalEventBus` (package `Headless.Domain.LocalEventBus`) before commit. Opt in with `.AddDomainEvents()` (in `Headless.Orm.EntityFramework`).
- **Integration events** (`IIntegrationEvent`, emitted by `IIntegrationEventEmitter` entities) — distributed, enqueued to the transactional outbox through `IHeadlessOutboxDispatcher` and delivered to the broker after commit by the messaging relay. Opt in with `.AddIntegrationEventOutbox()` (in `Headless.Orm.EntityFramework.Messaging`).

## Package Validation Snapshot

Validated against current source tree on `04-06-2026` (UTC).

| Package                                  | Source package path                                                                       | Status  |
| ---------------------------------------- | ----------------------------------------------------------------------------------------- | ------- |
| `Headless.Orm.EntityFramework`           | `src/Headless.Orm.EntityFramework/Headless.Orm.EntityFramework.csproj`                     | Present |
| `Headless.Orm.EntityFramework.Messaging` | `src/Headless.Orm.EntityFramework.Messaging/Headless.Orm.EntityFramework.Messaging.csproj` | Present |
| `Headless.Orm.Couchbase`                 | `src/Headless.Orm.Couchbase/Headless.Orm.Couchbase.csproj`                                 | Present |

Validation notes:

- Domain frontmatter includes only existing ORM packages.
- API guidance below maps to currently present consumer-facing symbols in source (for example: `HeadlessDbContext`, `HeadlessDbContextServices`, `HeadlessDbContextOptions`, `IHeadlessDbContextBuilder`, `IHeadlessSaveEntryProcessor`, `IHeadlessSaveChangesPipeline`, `IHeadlessOutboxDispatcher`, `ILocalEventBus`, `AddHeadlessDbContext<TDbContext>`, `AddHeadlessDbContextServices`, `AddDomainEvents`, `AddIntegrationEventOutbox`, `ExecuteTransactionAsync`, `CouchbaseBucketContext`, `DocumentSetExtensions`, `IBucketContextProvider`).

## Agent Instructions

- Treat this domain as exactly three packages (`Headless.Orm.EntityFramework`, `Headless.Orm.EntityFramework.Messaging`, `Headless.Orm.Couchbase`). Do not reference non-existing ORM packages.
- For relational stores, inherit from `HeadlessDbContext` and register with `AddHeadlessDbContext<TDbContext>(...)`.
- `HeadlessDbContext` requires two ctor parameters: `(HeadlessDbContextServices services, DbContextOptions options)`, and subclasses must override `public abstract string? DefaultSchema { get; }` (empty string means "use the provider default").
- Enable tenant-owned write protection through `builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()))` when the host uses root tenancy.
- Always call `base.OnModelCreating(modelBuilder)` in `HeadlessDbContext` subclasses.
- Use `ExecuteTransactionAsync(...)` (from `DbContextTransactionExtensions`) for multi-step EF operations that must be atomic under retry execution strategies.
- Use `ExecuteCoordinatedTransactionAsync(...)` (from `HeadlessCoordinatedTransactionExtensions`) instead when the operation also publishes integration events or enqueues durable jobs that must drain atomically on commit — it welds `EnlistCommitCoordination` into the transaction so the enlist cannot be forgotten. On `HeadlessDbContext` it self-sources the request scope and the operation lambda receives the concrete `HeadlessDbContext` (no cast); plain `DbContext`/`SqlConnection`/`NpgsqlConnection` overloads (in `Headless.CommitCoordination.*`) require an explicit `IServiceProvider`.
- `AddHeadlessDbContextServices(...)` returns `IHeadlessDbContextBuilder`; chain `.AddDomainEvents()` and `.AddIntegrationEventOutbox()` off it to opt in to each event tier. `.AddDomainEvents()` lives in `Headless.Orm.EntityFramework`; `.AddIntegrationEventOutbox()` lives in `Headless.Orm.EntityFramework.Messaging` and is parameterless (broker/storage/retry are configured on `AddHeadlessMessaging`, which must include an outbox storage provider).
- Customize the save pipeline through `AddSaveEntryProcessor<TProcessor>(ServiceLifetime)` on `HeadlessDbContextOptions`; replace `IHeadlessSaveChangesPipeline` only when you need full orchestration control.
- Apply module-specific EF mappings explicitly through `ModelBuilder` extensions inside the consumer's `OnModelCreating`: `modelBuilder.AddHeadlessAuditLog(auditLogStorageOptions)`, `modelBuilder.AddHeadlessFeatures(featuresStorageOptions)`, `modelBuilder.AddHeadlessPermissions(permissionsStorageOptions)`, `modelBuilder.AddHeadlessSettings(settingsStorageOptions)`. The legacy `Add*Configuration(this)` shapes were replaced by these options-driven extensions.
- Entities that emit domain events require `.AddDomainEvents()` (registers `ILocalEventBus`); entities that emit integration events require `.AddIntegrationEventOutbox()` (registers `IHeadlessOutboxDispatcher` from the bridge package). There is no startup validation — a runtime guard throws `InvalidOperationException` at save, only when an entity actually emits an event for a tier that is not registered (zero false positives). The guard message names the exact registration to add.
- Integration events are enqueued to the transactional outbox before the EF transaction commits and delivered to the broker after commit by the messaging relay. Domain events are published in-process via `ILocalEventBus` before commit, inside the save transaction (so handlers can enlist further changes into the same `SaveChanges`).
- Do not mix framework concurrency stamping with ASP.NET Identity `ConcurrencyStamp` ownership on identity entities.
- For Couchbase, use `CouchbaseBucketContext` + `IBucketContextProvider` and keep cluster/bucket names explicit.
- `DocumentSetExtensions` are constrained to `IEntity` models and provide high-level KV operations.
- **Delivery semantics differ by tier.** Domain-event handlers are **at-most-once**: the save pipeline guards domain-event publication so a retrying EF execution strategy that replays the save transaction does **not** re-invoke handlers. Because publication happens before commit, a handler can still run on an attempt that later fails to commit — so domain-event side effects must tolerate a rolled-back save (keep them idempotent / replay-safe), but they will never fire twice for one logical save. Integration events are **exactly-once** end-to-end via the transactional outbox (rows commit atomically with the data, then the relay delivers post-commit). Rule of thumb: use domain events for in-process reactions in the same unit of work; use integration events when delivery must be coupled to a committed transaction.
- For capturing integration events outside the application process, Change Data Capture (for example, Debezium) is an advanced alternative deployment that bypasses `IHeadlessOutboxDispatcher`; it is a host-infrastructure decision, not a package option.

---

# Headless.Orm.EntityFramework

Entity Framework Core integration with framework conventions and save pipeline orchestration.

## Problem Solved

Provides a framework-aware base `DbContext` with conventions for auditing, soft delete, tenant filters, two-tier event dispatch (in-process domain events plus transactional integration-event outbox), and transaction-aware save behavior.

## Key Features

- `HeadlessDbContext` base context (requires the hidden `HeadlessDbContextServices` constructor pass-through parameter and `DefaultSchema` override)
- DI registration via `AddHeadlessDbContext<TDbContext>(...)`
- Application-generated Guid keys: every `IEntity<Guid>` mapped in a `HeadlessDbContext` is configured `ValueGenerated.Never` with an EF Core value generator that produces the key client-side as the entity transitions to `Added` (via `Add`, a direct state change, or attach-then-promote) — never database-generated. Guid keys come from provider-keyed `IGuidGenerator` strategies (`SqlServer` comb for SQL Server, `Version7` for other providers). The id is therefore known before `SaveChanges` (usable for foreign keys, outbox, and domain events in the same unit of work) and is provider-portable.
- Automatic audit fields for `ICreateAudit` / `IUpdateAudit` / `IDeleteAudit` / `ISuspendAudit` entities (`DateCreated`, `DateUpdated`, `DateDeleted`, `DateSuspended` + `CreatedById` / `UpdatedById` / `DeletedById` / `SuspendedById` when the entity carries `UserId` or `AccountId` audits)
- Three named global filters: `MultiTenancyFilter` (`IMultiTenant`), `NotDeletedFilter` (`IDeleteAudit`), `NotSuspendedFilter` (`ISuspendAudit`); per-query bypass via `IgnoreMultiTenancyFilter()` / `IgnoreNotDeletedFilter()` / `IgnoreNotSuspendedFilter()`
- Composable save pipeline driven by `HeadlessDbContextOptions` and a fixed default chain of `IHeadlessSaveEntryProcessor` instances
- Optional tenant write guard for `IMultiTenant` save protection
- Two-tier event dispatch collected inside `SaveChanges`: domain events published via `ILocalEventBus` before commit (`.AddDomainEvents()`), and integration events enqueued to the transactional outbox via `IHeadlessOutboxDispatcher` (`.AddIntegrationEventOutbox()`, from `Headless.Orm.EntityFramework.Messaging`)
- `IHeadlessDbContextBuilder` returned by `AddHeadlessDbContextServices(...)` so event tiers chain off it
- Runtime guard that fails the save with a remediation message when an entity emits events but the matching dispatch tier is not registered
- Resilient transaction helpers: `ExecuteTransactionAsync(...)`
- Extensibility hooks through model processing services

## Installation

```bash
dotnet add package Headless.Orm.EntityFramework
```

## Quick Start

```csharp
public sealed class AppDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<AppDbContext> options
) : HeadlessDbContext(services, options)
{
    public DbSet<Product> Products => Set<Product>();

    public override string? DefaultSchema => "app"; // "" means use the provider default

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

builder.Services.AddHeadlessDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// Opt in to the event tiers your entities use. AddHeadlessDbContextServices(...)
// returns IHeadlessDbContextBuilder so the tiers chain off it.
builder.Services.AddHeadlessDbContextServices()
    .AddDomainEvents()             // ILocalEventBus for in-process domain events
    .AddIntegrationEventOutbox();  // outbox dispatch for integration events (bridge package)
```

Emitting domain events without `.AddDomainEvents()`, or integration events without `.AddIntegrationEventOutbox()`, throws an `InvalidOperationException` at save naming the missing registration. The guard fires only when events are actually emitted.

## Pooling

`HeadlessDbContext` is intentionally **not poolable**. Do not register subclasses with `AddDbContextPool` or `AddPooledDbContextFactory`.

Why:

- The context holds a private `HeadlessDbContextRuntime` field that captures the request-scoped integration-event outbox dispatcher (`IHeadlessOutboxDispatcher`) and audit persistence (`IHeadlessAuditPersistence`). Pooled instances would reuse a prior request's unit of work — EF only resets state it knows about, and the save pipeline is opaque to EF.
- The constructor signature is `(HeadlessDbContextServices services, DbContextOptions options)`. `AddDbContextPool` only supports a single-`DbContextOptions` constructor.

`ICurrentTenant` (AsyncLocal-backed) and `IClock` (singleton) are **not** the blocker — they resolve fresh per request even from a long-lived instance.

For read-heavy hot paths that don't need the Headless write machinery, use a plain `DbContext` with `AddDbContextPool` alongside the write-side `HeadlessDbContext`. Writes don't benefit from pooling, so the split (heavy scoped write context + plain poolable storage contexts) is deliberate.

## Transaction Pattern

```csharp
await dbContext.ExecuteTransactionAsync(async (ctx, ct) =>
{
    var order = await ctx.Set<Order>().FindAsync([orderId], ct);
    if (order is null)
    {
        return;
    }

    order.Cancel();
    await ctx.SaveChangesAsync(ct);
}, cancellationToken: ct);
```

Use this when multiple operations must commit or roll back as one unit.

## Configuration

- Registration API: `AddHeadlessDbContext<TDbContext>(...)`
- EF options can be provided with `Action<DbContextOptionsBuilder>` or `Action<IServiceProvider, DbContextOptionsBuilder>`
- Framework extension is added through `AddHeadlessExtension()` internally during registration

### Module Model Mapping

Each feature-storage EF package exposes a `ModelBuilder` extension that maps its entities using the corresponding storage options. Call them inside `OnModelCreating` after `base.OnModelCreating(modelBuilder)`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddHeadlessAuditLog(_auditLogStorage.Value);
    modelBuilder.AddHeadlessFeatures(_featuresStorage.Value);
    modelBuilder.AddHeadlessPermissions(_permissionsStorage.Value);
    modelBuilder.AddHeadlessSettings(_settingsStorage.Value);
}
```

These replace the older `Add*Configuration(this)` shapes — the options-driven extensions read `Schema` and `*TableName` from the validated `*StorageOptions` (validated for SQL-identifier safety) and apply them consistently across providers.

### Tenant Write Guard

The tenant write guard is disabled by default. Opt in when tenant-owned writes must be scoped to the current tenant:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy
    .EntityFramework(ef => ef.GuardTenantWrites()));
```

The guard runs in the `HeadlessDbContext` save pipeline before audit capture, domain-message publishing, and persistence. It applies only to entities that implement `IMultiTenant`:

- Missing ambient tenant on tenant-owned writes throws `MissingTenantContextException`.
- Cross-tenant add, update, soft-delete, or physical delete throws `CrossTenantWriteException`.
- Added tenant-owned entities with no `TenantId` are stamped with `ICurrentTenant.Id`.
- Non-tenant entities are unaffected.

For intentional host/admin writes, resolve `ITenantWriteGuardBypass` and wrap only the intended operation in `BeginBypass()`. Query filter bypasses such as `IgnoreMultiTenancyFilter()` do not bypass write protection.

For package-level wiring without the root tenancy surface, `builder.Services.AddHeadlessTenantWriteGuard()` remains available.

#### Defense Layers and Known Gaps

`IMultiTenant` writes are protected by two complementary layers, plus paths that remain out of scope:

1. **Global query filter** — always on for `IMultiTenant` entities, wired by `HeadlessDbContextRuntime._ConfigureQueryFilters` and registered under the constant `HeadlessQueryFilters.MultiTenancyFilter` (string value `"MultiTenantFilter"`). Scopes reads, `IQueryable<T>.ExecuteUpdate(...)`, and `IQueryable<T>.ExecuteDelete(...)` to `ICurrentTenant.Id`. Opt-out is `IgnoreMultiTenancyFilter()` (audit-logged via `HeadlessQueryFilters._LogFilterBypassed`).
2. **`SaveChanges` write guard** — opt-in via `.EntityFramework(ef => ef.GuardTenantWrites())`. Operates on EF's `ChangeTracker` and rejects unsafe `Add` / `Update` / `Remove` / tracked-property writes with `CrossTenantWriteException` before persistence.

Known gaps:

- **Attach-then-modify.** Attacker-controlled `Attach` populates `OriginalValue` from caller state, so the in-memory guard's `OriginalValue == currentTenantId` check can pass for a row owned by another tenant. A SQL-level concurrency-style `WHERE TenantId = @currentTenantId` predicate on the SaveChanges-generated UPDATE/DELETE is the planned follow-up — tracked in the security follow-up issue on the project tracker.
- **Raw SQL** (`DbContext.Database.ExecuteSql(...)`, `ExecuteSqlInterpolated(...)`, `ExecuteSqlRaw(...)`, stored procedures, triggers) is out of scope for both layers. Consumers must include their own `WHERE TenantId = @currentTenantId` predicate or wrap the call in `ITenantWriteGuardBypass.BeginBypass()` under an audited host context.

## Dependencies

- `Headless.Domain`
- `Headless.Core`
- `Headless.Hosting`
- `Headless.MultiTenancy`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `HeadlessDbContextServices`, `IHeadlessSaveChangesPipeline`, and the default save-entry processor chain (`HeadlessEntitySaveEntryProcessor`, `HeadlessAuditSaveEntryProcessor`, `HeadlessLocalEventSaveEntryProcessor`, `HeadlessMessageCollectorSaveEntryProcessor`)
- `.AddDomainEvents()` registers `ILocalEventBus` (via `services.AddHeadlessLocalEventBus()`); `.AddIntegrationEventOutbox()` (from `Headless.Orm.EntityFramework.Messaging`) registers `IHeadlessOutboxDispatcher`. Neither is registered by default — emitting events without the matching tier throws at save
- Registers `TenantWriteGuardOptions` and `ITenantWriteGuardBypass` for opt-in tenant write protection
- Registers framework defaults via `TryAddSingleton`: `IClock`, keyed `IGuidGenerator` strategies (`Version7` and `SqlServer`) plus an unkeyed `Version7` default, `ICurrentTenant`, `ICurrentTenantAccessor`, `ICurrentUser` (`NullCurrentUser`), `ICorrelationIdProvider`, `TimeProvider.System`
- Replaces compiled query cache key generator so tenant-scoped queries share plans correctly
- Forwards `DbContext` to registered `TDbContext` via scoped registration

## Validation Checklist

- `src/Headless.Orm.EntityFramework/Headless.Orm.EntityFramework.csproj` exists
- Public setup API found: `AddHeadlessDbContext<TDbContext>(...)`, `AddHeadlessDbContextServices(...)` (returns `IHeadlessDbContextBuilder`), `AddDomainEvents()`
- Base context found: `HeadlessDbContext` with abstract `DefaultSchema`
- Extension points: `IHeadlessSaveEntryProcessor`, `IHeadlessSaveChangesPipeline`, `IHeadlessOutboxDispatcher`, `ILocalEventBus`, `HeadlessDbContextOptions.AddSaveEntryProcessor<TProcessor>(ServiceLifetime)`
- Transaction extensions found: `ExecuteTransactionAsync(...)`, `ExecuteCoordinatedTransactionAsync(...)` (from `HeadlessCoordinatedTransactionExtensions`)
- Filter API found: `HeadlessQueryFilters.MultiTenancyFilter`, `NotDeletedFilter`, `NotSuspendedFilter` with matching `Ignore*Filter()` extensions

---

# Headless.Orm.EntityFramework.Messaging

Bridge package that supplies the real `IHeadlessOutboxDispatcher` for EF integration-event outbox dispatch.

## Problem Solved

`Headless.Orm.EntityFramework` defines the `IHeadlessOutboxDispatcher` seam but ships no implementation, so it carries no messaging dependency. This package supplies the implementation: integration events emitted by entities during an EF `SaveChanges` are written to the messaging outbox atomically with the business data and delivered to the broker after commit — without the core ORM package depending on messaging.

## Key Features

- Transactional outbox enlistment in the EF save transaction, so outbox rows commit atomically with the business data
- Routes each concrete `IIntegrationEvent` to `IOutboxBus.PublishAsync<TConcrete>` through a cached compiled invoker (one compiled delegate per runtime event type)
- Sync and async save paths (`Dispatch` and `DispatchAsync`)
- `.AddIntegrationEventOutbox()` builder extension on `IHeadlessDbContextBuilder`

## Design Notes

- **Commit-coordinated enlistment.** The save pipeline opens its transaction and synchronously enlists it in commit coordination (`DatabaseFacade.EnlistCommitCoordination`), so the ambient commit coordinator carries the live transaction. The dispatcher just publishes each integration event; the outbox writer sees the ambient coordinator and writes the rows **inside** the transaction (buffered, not sent to the broker in-band). The registered EF `IDbTransactionInterceptor` drains the buffered dispatch when the transaction commits and discards it on rollback, so outbox rows commit atomically with the business data. The enlist is synchronous on purpose: an `AsyncLocal` push performed inside an `async` helper does not flow back to the caller, so the pipeline pushes the ambient scope in its own frame.
- **Post-commit delivery.** The interceptor triggers the buffered dispatch on commit; the background relay also sweeps committed rows independently for crash recovery (on PostgreSQL the relay is the primary latency-bounded path). Pick the storage provider on `AddHeadlessMessaging` with that trade-off in mind.
- **Dependency isolation.** Keeping the implementation here leaves `Headless.Orm.EntityFramework` free of any messaging dependency — this bridge is the only messaging-aware seam between the two domains.
- **CDC alternative.** Change Data Capture (for example, Debezium reading the database transaction log) is an advanced alternative deployment for capturing integration events outside the application process; it bypasses this dispatcher entirely and is a host-infrastructure decision.

## Installation

```bash
dotnet add package Headless.Orm.EntityFramework.Messaging
```

## Quick Start

```csharp
builder.Services.AddHeadlessDbContextServices()
    .AddDomainEvents()             // ILocalEventBus for in-process domain events
    .AddIntegrationEventOutbox();  // outbox dispatch for integration events

// A messaging setup with an outbox storage provider is required.
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UsePostgreSql(connectionString);
});
```

`.AddIntegrationEventOutbox()` is parameterless — the dispatcher has no options. Broker, storage, and retry behavior are configured on `AddHeadlessMessaging`.

## Configuration

`None.` (configured via `AddHeadlessMessaging`.)

## Dependencies

- `Headless.Orm.EntityFramework`
- `Headless.Domain`
- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Abstractions`

## Side Effects

- Registers `IHeadlessOutboxDispatcher` (scoped, `TryAdd`)
- Registers an integration-event publish invoker cache (singleton, `TryAdd`)

## Validation Checklist

- `src/Headless.Orm.EntityFramework.Messaging/Headless.Orm.EntityFramework.Messaging.csproj` exists
- Builder extension found: `AddIntegrationEventOutbox()` on `IHeadlessDbContextBuilder`
- Dispatcher implementation found: `OutboxIntegrationEventDispatcher : IHeadlessOutboxDispatcher`
- Invoker cache found: `IntegrationEventPublishInvokerCache` routing to `IOutboxBus.PublishAsync<T>`

---

# Headless.Orm.Couchbase

Couchbase integration for bucket-context based document access, transactions, and collection management.

## Problem Solved

Provides a typed context model over Couchbase buckets with helper APIs for querying, KV operations, and transaction execution.

## Key Features

- `CouchbaseBucketContext` base context
- `IBucketContextProvider` for resolving typed contexts per cluster + bucket
- `ICouchbaseClustersProvider` for cluster and transaction object lifecycle
- `DocumentSetExtensions` for KV and document operations
- `ICouchbaseManager` utilities for scope/collection/index lifecycle operations

## Installation

```bash
dotnet add package Headless.Orm.Couchbase
```

## Quick Start

```csharp
public sealed class AppBucketContext(IBucket bucket, Transactions transactions, ILogger<CouchbaseBucketContext> logger)
    : CouchbaseBucketContext(bucket, transactions, logger)
{
    public DocumentSet<Product> Products => GetDocumentSet<Product>("products");
}

var context = await bucketContextProvider.GetAsync<AppBucketContext>(
    clusterKey: "default",
    bucketName: "app",
    defaultScopeName: "_default"
);
```

## Transaction Pattern

```csharp
await context.ExecuteTransactionAsync(async attempt =>
{
    // return true to commit, false to rollback
    // perform transactional operations through attempt
    return true;
});
```

## Configuration

- Provide cluster options through `ICouchbaseClusterOptionsProvider`
- Provide transaction config through `ICouchbaseTransactionConfigProvider`
- Resolve bucket contexts through `IBucketContextProvider`
- Use `ICouchbaseManager` when bootstrapping scopes, collections, and indexes

## Dependencies

- `Headless.Domain`
- `Headless.Hosting`
- `Couchbase.Extensions.DependencyInjection`
- `Couchbase.Transactions`
- `Linq2Couchbase`

## Side Effects

- Caches cluster connections by `clusterKey` in provider lifecycle
- Initializes bucket contexts via reflection-based context initializer
- Emits transaction completion/failure logs from `CouchbaseBucketContext`

## Validation Checklist

- `src/Headless.Orm.Couchbase/Headless.Orm.Couchbase.csproj` exists
- Base context found: `CouchbaseBucketContext`
- Context provider found: `IBucketContextProvider` / `BucketContextProvider`
- Cluster provider found: `ICouchbaseClustersProvider`
- Document helper APIs found: `DocumentSetExtensions`
- Manager APIs found: `ICouchbaseManager`
