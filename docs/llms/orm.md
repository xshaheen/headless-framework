---
domain: ORM
packages: EntityFramework.Primitives, EntityFramework, EntityFramework.Messaging, Couchbase
---

# ORM

> Persistence through EF Core (`Headless.EntityFramework`, with `Headless.EntityFramework.Primitives` and the `Headless.EntityFramework.Messaging` outbox bridge) or Couchbase (`Headless.Couchbase`).

## Orientation

Choose by storage model:

- `Headless.EntityFramework`: the default for any relational store (PostgreSQL, SQL Server, SQLite). It provides `HeadlessDbContext` with audit fields, EF model-driven audit-log capture, soft delete, tenant filters, domain and integration events, and a save pipeline that joins the unit of work. Unit-of-work support is built in: `IUnitOfWorkFactory.BeginAsync(db)`, `Enlist(db, transaction)`, and `RunAsync(db, …)` work with no extra package. [Unit of Work](unit-of-work.md) owns that contract.
- `Headless.EntityFramework.Primitives`: provider-neutral EF Core converters, primitive mappings, and query helpers, with no `HeadlessDbContext` and no save pipeline. Reference it alone when you need only those helpers.
- `Headless.EntityFramework.Messaging`: an add-on to `Headless.EntityFramework`, never an alternative. It supplies `IHeadlessOutboxDispatcher`, so integration events that entities emit are written to the messaging outbox in the same transaction as the business rows. Add it when entities emit integration events.
- `Headless.Couchbase`: document storage through `CouchbaseBucketContext`, `IBucketContextProvider`, `ICouchbaseClustersProvider`, `DocumentSetExtensions`, and `ICouchbaseManager`. It has no global filters, no auditing, and no events.

For raw SQL connection factories and Dapper-style access, use the [SQL](sql.md) packages.

### The two-tier event model

`HeadlessDbContext` collects two kinds of events during `SaveChanges` and dispatches each through its own interface:

- **Domain events**: payloads that `IDomainEventEmitter` entities emit. They run in process, inside the save transaction, through `IDomainEventDispatcher`, before the business rows are written. Opt in with `.AddDomainEvents()` (in `Headless.EntityFramework`).
- **Integration events**: payloads that `IIntegrationEventEmitter` entities emit. They go to the transactional outbox through `IHeadlessOutboxDispatcher` before commit, and the messaging relay delivers them to the broker after commit. Opt in with `.AddIntegrationEventOutbox()` (in `Headless.EntityFramework.Messaging`).

Neither tier is registered by default. No startup check covers them: a save throws `InvalidOperationException` naming the missing registration, and only when an entity emits an event for that tier.

## Agent Rules

