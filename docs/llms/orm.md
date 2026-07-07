---
domain: ORM
packages: Orm.EntityFramework, Orm.EntityFramework.Messaging, Orm.Couchbase, MultiTenancy
---

# ORM

## Table of Contents

- [Quick Orientation](#quick-orientation)
    - [The two-tier event model](#the-two-tier-event-model)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [HeadlessDbContext conventions](#headlessdbcontext-conventions)
    - [Global query filters](#global-query-filters)
    - [Save pipeline and auditing](#save-pipeline-and-auditing)
    - [DDD aggregate support](#ddd-aggregate-support)
    - [Outbox-within-save-transaction bridge](#outbox-within-save-transaction-bridge)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Orm.EntityFramework](#headlessormentityframework)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Orm.EntityFramework.Messaging](#headlessormentityframeworkmessaging)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Orm.Couchbase](#headlessormcouchbase)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> ORM domain: `Headless.Orm.EntityFramework` for relational stores via EF Core (with global filters, auditing, DDD event dispatch, and tenant write protection), `Headless.Orm.EntityFramework.Messaging` as the outbox bridge add-on, and `Headless.Orm.Couchbase` for document storage via Couchbase.

## Quick Orientation

Choose by storage model:

- `Headless.Orm.EntityFramework` — relational databases via EF Core. Provides `HeadlessDbContext` with conventions for auditing, soft-delete, multi-tenancy filter, DDD event dispatch (domain + integration), and transaction-aware save behavior. The default choice for any relational store (PostgreSQL, SQL Server, SQLite).
- `Headless.Orm.EntityFramework.Messaging` — add-on bridge. Supplies the real `IHeadlessOutboxDispatcher` so integration events emitted by EF entities are written to the messaging outbox atomically with the business data. Add it when entities emit `IIntegrationEvent`. It is not an alternative provider — it is always used alongside `Headless.Orm.EntityFramework`.
- `Headless.Orm.Couchbase` — document database via Couchbase. Provides `CouchbaseBucketContext`, `IBucketContextProvider`, `ICouchbaseClustersProvider`, `DocumentSetExtensions`, and `ICouchbaseManager`. Adds no relational conventions — no EF, no global filters, no auditing pipeline.

Use these packages for ORM-level persistence primitives. For raw SQL connection factories and Dapper-style access, use the SQL packages instead.

### The two-tier event model

`HeadlessDbContext` collects two kinds of events during `SaveChanges` and dispatches each through its own seam:

- **Domain events** (`IDomainEvent`, emitted by `IDomainEventEmitter` entities) — in-process and in-transaction, published through `ILocalEventBus` before commit. Opt in with `.AddDomainEvents()` (in `Headless.Orm.EntityFramework`).
- **Integration events** (`IIntegrationEvent`, emitted by `IIntegrationEventEmitter` entities) — distributed, enqueued to the transactional outbox through `IHeadlessOutboxDispatcher` and delivered to the broker after commit by the messaging relay. Opt in with `.AddIntegrationEventOutbox()` (in `Headless.Orm.EntityFramework.Messaging`).

## Agent Instructions

- Treat this domain as exactly three packages (`Headless.Orm.EntityFramework`, `Headless.Orm.EntityFramework.Messaging`, `Headless.Orm.Couchbase`). Do not reference non-existing ORM packages.
- **Use `AddHeadlessDbContext<TDbContext>(...)` not raw `AddDbContext`.** The raw registration misses the save pipeline, EF interceptors wiring (commit coordination), the `IDbContextFactory<TDbContext>` singleton, and the compiled-query cache key replacement. `AddHeadlessDbContext` registers all of these.
- `HeadlessDbContext` requires two constructor parameters: `(HeadlessDbContextServices services, DbContextOptions options)`. Subclasses must override `public abstract string? DefaultSchema { get; }` — an empty string or `null` means use the provider default, a non-empty string sets `modelBuilder.HasDefaultSchema`.
- Always call `base.OnModelCreating(modelBuilder)` in `HeadlessDbContext` subclasses before applying your own entity configurations. Skipping it omits global filter wiring, convention configuration, and model processing from `HeadlessDbContextRuntime`.
- **Never pool `HeadlessDbContext`.** Do not register subclasses with `AddDbContextPool` or `AddPooledDbContextFactory`. The context holds a private `HeadlessDbContextRuntime` that captures the request-scoped outbox dispatcher and audit persistence. Pooling reuses a prior request's unit of work — a captive-dependency bug, not a perf trade-off.
- Enable tenant write protection with `builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()))` when the host uses root tenancy. Without it, the multi-tenancy query filter still scopes reads and bulk operations, but `SaveChanges` does not validate which tenant owns the entity.
- **`IgnoreMultiTenancyFilter()` is read-side only.** It does not relax write protection under `GuardTenantWrites()`. When the same code path writes, also wrap the save in `ITenantWriteGuardBypass.BeginBypass()` — the two bypasses are independent.
- Use `ExecuteTransactionAsync(...)` (from `DbContextTransactionExtensions`) for multi-step EF operations that must be atomic under retry execution strategies (e.g. SQL Server `EnableRetryOnFailure`).
- Use `ExecuteCoordinatedTransactionAsync(...)` (from `HeadlessCoordinatedTransactionExtensions`) instead when the operation also publishes integration events or enqueues durable jobs that must drain atomically on commit. On any `IHeadlessDbContext` (`HeadlessDbContext` or `HeadlessIdentityDbContext`) it self-sources the request scope and the operation lambda receives the concrete context type with no cast. Plain `DbContext`/`SqlConnection`/`NpgsqlConnection` overloads (in `Headless.CommitCoordination.*`) require the scope passed explicitly.
- `AddHeadlessDbContextServices(...)` returns `IHeadlessDbContextBuilder`; chain `.AddDomainEvents()` and `.AddIntegrationEventOutbox()` off it to opt in to each event tier. `.AddDomainEvents()` lives in `Headless.Orm.EntityFramework`; `.AddIntegrationEventOutbox()` lives in `Headless.Orm.EntityFramework.Messaging` and is parameterless.
- **There is no startup validation for event tiers.** A runtime guard throws `InvalidOperationException` at save time, only when an entity actually emits an event for a tier that is not registered. The guard message names the exact registration to add.
- Customize the save pipeline through `options.AddSaveEntryProcessor<TProcessor>(ServiceLifetime)` on `HeadlessDbContextOptions`; use `options.RemoveSaveEntryProcessor<TProcessor>()` to opt out of a built-in processor. Replace `IHeadlessSaveChangesPipeline` only when you need full orchestration control.
- Apply module-specific EF mappings explicitly through `ModelBuilder` extensions inside `OnModelCreating`: `modelBuilder.AddHeadlessAuditLog(...)`, `modelBuilder.AddHeadlessFeatures(...)`, `modelBuilder.AddHeadlessPermissions(...)`, `modelBuilder.AddHeadlessSettings(...)`. These read schema and table names from validated `*StorageOptions`.
- **Delivery semantics differ by tier.** Domain-event handlers are **at-most-once**: the save pipeline guard prevents re-invocation on EF execution-strategy replay, but handlers can run on a failed attempt (before rollback), so keep them idempotent. Integration events are **exactly-once** end-to-end via the transactional outbox. Use domain events for in-process reactions in the same unit of work; use integration events when delivery must be coupled to a committed transaction.
- **Raw SQL bypasses both protection layers** (`ExecuteSql`, stored procedures, triggers). For tenant-owned tables, include a `WHERE TenantId = @currentTenantId` predicate or wrap the call in `ITenantWriteGuardBypass.BeginBypass()`.
- **Attach-then-modify is a known gap in write protection.** `Attach` populates `OriginalValue` from caller state; the in-memory guard's `OriginalValue == currentTenantId` check can pass for a row owned by another tenant. A SQL-level predicate on the generated UPDATE/DELETE is the planned follow-up.
- Do not mix framework concurrency stamping with ASP.NET Identity `ConcurrencyStamp` ownership on identity entities.
- For Couchbase, use `CouchbaseBucketContext` + `IBucketContextProvider` and keep cluster/bucket names explicit. `DocumentSetExtensions` are constrained to `IEntity` models.

## Core Concepts

### HeadlessDbContext conventions

`HeadlessDbContext` is a framework-aware `DbContext` base that layers conventions on top of EF Core:

- **Guid key generation is client-side.** Every `IEntity<Guid>` mapped in a `HeadlessDbContext` is configured `ValueGenerated.Never` with an EF Core value generator that produces the key client-side when the entity transitions to `Added` — never database-generated. The key is therefore known before `SaveChanges`, making it usable for foreign keys, outbox rows, and domain events in the same unit of work. The `IGuidGenerator` strategy is provider-keyed: `SqlServer` comb for SQL Server, `Version7` for other providers.
- **Numeric keys are not generated by Headless.** Entities using `IEntity<long>` or other numeric key types own their own EF/database/provider generation strategy.
- **`DefaultSchema` controls the schema for all entities.** Return `null` or empty string to use the provider default. Return a non-empty string to call `modelBuilder.HasDefaultSchema(DefaultSchema)`.
- **Convention configuration is applied automatically** via `ConfigureConventions`, including value converters for `Money`, `Month`, `AccountId`, `UserId`, `Locale`, `ExtraProperties`, and `DateTime` normalization.
- **`IDbContextFactory<TDbContext>` is registered automatically** as a singleton by `AddHeadlessDbContext<TDbContext>`. It creates a fresh service scope per call and transfers scope ownership to the returned context, so background services and initializers can resolve detached contexts safely without a separate `AddDbContextFactory` call.

### Global query filters

Three named global filters are wired automatically when entities implement the corresponding interfaces:

| Interface | Filter name constant | Bypass extension |
|---|---|---|
| `IMultiTenant` | `HeadlessQueryFilters.MultiTenancyFilter` | `IgnoreMultiTenancyFilter()` |
| `IDeleteAudit` | `HeadlessQueryFilters.NotDeletedFilter` | `IgnoreNotDeletedFilter()` |
| `ISuspendAudit` | `HeadlessQueryFilters.NotSuspendedFilter` | `IgnoreNotSuspendedFilter()` |

Filter names are string constants (e.g. `"MultiTenantFilter"`) used by EF Core's named-filter API. All three apply automatically — no opt-in is required. `IQueryable<T>.ExecuteUpdate(...)` and `IQueryable<T>.ExecuteDelete(...)` consume the same `IQueryable<T>`, so the multi-tenancy filter scopes bulk operations to the current tenant by default.

Bypasses emit a `[SECURITY AUDIT]` trace through `Debug.WriteLine` with the caller member + file for auditability. They are scoped to a single `IQueryable<T>` chain.

### Save pipeline and auditing

`HeadlessDbContext.SaveChanges` / `SaveChangesAsync` delegate to `IHeadlessSaveChangesPipeline`, which orchestrates a fixed default chain of `IHeadlessSaveEntryProcessor` instances before the underlying EF save:

1. `HeadlessEntitySaveEntryProcessor` — stamps tenant IDs and concurrency stamps. Guid keys are produced earlier (at `Add` time) by the EF Core value generators, not here.
2. `HeadlessAuditSaveEntryProcessor` — stamps create/update/delete/suspend audit fields for `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit` entities.
3. `HeadlessLocalEventSaveEntryProcessor` — emits `EntityCreated`, `EntityUpdated`, `EntityDeleted`, `EntityChanged` lifecycle domain events on `IDomainEventEmitter` entities.
4. `HeadlessMessageCollectorSaveEntryProcessor` — collects pending domain + integration events onto the save context.

Custom processors registered via `options.AddSaveEntryProcessor<TProcessor>(ServiceLifetime)` are inserted **before** the terminal lifecycle and message-collector processors, so app-specific mutations and queued messages are visible to the framework collectors. The same type can be re-registered — the prior entry is replaced, not duplicated. Use `options.RemoveSaveEntryProcessor<TProcessor>()` to opt out of a built-in processor entirely.

The full save-transaction order within a `HeadlessDbContext` pipeline-owned transaction is:

1. Domain events published via `ILocalEventBus` (per event, before business save)
2. Business `SaveChanges` to the database
3. Audit persistence
4. Integration events enqueued to the outbox via `IHeadlessOutboxDispatcher` (before commit)
5. Transaction commit

### DDD aggregate support

`HeadlessDbContext` supports domain-driven design aggregate patterns:

- Entities implementing `IDomainEventEmitter` can emit `IDomainEvent` objects that are collected and published via `ILocalEventBus` inside the save transaction. Handlers that enlist further changes into the same `SaveChanges` are supported because publication precedes the business save.
- Entities implementing `IIntegrationEventEmitter` can emit `IIntegrationEvent` objects that are enqueued to the transactional outbox via `IHeadlessOutboxDispatcher`. Each event is routed through `IOutboxBus.PublishAsync<TConcrete>` using a cached compiled delegate (one per runtime event type) for allocation efficiency.
- Both tiers are opt-in: neither `ILocalEventBus` nor `IHeadlessOutboxDispatcher` is registered by default. The runtime guard fires only when events are actually emitted against a missing tier — zero false positives at startup.

### Outbox-within-save-transaction bridge

`Headless.Orm.EntityFramework.Messaging` is the seam that keeps `Headless.Orm.EntityFramework` free of any messaging dependency while still guaranteeing atomic outbox writes:

1. The save pipeline opens its transaction and synchronously enlists it in commit coordination (`DatabaseFacade.EnlistCommitCoordination`). The synchronous enlist is by design — an `AsyncLocal` push inside an `async` helper does not flow back to the caller.
2. The `OutboxIntegrationEventDispatcher` publishes each integration event to `IOutboxBus.PublishAsync<T>`. The outbox writer sees the ambient coordinator and buffers the rows inside the transaction — not sent to the broker in-band.
3. The registered `IDbTransactionInterceptor` drains the buffered dispatch when the transaction commits and discards it on rollback.
4. The background messaging relay sweeps committed rows independently for crash recovery. On PostgreSQL the relay is the primary latency-bounded path; pick the outbox storage provider on `AddHeadlessMessaging` with that trade-off in mind.

Change Data Capture (e.g. Debezium) is an advanced alternative that bypasses this bridge entirely — it reads the database transaction log and is a host-infrastructure decision, not a package option.

## Choosing a Provider

| | `Headless.Orm.EntityFramework` | `Headless.Orm.Couchbase` |
|---|---|---|
| **Storage model** | Relational (PostgreSQL, SQL Server, SQLite, …) | Document (Couchbase bucket + collections) |
| **Use when** | Strong consistency, rich queries, schema-enforced invariants, auditing, multi-tenancy, DDD aggregates, outbox integration events | Flexible schema, horizontal scaling, KV-first access patterns, Couchbase N1QL queries |
| **Avoid when** | Schema-less or flexible-schema documents; extreme horizontal write scale | ACID transactions across multiple entities/tables; when strong relational queries or auditing conventions are needed |
| **Global filters** | `IMultiTenant`, `IDeleteAudit`, `ISuspendAudit` — automatic | None; consumers implement their own query predicates |
| **Auditing** | Automatic via `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit` | None |
| **Events** | Domain events (in-process) + integration events (outbox) | None |
| **Transactions** | EF Core execution strategy + `ExecuteTransactionAsync` / `ExecuteCoordinatedTransactionAsync` | Couchbase Transactions via `ExecuteTransactionAsync(Func<AttemptContext, Task<bool>>)` |
| **DI** | `AddHeadlessDbContext<TDbContext>(...)` | `AddHeadlessCouchbase()` for the framework providers; the consumer supplies `ICouchbaseClusterOptionsProvider` + `ICouchbaseTransactionConfigProvider` |

`Headless.Orm.EntityFramework.Messaging` is an add-on to `Headless.Orm.EntityFramework`, not a competing provider. It does not appear in the table above.

---

## Headless.Orm.EntityFramework

Entity Framework Core integration with framework conventions and save pipeline orchestration.

### Problem Solved

Provides a framework-aware base `DbContext` with conventions for auditing, soft delete, tenant filters, two-tier event dispatch (in-process domain events plus transactional integration-event outbox), and transaction-aware save behavior — so application contexts inherit a consistent, tested baseline without hand-wiring each concern.

### Key Features

- `HeadlessDbContext` base context — requires `(HeadlessDbContextServices services, DbContextOptions options)` constructor parameters and `public abstract string? DefaultSchema { get; }` override
- `IHeadlessDbContext` interface implemented by `HeadlessDbContext` and `HeadlessIdentityDbContext` — shared seam for coordinated-transaction helpers and the factory
- DI registration via `AddHeadlessDbContext<TDbContext>(...)` (registers context, `IDbContextFactory<TDbContext>`, commit-coordination interceptors, and headless services)
- Application-generated Guid keys: every `IEntity<Guid>` is configured `ValueGenerated.Never`; key is produced client-side at add time via a provider-keyed `IGuidGenerator` (`SqlServer` comb, `Version7` for others). Numeric keys are not generated by Headless.
- Automatic audit fields for `ICreateAudit` / `IUpdateAudit` / `IDeleteAudit` / `ISuspendAudit` entities
- Three named global query filters: `MultiTenancyFilter` (`IMultiTenant`), `NotDeletedFilter` (`IDeleteAudit`), `NotSuspendedFilter` (`ISuspendAudit`); per-query bypass via `IgnoreMultiTenancyFilter()` / `IgnoreNotDeletedFilter()` / `IgnoreNotSuspendedFilter()`
- Composable save pipeline driven by `HeadlessDbContextOptions` and an ordered chain of `IHeadlessSaveEntryProcessor` instances
- `AddSaveEntryProcessor<TProcessor>(ServiceLifetime)` / `RemoveSaveEntryProcessor<TProcessor>()` for custom pipeline extension
- Optional tenant write guard for `IMultiTenant` save protection (`CrossTenantWriteException`, `MissingTenantContextException`)
- Two-tier event dispatch collected inside `SaveChanges`: domain events via `ILocalEventBus` before commit (`.AddDomainEvents()`), integration events via `IHeadlessOutboxDispatcher` in-transaction before commit (`.AddIntegrationEventOutbox()`, from `Headless.Orm.EntityFramework.Messaging`)
- `IHeadlessDbContextBuilder` returned by `AddHeadlessDbContextServices(...)` for chaining event tiers
- Runtime guard that fails the save with a remediation message when an entity emits events but the matching tier is not registered
- Resilient transaction helpers: `ExecuteTransactionAsync(...)` (wraps in EF execution strategy), `ExecuteCoordinatedTransactionAsync(...)` (also enlists commit coordination for outbox/jobs drain)
- Value converters: `MoneyValueConverter`, `MonthValueConverter`, `AccountIdValueConverter`, `UserIdValueConverter`, `LocaleValueConverter`, `NormalizeDateTimeValueConverter`, `JsonValueConverter`, `ExtraPropertiesValueConverter`
- `DataGridExtensions` for pagination and ordering on `IQueryable<T>`
- `IDbContextFactory<TDbContext>` auto-registered as singleton via `HeadlessDbContextFactory<TDbContext>`

### Design Notes

- **Not poolable by design.** `HeadlessDbContext` holds a private `HeadlessDbContextRuntime` that captures the request-scoped outbox dispatcher (`IHeadlessOutboxDispatcher`) and audit persistence (`IHeadlessAuditPersistence`). Pooling reuses a prior request's unit of work — a captive-dependency correctness bug. The two-argument constructor also violates EF's single-`DbContextOptions` pooling contract. For read-heavy hot paths that don't need the write pipeline, use a plain `DbContext` with `AddDbContextPool` alongside the write-side `HeadlessDbContext`.
- **Client-side Guid generation is intentional.** The key is available before `SaveChanges`, so it can be used for foreign keys, outbox rows, and domain events in the same unit of work. The `Version7` (time-ordered) and `SqlServer` comb strategies ensure monotonic insertion order per provider, limiting index fragmentation.
- **Synchronous enlist in commit coordination.** The save pipeline opens its transaction and synchronously enlists it in commit coordination so the ambient coordinator carries the live EF transaction. An `AsyncLocal` push inside an `async` helper does not flow back to the caller, making synchronous enlistment the only correct approach. This is why `AddHeadlessDbContextServices` always calls `AddEntityFrameworkCommitCoordination()` — the enlist is harmless when nothing is enlisted.
- **Domain-event at-most-once guard.** The pipeline runs domain-event publication inside the EF execution strategy. A guard ensures handlers are invoked only on the first attempt and are not re-invoked on a replay. Because publication precedes commit, a handler can run on an attempt that ultimately fails to commit — keep domain-event side effects idempotent. Under a caller-managed transaction driven by your own retry loop, each `SaveChanges` is a fresh invocation with a fresh guard, so handlers can publish again; idempotency is the right defensive posture regardless.
- **Negative index pagination is page-from-end.** `ToIndexPageAsync(index: -1, size: N)` returns the final page, not just the last `N` rows, and normalizes the returned `IndexPage.Index` to the actual zero-based page index. EF queries use `Skip`/`Take` so providers can translate the slice to SQL.

### Installation

```bash
dotnet add package Headless.Orm.EntityFramework
```

### Quick Start

```csharp
public sealed class AppDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<AppDbContext> options
) : HeadlessDbContext(services, options)
{
    public DbSet<Product> Products => Set<Product>();

    public override string? DefaultSchema => "app"; // null or "" uses provider default

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // required — wires filters, conventions, runtime
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

// Registration
builder.Services.AddHeadlessDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// Opt in to event tiers — AddHeadlessDbContextServices(...) returns IHeadlessDbContextBuilder:
builder.Services.AddHeadlessDbContextServices()
    .AddDomainEvents()             // ILocalEventBus for in-process domain events
    .AddIntegrationEventOutbox();  // IHeadlessOutboxDispatcher from the bridge package
```

If an entity emits domain events but `.AddDomainEvents()` was not called, or emits integration events but `.AddIntegrationEventOutbox()` was not called, the save throws an `InvalidOperationException` naming the missing registration. Guards fire only when events are actually emitted.

#### Resilient Transactions

```csharp
// No-return form
await dbContext.ExecuteTransactionAsync(
    async (ctx, ct) =>
    {
        ctx.Products.Add(new Product { Name = "Widget" });
        await ctx.SaveChangesAsync(ct);
    },
    cancellationToken: ct
);

// Return-value form
var productId = await dbContext.ExecuteTransactionAsync(
    async (ctx, ct) =>
    {
        var product = new Product { Name = "Widget" };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync(ct);
        return product.Id;
    },
    cancellationToken: ct
);

// Commit-coordinated (outbox/jobs drain atomically on commit)
await dbContext.ExecuteCoordinatedTransactionAsync(
    async (ctx, ct) =>
    {
        ctx.Products.Add(new Product { Name = "Widget" });
        await ctx.SaveChangesAsync(ct);
    },
    cancellationToken: ct
);
```

### Configuration

#### Global Filters

Three named filters are applied automatically when entities implement the corresponding interface. Bypass per-query with the matching extension method:

```csharp
// Read soft-deleted entities for admin purposes
var all = await dbContext.Products.IgnoreNotDeletedFilter().ToListAsync(ct);

// Read across tenants (host/admin path only)
var allTenants = await dbContext.Products.IgnoreMultiTenancyFilter().IgnoreNotDeletedFilter().ToListAsync(ct);
```

Each bypass emits a `[SECURITY AUDIT]` trace through `Debug.WriteLine` with caller member + file.

#### Tenant Write Guard

Disabled by default. Opt in to scope writes to the current tenant:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
```

When enabled:
- Missing ambient tenant on tenant-owned writes throws `MissingTenantContextException`.
- Cross-tenant add, update, soft-delete, or physical delete throws `CrossTenantWriteException`.
- Added `IMultiTenant` entities with no `TenantId` are stamped with `ICurrentTenant.Id`.

For intentional host/admin writes, use `ITenantWriteGuardBypass.BeginBypass()`:

```csharp
var bypass = serviceProvider.GetRequiredService<ITenantWriteGuardBypass>();

using (bypass.BeginBypass())
{
    await dbContext.SaveChangesAsync(ct);
}
```

`IgnoreMultiTenancyFilter()` does not relax write protection — both bypasses must be applied independently when the same code path reads and writes across tenants.

Known gaps: attach-then-modify and raw SQL are out of scope for both layers (see Agent Instructions).

For package-level wiring without the tenancy surface, `services.AddHeadlessTenantWriteGuard()` remains available.

#### Module Model Mapping

Each feature-storage EF package exposes a `ModelBuilder` extension. Call them inside `OnModelCreating` after `base.OnModelCreating(modelBuilder)`:

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

These read `Schema` and `*TableName` from validated `*StorageOptions` and apply them consistently across providers.

#### Custom Save Processors

```csharp
public sealed class AppSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        // Applied to every tracked entity before SaveChanges.
    }
}

builder.Services.AddHeadlessDbContextServices(options =>
{
    options.AddSaveEntryProcessor<AppSaveEntryProcessor>(ServiceLifetime.Scoped);
    // options.RemoveSaveEntryProcessor<HeadlessAuditSaveEntryProcessor>(); // opt out of built-in
});
```

Custom processors are inserted before the terminal lifecycle and message-collector processors.

#### Value Converters

```csharp
modelBuilder.Entity<Order>().Property(o => o.Total).HasConversion<MoneyValueConverter>();

// Or apply globally across all matching CLR types:
configurationBuilder.Properties<Money>().HaveConversion<MoneyValueConverter>();
```

### Dependencies

- `Headless.Domain`
- `Headless.Core`
- `Headless.Hosting`
- `Headless.MultiTenancy`
- `Headless.CommitCoordination.EntityFramework`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Registers `HeadlessDbContextServices`, `IHeadlessSaveChangesPipeline`, the default save-entry processor chain (`HeadlessEntitySaveEntryProcessor`, `HeadlessAuditSaveEntryProcessor`, `HeadlessLocalEventSaveEntryProcessor`, `HeadlessMessageCollectorSaveEntryProcessor`)
- Registers `IDbContextFactory<TDbContext>` as singleton (`HeadlessDbContextFactory<TDbContext>`); creates a fresh service scope per factory call
- Registers `IDbContextOptionsConfiguration<TDbContext>` that auto-attaches DI-registered `IInterceptor` instances to EF's option pipeline (covers both `AddHeadlessDbContext` and consumer's own `AddDbContext`)
- Registers `EntityFrameworkCommitCoordination` (EF interceptor + ambient commit coordinator) via `AddEntityFrameworkCommitCoordination()`
- `.AddDomainEvents()` registers `ILocalEventBus` (via `services.AddHeadlessLocalEventBus()`); `.AddIntegrationEventOutbox()` (from `Headless.Orm.EntityFramework.Messaging`) registers `IHeadlessOutboxDispatcher`; neither is registered by default
- Registers `TenantWriteGuardOptions` and `ITenantWriteGuardBypass` (always; guard is disabled by default)
- Registers via `TryAddSingleton`: `IClock`, keyed `IGuidGenerator` strategies (`Version7` and `SqlServer`) plus an unkeyed `Version7` default, `ICurrentTenantAccessor`, `ICurrentUser` (`NullCurrentUser`), `ICorrelationIdProvider`, `TimeProvider.System`
- Registers `ICurrentTenant` (`CurrentTenant`), replacing only the framework-fallback `NullCurrentTenant` while preserving consumer-provided tenant implementations
- Replaces `ICompiledQueryCacheKeyGenerator` so tenant-scoped queries share compiled plans correctly
- Registers `IAmbientDbTransactionAccessor` and `IAuditChangeCapture` (`EfAuditChangeCapture`) for audit-log integration

---

## Headless.Orm.EntityFramework.Messaging

Bridge package that supplies the real `IHeadlessOutboxDispatcher` for EF integration-event outbox dispatch.

### Problem Solved

`Headless.Orm.EntityFramework` defines the `IHeadlessOutboxDispatcher` seam but ships no implementation, so it carries no messaging dependency. This package supplies the implementation: integration events emitted by entities during an EF `SaveChanges` are written to the messaging outbox atomically with the business data and delivered to the broker after commit — without the core ORM package depending on messaging.

### Key Features

- Transactional outbox enlistment in the EF save transaction, so outbox rows commit atomically with the business data
- Routes each concrete `IIntegrationEvent` to `IOutboxBus.PublishAsync<TConcrete>` through a cached compiled invoker (`IntegrationEventPublishInvokerCache`) — one compiled delegate per runtime event type for allocation efficiency
- Both sync (`Dispatch`) and async (`DispatchAsync`) save paths via `OutboxIntegrationEventDispatcher`
- `.AddIntegrationEventOutbox()` builder extension on `IHeadlessDbContextBuilder`

### Design Notes

- **Commit-coordinated enlistment.** The save pipeline opens its transaction and synchronously enlists it in commit coordination (`DatabaseFacade.EnlistCommitCoordination`), so the ambient commit coordinator carries the live transaction. The dispatcher publishes each integration event; the outbox writer buffers the rows inside the transaction — not sent to the broker in-band. The registered `IDbTransactionInterceptor` drains the buffered dispatch on commit and discards it on rollback. Outbox rows commit atomically with the business data.
- **Post-commit delivery.** The interceptor triggers the buffered dispatch on commit; the background relay also sweeps committed rows independently for crash recovery. On PostgreSQL the relay is the primary latency-bounded path. Pick the outbox storage provider on `AddHeadlessMessaging` with that trade-off in mind.
- **Dependency isolation.** This bridge stays the only messaging-aware seam between the two domains. `Headless.Orm.EntityFramework` takes a dependency on `Headless.CommitCoordination.EntityFramework` (generic, datastore-agnostic transaction coordination — not messaging) to own the coordinated save scope. The messaging dependency is isolated to this bridge.
- **CDC alternative.** Change Data Capture (e.g. Debezium reading the database transaction log) is an advanced alternative deployment for capturing integration events outside the application process; it bypasses this dispatcher entirely and is a host-infrastructure decision, not a package option.

### Installation

```bash
dotnet add package Headless.Orm.EntityFramework.Messaging
```

### Quick Start

```csharp
// Chain after AddHeadlessDbContextServices:
builder
    .Services.AddHeadlessDbContextServices()
    .AddDomainEvents() // ILocalEventBus for in-process domain events
    .AddIntegrationEventOutbox(); // IHeadlessOutboxDispatcher — this package

// A messaging setup with an outbox storage provider is required:
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory(); // broker
    setup.UsePostgreSql(connectionString); // outbox storage
});
```

`.AddIntegrationEventOutbox()` is parameterless — the dispatcher has no options. Broker, storage, and retry behavior are configured on `AddHeadlessMessaging`. Once registered, integration events emitted by `IIntegrationEventEmitter` entities during a save are enqueued to the outbox before commit and delivered after commit.

### Configuration

None. (Configured via `AddHeadlessMessaging`.)

### Dependencies

- `Headless.Orm.EntityFramework`
- `Headless.Domain`
- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Abstractions`

### Side Effects

- Registers `IHeadlessOutboxDispatcher` as scoped (`TryAdd`) — `OutboxIntegrationEventDispatcher`
- Registers `IntegrationEventPublishInvokerCache` as singleton (`TryAdd`)

---

## Headless.Orm.Couchbase

Couchbase integration for bucket-context based document access, transactions, and collection management.

### Problem Solved

Provides a typed context model over Couchbase buckets with helper APIs for document operations (KV, LookupIn, MutateIn, scan, transactions) and schema bootstrap (scope/collection/index lifecycle), following the same context-provider pattern as `Headless.Orm.EntityFramework` but for the document model.

### Key Features

- `CouchbaseBucketContext` base context over Linq2Couchbase `BucketContext` — exposes typed `Query<T>(scope, collection)` for N1QL and `ExecuteTransactionAsync(Func<AttemptContext, Task<bool>>)` for Couchbase Transactions
- `IBucketContextProvider` / `BucketContextProvider` — resolves typed contexts per cluster key + bucket name + default scope; wires cluster and transaction objects via `ICouchbaseClustersProvider`
- `ICouchbaseClustersProvider` / `CouchbaseClustersProvider` — manages cluster connections keyed by `clusterKey`, each lazily initialized and cached; returns a `(ICluster, Transactions)` tuple per call to `GetClusterAsync`
- `DocumentSetExtensions` — KV operations (`GetAsync`, `ExistsAsync`, `UpsertAsync`, `InsertAsync`, `ReplaceAsync`, `RemoveAsync`, `UnlockAsync`, `TouchAsync`, `GetAndLockAsync`, `GetAnyReplicaAsync`, `LookupInAsync`, `MutateInAsync`, `ScanAsync`) typed against `IEntity` models; keys are derived from `IEntity.GetKey()`
- `ICouchbaseManager` / `CouchbaseManager` — idempotent scope, collection, and index bootstrapping with Polly retry; `CreateScopeAsync`, `CreateCollectionsAsync`, `CreateSecondaryIndexAsync`, `BuildDeferredIndexesAsync`
- `ICouchbaseClusterOptionsProvider` — consumer-supplied cluster connection options per cluster key
- `ICouchbaseTransactionConfigProvider` — consumer-supplied transaction configuration per cluster key
- `CouchbaseEventingFunctionsSeeder` — seeds eventing functions from embedded resources
- `SetupCouchbase.AddHeadlessCouchbase()` — registers the framework-owned providers (`ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, `ICouchbaseAssemblyCollectionsReader`) in one call

### Installation

```bash
dotnet add package Headless.Orm.Couchbase
```

### Quick Start

```csharp
// Define a typed bucket context
public sealed class AppBucketContext(
    IBucket bucket,
    Transactions transactions,
    ILogger<CouchbaseBucketContext> logger
) : CouchbaseBucketContext(bucket, transactions, logger)
{
    public DocumentSet<Product> Products => GetDocumentSet<Product>("products");
}

// Resolve context via the provider (typically injected via ICouchbaseClustersProvider + IBucketContextProvider)
var context = await bucketContextProvider.GetAsync<AppBucketContext>(
    clusterKey: "default",
    bucketName: "app",
    defaultScopeName: "_default"
);

// KV operations via DocumentSetExtensions
var product = await context.Products.GetAsync<Product, string>("product-123");
await context.Products.UpsertAsync(product);

// N1QL query
var results = context.Query<Product>("_default", "products")
    .Where(p => p.IsActive)
    .ToList();

// Couchbase Transaction — return true to commit, false to rollback
await context.ExecuteTransactionAsync(async attempt =>
{
    // perform transactional KV operations
    return true;
});
```

### Configuration

- Implement and register `ICouchbaseClusterOptionsProvider` to supply cluster options (connection string, credentials) per cluster key.
- Implement and register `ICouchbaseTransactionConfigProvider` to supply transaction configuration per cluster key.
- Resolve `IBucketContextProvider` from DI to get typed bucket contexts.
- Use `ICouchbaseManager` during application startup or `IInitializer` to bootstrap scopes, collections, and indexes idempotently.

```csharp
// Supply the two application-specific providers (or register the shipped defaults):
services.AddSingleton<ICouchbaseClusterOptionsProvider, MyClusterOptionsProvider>();
services.AddSingleton<ICouchbaseTransactionConfigProvider, MyTransactionConfigProvider>();

// Register the framework-owned providers in one call:
services.AddHeadlessCouchbase();
```

### Dependencies

- `Headless.Domain`
- `Headless.Hosting`
- `Couchbase.Extensions.DependencyInjection`
- `Couchbase.Transactions`
- `Linq2Couchbase`
- `Polly`
- `Humanizer`

### Side Effects

- `AddHeadlessCouchbase()` registers `ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, and `ICouchbaseAssemblyCollectionsReader` as singletons via `TryAdd` (a consumer's own registration wins). It does not register `ICouchbaseClusterOptionsProvider` or `ICouchbaseTransactionConfigProvider` — those remain the consumer's responsibility.
- Cluster connections are lazily initialized and statically cached by `clusterKey` in `CouchbaseClustersProvider`. Each cluster waits up to 1 minute for readiness on first access; a readiness failure is logged but does not throw (operations fail at call time).
- `CouchbaseManager` caches scope/collection specs per `clusterKey + bucketName` in-memory to reduce repeated `GetAllScopesAsync` calls; cache is invalidated on scope creation.
- `CouchbaseBucketContext.ExecuteTransactionAsync` emits `Information` logs on success and `Error` logs on failure via structured logging.
