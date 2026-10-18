---
domain: Feature Management
packages: Features.Abstractions, Features, Features.Storage.EntityFramework, Features.Storage.PostgreSql, Features.Storage.SqlServer
---

# Feature Management

> Dynamic feature flags and feature value management with hierarchical resolution (Tenant > Edition > Default), caching, and database persistence via EF Core, PostgreSQL, or SQL Server.

## Orientation

Install `Headless.Features.Abstractions` plus `Headless.Features` and exactly one storage provider:

- `Headless.Features.Abstractions` — interfaces (`IFeatureManager`, `IFeatureDefinitionProvider`, `IFeatureDefinitionManager`)
- `Headless.Features` — full implementation with caching, value providers, and the definitions seeder
- `Headless.Features.Storage.EntityFramework` — EF Core persistence using the consumer's `DbContext`
- `Headless.Features.Storage.PostgreSql` — raw ADO.NET persistence for PostgreSQL (no EF dependency)
- `Headless.Features.Storage.SqlServer` — raw ADO.NET persistence for SQL Server (no EF dependency)

Feature gates on HTTP endpoints are ASP.NET Core authorization requirements (see [Gating HTTP endpoints](#gating-http-endpoints)), so `AddHeadlessFeatures` alone enforces them. `Headless.Features.Abstractions` references no ASP.NET Core package; `Headless.Features`, which owns the gate types, references only the host-agnostic `Microsoft.AspNetCore.Authorization` package, so console and worker hosts run every package above without the ASP.NET runtime.

Typical registration:

```csharp
// Required dependencies
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// 1. Register feature definitions
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// 2. Register the management core + storage in one call
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

`AddHeadlessFeatures` requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered before it is called.

## Agent Rules

- Inject `IFeatureManager` to read or write feature values. Do NOT use `Microsoft.FeatureManagement` — this is a separate system with a different model.
- Define features by implementing `IFeatureDefinitionProvider` and calling `context.AddGroup()` / `group.AddChild()`. Register the provider with `services.AddFeatureDefinitionProvider<T>()`.
- Value resolution order is Tenant > Edition > Default. When no provider is specified, `GetAsync` walks providers from highest to lowest priority and returns the first non-null value.
- `AddHeadlessFeatures(configure)` is the single entry point — it registers the management core automatically alongside the selected storage provider. Only one storage provider (EF / PostgreSQL / SqlServer) can be registered per application.
- To tune management options, call `setup.ConfigureManagement(options => ...)` inside the `AddHeadlessFeatures` block. An `(options, IServiceProvider)` overload is available for late-bound configuration. `services.Configure<FeatureManagementOptions>(...)` also works and composes regardless of call order.
- To tune storage options (schema, table names), call `setup.ConfigureStorage(o => ...)` inside the `AddHeadlessFeatures` block. The `IConfiguration` overload binds the `Headless:Features:Storage` section instead.
- For EF storage: register a singleton `IDbContextFactory<TContext>` (`AddDbContextFactory<TContext>()` or `AddPooledDbContextFactory<TContext>()` for a plain `DbContext`, or `AddHeadlessDbContext<TContext>()` or `AddHeadlessDbContextPool<TContext>()` for a `HeadlessDbContext`; both Headless registrations add the factory) and call `modelBuilder.ConfigureHeadlessFeatures(this)` in `OnModelCreating` before calling `setup.UseEntityFramework<TContext>()`. The factory must be a singleton (the default lifetime of all four registrations): the EF repositories are singletons that would capture a scoped or transient factory for the life of the host, so startup refuses one with `InvalidServiceLifetimeException`.
- The `FeatureDefinitions` seeder writes static definitions to the database, and only when the host seeds — do NOT call `IDynamicFeatureDefinitionStore.SaveAsync` directly. Seeding is opt-in: see [Definition Seeding](#definition-seeding).
- Feature value caching is automatic. Both `IFeatureManager` writes and direct `IFeatureValueRecordRepository` writes invalidate the affected cache entry (the repository removes it after `SaveChangesAsync`), and a distributed cache propagates the eviction across nodes. Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale.
- Custom value providers must implement `IFeatureValueReadProvider` (read-only) or `IFeatureValueProvider` (read-write). Register with `services.AddFeatureValueProvider<T>()`. The last-registered provider has the highest resolution priority. Every multi-name read (`GetAllAsync(featureNames)`, `GetAllAsync(providerName, …)`, the multi-name `IsEnabledAsync` / `EnsureEnabledAsync`) asks each provider once with the whole pending batch through `IFeatureValueReadProvider.GetAllAsync`, so a custom read provider must implement it or its features silently disappear from bulk results. `FeatureManager` writes through `IFeatureValueProvider.SetAllAsync`; its default implementation calls `SetAsync` / `ClearAsync` once per entry, so a custom provider overrides it when its source can apply a batch atomically.
- `FeatureDefinition.Providers` restricts which providers can read/write a feature. An empty list means all providers are allowed — the most common case.
- Gate HTTP access with `[RequiresFeature("FeatureName")]` on controllers, actions, or Minimal API handlers, with `.RequireAuthorization(policy => policy.RequireFeatures("FeatureName"))` on endpoints or route groups, or with `.WithMetadata(new RequiresFeatureAttribute("FeatureName"))`. The attribute is authorization data that ASP.NET Core's authorization middleware enforces through the handler `AddHeadlessFeatures` registers; no other registration exists. Use `[DisableFeatureCheck]` or `.WithMetadata(new DisableFeatureCheckAttribute())` to bypass a controller-level or group-level gate. A disabled feature fails authorization like a missing permission: 403, or 401 for an anonymous caller, with `Headless.Api` putting the localized "feature currently unavailable" text in the 403 `detail`; see [Gating HTTP endpoints](#gating-http-endpoints).
- `SetAsync` with `forceToSet: false` (default) skips the write when the supplied value equals the fallback value of the next lower-priority provider. Set `forceToSet: true` when you must persist the value explicitly (e.g., `GrantAsync`/`RevokeAsync` always use `forceToSet: true`).
- To write several features at once, call `SetAsync(values, providerName, providerKey)` with an `IReadOnlyDictionary<string, string?>` keyed by feature name; a `null` value clears that feature. It checks every name, the provider, and its writability before writing anything, so an undefined name or a read-only provider rejects the whole batch with `ConflictException` and changes nothing. The built-in stores (EF, PostgreSQL, SQL Server) then write the batch in one transaction, so a failed write leaves every value as it was, and a successful one publishes a single `FeatureChangedMessage` listing every name. `forceToSet` applies to each value as it does for the single-name call. An empty dictionary writes and announces nothing. The single-name `SetAsync` is the one-entry case of this call. Clearing a value removes only the row stored under the exact provider key the provider resolves, not that feature under every key of the provider. Atomicity holds per provider: when several registered providers share `providerName`, each writes the batch in its own transaction. The store-backed providers open their own connection and transaction, so the write does not join a unit of work the caller has open, and rolling that unit back does not undo it. When a concurrent writer inserts or deletes one of the batch's rows between the store's read and its save, the store reads again and retries (up to three attempts); the last writer's value wins.
- `DeleteAsync` removes all feature values for a given provider and key (e.g., all tenant overrides for a deleted tenant). It silently skips read-only providers.
- The raw-DDL providers (PostgreSQL / SqlServer) never write DDL when the host starts: they only verify the schema and fail fast when a step is missing. Create the tables by running `resources setup` at deploy time, also when a migrations job owns the schema, because every step is idempotent; see [Stateful resources](utilities.md#stateful-resources). EF storage uses migrations.

## Core Concepts

### Feature Definitions vs. Feature Values

A *feature definition* describes a feature's metadata: its name, default value, visibility, and which value providers are allowed to supply a value. Definitions are registered statically at startup via `IFeatureDefinitionProvider` and optionally persisted to the database by the `FeatureDefinitions` seeder. A *feature value* is the resolved runtime state of a feature for a specific subject (e.g., a specific tenant ID or edition ID). Definitions live in `IStaticFeatureDefinitionStore` / `IDynamicFeatureDefinitionStore`; values live in `IFeatureValueStore` backed by `IFeatureValueRecordRepository`.

### Value Providers and Resolution Order

Feature values are resolved by a chain of `IFeatureValueReadProvider` implementations registered in priority order. The three built-in providers, from lowest to highest priority, are: `DefaultValue` (reads from `FeatureDefinition.DefaultValue`), `Edition`, and `Tenant`. `IFeatureManager.GetAsync` walks the chain from highest priority to lowest and returns the first non-null result. The registration order in the DI container controls precedence: the last-added provider has the highest priority. Custom providers added via `services.AddFeatureValueProvider<T>()` are appended after `Tenant` and thus have the highest priority of all.

### Static Store vs. Dynamic Store

The *static store* (`IStaticFeatureDefinitionStore`) builds the feature catalog once at startup by invoking all registered `IFeatureDefinitionProvider` implementations. It is thread-safe and lazily initialized. The *dynamic store* (`IDynamicFeatureDefinitionStore`) reads feature definitions from the database, caches them in-process with a configurable expiry (`DynamicDefinitionsMemoryCacheExpiration`, default 30 seconds), and coordinates cross-instance refreshes via a distributed cache stamp and a distributed lock. The dynamic store is disabled by default (`IsDynamicFeatureStoreEnabled = false`); enable it only when feature definitions must be edited at runtime without redeployment. When it is enabled, the `FeatureDefinitions` [warm-up](utilities.md#warm-up) loads it through `IDynamicFeatureDefinitionStore.GetGroupsAsync` after the host starts, so the first request does not wait on the database; the `warmup` readiness check reports `Unhealthy` until that load succeeds, and a failed load is retried in the background. With the dynamic store disabled, the warm-up does nothing and does not hold readiness.

### Feature Value Caching

`FeatureValueStore` caches resolved feature values to avoid repeated database reads. The cache is backed by the registered `ICache` (or a named cache instance when `FeatureManagementOptions.FeatureValueCacheName` is set). When `IFeatureManager.SetAsync` writes a value, `FeatureValueStore` updates or evicts the affected cache entries directly through `ICache` (a distributed cache propagates the eviction across nodes via `CacheInvalidationMessage`). Direct `IFeatureValueRecordRepository` writes also evict the affected cache entry — the repository removes it after `SaveChangesAsync` — so a repository-level write bypassing the manager is still reflected on the next read. Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale.

### Reacting to a change

`FeatureManager` publishes one `FeatureChangedMessage` over `IBus` after each successful `SetAsync` or `DeleteAsync`, listing every name that call changed, so an instance holding a resolved value learns it is stale instead of polling for it. The state it refreshes lives in each process, so consume it with an [every-instance consumer](messaging.md#every-instance-bus-delivery): every process receives every announcement instead of one replica taking the only copy. Delivery is at most once, so reload after a gap in the subscription too. Wire contract: `AddHeadlessFeatures` declares the message as `headless.features.changed`, contract version `1` (`FeatureChangedMessage.MessageName`), so the consumer below needs no `Message<T>` declaration of its own. The host's naming conventions (`UseConventions`) do not rename it; only the host-wide `MessagingOptions.MessageNamePrefix` applies, as it does to every message.

```csharp
[BusConsumer("app.reload-tenant-features", EveryInstance = true)]
public sealed class ReloadTenantFeatures(TenantFeatureCache cache) : IConsume<FeatureChangedMessage>, IOnSubscriptionEstablished
{
    public async ValueTask ConsumeAsync(ConsumeContext<FeatureChangedMessage> context, CancellationToken ct)
    {
        var message = context.Message;

        // Scope matters: another tenant's or edition's write must not invalidate this tenant's snapshot. Its own
        // edition's write must: a feature the tenant does not override resolves from the edition.
        var concernsThisTenant = message.ProviderName switch
        {
            FeatureValueProviderNames.Tenant =>
                string.Equals(message.ProviderKey, cache.TenantId, StringComparison.Ordinal),
            FeatureValueProviderNames.Edition =>
                string.Equals(message.ProviderKey, cache.EditionId, StringComparison.Ordinal),
            _ => false,
        };

        if (!concernsThisTenant)
        {
            return;
        }

        if (message.FeatureNames.Any(cache.Tracks))
        {
            await cache.ReloadAsync(ct);
        }
    }

    // Announcements published while this process was not subscribed never arrive. That includes the first
    // establishment: anything loaded at startup was read before the subscription went live.
    public async ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken ct)
    {
        await cache.ReloadAsync(ct);
    }
}
```

The message carries feature names and the scope where they changed, never values. Values would increase broker payloads and make delivery order load-bearing, so the message is only a re-read trigger. Re-reading is idempotent and order-free. `OriginHostName` names the host that wrote the change (`IHostIdentityAccessor.HostName`) for logs and telemetry. Do not filter on it. The writing process holds copies too, since the manager knows nothing about a value that a consumer copied into a field, so the origin must re-read like every other instance.

`IBus` is optional. A host that never calls `AddHeadlessMessaging` writes feature values exactly as before and publishes nothing. A failed publish is logged and never fails the write that already succeeded. In both cases, a peer keeps its copy until it re-reads for its own reasons, so a consumer that must converge without a bus still needs a periodic refresh.

The announcement follows a committed write. A feature write never joins a unit of work the caller has open: the EF store saves through a fresh context from `IDbContextFactory<TContext>`, and the PostgreSQL and SQL Server stores open their own connection and transaction. `SetAsync` commits before it returns, even inside a caller's `RunAsync(db, …)`, and the message goes out after that commit, so a peer that re-reads on it loads the new value. The write is not atomic with the caller's other writes: a `SetAsync` inside a unit that later rolls back leaves the feature value changed.

This signal is separate from cache coherence, which `FeatureValueStore` already handles. A store-backed write updates or evicts its cache entry, and a distributed cache propagates the change through `CacheInvalidationMessage`. `FeatureChangedMessage` covers state that the framework cannot see, such as a value that a consumer copied into a field.

The two messages travel on separate subscriptions, and nothing orders them. With a hybrid cache, a peer can receive `FeatureChangedMessage` before its own `CacheInvalidationMessage`, re-read, and get the old value from its local tier. A consumer that re-reads once on the announcement then stays stale until its next refresh. Re-read again a few seconds later when the first read shows no change, or rely on a periodic refresh.

### Definition Seeding

`AddHeadlessFeatures` registers an `ISeeder` named `FeatureDefinitions`. When `SaveStaticFeaturesToDatabase` is true, it saves the static feature definitions through `IDynamicFeatureDefinitionStore.SaveAsync` (idempotent and guarded by a distributed lock), with up to 10 jittered exponential-back-off retries capped at 30 seconds. Cancellation, `ArgumentException`, and `NotSupportedException` fail immediately without retry; any other failure surfaces from the seed run after the last retry. The back-off waits run on the system clock, not the registered `TimeProvider`, so a host that registers a `FakeTimeProvider` still retries in real time.

The host start does not seed. In production, run the `seed` verb in the deployment (`dotnet app.dll seed`); in development, call `services.AddHeadlessSeedOnStartup()`. `HeadlessTestServer` seeds by default. See [Database Seeders](utilities.md#database-seeders).

A host that never seeds still serves its static definitions from memory, and the dynamic store loads on its first read. But:

- Definitions listed in `DeletedFeatures` or `DeletedFeatureGroups` are not removed from the database.
- Other applications that share the database keep serving stale definitions, because the common stamp is not bumped.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Features.Storage.EntityFramework` | You already use EF Core and want schema managed via EF migrations | You need to avoid an EF dependency or want zero-overhead ADO.NET | Portable across any EF-supported DB; startup validates that all feature entities are in the EF model before hosted services start |
| `Headless.Features.Storage.PostgreSql` | You use PostgreSQL and want no EF Core dependency | You run SQL Server or need EF migrations for schema management | Creates schema idempotently via raw DDL when the deployment runs `resources setup`; identifier names are validated against PostgreSQL naming rules |
| `Headless.Features.Storage.SqlServer` | You use SQL Server and want no EF Core dependency | You run PostgreSQL or need EF migrations for schema management | Creates schema idempotently via raw DDL when the deployment runs `resources setup`; identifier names are validated against SQL Server naming rules |

---

## Headless.Features.Abstractions

Defines the unified interface for feature management and feature flags across different storage providers.

### API and behavior

- `IFeatureManager` — reads and writes feature values across the registered provider chain; supports single-feature and bulk queries with optional provider targeting and fallback
- `IFeatureDefinitionProvider` — contributes feature groups and feature definitions at startup via `IFeatureDefinitionContext`
- `IFeatureDefinitionManager` — looks up and enumerates all registered feature definitions
- `FeatureDefinition` — describes a feature's name, default value, display metadata, allowed providers, and child features (tree structure); implements `ICanAddChildFeature` for fluent `AddChild(...)`
- `FeatureDefinitionCreateOptions` — initializer-based feature metadata with a required `Name`; optional values remain additive without constructor churn
- `FeatureGroupDefinition` — organizes related `FeatureDefinition` instances; supports `GetFlatFeatures()` for depth-first enumeration; also implements `ICanAddChildFeature`
- `ICanAddChildFeature` — shared fluent contract (`AddChild(...)`) implemented by both `FeatureGroupDefinition` and `FeatureDefinition` so top-level and nested features build the same way (renamed from `ICanCreateChildFeature`)
- `IFeatureDefinitionContext` — passed to each provider's `Define`; exposes `AddGroup(name, displayName)`, `GetGroupOrDefault(name)`, and `RemoveGroup(name)`. Groups are created by name — there is no instance-taking `AddGroup(FeatureGroupDefinition)` overload (the group ctor is internal, so consumers cannot construct one)
- `FeatureValue` — record returned by `GetAsync`/`GetAllAsync` carrying the resolved string value and the `FeatureValueProvider` that supplied it; provider-scoped bulk reads (`GetAllAsync(providerName, …)`, `GetAllForTenantAsync`, `GetAllForEditionAsync`, `GetAllDefaultAsync`) return `IReadOnlyList<FeatureValue>`
- `IFeatureManager.GetAllAsync(IReadOnlySet<string> featureNames)` — resolves a named set through the full provider chain in one call, exactly as `GetAsync(name)` resolves one; returns a `Dictionary<string, FeatureValue>` where a feature with no value maps to a `FeatureValue` whose `Value` is `null`, and undefined names are omitted. Each provider is asked once with the whole pending batch (one cache round-trip per store-backed provider) rather than once per feature.
- `FeatureValueProviderNames` — constants `Tenant`, `Edition`, `DefaultValue` for targeting built-in providers
- Extension methods on `IFeatureManager`: `IsEnabledAsync`, `GetAsync<T>`, `EnsureEnabledAsync`, `GrantAsync`, `RevokeAsync`. The multi-name `IsEnabledAsync(requiresAll, featureNames)` and `EnsureEnabledAsync(requiresAll, featureNames)` resolve every name through one `GetAllAsync(featureNames)` call instead of one provider-chain walk per name; an undefined name still reports through `GetAsync` exactly as the per-name loop did.
- Scoped extension methods: `GetForTenantAsync`, `SetForTenantAsync`, `GrantToTenantAsync`, `RevokeFromTenantAsync`, `DeleteForTenantAsync` (tenant); equivalent `*ForEditionAsync` / `*ToEditionAsync` set (edition); `GetDefaultAsync`, `GetAllDefaultAsync` (default provider)

This package references no ASP.NET Core package. The HTTP gate types (`RequiresFeatureAttribute`, `DisableFeatureCheckAttribute`, `FeatureRequirement`) live in `Headless.Features`, the same split `Headless.Permissions` uses.

### Install

```bash
dotnet add package Headless.Features.Abstractions
```

### Setup and use

```csharp
public sealed class BillingService(IFeatureManager features)
{
    public async Task ProcessAsync(string tenantId, CancellationToken ct)
    {
        // Check a boolean flag (defaults to false when value is absent)
        if (await features.IsEnabledAsync("EnableReports", ct))
        {
            // report logic
        }

        // Read a typed value for a specific tenant
        var maxUsers = await features.GetAsync<int>(
            "MaxUsers",
            providerName: FeatureValueProviderNames.Tenant,
            providerKey: tenantId,
            fallback: true,
            cancellationToken: ct
        );
    }
}

// Shorter form using scoped extension members
public sealed class TenantOnboardingService(IFeatureManager features)
{
    public Task GrantPremiumAsync(string tenantId) => features.GrantToTenantAsync("EnableReports", tenantId);

    public Task RevokeAsync(string tenantId) => features.RevokeFromTenantAsync("EnableReports", tenantId);
}
```

#### Defining Features

```csharp
public sealed class MyFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup("App.Features");

        group.AddChild(new FeatureDefinitionCreateOptions { Name = "MaxUsers", DefaultValue = "10" });
        group.AddChild(new FeatureDefinitionCreateOptions { Name = "EnableReports", DefaultValue = "false" });

        // Nested child features
        var billingFeature = group.AddChild(
            new FeatureDefinitionCreateOptions { Name = "Billing", DefaultValue = "false" }
        );
        billingFeature.AddChild(
            new FeatureDefinitionCreateOptions { Name = "Billing.Invoices", DefaultValue = "false" }
        );
    }
}
```

### Configuration

None. This is an abstractions-only package.

### Runtime behavior

None.

---

## Headless.Features

Core implementation of feature management with caching, value providers, and definition management.

### API and behavior

- `FeatureManager` — full implementation of `IFeatureManager`; walks the registered provider chain, caches results, and coordinates writes with cache invalidation
- `IFeatureValueProvider` / `IFeatureValueReadProvider` — read-write and read-only contracts for custom value providers
- Built-in value providers: `DefaultValueFeatureValueProvider`, `EditionFeatureValueProvider`, `TenantFeatureValueProvider`
- `IStaticFeatureDefinitionStore` — builds the feature catalog lazily and thread-safely from all registered `IFeatureDefinitionProvider` implementations
- `IDynamicFeatureDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination
- `FeatureDefinitions` seeder — saves static definitions when the host seeds, with up to 10 jittered exponential-back-off retries capped at 30 seconds
- `FeatureDefinitions` warm-up — loads the dynamic store into its in-process cache after the start, behind readiness, when `IsDynamicFeatureStoreEnabled` is true
- `FeatureManagementOptions` — tuning options for lock keys, cache expiries, dynamic store toggle, and named cache routing
- `FeaturesStorageOptions` — schema and table name configuration shared across all storage providers
- `RelationalFeaturesOptions` — base of `PostgreSqlFeaturesOptions` and `SqlServerFeaturesOptions` (`ConnectionString`, `CommandTimeout`). Core also holds the one relational value and definition repository both raw providers run, written over the `ISqlDialect` statement kit; each provider package supplies only its options, DDL, and registration
- `HeadlessFeaturesSetupBuilder` — fluent builder returned to `AddHeadlessFeatures`; exposes `ConfigureManagement`, `ConfigureStorage`, and `RegisterExtension`
- `services.AddFeatureDefinitionProvider<T>()` — registers a custom `IFeatureDefinitionProvider`
- `services.AddFeatureValueProvider<T>()` — registers a custom `IFeatureValueReadProvider` (idempotent by type)
- `RequiresFeatureAttribute` — gates a controller class, action, or Minimal API handler on one or more features; `IsAnd` property controls AND vs. OR policy (default: OR). It implements `IAuthorizationRequirementData`, so on HTTP the authorization middleware adds a `FeatureRequirement` to the endpoint's policy; other call sites evaluate it through `IMethodInvocationFeatureCheckerService`. An attribute with no feature names adds no requirement
- `DisableFeatureCheckAttribute` — bypasses every feature requirement on one action or endpoint, including a controller-level, route-group, or route-convention gate; other policies still apply. On Minimal APIs, put it on the handler or add it with `.WithMetadata(new DisableFeatureCheckAttribute())`
- `FeatureRequirement(string[] featureNames, bool requiresAll)` — the authorization requirement behind the attribute. Its handler fails a disabled feature with the localized "feature currently unavailable" text as the `AuthorizationFailureReason`. An empty list throws `ArgumentException`
- `AuthorizationPolicyBuilder.RequireFeatures(params string[] features)` / `RequireFeatures(bool requiresAll, params string[] features)` (namespace `Microsoft.AspNetCore.Authorization`) — adds a `FeatureRequirement` to a policy: `.RequireAuthorization(policy => policy.RequireFeatures("Reports"))` or `options.AddPolicy("Reports", policy => policy.RequireFeatures("Reports"))`
- `IMethodInvocationFeatureCheckerService.CheckAsync(context, cancellationToken)` — evaluates the `[RequiresFeature]` attributes of a `MethodInfo` (method plus, for a public method, its declaring type); for interceptors and other non-HTTP call sites
- `IClientVisibleFeaturesReader` (`Headless.Features`) — `GetAsync(PrincipalContext, …)` returns the effective value of every feature whose definition is `IsVisibleToClients`, keyed by name, for example to include in the configuration an application returns to its front end. Headless ships no endpoint; see the client-config recipe in `docs/llms/permissions.md`

### Design constraints

- `IClientVisibleFeaturesReader` resolves for the principal and tenant in its `PrincipalContext`, not the ambient ones: it switches `ICurrentPrincipalAccessor` and `ICurrentTenant` to the context for the duration of the read and restores them afterwards. Pass `TenantId: null` for the host. This is what makes it safe inside a login or token-refresh response, where the newly issued principal is not yet ambient.

- Value providers are registered with the last-added provider having the highest resolution priority. The built-in order is `DefaultValue` → `Edition` → `Tenant` (Tenant wins). Custom providers added via `AddFeatureValueProvider<T>()` are appended after `Tenant` and therefore have the highest priority. This matters when writing custom providers that must override built-in resolution.
- `TenantFeatureValueProvider` and `EditionFeatureValueProvider` resolve their store key as `providerKey ?? ambient` — an explicit key (e.g. `GetForTenantAsync(name, tenantId)`) always wins, and a `null` key falls back to `ICurrentTenant.Id` / the principal's edition claim. The same rule applies to reads and writes, so a value written for one tenant is read back for that tenant only.
- **Keys must be text every provider keeps unchanged.** Every `IFeatureValueStore` entry point throws `ArgumentException` before touching storage when a feature name, provider name, or provider key starts or ends with white space, on reads as well as writes. SQL Server ignores trailing spaces when it compares keys, so `"acme "` would read and overwrite the `"acme"` row there while PostgreSQL keeps the two apart. Every provider must keep a key unchanged, so the store also refuses a NUL character (PostgreSQL cannot store it) and an unpaired UTF-16 surrogate (SqlClient sends it as U+FFFD, so SQL Server would merge keys that differ only in which lone surrogate they carry). The rule is `Argument.IsPortableKey`. Normalize keys at your own boundary; the store refuses rather than trims.
- `AddHeadlessFeatures` is guarded on `IFeatureManager` so it is safe to call more than once (only the first call registers the core; the storage extension always applies). However, only one storage provider extension may be registered — a second call with a different provider throws at startup.
- Nothing writes definitions at startup unless the host opts in to seeding; see [Definition Seeding](#definition-seeding). A failed `FeatureDefinitions` seed fails the `seed` verb, or the host start under `AddHeadlessSeedOnStartup()`.
- `FeatureValueRecord` implements `ICreateAudit` / `IUpdateAudit`, carrying `CreatedAt` (stamped on insert) and `UpdatedAt` (stamped on update). On the EF path these are populated by the Headless audit save-processor; the raw-SQL PostgreSQL / SQL Server providers stamp them from the registered `TimeProvider`. Features scope tenancy through `ProviderName`/`ProviderKey` (e.g. `ProviderName == "Tenant"` with the tenant id in `ProviderKey`) — there is deliberately no first-class `TenantId` column nor `IMultiTenant`; a scoping value provider expresses tenant, edition, and other scopes uniformly. This is an intentional divergence from `PermissionGrantRecord`, not drift.

### Install

```bash
dotnet add package Headless.Features
```

### Setup and use

Register the required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessFeatures`:

> A caching provider is a hard prerequisite. This package references `Headless.Caching.Abstractions` only, so the `ICache` that `FeatureValueStore` reads through — or the named cache it takes from `ICacheProvider` when `FeatureValueCacheName` is set — comes from `AddHeadlessCaching(...)` with a provider (`UseInMemory` / `UseRedis` / `UseHybrid`). `AddHeadlessFeatures` declares it via `Headless.Hosting`'s `RequireRegisteredService<T>`, so a host without one is refused at startup with a `MissingRequiredServiceException` rather than failing on the first feature check. Registration order does not matter — the check runs at host start.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Required dependencies
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// Register feature definitions
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// Register the management core + storage in one call
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

#### Custom Value Provider

```csharp
// T must implement IFeatureValueReadProvider (read-only) or IFeatureValueProvider (read-write)
builder.Services.AddFeatureValueProvider<MyCustomFeatureValueProvider>();
```

### Configuration

#### FeatureManagementOptions

Configure via `setup.ConfigureManagement(...)` or `services.Configure<FeatureManagementOptions>(...)`:

```csharp
services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureManagement(options =>
    {
        // Distributed lock key coordinating cross-instance definition saves (default: "features:common_update_lock")
        options.CrossApplicationsCommonLockKey = "features:common_update_lock";

        // Route feature-value cache to a named ICache instance; null/empty uses the default ICache
        options.FeatureValueCacheName = null;

        // Persist static definitions to the DB when the host seeds (default: true)
        options.SaveStaticFeaturesToDatabase = true;

        // Enable the dynamic definition store (default: false)
        options.IsDynamicFeatureStoreEnabled = false;

        // How long dynamic definitions stay in the in-process cache before the distributed stamp is re-checked (default: 30 seconds)
        options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

All lock- and cache-expiry options default to reasonable production values. The validator rejects empty lock keys and zero/negative expiry spans.

#### FeaturesStorageOptions

Configure schema and table names via `setup.ConfigureStorage(...)`:

```csharp
services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "headless"; // default, shared by every Headless feature
        o.FeatureValuesTableName = null; // default: feature_values on PostgreSQL, FeatureValues elsewhere
        o.FeatureDefinitionsTableName = null; // default: feature_definitions / FeatureDefinitions
        o.FeatureGroupDefinitionsTableName = null; // default: feature_group_definitions / FeatureGroupDefinitions
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

Every object follows its database's naming convention. On PostgreSQL the tables, columns, primary keys, and indexes are snake_case (`feature_values`, `provider_key`, `pk_feature_values`, `ix_feature_values_provider_name_provider_key`); on SQL Server and other databases they are PascalCase (`FeatureValues`, `ProviderKey`, `PK_FeatureValues`, `IX_FeatureValues_ProviderName_ProviderKey`). A table-name option left `null` takes that convention's default. A table name you set is used verbatim, and its key and index names derive from it (`pk_MyValues`). The raw providers and the EF mapping produce the same names on the same database. Because PostgreSQL silently truncates identifiers longer than 63 bytes, every provider refuses a configured table name whose longest derived PostgreSQL key or index name would exceed that: at most 23 characters for the values table, 49 for definitions, and 55 for groups.

### Runtime behavior

- Registers `IFeatureManager` and `IClientVisibleFeaturesReader` as transient
- Registers `IStaticFeatureDefinitionStore`, `IDynamicFeatureDefinitionStore`, `IFeatureDefinitionManager`, `IFeatureValueStore`, `IFeatureValueProviderManager` as singletons
- Registers `DefaultValueFeatureValueProvider`, `EditionFeatureValueProvider`, `TenantFeatureValueProvider` as singletons
- Registers the `FeatureDefinitions` seeder (`ISeeder`); it runs only when the host seeds
- Registers the `FeatureDefinitions` warm-up (`IWarmup`), which adds the `warmup` readiness check; it loads and gates readiness only when `IsDynamicFeatureStoreEnabled` is true
- Registers `IMethodInvocationFeatureCheckerService` as singleton
- Registers the feature authorization handler (transient, once however often `AddHeadlessFeatures` runs) and calls `AddAuthorizationCore()`, which only adds services the host has not registered

### Gating HTTP endpoints

A feature gate is an ASP.NET Core authorization requirement, so `AddHeadlessFeatures` plus the host's ordinary `AddAuthorization()` / `UseAuthorization()` enforce it. No HTTP-specific registration exists to forget.

- **Declaring a gate.** `[RequiresFeature]` on a controller, an action, or a Minimal API handler; `.WithMetadata(new RequiresFeatureAttribute(...))` on an endpoint, a route group, or `MapControllers()`; or a policy, `.RequireAuthorization(policy => policy.RequireFeatures(...))`. Each attribute or policy adds its own `FeatureRequirement`, so a controller and an action requirement, or a group and an endpoint requirement, must both pass. `[DisableFeatureCheck]` on an action or handler, or `.WithMetadata(new DisableFeatureCheckAttribute())`, skips every feature requirement on that endpoint, whichever shape declared it.
- **Which shape.** The attribute and metadata shapes add only the feature requirement, so the fallback policy still applies to the endpoint. `RequireAuthorization(policy => policy.RequireFeatures(...))` adds no user requirement either, but like any `RequireAuthorization` call it gives the endpoint its own authorization data, so the fallback policy no longer applies there; prefer the metadata shape when the fallback policy matters.
- **Composition.** The requirement joins the endpoint's other authorization data in one policy: a `[Authorize("Reports.View")]` permission and a `[RequiresFeature("Reports")]` both apply. A feature gate is not `[Authorize]`, so on an endpoint without `[Authorize]` the fallback policy still applies, and the gate adds no authenticated-user requirement of its own.
- **Anonymous callers.** A feature is the tenant's or edition's state, not a right of the caller, so the gate applies to anonymous callers too: with the feature off they get ASP.NET Core's challenge (401), as any failed authorization does for an anonymous caller; with it on they pass unless another policy requires a user. `[AllowAnonymous]` skips ASP.NET Core authorization entirely, feature requirements included, so never combine it with a feature gate; an endpoint that must serve anonymous callers behind a gate needs no `[Authorize]` and no fallback policy that requires a user.
- **Failure.** A disabled feature fails the evaluation with `Fail(new AuthorizationFailureReason(...))`, its message the localized "feature currently unavailable" text resolved in the request culture (so `UseRequestLocalization()` must run before `UseAuthorization()`). An authenticated caller gets 403; with `Headless.Api`'s status-codes rewriter (`AddStatusCodesRewriterMiddleware()` + `UseStatusCodesRewriter()`, wired by `AddHeadless()` / `UseHeadless()`) the 403 is the standard forbidden problem with the failure reasons as `detail`, so a caller missing both a permission and a feature still learns about the feature. An anonymous caller is challenged (401). Without `Headless.Api`, ASP.NET Core's bare 403 applies.
- **403 at the endpoint, 409 in code.** The endpoint gate is authorization: it decides whether this request may reach the endpoint, so it fails the way every authorization requirement does. `IFeatureManager.EnsureEnabledAsync` in application code is a business rule: it throws `ConflictException`, which the Headless exception handler returns as 409 with the `g:feature_currently_not_available` error code, the response a domain operation gives when the current state forbids it.
- **Fail-closed wiring.** `AddHeadlessFeatures` registers the authorization handler services, so `WebApplication` adds the authorization middleware itself when the host never calls `UseAuthorization()`, and a host that never calls `AddAuthorization()` fails at startup instead of serving gated endpoints unchecked (MVC's `AddControllers()` calls `AddAuthorization()` for you). The gate needs `UseAuthorization()` after routing: a host that calls `UseRouting()` without it, or with the implicit middleware ahead of routing, skips feature requirements, as ASP.NET Core does for its own requirement attributes. Call `UseAuthorization()` after `UseRouting()`, and `UseStatusCodesRewriter()` before it so the rewriter wraps authorization.
- Code outside HTTP, such as an interceptor or a job, checks the same attributes through `IMethodInvocationFeatureCheckerService`, or calls `IFeatureManager.EnsureEnabledAsync` directly.

```csharp
builder.AddHeadless();
builder.Services.AddHeadlessFeatures(setup => setup.UsePostgreSql());
builder.Services.AddControllers();

var app = builder.Build();
app.UseHeadless(); // includes UseStatusCodesRewriter()
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var reports = app.MapGroup("/api/reports")
    .RequireAuthorization("Reports.View")
    .RequireAuthorization(policy => policy.RequireFeatures("Reports"));
reports.MapGet("/", () => Results.Ok());
reports.MapGet("/export", () => Results.Ok())
    .RequireAuthorization(policy => policy.RequireFeatures(requiresAll: true, "Reports.Export", "Exports"));
app.MapGet("/api/catalog", () => Results.Ok()).WithMetadata(new RequiresFeatureAttribute("Catalog")); // keeps the fallback policy
reports.MapGet("/status", () => Results.Ok()).WithMetadata(new DisableFeatureCheckAttribute()); // reachable while "Reports" is off

[ApiController]
[Route("reports")]
[RequiresFeature("Reports")]
public sealed class ReportsController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok();

    [HttpGet("export")]
    [RequiresFeature("Reports.Export")] // checked in addition to "Reports"
    public IActionResult Export() => Ok();

    [HttpGet("status")]
    [DisableFeatureCheck] // reachable while "Reports" is off
    public IActionResult Status() => Ok();
}
```

---

## Headless.Features.Storage.EntityFramework

Entity Framework Core storage implementation for feature management.

### API and behavior

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via the `HeadlessFeaturesSetupBuilder`
- `modelBuilder.ConfigureHeadlessFeatures(DbContext context)` — applies entity configurations by resolving `FeaturesStorageOptions` from the context's service provider (no constructor injection required) and the naming style from `context.Database.ProviderName`: snake_case on Npgsql, PascalCase on every other provider
- `modelBuilder.ConfigureHeadlessFeatures(FeaturesStorageOptions options, StorageNamingStyle style)` — overload for when you already hold the options; pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` (namespace `Headless.Hosting`) so the style matches the database
- EF repositories for `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
- `FeatureValueRecord` maps `CreatedAt` / `UpdatedAt` audit columns (via `ConfigureHeadlessConvention`); the Headless audit save-processor stamps them on `SaveChanges`
- `FeaturesStorageOptions` for schema and table-name configuration (shared with raw-DDL providers)
- Startup validation gate that inspects the EF model before hosted services start and fails with an actionable message if any feature entity is missing from the model
- Value uniqueness declared as a pair of filtered unique indexes — `(Name, ProviderName, ProviderKey) WHERE "ProviderKey" IS NOT NULL` and `(Name, ProviderName) WHERE "ProviderKey" IS NULL` — matching the raw-DDL providers, so NULL-key values stay unique on databases that treat NULLs as distinct (PostgreSQL, SQLite)

### Install

```bash
dotnet add package Headless.Features.Storage.EntityFramework
```

### Setup and use

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Resolves FeaturesStorageOptions from the context's service provider —
        // no need to inject IOptions<FeaturesStorageOptions> into the constructor.
        modelBuilder.ConfigureHeadlessFeatures(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// AddHeadlessFeatures registers the management core automatically.
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_features");
    setup.UseEntityFramework<AppDbContext>();
});
```

### Configuration

`FeaturesStorageOptions` defaults:

- `Schema = "headless"`, the schema every Headless feature shares (see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features))
- `FeatureValuesTableName = null`: `feature_values` on PostgreSQL, `FeatureValues` elsewhere
- `FeatureDefinitionsTableName = null`: `feature_definitions` on PostgreSQL, `FeatureDefinitions` elsewhere
- `FeatureGroupDefinitionsTableName = null`: `feature_group_definitions` on PostgreSQL, `FeatureGroupDefinitions` elsewhere

The registration validates identifier names using cross-provider rules (SQL Server superset). The startup gate inspects the EF model before hosted services start and fails with an actionable message if any features entity is missing.

The EF provider writes no DDL; create its tables with EF migrations.

### Runtime behavior

- Registers `IFeatureDefinitionRecordRepository` (`EfFeatureDefinitionRecordRepository<TContext>`) as singleton
- Registers `IFeatureValueRecordRepository` (`EfFeatureValueRecordRecordRepository<TContext>`) as singleton
- Registers validated `FeaturesStorageOptions`
- Registers `FeaturesEntityStartupValidator<TContext>` as an `IHeadlessStartupValidator`

---

## Headless.Features.Storage.PostgreSql

PostgreSQL raw-DDL storage for feature management.

### API and behavior

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — binds `PostgreSqlFeaturesOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlFeaturesOptions> configure)` — overload for full option control
- `setup.UsePostgreSql(Action<PostgreSqlFeaturesOptions, IServiceProvider> configure)` — overload with service-provider access for late-bound configuration
- `setup.UsePostgreSql()` — reads the connection registered by `AddPostgreSqlSql`, so one connection string serves every feature; see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features)
- Table and index creation as schema steps (`Features/1` tables, `Features/2` indexes) that `resources setup` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), with snake_case tables, columns, keys, and indexes (`feature_values`, `provider_key`, `ix_feature_values_provider_name_provider_key`)
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- `PostgreSqlFeaturesOptions` — connection string and command timeout (`CommandTimeout`, default 30 seconds), inherited from `RelationalFeaturesOptions`
- Shares `FeaturesStorageOptions` with the EF provider (schema, table names)

### Install

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

### Setup and use

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` registers the management core automatically.