- Register a context with `AddHeadlessDbContext<TDbContext>(...)` or `AddHeadlessDbContextPool<TDbContext>(...)`, not raw `AddDbContext`. Raw registration misses the Headless services, DI-registered interceptor attachment, the `IDbContextFactory<TDbContext>` singleton, request-scope binding, and the compiled-query cache key replacement.
- Prefer `AddHeadlessDbContextPool<TDbContext>` for high-throughput services. Use `AddHeadlessDbContext<TDbContext>` when the context constructor or the options callback needs scoped services. See [Registration modes and scope binding](#registration-modes-and-scope-binding).
- Keep no per-request state in fields of a pooled context. The pool does not reset fields you add. A pooled context declares exactly one public constructor that takes its `DbContextOptions` and, optionally, singleton services.
- Declare a subclass as `AppDbContext(DbContextOptions<AppDbContext> options) : HeadlessDbContext(options)`; `protected HeadlessDbContext(DbContextOptions options)` is the only base constructor. Override `public abstract string? DefaultSchema { get; }`: `null` or empty uses the provider default, and a non-empty string calls `modelBuilder.HasDefaultSchema`.
- Call `base.OnModelCreating(modelBuilder)` and `base.ConfigureConventions(configurationBuilder)` in every subclass. Without `ConfigureConventions`, the context loses the audit and concurrency-stamp mapping, client-side Guid keys, `DateTime` normalization, and the soft-delete and tenant filters.
- Pass model-shaping state (a schema or table name, a feature flag, a tenant-specific mapping) through a primary-constructor parameter, a field initializer, a constant property, or the options. `OnModelCreating` runs during the base constructor, before a derived constructor body, so it sees none of that body's assignments. EF Core caches the model per context type for the life of the process.
- Run multi-step atomic work through `IUnitOfWorkFactory.RunAsync(db, (unit, ct) => …)`, not a hand-opened transaction. It is the only API that opens a transaction under a retrying execution strategy (for example, SQL Server `EnableRetryOnFailure`), and there is no overload without a unit of work. Inject the singleton `IUnitOfWorkFactory`. See [Resilient Transactions](#resilient-transactions).
- Chain `.AddDomainEvents()` and `.AddIntegrationEventOutbox()` off `AddHeadlessDbContextServices(...)`, which returns `IHeadlessDbContextBuilder`. `.AddIntegrationEventOutbox()` lives in `Headless.EntityFramework.Messaging` and takes no parameters.
- Keep domain-event handlers replay-safe and keep external effects out of them. After the first handler returns, a pipeline-owned save no longer retries. See [Retry and replay](#retry-and-replay).
- Customize the save pipeline with `options.AddSaveEntryProcessor<TProcessor>(ServiceLifetime)` on `HeadlessDbContextOptions`, and opt out of a built-in processor with `options.RemoveSaveEntryProcessor<TProcessor>()`. Replace `IHeadlessSaveChangesPipeline` only when you need full control of the save.
- Insert a row that no signed-in user authored, such as a bulk import inside a request, with `db.Set<T>().Add(entity).SkipCreatedByIdStamp()` (namespace `Microsoft.EntityFrameworkCore.ChangeTracking`). The save keeps `CreatedById` as the entity holds it and still stamps `CreatedAt`. For a change no signed-in user made to an existing row, call `db.Entry(entity).SkipUpdatedByIdStamp()`, `SkipDeletedByIdStamp()`, or `SkipRestoredByIdStamp()` before the save. Do not null an actor ID back out in a second save, and do not change the current principal around the save: that also strips the actor from every other entity the save stamps.
- Configure audit capture in the EF model with `IsAudited()`, `ExcludeFromAudit()`, and `IsAuditSensitive(...)`. Domain entities carry no audit marker or attribute. Unconfigured entities follow `AuditLogOptions.AuditByDefault`.
- Page a list query with `ToIndexPageAsync(index, size, ct)` or `ToIndexPageAsync(request, ct)`, and give an optional query-string page a default before the call. There is no overload that takes a nullable index, size, or request. The overloads that take no page input read every row: `ToIndexPageAsync(ct)`, `ToIndexPageAsync(orderBy, ascending, ct)`, and `ToIndexPageAsync(orderBy, ascending, selector, ct)`. Call them only on a query you have already bounded. `IDataGridRequest.Page` is required for the same reason.
- Load a soft-deleted row with `IgnoreNotDeletedFilter()`, then call `Restore(...)` or set `IsDeleted = false`. The soft-delete filter hides it otherwise.
- Put `IDeleteAudit` on the root of an inheritance hierarchy. EF Core filters only hierarchy roots, so a derived-only `IDeleteAudit` type is never filtered and `IgnoreNotDeletedFilter()` changes nothing. Model building logs the `HeadlessDerivedDeleteAuditNotFiltered` warning naming the type and its root. If the root must stay hard-deleted, filter `IsDeleted` yourself in each query on the derived set.
- Model suspension as your own domain state with your own filter. The framework has no suspension audit or filter.
- Make a navigation to a filtered principal optional, or bypass the filter on that query. A required navigation becomes an inner join and drops the dependent row too.
- Declare tenant ownership of a third-party root with `IsTenantOwned()` after `base.OnModelCreating(modelBuilder)`. `IMultiTenant` is the default ownership signal, and `IsNotTenantOwned()` excludes a root.
- Wrap a host or admin write in `ITenantWriteGuardBypass.BeginBypass()` when `GuardTenantWrites()` is on. `IgnoreMultiTenancyFilter()` lifts only the read filter, and the two bypasses are independent.
- Create a fresh `DbContext` for each tenant scope. The tenant filter reads the current tenant on every query, but `FindAsync` can return an entity already tracked under another tenant.
- Add explicit tenant predicates and authorization to raw SQL and stored procedures. They bypass the query filters and the write guard, and `BeginBypass()` has no effect on them. Bulk `ExecuteUpdate` and `ExecuteDelete` apply the query filters but skip the write guard.
- Answer a lost uniqueness race with your own error through `db.SaveChangesOrConflictAsync(indexName, descriptor, ct)`, not a hand-written `catch (DbUpdateException)` that parses the provider exception, and not a pre-save existence check alone, which a concurrent writer can pass at the same time. See [Unique conflicts](#unique-conflicts).
- Treat a zero-row update or delete as `DbUpdateConcurrencyException`, as EF Core reports it. Never translate it into a tenancy exception, and never assume that domain-event handlers did not run.
- Write your own migrations for tenant columns, keys, and indexes. Headless supplies no backfill and no default tenant. See [Tenant schema rollout](#tenant-schema-rollout).
- Leave `ConcurrencyStamp` on ASP.NET Identity entities to Identity. Do not add framework concurrency stamping to them.
- Map concurrency versions natively: a `uint` property to `xmin` with `IsRowVersion()` on PostgreSQL, and a `byte[]` property to `rowversion` on SQL Server. Derive HTTP entity tags at the API boundary. Do not model a provider-neutral `byte[]` as a database-generated row version.
- Map feature storage tables with the `ModelBuilder` extensions inside `OnModelCreating`: `ConfigureHeadlessAuditLog(...)`, `ConfigureHeadlessFeatures(...)`, `ConfigureHeadlessPermissions(...)`, and `ConfigureHeadlessSettings(...)`.
- Clamp a page size that comes from a request before you page with it: `query.ToIndexPageAsync(index, Math.Clamp(size, 1, MaxPageSize), ct)`, not `query.ToIndexPageAsync(index, size, ct)` with the query-string value passed through. Nothing in the framework caps a page size, so `ToIndexPageAsync(0, int.MaxValue, ct)` reads every row. Clamp the `Size` of an `IIndexPageRequest` or a `DataGridRequest.Page` the same way before you pass it to `ToIndexPageAsync(request, ct)` or `ToDataGridAsync(request, ct)`. Choose the cap per endpoint.
- Preserve each `EventContext<object>` in a custom `IHeadlessOutboxDispatcher`: keep its IDs and business context, including a null root cause or system tenant, across save and retry. Never recapture at dispatch.
- For Couchbase, use `CouchbaseBucketContext` with `IBucketContextProvider` and name the cluster and bucket explicitly. `DocumentSetExtensions` accept only `IEntity` models.

## Core Concepts

### HeadlessDbContext conventions

- **Guid keys are generated on the client.** Every `IEntity<Guid>` is mapped `ValueGenerated.Never`, and a value generator assigns the key when the entity becomes `Added`. The key exists before `SaveChanges`, so foreign keys, outbox rows, and domain events in the same unit can use it. The `IGuidGenerator` strategy depends on the provider: `SqlServer` comb on SQL Server and `Version7` elsewhere, both time-ordered to limit index fragmentation.
- **The Guid generator resolves `IGuidGenerator` from the context's application service provider.** A per-scope context resolves a scoped generator per scope. A pooled context uses the root provider, so register a replacement generator as a singleton: a scoped one fails DI scope validation, and without validation one instance serves the whole host.
- **Headless does not generate numeric keys.** An `IEntity<long>` or other numeric key uses your own EF, database, or provider strategy.
- **`DefaultSchema` sets the schema of every entity.** `null` or empty uses the provider default.
- **`ConfigureConventions` adds value converters** for `Money`, `Month`, `AccountId`, `UserId`, `Locale`, `ExtraProperties`, and `DateTime` normalization.
- **Entity mapping is a model convention.** For every non-owned `IEntity` it maps the `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `IHasConcurrencyStamp`, and `IHasExtraProperties` columns, maps each audit actor navigation (`CreatedBy`, `UpdatedBy`, `DeletedBy`, `RestoredBy`) as an optional one-way reference with `DeleteBehavior.Restrict`, and adds the Guid key generator. It normalizes `DateTime` properties on every entity type, owned types and TPH-derived properties included, and adds `NotDeletedFilter` to `IDeleteAudit` roots. The convention reaches entity types added with `modelBuilder.Entity<T>()` after the base call.
- **Your explicit configuration wins.** Configure a facet before or after `base.OnModelCreating`, and the convention leaves it alone: rename the `IsDeleted` column, map the key as store-generated, or replace a `DateTime` converter. Fluent configuration of an audit actor navigation replaces the Headless one-way reference.
- **`IDbContextFactory<TDbContext>` is registered as a singleton** by both `AddHeadlessDbContext<TDbContext>` and `AddHeadlessDbContextPool<TDbContext>`, so background services create contexts without `AddDbContextFactory`.

### Registration modes and scope binding

A `HeadlessDbContext` does not store its scoped collaborators (save pipeline, audit persistence, save-entry processors). It resolves them from the DI scope bound to the current use. It reads the singleton `ICurrentTenant`, the guard options, and the write-guard bypass on every access, without a scope. That is why the context is safe to pool. A scoped or transient `ICurrentTenant` registration fails host startup with `InvalidServiceLifetimeException`.

| Registration | Instance per scope | `IDbContextFactory<TDbContext>` | Constructor may take |
| --- | --- | --- | --- |
| `AddHeadlessDbContext<TDbContext>(optionsAction, [configureHeadlessOptions], contextLifetime = Scoped, optionsLifetime)` | New instance per scope | Creates a DI scope per context and disposes it with the context | Its options plus scoped or singleton services |
| `AddHeadlessDbContextPool<TDbContext>(optionsAction, [configureHeadlessOptions], poolSize = 1024)` | Leased from the pool, returned when the scope ends | EF Core's `PooledDbContextFactory<TDbContext>` | Exactly one public constructor: its options plus, optionally, singleton services |

- **`contextLifetime` must be `Scoped`**, the default. `Singleton` and `Transient` throw `ArgumentException`, because a domain-event handler that injects a transient context gets a different instance with no unit of work, and `db.UnitOfWork()?.Outbox.PublishAsync(…)` publishes nothing. Use `IDbContextFactory<TDbContext>` outside the request scope.
- **`optionsLifetime` defaults to `Scoped`** on every `AddHeadlessDbContext` overload, the Identity context's included, so the options are built in every scope. Pass `ServiceLifetime.Singleton` to build them once from the root provider and share them across scopes and factory contexts, which saves about 15 KB of options per scope. Each scope still gets a copy bound to that scope, so `new AppDbContext(options)` uses the request scope and a scoped `IGuidGenerator` still resolves per scope.
- **Do not pass `Singleton` options when the options action reads per-request or per-tenant state** (a connection string from a static field or an `AsyncLocal`, a closure over `IHttpContextAccessor`), or creates a connection, an `NpgsqlDataSource`, or an interceptor per call. The action then runs once with the root provider, every scope gets the first build's values, and a database-per-tenant application runs every tenant on the first tenant's connection with no error. Nothing checks this. A scoped `IInterceptor` resolved from the root fails DI scope validation.
- **Pooled options are singletons.** The pooled overloads take `Action<DbContextOptionsBuilder>` or `Action<IServiceProvider, DbContextOptionsBuilder>`, and the provider is the root provider, so the callback must not resolve scoped services.

How a context gets its scope:

- **Resolved from DI** (either Headless registration): bound to the resolving scope. Ending the scope disposes a per-scope context or returns a pooled one. Disposing a scope-resolved pooled context yourself does nothing, as with EF Core's `AddDbContextPool`.
- **Built over per-scope options**: a plain EF `AddDbContext<TDbContext>()` (scoped options by default), or `new` over options with `UseApplicationServiceProvider(scope.ServiceProvider)`. The context uses that scope and does not dispose it. A plain `AddDbContext` with `optionsLifetime: ServiceLifetime.Singleton`, which EF Core requires when `AddDbContextFactory` is registered for the same type, carries the root provider instead and does not see the request's scoped services. Register it with `AddHeadlessDbContext` or `AddHeadlessDbContextPool` to keep the request scope.
- **Created outside any scope**: from a pooled or stock EF `IDbContextFactory`, or `new` over options whose application provider is the root. The context opens a private DI scope the first time it needs a scoped collaborator (the save pipeline on `SaveChanges`), and disposes it when the context is disposed or returned to the pool. A read-only context opens no scope. Under a Headless registration, the context type resolves to the same instance inside that private scope, so a domain-event handler that injects the context gets the instance being saved and reads its unit with `db.UnitOfWork()`.
- **No application service provider**: the first operation that needs services throws `InvalidOperationException` naming three remedies: resolve it from DI, create it through `IDbContextFactory`, or build its options with `UseApplicationServiceProvider(...)`.

`IHeadlessDbContext.ServiceProvider` returns the provider of the bound or private scope.

Plain EF registrations (`AddDbContext<TDbContext>()`, `AddDbContextFactory<TDbContext>()`, `AddPooledDbContextFactory<TDbContext>()`) also work with a `HeadlessDbContext` when you register the Headless services with `AddHeadlessDbContextServices()`. To attach DI-registered interceptors there, call `options.AddDiRegisteredInterceptors(serviceProvider)` (namespace `Microsoft.EntityFrameworkCore`) from the `(serviceProvider, options)` action.

### Global query filters

| Applies to | Filter name constant | Bypass extension |
| --- | --- | --- |
| `IsTenantOwned()` roots, or `IMultiTenant` by default | `HeadlessQueryFilters.MultiTenancyFilter` | `IgnoreMultiTenancyFilter()` |
| `IDeleteAudit` roots | `HeadlessQueryFilters.NotDeletedFilter` | `IgnoreNotDeletedFilter()` |

The constants are EF Core named-filter names (for example, `"MultiTenantFilter"`). Your own filters stay in place. A bypass applies to one `IQueryable<T>` chain and writes a `[SECURITY AUDIT]` trace through `Debug.WriteLine` with the caller member and file.

`ExecuteUpdate(...)` and `ExecuteDelete(...)` apply the same filters, so bulk operations are tenant-scoped by default. They skip `SaveChanges`, so they skip the write guard and the tenant concurrency predicates.

### Save pipeline and auditing

`SaveChanges` and `SaveChangesAsync` run `IHeadlessSaveChangesPipeline`, which runs this chain of `IHeadlessSaveEntryProcessor` instances before the EF save:

1. `HeadlessEntitySaveEntryProcessor`: stamps tenant IDs and concurrency stamps. Guid keys were already assigned when the entity was added.
2. `HeadlessAuditSaveEntryProcessor`: stamps the create, update, and delete audit fields of `ICreateAudit`, `IUpdateAudit`, and `IDeleteAudit` entities. Every modified save restamps `UpdatedAt` and `UpdatedById`. An `IsDeleted` transition stamps the delete or restore pair and keeps the opposite pair as history. A non-null value the save set explicitly wins. A delete, restore, or update with no actor, neither given nor resolved from `ICurrentUser`, records a null actor ID, not the previous actor; a same-save `Update(now, byId, by)` still wins. The [`Skip{Property}Stamp()` extensions](#skipping-an-actor-stamp) override it for one entity.
3. `HeadlessLocalEventSaveEntryProcessor`: emits the `EntityCreated`, `EntityUpdated`, `EntityDeleted`, and `EntityChanged` lifecycle domain events on `IDomainEventEmitter` entities. With the built-in dispatcher, it emits a lifecycle event only when an `IDomainEventHandler<>` for that exact type is registered, closed or open generic. A custom `IDomainEventDispatcher` receives every lifecycle event.
4. `HeadlessMessageCollectorSaveEntryProcessor`: collects pending domain and integration events.

A processor you add with `AddSaveEntryProcessor<TProcessor>` runs before the last two, so lifecycle events and collected messages see its changes. It also costs every transactional save a second change-tracker scan before the final audit capture, because a custom processor may change entities without the change tracker seeing it. An audited save that opens its own transaction then captures its audit rows twice. The built-in chain skips both. Adding the same type again replaces the earlier entry.

#### Skipping an actor stamp

The `Skip{Property}Stamp()` extensions on `EntityEntry`, in `Microsoft.EntityFrameworkCore.ChangeTracking`, stop the processor from filling one actor ID from `ICurrentUser` for one entity. The matching timestamp is still stamped, and an explicitly set actor ID still wins.

| Call | Effect | How long the mark lasts |
| --- | --- | --- |
| `SkipCreatedByIdStamp()` | `CreatedById` keeps the value the entity holds on insert. | Every insert of that instance, through failed saves and an explicit unit of work. |
| `SkipUpdatedByIdStamp()` | The update records a null `UpdatedById`. | The next save that stamps `UpdatedAt`. |
| `SkipDeletedByIdStamp()` | The soft delete records a null `DeletedById`. | The next save that stamps `DeletedAt`. |
| `SkipRestoredByIdStamp()` | The restore records a null `RestoredById`. | The next save that stamps `RestoredAt`. |

A save that reaches an update, delete, or restore stamp removes that mark, whether the save commits or throws. If a save fails and you call `SaveChanges` again, the retry stamps the current user unless you mark the entry again. Mark the entry inside a `RunAsync` block, so a replay by the execution strategy marks it again. A soft delete or restore is also an update, so mark `UpdatedById` too when the change should carry no actor at all. Each call throws `ArgumentException` when the entity does not implement the matching `ICreateAudit<TId>`, `IUpdateAudit<TId>`, or `IDeleteAudit<TId>` with `UserId` or `AccountId`.

An audited save issues one database save when every audit row's entity ID is known at capture: inserts with client-assigned keys, updates, and deletes. Audit rows for inserts whose key the store generates (identity, sequence, database default) follow in a second save. `SaveChanges` returns the count of your rows only, never audit rows. This holds for the EF audit store; the raw ADO.NET audit stores write after the entity save succeeds.

After the audit save, your saved entities keep their saved values and store-generated read-backs (concurrency tokens, computed columns) in the change tracker, so a second `SaveChanges` on the same tracked entity in the same scope succeeds.

EF Core leaves a principal `Unchanged` when only its navigations change (a child added to or removed from a collection). The principal then gets no update stamp, audit row, or `EntityUpdated` event. The child rows carry their own.

Within a pipeline-owned transaction, a save runs in this order:

1. Domain events dispatched through `IDomainEventDispatcher` until no new ones appear, then final audit capture.
2. Business `SaveChanges`.
3. Audit persistence.
4. Integration events enqueued to the outbox through `IHeadlessOutboxDispatcher`.
5. Commit.

### DDD aggregate support

- Handlers of domain events can change more entities in the same `SaveChanges`, because dispatch runs before the business save. Each drain pass picks up newly emitted events and newly tracked emitters. Each tracked entity gets lifecycle events once per save.
- A save rejects more than 1,024 domain events, lifecycle events included, before any business row is written, which stops recursive emission. Final audit capture includes handler changes and newly tracked entities.
- A successful save clears only the batch it captured. Events emitted after the drain stay pending.
- With the built-in dispatcher, a domain event that no registered handler observes is not dispatched and does not open a transaction. An aggregate save with no handlers, no integration events, and no audit rows runs as one plain EF Core save, and its pending domain events are still cleared. A handler registered as an open generic counts. A container that cannot answer `IServiceProviderIsService` is treated as having handlers.

### Outbox-within-save-transaction bridge

With `Headless.EntityFramework.Messaging` registered, a save writes integration events this way:

1. The save pipeline picks the unit that owns its transaction. With no transaction on the context, it opens one, enlists it with `IUnitOfWorkFactory.Enlist(db, transaction)`, and commits it. With a caller-owned transaction, it uses the unit bound to the context (`db.UnitOfWork()`, set by `BeginAsync(db)`, `Enlist(db, tx)`, or `RunAsync(db, …)`), and refuses a save that carries integration events when there is none. The dispatcher also throws on a unit with no resource instead of publishing outside the transaction.
2. The dispatcher publishes each event through that unit's `Outbox`, always durable, so the row is written inside the unit's transaction and nothing is sent to the broker yet. It never uses `IBus`, whose rows would survive a rollback.
3. `IUnitOfWork.CompleteAsync` sends the buffered messages after commit. A rollback, explicit or abandoned, discards them.
4. The messaging relay also sweeps committed rows for crash recovery. On PostgreSQL the relay is the main delivery path with bounded latency; pick the outbox storage provider on `AddHeadlessMessaging` with that in mind.

On a transactional messaging consume path, the same local transaction also completes the inbox entry. The atomic boundary covers enlisted EF state and rows the handler publishes through `context.UnitOfWork.Outbox`. It does not cover handler entry, `IBus` or `IQueue` publishes (which never join the unit), `Direct`, or external effects.

Change Data Capture (for example, Debezium reading the transaction log) is a host-infrastructure alternative that skips this bridge. It is not a package option.

### Retry and replay

These rules apply when the execution strategy retries (for example, SQL Server `EnableRetryOnFailure`).

- **No replay after commit starts.** Both a pipeline-owned `SaveChanges` and `IUnitOfWorkFactory.RunAsync(db, …)` retry eligible failures only before commit. Once commit begins, or after `IUnitOfWork.PreventRetry()`, the exception reaches you without replay. A pipeline-owned save whose commit may have reached the database throws `UnitOfWorkInDoubtException` (the driver's fault as `InnerException`), and its unit fails with `InDoubt`. A commit that certainly failed throws its own exception and fails the unit with `Faulted`. Reconcile an in-doubt commit with your own idempotency before you repeat the operation. A fault in an `OnCompleted` callback is logged, and the save returns its count, because the rows are already durable.
- **`RunAsync(db, …)` replays on a cleared change tracker.** Each replay starts with an empty tracker, so the block re-reads its rows. A context that tracked entities before `RunAsync` began does not replay; the transient fault reaches you.
- **A pipeline-owned save stops replaying once a domain-event handler returns.** A replay re-runs only the save, not the handlers that already returned, so anything such a handler wrote outside the change tracker (`db.Database.ExecuteSql…`, a raw-ADO block on `db.Database.GetDbConnection()`, `unit.TransactionLocks`, a Jobs write, a `unit.Outbox` publish) would be lost while the aggregate commits. The pipeline therefore calls `IUnitOfWork.PreventRetry()` after the first handler returns. A later transient fault (a deadlock in the business save, a dropped connection, a fault in a later handler) reaches you; retry with a fresh context and aggregate graph. A fault before any handler returned, including inside the first handler, still replays, and the handler runs again from its start.
- **Saves that keep replay**: a save whose events have no handlers, a save that carries only integration events, and Jobs or `unit.Outbox` writes issued directly in your own `RunAsync(db, …)` block, because the replay re-runs the block.
- **A caller-owned save that dispatched events ends replay of its `RunAsync` block.** The save clears the emitters, so a replayed block would have nothing to dispatch.
- **Save completion is not commit.** A pipeline-owned save accepts changes only after commit succeeds. Inside a caller-owned transaction, each successful save clears its own event batch before the caller commits, and later saves capture only new events. After a known rollback of the caller's transaction, dispose the context, drop its aggregate graph, and recover through a fresh unit and your own idempotency. Replaying a command can create new event IDs.
- **Delivery stays at-least-once.** The outbox commits atomically with your rows, but broker delivery and external effects can repeat. Make handlers idempotent.

## Choosing a Provider

| | `Headless.EntityFramework` | `Headless.Couchbase` |
| --- | --- | --- |
| **Storage model** | Relational (PostgreSQL, SQL Server, SQLite) | Document (Couchbase bucket and collections) |
| **Use when** | Strong consistency, rich queries, schema-enforced invariants, auditing, multi-tenancy, DDD aggregates, outbox integration events | Flexible schema, horizontal scaling, key-value access, N1QL queries |
| **Avoid when** | Schema-less documents or extreme horizontal write scale | ACID transactions across many entities, relational queries, or auditing |
| **Global filters** | `IMultiTenant` and `IDeleteAudit`, automatic | None; write your own predicates |
| **Auditing** | Automatic through `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit` | None |
| **Events** | Domain events (in process) and integration events (outbox) | None |
| **Transactions** | `IUnitOfWorkFactory.RunAsync(db, …)` under the EF execution strategy | Couchbase Transactions through `ExecuteTransactionAsync(Func<AttemptContext, Task>)` |
| **Registration** | `AddHeadlessDbContext<TDbContext>(...)` | `AddHeadlessCouchbase()`, plus your `ICouchbaseClusterOptionsProvider` and `ICouchbaseTransactionConfigProvider` |

---

## Headless.EntityFramework.Primitives

Provider-neutral EF Core primitives for code that does not use `HeadlessDbContext`.

### Setup

```bash
dotnet add package Headless.EntityFramework.Primitives
```

```csharp
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;

public sealed class ScheduledWork
{
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

modelBuilder.Entity<ScheduledWork>(entity =>
{
    entity.Property(x => x.CreatedAt).HasConversion(new NormalizeDateTimeValueConverter());
    entity.Property(x => x.CompletedAt).HasConversion(new NullableNormalizeDateTimeValueConverter());
});

var page = await dbContext.Set<ScheduledWork>().ToIndexPageAsync(0, 25, cancellationToken);
```

**Verify:** no startup signal; the package registers nothing. Assert the mapping in a test: `db.Model.FindEntityType(typeof(ScheduledWork))!.FindProperty(nameof(ScheduledWork.CreatedAt))!.GetValueConverter()` is a `NormalizeDateTimeValueConverter`.

### Configuration

Converters accept optional EF Core mapping hints. Query and model helpers need no registration.

### Design and runtime behavior

- Provides converters and comparers for dates, JSON-backed values, locales, extra properties, and Headless primitives; money and phone model configuration; and pagination, ordering, data-grid, date aggregation, and lookup helpers.
- `FirstByIdAsync(id, ct)` loads an `IEntity<TKey>` by key. `FirstOrNotFoundAsync(entity, key, ct)` returns the first element of any query, typically a projection, and requires a reference-type element. Both throw `EntityNotFoundException` (404 through the API exception handler) naming the entity type and key when nothing matches.
- `ToIndexPageAsync(index, size)` and `ToIndexPageAsync(request)` run one `COUNT` and one `Skip`/`Take` query and return that page with `TotalItems`. A `null` request throws `ArgumentNullException`, and a size below 1 throws `ArgumentOutOfRangeException`. `ToDataGridAsync(request)` applies `request.Orders`, then pages by `request.Page`.
- `ToIndexPageAsync(index: -1, size: N)` returns the final page, not the last `N` rows, and sets the returned `IndexPage.Index` to the real zero-based page index. EF queries use `Skip` and `Take`, so the provider translates the slice to SQL.
- `ToDataGridAsync(request, ct)` takes an `IDataGridRequest`. Derive your request from `DataGridRequest` and bind it from a JSON body (`{"page":{"index":1,"size":5}}`) or, in MVC, from the query string (`?page.index=1&page.size=5`). `Page` is the concrete, sealed `IndexPageRequest`, so the binder creates it without a converter. `ToDataGridAsync` throws `ArgumentOutOfRangeException` when `Page.Size` is zero or less, which the API exception handler answers with a 500, whatever the binding path. Validate the page with `RuleFor(x => x.Page).IndexPage()` from `Headless.Api.FluentValidation`, so a bad size answers 422. A JSON body whose `page` omits `index` or `size` fails binding with a 400. MVC query-string binding skips `required`, so a missing `page.size` binds as `0` and only the validator rejects it.
- The audit policy extensions `IsAudited()`, `ExcludeFromAudit()`, and `IsAuditSensitive(...)` (`HeadlessAuditPolicyExtensions`, namespace `Microsoft.EntityFrameworkCore`) only write model annotations; `Headless.EntityFramework` reads them at save. `model.GetAuditPolicies(options)` and `entityType.GetAuditPolicy(options)` (`HeadlessAuditModelPolicyExtensions`) return the resolved `AuditEntityPolicy` and `AuditPropertyPolicy` by the same rules capture uses.
- `NormalizeDateTimeValueConverter` is the UTC-normalization converter. It treats `DateTimeKind.Unspecified` as an already-UTC value and does not shift it.
- `ExtraProperties`, `File`, and `Image` columns compare by stored content. Two `ExtraProperties` bags are equal when they hold the same keys with values that serialize to the same JSON, whatever the key order or boxing. The snapshot deep-copies nested values, so an in-place edit of a nested value is saved and reassigning an equal value issues no `UPDATE`. `File` and `Image` compare every stored member: `Metadata` regardless of key order, and `UploadedAt` with its offset.
- `ExtraProperties` values read back as JSON types: strings (dates, times, `Guid`, and `TimeSpan` included), `long`, `decimal` or `double`, `bool`, and `JsonElement` for nested objects and arrays. Read a typed value with `GetProperty<T>(name)`, which converts with the invariant culture: a `decimal` keeps every digit, a UTC `DateTime` keeps `DateTimeKind.Utc`, a `DateTimeOffset` keeps its offset, and enum names match case-insensitively. `GetProperty(name)` and the indexer return the stored type, so `bag["at"]` is a `string` after a reload even when you wrote a `DateTime`.
- Pair a JSON converter for a mutable reference type (a collection, dictionary, or object) with a value comparer. Without one, EF compares references and never saves in-place edits. Use `JsonValueConverter<T>` with `JsonValueComparer<T>`, and pass the same `JsonSerializerOptions` to both.
- The package depends on no database provider, hosting, tenancy, or save pipeline. Its one audit dependency is `Headless.AuditLog.Abstractions`, for the `SensitiveDataStrategy` that `IsAuditSensitive(...)` takes.

---

## Headless.EntityFramework

`HeadlessDbContext`, its registrations, and the save pipeline for relational stores. It includes `Headless.EntityFramework.Primitives`.

### Setup

```bash
dotnet add package Headless.EntityFramework
```

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : HeadlessDbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public override string? DefaultSchema => "app"; // null or "" uses provider default

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // required — wires filters, conventions, runtime
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Entity<Product>().IsAudited();
    }
}

// Registration: pooled (high throughput) ...
builder.Services.AddHeadlessDbContextPool<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// ... or one instance per scope, when the constructor or options callback needs scoped services:
// builder.Services.AddHeadlessDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Opt in to event tiers — AddHeadlessDbContextServices(...) returns IHeadlessDbContextBuilder:
builder.Services.AddHeadlessDbContextServices()
    .AddDomainEvents()             // IDomainEventDispatcher for in-process domain events
    .AddIntegrationEventOutbox();  // IHeadlessOutboxDispatcher from the bridge package
```

To apply the context's EF Core migrations as a deploy step, add `services.AddDbContextMigrations<AppDbContext>()`. It registers a [resource provisioner](utilities.md#resource-provisioning) of kind `EntityFramework` (`HeadlessDbContextMigrationsServiceCollectionExtensions.MigrationsResourceKind`), named after the context: `resources provision` (or `AddDeployStepsOnStartup` in development) applies the migrations, `resources verify` names each pending migration, and the host fails to start while one is pending. It runs after every `Schema` resource whatever the registration order, so a migration can reference a framework table or function. For a script-based deploy, apply the exported Headless schema script before the EF migration script or bundle. Purge and delete are unsupported. Registering it twice does nothing. A provisioner of your own that must run after the migrations, such as one that creates views over their tables, selects that constant in its `DependsOn` ([example](utilities.md#add-a-resource)).

**Verify:** the readiness health check `dbcontext-{TDbContext full name}` (tags `ready`, `headless`, `database`) reports healthy; see [Health checks](utilities.md#health-checks). It runs `Database.CanConnectAsync` on a context from its own scope and reports fixed text instead of the driver message on failure. With `AddDbContextMigrations`, `resources verify` lists no pending migration. The event tiers have no startup signal: a save that emits an event for an unregistered tier throws `InvalidOperationException` naming the registration to add.

#### Resilient Transactions

```csharp
// unitOfWork is the singleton IUnitOfWorkFactory; dbContext is captured from the enclosing scope.

// No-return form
await unitOfWork.RunAsync(
    dbContext,
    async (unit, ct) =>
    {
        dbContext.Products.Add(new Product { Name = "Widget" });
        await dbContext.SaveChangesAsync(ct);
    },
    cancellationToken: ct
);

// Return-value form
var productId = await unitOfWork.RunAsync(
    dbContext,
    async (unit, ct) =>
    {
        var product = new Product { Name = "Widget" };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(ct);
        return product.Id;
    },
    cancellationToken: ct
);
```

`RunAsync(db, …)` begins a unit of work on the context inside the execution strategy and passes it to the operation as `unit`. Work enlisted on `unit` (outbox rows, job writes, `OnCompleted` registrations) commits with the entity rows and runs or sends after commit. It works the same for a `HeadlessDbContext`, a `HeadlessIdentityDbContext`, and a plain `DbContext`, and `RunAsync(connection, …)` covers `SqlConnection` and `NpgsqlConnection`.

`RunAsync(db, …)` on a context that already carries a live unit joins that unit, so a service can wrap itself and still compose under a caller's transaction. Code that receives only the context reads the unit with `db.UnitOfWork()`. A raw-ADO helper that receives `db.Database.GetDbConnection()` joins the same unit through `RunAsync(connection, …)`. Replay rules are in [Retry and replay](#retry-and-replay); the full contract is in [Unit of Work](unit-of-work.md).

**Verify:** in an integration test, throw inside the operation after `SaveChangesAsync` and assert that the row is absent afterward.

### Configuration

#### Global Filters

```csharp
// Read soft-deleted entities for admin purposes
var all = await dbContext.Products.IgnoreNotDeletedFilter().ToListAsync(ct);

// Restore a soft-deleted entity: the filter hides it, so bypass it to load the row
var product = await dbContext.Products.IgnoreNotDeletedFilter().FirstByIdAsync(id, ct);
product.Restore(now, byId);
await dbContext.SaveChangesAsync(ct);

// Project a single row, with the same not-found failure as FirstByIdAsync
var view = await dbContext.Products
    .Where(x => x.Id == id)
    .Select(x => new ProductView { Id = x.Id, Name = x.Name })
    .FirstOrNotFoundAsync(nameof(Product), id, ct);

// Read across tenants (host/admin path only)
var allTenants = await dbContext.Products.IgnoreMultiTenancyFilter().IgnoreNotDeletedFilter().ToListAsync(ct);
```

[Global query filters](#global-query-filters) lists the filters and their bypass rules.

#### Automatic Audit Policy

Configure audit capture on entity and property metadata in `OnModelCreating` or an `IEntityTypeConfiguration<TEntity>`:

```csharp
modelBuilder.Entity<Patient>(patient =>
{
    patient.IsAudited();
    patient.Property(x => x.NationalId).IsAuditSensitive();
    patient.Property(x => x.CreditCardToken).IsAuditSensitive(SensitiveDataStrategy.Exclude);
    patient.Property(x => x.LastComputedAt).ExcludeFromAudit();
});

modelBuilder.Entity<InternalJob>().ExcludeFromAudit();
```

- Entity policy has three states: explicit `IsAudited()` or `ExcludeFromAudit()` wins, and an unconfigured entity follows `AuditLogOptions.AuditByDefault`. Owned entries inherit eligibility from their root owner. Derived types inherit the nearest configured base policy unless they override it.
- Property precedence: framework default exclusions, then explicit `ExcludeFromAudit()`, then the `PropertyFilter` veto, then sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides the global `SensitiveDataStrategy`. `IsEnabled` is the master switch, and `EntityFilter` is the final entity veto.
- The raw PostgreSQL and SQL Server audit packages are storage providers for this capture. They do not define a second policy.

**Verify:** read the resolved policy from the built model in a test, with the options the host runs with:

```csharp
var options = host.Services.GetRequiredService<IOptions<AuditLogOptions>>().Value;
var policies = db.Model.GetAuditPolicies(options);

policies.Where(p => !p.EntityType.IsOwned() && !p.IsAudited).Should().BeEmpty();
policies.SelectMany(p => p.Properties).Where(p => p.Property.Name.Contains("NationalId") && !p.IsSensitive).Should().BeEmpty();
```

`AuditEntityPolicy.IsAudited` is the effective answer. `ConfiguredIsAudited` is `null` for an entity no `IsAudited()` or `ExcludeFromAudit()` call covers, so a test can require an explicit decision for every entity. `AuditPropertyPolicy` reports `IsSensitive`, the effective `SensitiveDataStrategy`, and `IsCaptured`. An owned type reports the root owner its metadata names (`PolicyEntityType`). At save time an owned entry follows its runtime owner, so a derived owner's override shows only on the derived owner's own policy.

#### Tenant Write Guard

Off by default. `TenantGuardOptions.GuardWrites` is read-only; turn the guard on through the tenancy builder:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
```

- With the guard on, a missing tenant is stamped from `ICurrentTenant.Id` when the entry becomes `Added`, including a detached-to-Added transition. A supplied tenant value is kept. Adding under tenant A and saving under tenant B fails instead of changing ownership.
- A missing ambient tenant, or a missing required tenant on an `Added` entry, throws `MissingTenantContextException`. A cross-tenant write, or a mismatch between an existing row's original and current tenant, throws `CrossTenantWriteException`. This covers updates, physical deletes, soft deletes, and changes inside supported owned graphs. A required tenant key can fail during tracking, before `SaveChanges`.
- Entities owned only through metadata (`IsTenantOwned()` with no `IMultiTenant`) are stamped only under the guard. Without it, assign the tenant property before tracking. Without the guard, save-time stamping covers only `IMultiTenant` entities.
- Guard errors raise before domain-event handlers run. A SQL concurrency failure can raise after handlers ran, because the business save follows dispatch. A failed save prevents the outbox rows from persisting, but does not undo local or external handler effects.

For an intentional host or admin write, use `ITenantWriteGuardBypass.BeginBypass()`:

```csharp
var bypass = serviceProvider.GetRequiredService<ITenantWriteGuardBypass>();

using (bypass.BeginBypass())
{
    await dbContext.SaveChangesAsync(ct);
}
```

`BeginBypass()` turns off only the in-memory guard. SQL tenant predicates, required columns, and database constraints still apply, and the bypass does not supply a valid original tenant value.

The tenant property of every tenant-owned root is a concurrency token alongside your own tokens, with or without the guard. Generated updates and deletes match the original tenant, detached writes included, so a forged tenant that matches the ambient one cannot target another tenant's row. When SQL affects zero rows, EF throws `DbUpdateConcurrencyException` unchanged.

**Verify:** in a test with the guard on and no current tenant, saving a new tenant-owned entity throws `MissingTenantContextException`.

#### Tenant Read Guard

Off by default and independent of the write guard. `TenantGuardOptions.GuardReads` is read-only:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantReads()));
```

- With the guard on, a query over an entity with a required tenant column throws `MissingTenantContextException` at execution when `ICurrentTenant.Id` is null or white space, instead of returning no rows. This covers materializing queries, `ExecuteUpdate`, `ExecuteDelete`, `Include`, and navigation loads. The exception reaches you unwrapped.
- Entities with a nullable tenant column are not guarded and keep returning host rows. Among framework entities, only tenant-owned Identity is affected.
- `IgnoreMultiTenancyFilter()` lifts the guard for that query. `ITenantWriteGuardBypass.BeginBypass()` does not. Raw SQL and `FindAsync` hits on tracked entities run no filter, so the guard does not apply to them.
- A later registration that resolves the options to disabled fails host startup with `HEADLESS_TENANCY_EF_READ_GUARD_DISABLED`. See [EF Tenant Read Guard](multi-tenancy/entity-framework.md#ef-tenant-read-guard).

**Verify:** in a test with the guard on and no current tenant, a query over a required-tenant entity throws `MissingTenantContextException`.

#### Tenant ownership

Declare ownership after the base model call. The declarations take effect at model finalization, after your mappings:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
	base.OnModelCreating(modelBuilder);
	var row = modelBuilder.Entity<TenantRow>();
	row.IsTenantOwned(nameof(TenantRow.Owner));
	row.Property(x => x.Owner).HasColumnName("tenant_key").HasMaxLength(41);
	row.Property(x => x.Code).HasMaxLength(100);
	row.HasIndex(x => x.Code).IsUnique().HasDatabaseName("TenantCodeIndex").IsTenantScoped();

	modelBuilder.Entity<ShadowTenantRow>().IsTenantOwned();
	modelBuilder.Entity<ShadowTenantRow>().Property<string>("TenantId").HasMaxLength(41);
}
```

`TenantRow` has `Id`, `Owner`, and `Code` properties. `ShadowTenantRow` has an `Id` and no tenant CLR member. Neither needs `IMultiTenant`.

- `IsTenantOwned(propertyName = "TenantId")` and `IsNotTenantOwned()` work on generic and non-generic entity builders. Explicit root configuration overrides interface defaults. The metadata readers `IsTenantOwned()`, `GetTenantPropertyName()`, and `GetTenantOwnerEntityType()` return the resolved policy.
- Roots owned only through metadata require a tenant. `IMultiTenant` keeps its existing nullability outside tenant-owned Identity, so nullable host rows remain.
- Configure ownership on the hierarchy root. Shared-row and JSON owned graphs inherit it and cannot declare their own.
- Supported roots are keyed, non-shared CLR types mapped to one table, with TPH for inheritance. Model finalization rejects keyless or shared CLR types, TPT and TPC, table fragments, separate roots sharing a table, and separately stored owned entities. The tenant property must be a `string` and must not be database-generated.
- `IsTenantScoped()` adds the tenant column once, ascending, to one selected unique index. It keeps the index's property order, directions, names, filters, uniqueness, and supported annotations. Other indexes, primary keys, and relationships stay unchanged. An unsupported positional provider annotation fails model validation instead of changing the index.
- You configure tenant column types, conversions, collations, and database validation in your own model and migrations. Headless picks no collation, adds no check constraint, rejects no trailing spaces, and never trims or normalizes tenant IDs. The write guard compares IDs ordinally in memory, while queries, concurrency predicates, unique indexes, and foreign keys use database equality, so make sure distinct IDs stay distinct under that equality.

**Verify:** in a test, `db.Model.FindEntityType(typeof(TenantRow))!.GetTenantPropertyName()` returns `"Owner"`.

#### Tenant schema rollout

You own migrations and tenant assignment. For existing data:

1. Add new tenant columns as nullable.
2. Backfill from verified application ownership. Do not assign a default tenant.
3. Validate tenant identity, equality, lengths, and your ID format. Resolve duplicate business names within each tenant and parent-child tenant mismatches.
4. Apply required columns, storage rules, alternate keys, composite foreign keys, and tenant-scoped unique indexes. Review the generated SQL for your provider before you enable the new model.

Existing `IMultiTenant` columns become concurrency tokens with no change to their types, collations, or constraints.

#### Module Model Mapping

Each feature-storage EF package exposes a `ModelBuilder` extension. Call it in `OnModelCreating` after `base.OnModelCreating(modelBuilder)`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureHeadlessAuditLog(_auditLogStorage.Value);
    modelBuilder.ConfigureHeadlessFeatures(_featuresStorage.Value);
    modelBuilder.ConfigureHeadlessPermissions(_permissionsStorage.Value);
    modelBuilder.ConfigureHeadlessSettings(_settingsStorage.Value);
}
```

Each extension reads `Schema` and the `*TableName` values from its validated `*StorageOptions`.

#### Custom Save Processors

```csharp
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

Custom processors run before the lifecycle-event and message-collector processors. See [Save pipeline and auditing](#save-pipeline-and-auditing).

#### Event Dispatch

[Save pipeline and auditing](#save-pipeline-and-auditing) gives the order inside a save. A custom `IHeadlessOutboxDispatcher` receives `IReadOnlyList<EventContext<object>>` in both of its methods. Keep each snapshot through retries: the Messaging bridge uses `EventId` as `MessageId` and publishes `Payload` with the captured correlation, causation, and tenant.

A completed local drain is not repeated by a persistence retry, but a failed local handler can run again. The outbox commits atomically with your rows; it does not make delivery or external effects exactly-once.

#### Value Converters

```csharp
modelBuilder.Entity<Order>().Property(o => o.Total).HasConversion<MoneyAmountValueConverter>();

// Or apply globally across all matching CLR types:
configurationBuilder.Properties<MoneyAmount>().HaveConversion<MoneyAmountValueConverter>();
```

#### Unique conflicts

A handler that checks that no other row holds a value can still lose to a concurrent writer that takes the value before the save. The unique index decides the race. Without a mapping, the Headless API exception handler answers the violation with the generic 409 `g:unique_violation`. To return your own error, save through `SaveChangesOrConflictAsync` (namespace `Microsoft.EntityFrameworkCore`) and name the index:

```csharp
// The model names the index, so the database reports the same name.
modelBuilder.Entity<Member>().HasIndex(x => x.Email).IsUnique().HasDatabaseName("IX_Members_Email");

// In the handler: the existence check gives the common case a clean error, the save covers the race.
if (await db.Members.AnyAsync(x => x.Email == command.Email, ct))
{
    throw new ConflictException(MemberErrors.EmailTaken());
}

db.Members.Add(member);
await db.SaveChangesOrConflictAsync("IX_Members_Email", MemberErrors.EmailTaken(), ct);
```

- A violation of the named constraint or unique index throws `ConflictException` with your descriptor, and the provider failure as `InnerException`. The Headless API exception handler returns it as 409 with your `errors[].code`.
- Pass an `IReadOnlyDictionary<string, ErrorDescriptor>` to map several names in one save. The dictionary's comparer matches the reported name; use `StringComparer.Ordinal`.
- A violation of any other constraint, and every other failure, propagates unchanged, so the generic 409 still answers it.
- Name the constraint as the database holds it: `HasDatabaseName(...)` for an index, `HasName(...)` for a key or alternate key. EF's default names (`IX_<Table>_<Column>`, `AK_...`, `PK_...`) work too, but a naming convention such as snake_case rewrites them.
- The constraint name comes from `RelationalFaults.IsUniqueViolation(exception, out var constraintName)` in `Headless.Extensions`, which you can call from your own catch filter. PostgreSQL reports the name in its own field. SQL Server reports it only in the message text, so the name is read from the English message (errors `2627` and `2601`). Under a session or login language that localizes the message (`SET LANGUAGE`, or a non-English default language), no name is read: the mapping does not apply, and the violation propagates as the generic 409. SQLite reports no name, so the mapping never applies there.

**Verify:** in an integration test against your engine, insert a row, add a second row with the same value, and assert that `SaveChangesOrConflictAsync` throws `ConflictException` whose `Errors[0].Code` is your code.

### Design and runtime behavior

- `IHeadlessDbContext` is implemented by `HeadlessDbContext` and `HeadlessIdentityDbContext`.
- Registers `IHeadlessSaveChangesPipeline` and the default processor chain (`HeadlessEntitySaveEntryProcessor`, `HeadlessAuditSaveEntryProcessor`, `HeadlessLocalEventSaveEntryProcessor`, `HeadlessMessageCollectorSaveEntryProcessor`).
- Registers `IDbContextFactory<TDbContext>` as a singleton. Under `AddHeadlessDbContext`, each factory context gets its own DI scope, disposed with it. Under `AddHeadlessDbContextPool`, a factory context opens a private scope only when it first needs a scoped service.
- Attaches every DI-registered `IInterceptor` to the context options, for `AddHeadlessDbContext` and for your own `AddDbContext`. Interceptors resolve from the provider the options are built from: scoped options (the `AddHeadlessDbContext` default) take a scoped interceptor from their scope, while singleton or pooled options resolve from the root, where a scoped interceptor fails scope validation.
- Registers the singleton `IUnitOfWorkFactory` (through `Headless.UnitOfWork.EntityFramework`).
- `.AddDomainEvents()` registers `IDomainEventDispatcher`. `.AddIntegrationEventOutbox()` registers `IHeadlessOutboxDispatcher`.
- Registers `TenantGuardOptions` and `ITenantWriteGuardBypass` always, with both guards off. The bypass registration is `TryAddSingleton`, shared with `AddHeadlessTenancy`, so your earlier registration wins.
- Registers with `TryAddSingleton`, so your own registration wins: `TimeProvider.System`; keyed `IGuidGenerator` strategies (`Version7` and `SqlServer`) plus an unkeyed `Version7` default; `ICurrentTenantAccessor`; `ICurrentUser` as `NullCurrentUser`, which `AddHeadless()` replaces with the HTTP user in either call order; and `ICorrelationIdProvider`.
- Registers `ICurrentTenant` as `CurrentTenant`, replacing only the framework fallback `NullCurrentTenant` and keeping your own implementation.
- Registers `IAmbientDbTransactionAccessor` and `IAuditChangeCapture`, which reads the audit policy from the finalized EF model.
- One compiled query plan serves every tenant; the tenant filter passes the current tenant as a query parameter.

---

## Headless.EntityFramework.Messaging

Supplies `IHeadlessOutboxDispatcher`, so integration events from EF entities are written to the messaging outbox in the save transaction.

### Setup

```bash
dotnet add package Headless.EntityFramework.Messaging
```

```csharp
// Chain after AddHeadlessDbContextServices:
builder
    .Services.AddHeadlessDbContextServices()
    .AddDomainEvents() // IDomainEventDispatcher for in-process domain events
    .AddIntegrationEventOutbox(); // IHeadlessOutboxDispatcher — this package

// A messaging setup with an outbox storage provider is required:
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory(); // broker
    setup.UsePostgreSql(connectionString); // outbox storage
});
```

`.AddIntegrationEventOutbox()` takes no parameters, and the dispatcher has no options. Configure broker, storage, and retries on `AddHeadlessMessaging`; see [Messaging](messaging.md).

**Verify:** in an integration test, save an entity that emits an integration event. Without this registration the save throws `InvalidOperationException` naming `.AddIntegrationEventOutbox()`. With it, the save succeeds and the message reaches a consumer of that event type after commit.

### Configuration

None. Configure messaging through `AddHeadlessMessaging`.

### Design and runtime behavior

- Registers `IHeadlessOutboxDispatcher` as scoped with `TryAdd`. The dispatcher is stateless and publishes through the `Outbox` of the unit the save pipeline passes to it. It throws before publishing when that unit has no resource. See [Outbox-within-save-transaction bridge](#outbox-within-save-transaction-bridge).
- Keeps each `EventContext<object>`: `EventId` becomes the Messaging `MessageId`, and correlation, immediate causation, and tenant stay the values captured at emission. Each durable publish sets `SuppressAmbientBusinessContext = true`, so consume or tenant state at save time cannot replace a captured null root cause or system tenant. `TenantContextRequired = true` still rejects a captured null tenant.
- Publishes each concrete payload through Messaging's contract name and version resolver. Handlers that derive new facts get new event IDs with the domain parent as causation; forwarding an existing event keeps its ID.
- Sends a save's integration events in one batch: on PostgreSQL and SQL Server the rows go to the database as one batch of insert statements (split under SQL Server's parameter limit), in event order, while the save's transaction holds its locks. A store failure surfaces from `SaveChanges` as the driver exception, before commit. SQLite, and any provider without `DbBatch`, sends one statement per row.
- Publish middleware turns batching off. A publish that middleware may wrap is stored on its own. Any Bus-lane publish middleware registered through the messaging builder applies to every message type, so it turns batching off for the whole save. Messaging tenant propagation (`tenancy.Messaging(m => m.PropagateTenant())`) registers publish middleware on both lanes, so it turns batching off for every event.
- Retry inside a pipeline-owned save reuses the captured IDs and the completed local drain. [Retry and replay](#retry-and-replay) covers recovery after rollback or an in-doubt commit.

---

## Headless.Couchbase

Document access through bucket contexts, Couchbase Transactions, and scope, collection, and index management.

### Setup

```bash
dotnet add package Headless.Couchbase
```

```csharp
// Supply the two application-specific providers (or register the shipped defaults):
services.AddSingleton<ICouchbaseClusterOptionsProvider, MyClusterOptionsProvider>();
services.AddSingleton<ICouchbaseTransactionConfigProvider, MyTransactionConfigProvider>();

// Register the framework-owned providers in one call:
services.AddHeadlessCouchbase();
```

```csharp
using Couchbase;
using Couchbase.Linq;
using Couchbase.Client.Transactions;

// Define a typed bucket context; the provider wires each attributed IDocumentSet<T> property to its collection
public sealed class AppBucketContext(
    IBucket bucket,
    Transactions transactions,
    ILogger<CouchbaseBucketContext> logger
) : CouchbaseBucketContext(bucket, transactions, logger)
{
    [CouchbaseCollection("_default", "products")]
    public IDocumentSet<Product> Products { get; set; } = null!;
}

// Resolve context via the provider (typically injected via ICouchbaseClustersProvider + IBucketContextProvider)
var context = await bucketContextProvider.GetAsync<AppBucketContext>(
    clusterKey: "default",
    bucketName: "app",
    defaultScopeName: null // use each document set's declared scope
);

// KV operations via DocumentSetExtensions
var product = await context.Products.GetAsync<Product, string>("product-123");
await context.Products.UpsertAsync(product);

// N1QL query
var results = context.Query<Product>("_default", "products")
    .Where(p => p.IsActive)
    .ToList();

// Couchbase Transaction — returning commits, throwing rolls back
await context.ExecuteTransactionAsync(async attempt =>
{
    // perform transactional KV operations
});
```

Bootstrap scopes, collections, and indexes with `ICouchbaseManager` from a deploy step, such as an `IResourceProvisioner` that `resources provision` runs. Its operations are idempotent.

**Verify:** invalid `CouchbaseManagerOptions` fail host startup. Connection has no startup signal: a cluster that is not ready logs `Failed to connect to the cluster {ClusterKey}` on first access, and the operation then fails at call time. Prove the wiring with a first `IBucketContextProvider.GetAsync` call and a document read in an integration test.

### Configuration

- Implement and register `ICouchbaseClusterOptionsProvider` to supply cluster options (connection string, credentials) per cluster key.
- Implement and register `ICouchbaseTransactionConfigProvider` to supply transaction configuration per cluster key.
- Tune the manager's retry policy with `CouchbaseManagerOptions` through the `AddHeadlessCouchbase(IConfiguration)`, `AddHeadlessCouchbase(Action<CouchbaseManagerOptions>)`, or `AddHeadlessCouchbase(Action<CouchbaseManagerOptions, IServiceProvider>)` overload:

| Option | Default | Validation |
| --- | --- | --- |
| `MaxRetries` | 3 | Greater than 0 |
| `RetryDelay` | 500 ms | Zero or more |
| `Timeout` | 10 s | Over 10 ms and under 24 h |

### Design and runtime behavior

- `AddHeadlessCouchbase()` registers `ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, and `ICouchbaseAssemblyCollectionsReader` as singletons with `TryAdd`, so your own registration wins. It does not register `ICouchbaseClusterOptionsProvider` or `ICouchbaseTransactionConfigProvider`.
- `CouchbaseBucketContext` exposes `Query<T>(scope, collection)` for N1QL and `ExecuteTransactionAsync(Func<AttemptContext, Task>)` for Couchbase Transactions. `IBucketContextProvider` resolves a typed context per cluster key, bucket name, and default scope.
- `DocumentSetExtensions` cover key-value operations (`GetAsync`, `ExistsAsync`, `UpsertAsync`, `InsertAsync`, `ReplaceAsync`, `RemoveAsync`, `UnlockAsync`, `TouchAsync`, `GetAndLockAsync`, `GetAnyReplicaAsync`, `GetAllReplicasAsync`, `LookupInAsync`, `MutateInAsync`, `ScanAsync`) on `IEntity` models, keyed by `IEntity.GetKey()`.
- `ICouchbaseManager` provides `CreateScopeAsync`, `CreateCollectionsAsync`, `CreateSecondaryIndexAsync`, and `BuildDeferredIndexesAsync`. It caches scope and collection specs per cluster key and bucket name in memory, and clears the cache when it creates a scope. `CouchbaseEventingFunctionsSeeder` seeds eventing functions from embedded resources.
- `ICouchbaseClustersProvider.GetClusterAsync` returns a `CouchbaseClusterConnection` (cluster and transaction manager). Each cluster connects lazily on first access and is cached per provider instance (one per container) by cluster key. First access waits up to 1 minute for readiness; a readiness failure is logged and does not throw.
- The manager's retry back-off, its `Timeout`, and its pause between creating a collection and its primary index use the system clock. A test host with a `FakeTimeProvider` still retries and times out in real time.
- `ExecuteTransactionAsync` commits when the operation returns and rolls back when it throws. The SDK reruns the whole operation on transient conflicts, so keep side effects outside the transaction out of it. It logs `Information` on success and `Error` on failure. The transaction types come from `CouchbaseNetClient`, namespace `Couchbase.Client.Transactions`.

### Cancellation (Couchbase)

- `ICouchbaseClusterOptionsProvider.GetAsync`, `ICouchbaseTransactionConfigProvider.GetAsync`, `ICouchbaseClustersProvider.GetClusterAsync`, `IBucketContextProvider.GetAsync`, and `CouchbaseBucketContext.ExecuteTransactionAsync` accept an optional trailing `CancellationToken`.
- `DocumentSetExtensions.GetAllReplicasAsync` returns an `IAsyncEnumerable<IGetReplicaResult>` that yields each replica's result as it answers, the active node included. Read it with `await foreach`, not by collecting tasks. Pass the token through `GetAllReplicasOptions.CancellationToken(...)`.
- The token passed to `GetClusterAsync` governs only the attempt that first connects a cluster. A caller that gets a cached cluster completes without observing it.
- `IBucketContextProvider.GetAsync` observes the token before it opens the bucket, not during, because `ICluster.BucketAsync` has no cancellation overload.
- `ExecuteTransactionAsync` observes the token only before the transaction begins, because the SDK's `Transactions.RunAsync` has no cancellation hook. Bound the transaction's duration with `PerTransactionConfig` (timeout and durability) instead.