```csharp
builder.Services.AddPostgreSqlSql(connectionString);
builder.Services.AddHeadlessFeatures(setup => setup.UsePostgreSql());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_features");
    setup.UsePostgreSql(connectionString);
});
```

Or with full option control:

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

### Configuration

#### Options

`PostgreSqlFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | PostgreSQL connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `FeaturesStorageOptions` via `setup.ConfigureStorage(...)`.

### Runtime behavior

- Registers the features schema contribution; `resources setup` applies it, and a host start only verifies it
- Registers the shared relational repositories from `Headless.Features`, over the PostgreSQL dialect, as `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository` (singletons)

---

## Headless.Features.Storage.SqlServer

SQL Server raw-DDL storage for feature management.

### API and behavior

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — binds `SqlServerFeaturesOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerFeaturesOptions> configure)` — overload for full option control
- `setup.UseSqlServer(Action<SqlServerFeaturesOptions, IServiceProvider> configure)` — overload with service-provider access for late-bound configuration
- `setup.UseSqlServer()` — reads the connection registered by `AddSqlServerSql`, so one connection string serves every feature; see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features)
- Table, index, and table-type creation as one schema step (`Features/1`) that `resources setup` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts)
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- `SqlServerFeaturesOptions` — connection string and command timeout (`CommandTimeout`, default 30 seconds), inherited from `RelationalFeaturesOptions`
- Shares `FeaturesStorageOptions` with the EF provider (schema, table names)

### Install

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

### Setup and use

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` registers the management core automatically.

```csharp
builder.Services.AddSqlServerSql(connectionString);
builder.Services.AddHeadlessFeatures(setup => setup.UseSqlServer());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_features");
    setup.UseSqlServer(connectionString);
});
```

Or with full option control:

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

### Configuration

#### Options

`SqlServerFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `FeaturesStorageOptions` via `setup.ConfigureStorage(...)`.

### Runtime behavior

- Registers the features schema contribution; `resources setup` applies it, and a host start only verifies it
- Registers the shared relational repositories from `Headless.Features`, over the SQL Server dialect, as `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository` (singletons)
