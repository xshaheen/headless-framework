---
domain: Feature Management
packages: Features.Abstractions, Features, Features.Storage.EntityFramework, Features.Storage.PostgreSql, Features.Storage.SqlServer
---

# Feature Management

> Feature flags and feature values per tenant and edition, resolved Tenant > Edition > Default, cached, and stored through EF Core, PostgreSQL, or SQL Server.

## Orientation

Install `Headless.Features.Abstractions`, `Headless.Features`, and exactly one storage provider:

| Package | Role |
| --- | --- |
| `Headless.Features.Abstractions` | `IFeatureManager`, `IFeatureDefinitionProvider`, `IFeatureDefinitionManager`, `unit.Features` |
| `Headless.Features` | The implementation, caching, value providers, the definitions seeder, and the HTTP gate (`[RequiresFeature]`) |
| `Headless.Features.Storage.EntityFramework` | Storage in your own `DbContext`, tables created by EF migrations |
| `Headless.Features.Storage.PostgreSql` | Raw ADO.NET storage for PostgreSQL, tables created by `resources provision` |
| `Headless.Features.Storage.SqlServer` | Raw ADO.NET storage for SQL Server, tables created by `resources provision` |

`Headless.Features` references only the host-agnostic `Microsoft.AspNetCore.Authorization` package, so console and worker hosts run every package above without the ASP.NET Core runtime.

Default registration:

```csharp
// Required: a cache, a distributed lock, TimeProvider, and IGuidGenerator.
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

**Verify:** the host starts. A missing cache fails the start with `MissingRequiredServiceException`. A `DbContext` without the feature entities fails it with `InvalidOperationException` naming `modelBuilder.ConfigureHeadlessFeatures(this)`. In a test, `await features.GetAsync("MaxUsers")` returns the definition's `DefaultValue`.

## Agent Rules

- Inject `IFeatureManager` to read and write feature values. Do not use `Microsoft.FeatureManagement`. It is a separate system with a different model.
- Define features in an `IFeatureDefinitionProvider` with `context.AddGroup(...)` and `group.AddChild(...)`. Register it with `services.AddFeatureDefinitionProvider<T>()`.
- Call `AddHeadlessFeatures(setup => ...)` once with one storage provider (`UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`). A second, different storage provider throws at startup. Calling `AddHeadlessFeatures` again with the same provider is safe.
- Register `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider`. The start fails with `MissingRequiredServiceException` when the cache is missing. Registration order does not matter.
- Use the hybrid cache (`UseHybrid`) in production. Every feature gate reads the value cache. On `UseRedis` alone, each feature check is one network round trip, and a second check of the same name in the same request repeats it. [Permissions](permissions.md#grant-store-and-caching) gives the round-trip count of a whole tenant-scoped request.
- Tune options inside the `AddHeadlessFeatures` block: `setup.ConfigureManagement(...)` for `FeatureManagementOptions`, `setup.ConfigureStorage(...)` for schema and table names. `ConfigureStorage(IConfiguration)` binds the `Headless:Features:Storage` section. `services.Configure<FeatureManagementOptions>(...)` also works in any order.
- For EF storage, register a singleton `IDbContextFactory<TContext>` and call `modelBuilder.ConfigureHeadlessFeatures(this)` in `OnModelCreating`. `AddDbContextFactory`, `AddPooledDbContextFactory`, `AddHeadlessDbContext`, and `AddHeadlessDbContextPool` all register a singleton factory. A scoped or transient factory fails the start with `InvalidServiceLifetimeException`.
- For PostgreSQL or SQL Server storage, run `resources provision` (or `deploy`) at deploy time. The host start never creates tables. It verifies them and fails when a step is missing. See [Resource provisioning](utilities.md#resource-provisioning).
- Run the `seed` verb to save definitions to the database. Do not call `IDynamicFeatureDefinitionStore.SaveAsync` yourself. See [Definition seeding](#definition-seeding).
- Write through `IFeatureManager` or `unit.Features`. Both update the value cache. A write through raw SQL, a direct `DbContext`, or the PostgreSQL or SQL Server repository leaves the cache stale until it expires.
- To commit a feature value with your other writes, write on the unit you hold: `unit.Features.SetForTenantAsync(...)`. The injected `IFeatureManager` never joins a unit. See [Writing feature values inside a unit of work](#writing-feature-values-inside-a-unit-of-work).
- `SetAsync` with `forceToSet: false` (the default) stores nothing when the value equals what the next lower provider supplies. Pass `forceToSet: true` to persist the value anyway. `GrantAsync` and `RevokeAsync` always force.
- To write several features atomically, call `SetAsync(values, providerName, providerKey)` with an `IReadOnlyDictionary<string, string?>`. See [Writing values](#writing-values).
- For an edit form that must not overwrite another admin's save, read with `GetVersionedAsync(names, providerName, providerKey)` and save with `SetVersionedAsync(values, providerName, providerKey, version)`. A stale save throws `ConflictException` with `g:feature_version_mismatch`. See [Editing a group with optimistic concurrency](#editing-a-group-with-optimistic-concurrency).
- `DeleteAsync(providerName, providerKey)` removes every value of one provider key, for example every override of a deleted tenant. It skips read-only providers.
- `FeatureDefinition.Providers` limits which providers may hold a value. An empty list allows all. A write through another provider throws `ConflictException` with error code `features:provider-not-allowed`, and reads ignore a value another provider holds. A feature limited to `Edition` cannot be overridden by a tenant value.
- Gate HTTP endpoints with `[RequiresFeature("Name")]`, `.WithMetadata(new RequiresFeatureAttribute("Name"))`, or `.RequireAuthorization(policy => policy.RequireFeatures("Name"))`. A disabled feature returns 403 (401 for an anonymous caller). `IFeatureManager.EnsureEnabledAsync` in application code throws `ConflictException` instead, returned as 409. See [Gating HTTP endpoints](#gating-http-endpoints).
- Never combine `[AllowAnonymous]` with a feature gate. `[AllowAnonymous]` skips all authorization, feature requirements included.
- Keep feature names, provider names, and provider keys free of leading or trailing white space, NUL characters, and lone UTF-16 surrogates. Every read and write throws `ArgumentException` for such a key instead of trimming it, because SQL Server ignores trailing spaces when it compares keys and would merge `"acme "` with `"acme"`. Normalize keys at your own boundary.

## Core Concepts

### Definitions and values

A *feature definition* is a feature's metadata: name, default value, display text, client visibility, allowed providers, and child features. You declare definitions in code through `IFeatureDefinitionProvider`. The `FeatureDefinitions` seeder can also save them to the database. A *feature value* is the stored value for one subject, such as a tenant id or an edition id.

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

`AddGroup` takes a name. You cannot construct a `FeatureGroupDefinition` yourself.

### Resolution order

Three built-in providers resolve a value, from lowest to highest priority: `DefaultValue` (the definition's `DefaultValue`), `Edition`, and `Tenant`. With no provider named, `GetAsync` asks the highest-priority provider first and returns the first non-null value. The constants are on `FeatureValueProviderNames`.

A custom provider added with `services.AddFeatureValueProvider<T>()` goes after `Tenant`, so it has the highest priority of all. The last provider registered wins.

`Tenant` and `Edition` resolve their key as the explicit provider key when you pass one (`GetForTenantAsync(name, tenantId)`), else the ambient tenant or its edition. Reads and writes use the same rule, so a value written for one tenant reads back for that tenant only.

### Edition resolution outside a request

The `Edition` provider reads the edition of the ambient tenant. It takes the edition claim (`UserClaimTypes.EditionId`) from the current principal only when that principal belongs to the tenant: its `tenant_id` claim matches `ICurrentTenant.Id` or is absent, and the manager has not switched to another tenant for the call.

Otherwise, and whenever no principal exists (a job or a message consumer restores the tenant, not the principal), the provider calls your `ITenantEditionResolver`, if you registered one:

```csharp
builder.Services.AddSingleton<ITenantEditionResolver, MyEditionResolver>();
```

`ITenantEditionResolver.GetEditionIdAsync(tenantId, cancellationToken)` maps a tenant id to its edition id. Without a resolver, those reads see no edition value. The framework calls the resolver on every such read, so cache its answer.

Writes follow the same rule. `SetForTenantAsync` clears a value that equals what the tenant inherits. When the manager cannot tell the edition of the tenant being written (no matching principal and no resolver), it stores the value as written instead of comparing it with the caller's edition. A host administrator's downgrade is therefore never dropped as a no-op. `GetForTenantAsync(name, tenantId)` and `GetAllForTenantAsync(tenantId)` with fallback read the edition of `tenantId`, not the caller's.

### Static and dynamic definition stores

By default, definitions come only from code, built once and held in memory.

Set `IsDynamicFeatureStoreEnabled = true` only when definitions must change at runtime without a redeploy. The dynamic store then reads definitions from the database and caches them in process for `DynamicDefinitionsMemoryCacheExpiration` (default 30 seconds), coordinating instances through a distributed cache stamp and a distributed lock. The `FeatureDefinitions` [warm-up](utilities.md#warm-up) loads it after the host starts. The `warmup` readiness check reports `Unhealthy` until that load succeeds, and a failed load retries in the background. With the dynamic store off, the warm-up does nothing and does not hold readiness.

### Value caching

Resolved values are cached in the registered `ICache`, or in the named cache `FeatureManagementOptions.FeatureValueCacheName` selects. You do not invalidate anything yourself:

- `IFeatureManager` writes and deletes update the affected cache entries. A distributed or hybrid cache propagates the change to other nodes through `CacheInvalidationMessage`, so the window between nodes is the cache backplane's.
- A write through `unit.Features` updates the cache only after its unit commits. A rolled-back unit leaves the cache unchanged. A cache fault after the commit is logged, not thrown.
- A direct write to the EF `IFeatureValueRecordRepository` also updates the cache.
- A write through raw SQL, a direct `DbContext`, or the PostgreSQL or SQL Server repository leaves the cache stale.
- Concurrent writers and readers leave the last committed value cached. Each store write costs one extra repository read to guarantee this.

### Reacting to a change

`IFeatureManager` publishes one `FeatureChangedMessage` over `IBus` after each successful `SetAsync` or `DeleteAsync`. It lists every feature name the call changed, with `ProviderName` and `ProviderKey` for the scope. Use it to refresh state you copied out of the feature manager instead of polling.

- **Consume on every instance.** The state lives in each process, so use an [every-instance consumer](messaging/consumers.md#every-instance-bus-delivery). Delivery is at most once, so also reload in `OnSubscriptionEstablishedAsync`.
- **Wire contract.** The message name is `headless.features.changed`, contract version `1` (`FeatureChangedMessage.MessageName`). `AddHeadlessFeatures` declares it, so your consumer needs no `Message<T>` declaration. Naming conventions (`UseConventions`) do not rename it. Only `MessagingOptions.MessageNamePrefix` applies.
- **Names only.** The message never carries values. Re-read on receipt. A re-read is idempotent and does not depend on delivery order.
- **The writer re-reads too.** `OriginHostName` names the writing host for logs and telemetry. Do not filter on it.
- **Messaging is optional.** Without `AddHeadlessMessaging`, writes work and nothing is published. A failed publish is logged and never fails the committed write. A consumer that must converge without a bus needs a periodic refresh.
- **Ordering with commits.** An `IFeatureManager` write commits before it returns, even inside your own `RunAsync(db, …)`, and the message goes out after that commit. A `unit.Features` write publishes from the unit's post-commit callback, under the tenant that wrote it. A rolled-back unit publishes nothing. A crash between the commit and the publish loses the message.
- **Race with cache invalidation.** `FeatureChangedMessage` and `CacheInvalidationMessage` travel on separate subscriptions with no ordering. On a hybrid cache, a peer can re-read before its local tier is evicted and get the old value. When the first re-read shows no change, re-read again a few seconds later, or rely on a periodic refresh.

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

**Verify:** in an integration test with messaging, call `SetForTenantAsync` for a tracked feature and assert the consumer reloaded.

### Writing values

`SetAsync(name, value, providerName, providerKey, forceToSet)` writes one value. `SetAsync(values, providerName, providerKey, forceToSet)` writes a batch from an `IReadOnlyDictionary<string, string?>` keyed by feature name. The single-name call is the one-entry batch.

- A `null` value clears that feature's row under the exact provider key the provider resolves. It does not clear the feature under other keys.
- Every name, the provider, and its writability are checked before anything is written. An undefined name or a read-only provider rejects the whole batch with `ConflictException` and changes nothing.
- The built-in stores write the batch in one transaction. A failed write leaves every value unchanged. A successful one publishes a single `FeatureChangedMessage` listing every name.
- An empty dictionary writes and publishes nothing.
- Atomicity holds per provider. When several registered providers share `providerName`, each writes the batch in its own transaction.
- The stores open their own connection and transaction. An `IFeatureManager` write does not join a unit of work you have open, and rolling that unit back does not undo it. Use `unit.Features` for that.
- When a concurrent writer inserts or deletes one of the batch's rows between the store's read and its save, the store reads again and retries, up to three attempts. The last writer wins.

### Editing a group with optimistic concurrency

An edit form reads a group of features for one scope (an edition or a tenant), lets an admin change them, and saves them back. `GetVersionedAsync` returns the values and the group's version. `SetVersionedAsync` saves only while the group still has that version. Otherwise it throws `ConflictException` with the error code `g:feature_version_mismatch` and writes nothing. Over HTTP, the version is the `ETag`, and the save takes it in `If-Match`. The Headless API exception handler maps the `ConflictException` to 409.

```csharp
HashSet<string> reporting = ["Reports", "Reports.MaxExports"];

app.MapGet("/editions/{editionId}/features/reporting", async (string editionId, IFeatureManager features, HttpResponse response, CancellationToken ct) =>
{
    var group = await features.GetVersionedAsync(reporting, FeatureValueProviderNames.Edition, editionId, cancellationToken: ct);
    response.Headers.ETag = $"\"{group.Version}\"";

    return Results.Ok(group.Values.ToDictionary(x => x.Name, x => x.Value));
});

app.MapPut("/editions/{editionId}/features/reporting", async (string editionId, Dictionary<string, string?> body, HttpRequest request, HttpResponse response, IFeatureManager features, CancellationToken ct) =>
{
    var version = request.Headers.IfMatch.ToString().Trim('"');

    if (version.Length == 0)
    {
        return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
    }

    // Every name of the group, so an omitted name clears its value instead of failing the version check.
    var values = reporting.ToDictionary(name => name, name => body.GetValueOrDefault(name), StringComparer.Ordinal);
    var savedVersion = await features.SetVersionedAsync(values, FeatureValueProviderNames.Edition, editionId, version, forceToSet: true, cancellationToken: ct);
    response.Headers.ETag = $"\"{savedVersion}\"";

    return Results.NoContent();
});
```

**Verify:** in a test, call `GetVersionedAsync`, write one of the names with `SetForEditionAsync`, then call `SetVersionedAsync` with the version you read. It throws `ConflictException` whose `Errors[0].Code` is `g:feature_version_mismatch`, and the other write's value is still stored.

- **The group.** The version covers the names you pass, in one provider scope (`providerName` and the key the provider resolves). Two forms over different names do not block each other.
- **Same names on save.** Save exactly the names you read. Another name set has another version, so a save with fewer or more names throws `g:feature_version_mismatch`.
- **Stored representation.** The version covers the current local rows, not write history. An unchanged save writes nothing and returns the same token. An absent → set → cleared sequence can reuse its original token.
- **Scope-local editing.** `GetVersionedAsync` defaults to `fallback: false`, so it returns only what the scope stores. Use `forceToSet: true` on saves as above. The save then preserves explicit local values even when they equal an inherited value. Do not pass `fallback: true` for an edit form: the version never covers inherited values, so a form that saves an inherited value back unchanged stores it as a local override, even after the inherited value changed, and no version mismatch reports it. If you display inherited values, read them separately and never submit them as local values.
- **Read from storage.** `GetVersionedAsync` reads the scope's values from the database, not the cache.
- **Atomicity.** The check and the write run in one serializable transaction. Of two saves that change the same group from one version, at most one succeeds. A serialization, deadlock, or unique-violation failure, including one reported at commit, triggers a fresh storage read. A changed version produces a version conflict. An unchanged version permits another attempt, up to three attempts. When the third attempt also conflicts, the save throws the same version-mismatch `ConflictException`, so the client re-reads and retries instead of receiving a 500. A lost commit reply never triggers replay. If storage confirms the intended representation, the save returns its version. Otherwise, the original failure propagates.
- **Returned version.** `SetVersionedAsync` returns the resulting token computed inside its transaction. Return that token as the PUT response's ETag, rather than issuing a later GET that could observe another writer. A cache-maintenance failure after a confirmed commit is logged and does not reject the save. Stale cache entries expire normally.
- **After the commit.** Every write, versioned or not, refreshes the cache and announces the change to other hosts even when the caller's cancellation token fires after the commit. A client that disconnects mid-save therefore cannot leave hosts serving the old value.
- **Not in a unit of work.** `unit.Features` has no versioned write, because the save owns its serializable transaction, its retries, and its commit. Do not wrap `SetVersionedAsync` in your own transaction or `TransactionScope`: it would not join it, and a rollback of yours would not undo it.
- **Supported stores.** The EF Core, PostgreSQL, and SQL Server storage support it. A custom `IFeatureValueProvider` must implement `GetAllVersionedAsync` and `SetAllVersionedAsync`, and a custom `IFeatureValueRecordRepository` must implement `SaveIfVersionAsync` and compute the version with `FeatureValueVersion.Compute`. Their defaults throw `NotSupportedException`. More than one provider registered under the same name also throws `NotSupportedException`.

### Writing feature values inside a unit of work

`unit.Features` is the write that joins your unit. `Headless.Features.Abstractions` adds it to `IUnitOfWork` (namespace `Headless.UnitOfWork`). It offers `SetAsync` (one name or a dictionary batch), `SetForTenantAsync`, `GrantToTenantAsync`, `RevokeFromTenantAsync`, `SetForEditionAsync`, and `DeleteAsync(providerName, providerKey)`. Each call runs on the unit's connection and transaction, so the value commits or rolls back with your other writes:

```csharp
await factory.RunAsync(db, async (unit, ct) =>
{
    db.Subscriptions.Add(subscription);
    await db.SaveChangesAsync(ct);
    await unit.Features.GrantToTenantAsync("Reports", subscription.TenantId, ct);
}, ct);
```

- **Which unit joins.** Any unit with a live relational transaction on the database the storage uses: an EF unit (`BeginAsync(db)`, `RunAsync(db, …)`) or a raw ADO.NET unit (`BeginAsync(connection)`), on all three stores. A unit on another database, a unit with no resource, or a completed unit throws `InvalidOperationException` before any write, and the unit stays usable. `unit.Features` also throws `InvalidOperationException` when `AddHeadlessFeatures` was not called.
- **EF with a retrying execution strategy.** A context with `EnableRetryOnFailure` joins only from inside `RunAsync(db, …)`. From a raw ADO.NET unit it throws `InvalidOperationException` naming `RunAsync(db, …)`.
- **Inherited values.** The no-op comparison with the next provider reads inside the unit's transaction, so it sees values the same unit already wrote.
- **Deleting a scope.** `unit.Features.DeleteAsync` deletes every value each writable provider stores for the key, including values whose definition no longer exists.
- **One attempt.** An enlisted batch is not retried on a concurrent-writer collision. The failed statement aborts the unit's transaction, and the unit's owner decides whether to run the unit again.
- **After commit.** The cache update and `FeatureChangedMessage` run after the unit commits. A rolled-back unit does neither.
- **Replay.** Inside an owned `RunAsync` block, the writes replay with the block. Inside an observed unit (a domain-event handler in the `HeadlessDbContext` save pipeline), the first write calls `IUnitOfWork.PreventRetry()`.
- **Replacing `IFeatureManager`.** `unit.Features` keeps using the framework's implementation.

**Verify:** in a test, throw after `unit.Features.SetForTenantAsync(...)` inside `RunAsync` and assert `GetForTenantAsync` still returns the old value.

### Definition seeding

`AddHeadlessFeatures` registers a seeder named `FeatureDefinitions`. When `SaveStaticFeaturesToDatabase` is true (the default), it saves the code definitions to the database under a distributed lock.

- The host start does not seed. In production, run the `seed` verb in the deployment (`dotnet app.dll seed`). In development, call `services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None)`. `HeadlessTestServer` seeds by default. See [Database Seeders](utilities.md#database-seeders).
- A failed save retries up to 10 times with jittered exponential back-off, capped at 30 seconds, on the system clock. A `FakeTimeProvider` does not speed the retries up. Cancellation, `ArgumentException`, and `NotSupportedException` fail at once. A failed seed fails the `seed` verb, or the host start under `AddDeployStepsOnStartup`.
- Re-seeding unchanged definitions writes nothing.
- A definition's `ExtraProperties` keep their key case. Values read back as `string`, `bool`, `long`, `decimal`, or `JsonElement`. Read them with `GetProperty<T>(name)`.
- A definition value too long for its column throws `ArgumentOutOfRangeException` when definitions are saved. See [Column lengths](#column-lengths).

A host that never seeds still serves its code definitions from memory. But definitions listed in `DeletedFeatures` or `DeletedFeatureGroups` stay in the database, and other applications sharing the database keep serving stale definitions.

**Verify:** `dotnet app.dll seed verify` reports the store current after a seed run.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Features.Storage.EntityFramework` | You use EF Core and manage schema with EF migrations | You want no EF dependency | Works on any EF-supported database. The start fails when a feature entity is missing from the model |
| `Headless.Features.Storage.PostgreSql` | You use PostgreSQL without EF Core | You run SQL Server or want EF migrations | `resources provision` creates the tables. Table names are checked against PostgreSQL identifier rules |
| `Headless.Features.Storage.SqlServer` | You use SQL Server without EF Core | You run PostgreSQL or want EF migrations | `resources provision` creates the tables. Table names are checked against SQL Server identifier rules |

---

## Headless.Features.Abstractions

The contracts application code calls. It references no ASP.NET Core package.

### Setup

```bash
dotnet add package Headless.Features.Abstractions
```

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

**Verify:** in a test with no stored value, `IsEnabledAsync("EnableReports")` returns `false` and `GetAsync<int>("MaxUsers")` returns `10`, the definition defaults.

### API and behavior

- `IFeatureManager.GetAsync(name, …)` returns a `FeatureValue` with the resolved string value and the `FeatureValueProvider` that supplied it.
- `IFeatureManager.GetAllAsync(IReadOnlySet<string> featureNames)` resolves a set of names through the full chain in one call and returns a `Dictionary<string, FeatureValue>`. A feature with no value maps to a `FeatureValue` whose `Value` is `null`. Undefined names are omitted. Each provider is asked once for the whole set, so prefer it over a loop of `GetAsync`.
- `GetAllAsync(providerName, …)`, `GetAllForTenantAsync`, `GetAllForEditionAsync`, and `GetAllDefaultAsync` read one provider's values and return `IReadOnlyList<FeatureValue>`.
- Extension members on `IFeatureManager`: `IsEnabledAsync`, `GetAsync<T>`, `EnsureEnabledAsync`, `GrantAsync`, `RevokeAsync`. The multi-name `IsEnabledAsync(requiresAll, featureNames)` and `EnsureEnabledAsync(requiresAll, featureNames)` read every name in one `GetAllAsync` call.
- `EnsureEnabledAsync` throws `ConflictException` for a disabled feature. The Headless exception handler returns it as 409 with error code `g:feature_currently_not_available`.
- Tenant members: `GetForTenantAsync`, `GetAllForTenantAsync`, `SetForTenantAsync`, `GrantToTenantAsync`, `RevokeFromTenantAsync`, `DeleteForTenantAsync`. Edition members follow the same pattern (`*ForEditionAsync`, `*ToEditionAsync`). Default members: `GetDefaultAsync`, `GetAllDefaultAsync`.
- `FeatureValueProviderNames` holds the constants `Tenant`, `Edition`, and `DefaultValue`.
- `FeatureGroupDefinition.GetFlatFeatures()` lists a group's features depth first.

---

## Headless.Features

The implementation, the registration, and the HTTP gate.

### Setup

```bash
dotnet add package Headless.Features
```

Register the required services, then `AddHeadlessFeatures` with a storage provider:

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

To add a custom value provider:

```csharp
// T must implement IFeatureValueReadProvider (read-only) or IFeatureValueProvider (read-write)
builder.Services.AddFeatureValueProvider<MyCustomFeatureValueProvider>();
```

**Verify:** the host starts. Without a cache registration, the start fails with `MissingRequiredServiceException`. After `dotnet app.dll seed`, `dotnet app.dll seed verify` reports the `FeatureDefinitions` seeder current.

### Configuration

#### FeatureManagementOptions

Set through `setup.ConfigureManagement(...)` (an `(options, IServiceProvider)` overload exists) or `services.Configure<FeatureManagementOptions>(...)`:

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

The validator rejects an empty lock key and a zero or negative expiry.

#### FeaturesStorageOptions

Set schema and table names through `setup.ConfigureStorage(...)`, or bind them from `Headless:Features:Storage`:

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

- On PostgreSQL, tables, columns, keys, and indexes are snake_case (`feature_values`, `provider_key`, `pk_feature_values`, `ix_feature_values_provider_name_provider_key`). On SQL Server and other databases they are PascalCase (`FeatureValues`, `ProviderKey`, `PK_FeatureValues`, `IX_FeatureValues_ProviderName_ProviderKey`). The raw providers and the EF mapping produce the same names on the same database.
- A table name you set is used verbatim, and its key and index names derive from it (`pk_MyValues`).
- PostgreSQL truncates identifiers longer than 63 bytes, so every provider refuses a table name whose longest derived key or index name would exceed that: at most 23 characters for the values table, 49 for definitions, and 55 for groups.

##### Column lengths

Each string column's maximum length is an option on `FeaturesStorageOptions`:

| Option | Column | Default |
| --- | --- | --- |
| `FeatureValuesNameMaxLength` | values `Name` | 128 |
| `FeatureValuesValueMaxLength` | values `Value` | 128 |
| `FeatureValuesProviderNameMaxLength` | values `ProviderName` | 64 |
| `FeatureValuesProviderKeyMaxLength` | values `ProviderKey` | 64 |
| `FeatureDefinitionsNameMaxLength` | definitions `Name` and `ParentName` | 128 |
| `FeatureDefinitionsDisplayNameMaxLength` | definitions `DisplayName` | 256 |
| `FeatureDefinitionsDescriptionMaxLength` | definitions `Description` | 256 |
| `FeatureDefinitionsDefaultValueMaxLength` | definitions `DefaultValue` | 256 |
| `FeatureDefinitionsProvidersMaxLength` | definitions `Providers` | 256 |
| `FeatureGroupDefinitionsNameMaxLength` | group definitions `Name` and definitions `GroupName` | 128 |
| `FeatureGroupDefinitionsDisplayNameMaxLength` | group definitions `DisplayName` | 256 |

```csharp
setup.ConfigureStorage(o => o.FeatureValuesProviderKeyMaxLength = 128);
```

- Each value must be between 1 and 4000. The registration refuses anything else when the options are first read.
- One option sets the EF Core model, the raw providers' `CREATE TABLE`, and their parameter sizes. Set it before the tables are created.
- A definition value that does not fit throws `ArgumentOutOfRangeException` when definitions are saved.
- The values table's unique indexes cover `Name`, `ProviderName`, and `ProviderKey`; the definitions table indexes `Name` and `GroupName`, and the group definitions table `Name`. SQL Server limits a nonclustered index key to 1,700 bytes (850 `nvarchar` characters), so the three values lengths together, and each indexed name length, must total at most 850. The registration refuses a wider configuration when the options are first read, on every provider, so one configuration stays valid on each database. `Value` is not indexed.

**Changing a length after the tables exist needs a migration in your application.** The option does not alter an existing column, and `resources provision` never re-runs an applied step. Change the option and the column in the same release:

- **EF Core storage**: change the option, then add a migration (`dotnet ef migrations add WidenFeatureValueProviderKeyColumn`). It contains the `AlterColumn`.
- **Raw PostgreSQL or SQL Server storage**: run the `ALTER` with the tool that owns your schema, then deploy the option.

  ```sql
  -- PostgreSQL: widening a varchar changes metadata only and keeps its indexes
  ALTER TABLE headless.feature_values ALTER COLUMN provider_key TYPE character varying(128);

  -- SQL Server: restate the nullability, or the column takes the session default
  ALTER TABLE [headless].[FeatureValues] ALTER COLUMN [ProviderKey] nvarchar(128) NULL;
  ```

Widen the column before, or with, the code that writes longer values. Narrowing a column that holds longer values fails on both databases, so shorten the data first.

### Runtime behavior

- `IFeatureManager` is a singleton. A replacement must be singleton-safe, because the feature authorization handler holds it.
- `AddHeadlessFeatures` registers an `AsyncLocal` `ICurrentTenant` fallback. A real tenancy registration replaces it.
- `AddHeadlessFeatures` calls `AddUnitOfWork()`, so `IUnitOfWorkFactory` resolves.
- The `FeatureDefinitions` warm-up adds the `warmup` readiness check. It loads and holds readiness only when `IsDynamicFeatureStoreEnabled` is true.

### Custom value providers

Implement `IFeatureValueReadProvider` (read-only) or `IFeatureValueProvider` (read-write) and register it with `services.AddFeatureValueProvider<T>()`. Registration is idempotent by type, and the provider takes the highest priority.

- Implement `IFeatureValueReadProvider.GetAllAsync`. Every multi-name read (`GetAllAsync(featureNames)`, `GetAllAsync(providerName, …)`, multi-name `IsEnabledAsync` and `EnsureEnabledAsync`) asks each provider once with the whole batch through it. Without it, the provider's features disappear from bulk results.
- `IFeatureValueProvider.SetAllAsync` defaults to one `SetAsync` or `ClearAsync` per entry. Override it when your source can apply a batch atomically.
- To join a unit of work, override `GetOrDefaultEnlistedAsync` (defaults to the ordinary read), `SetAllEnlistedAsync`, and `DeleteAllEnlistedAsync` (both default to throwing `NotSupportedException`). A custom `IFeatureValueRecordRepository` likewise implements `GetListEnlistedAsync` and `SaveEnlistedAsync`.

### Client-visible features

`IClientVisibleFeaturesReader.GetAsync(PrincipalContext, …)` returns the effective value of every feature whose definition sets `IsVisibleToClients`, keyed by name. Use it to build the configuration your front end receives. Headless ships no endpoint. See the client-config recipe in [Permissions](permissions.md).

It resolves for the principal and tenant in the `PrincipalContext`, not the ambient ones, and restores the ambient ones afterwards. Pass `TenantId: null` for the host. That makes it safe in a login or token-refresh response, where the new principal is not yet ambient.

### Gating HTTP endpoints

A feature gate is an ASP.NET Core authorization requirement. `AddHeadlessFeatures` plus the host's ordinary `AddAuthorization()` and `UseAuthorization()` enforce it. No other registration exists.

- **Declare a gate.** Use `[RequiresFeature]` on a controller, an action, or a Minimal API handler. Use `.WithMetadata(new RequiresFeatureAttribute(...))` on an endpoint, a route group, or `MapControllers()`. Or use a policy: `.RequireAuthorization(policy => policy.RequireFeatures(...))`, or `options.AddPolicy("Reports", policy => policy.RequireFeatures("Reports"))`.
- **Several names.** `RequiresFeatureAttribute` passes when any listed feature is on. Set `IsAnd = true` to require all. `RequireFeatures(requiresAll: true, ...)` does the same for a policy. An attribute with no names adds no requirement. A `FeatureRequirement` with no names throws `ArgumentException`.
- **Stacking.** Each attribute or policy adds its own requirement. A controller gate and an action gate, or a group gate and an endpoint gate, must both pass.
- **Opt out.** `[DisableFeatureCheck]` on an action or handler, or `.WithMetadata(new DisableFeatureCheckAttribute())`, skips every feature requirement on that endpoint, however it was declared. Other policies still apply.
- **Which shape.** The attribute and metadata shapes keep the fallback policy in force on the endpoint. `RequireAuthorization(policy => ...)` gives the endpoint its own authorization data, so the fallback policy no longer applies there. Prefer the metadata shape when the fallback policy matters.
- **Composition.** The feature requirement joins the endpoint's other authorization data in one policy. `[Authorize("Reports.View")]` and `[RequiresFeature("Reports")]` both apply. A feature gate adds no authenticated-user requirement of its own.
- **Anonymous callers.** The gate applies to anonymous callers too. With the feature off, they get a 401 challenge. With it on, they pass unless another policy requires a user. `[AllowAnonymous]` skips all authorization, feature requirements included, so never combine it with a feature gate.
- **Failure.** A disabled feature fails authorization with the localized "feature currently unavailable" text, in the request culture, so call `UseRequestLocalization()` before `UseAuthorization()`. An authenticated caller gets 403. With `Headless.Api`'s status-codes rewriter (wired by `AddHeadless()` and `UseHeadless()`), the 403 is the standard forbidden problem with the failure reasons as `detail`. Without `Headless.Api`, the caller gets ASP.NET Core's bare 403.
- **403 at the endpoint, 409 in code.** The endpoint gate fails like any authorization requirement. `IFeatureManager.EnsureEnabledAsync` in application code is a business rule: it throws `ConflictException`, returned as 409 with `g:feature_currently_not_available`.
- **Fail-closed wiring.** When the host never calls `UseAuthorization()`, `WebApplication` adds the authorization middleware itself. A host that never calls `AddAuthorization()` fails at startup instead of serving gated endpoints unchecked. `AddControllers()` calls `AddAuthorization()` for you.
- **Middleware order.** Call `UseAuthorization()` after `UseRouting()`, and `UseStatusCodesRewriter()` before it. A host that calls `UseRouting()` without `UseAuthorization()`, or with the implicit middleware ahead of routing, skips feature requirements.
- **Outside HTTP.** An interceptor or a job checks the same attributes through `IMethodInvocationFeatureCheckerService.CheckAsync(context, cancellationToken)`, which reads the `[RequiresFeature]` attributes of a method and, for a public method, its declaring type. Or call `IFeatureManager.EnsureEnabledAsync` directly.
- **Cost.** One request reads every feature its requirements name in one `GetAllAsync` call.

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

**Verify:** in a `HeadlessTestServer` test, revoke the feature for the tenant and assert the gated endpoint returns 403 for an authenticated caller and 401 for an anonymous one, then grant it and assert 200.

---

## Headless.Features.Storage.EntityFramework

Feature storage in your own `DbContext`. The package ships no `DbContext` of its own.

### Setup

```bash
dotnet add package Headless.Features.Storage.EntityFramework
```

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

Then add an EF migration for the feature tables. The EF provider writes no DDL.

**Verify:** the host starts. A context that does not map the feature entities fails the start with `InvalidOperationException`: "the registered DbContext ... does not contain ... Call `modelBuilder.ConfigureHeadlessFeatures(this)`".

### Configuration

- `ConfigureHeadlessFeatures(this)` reads `FeaturesStorageOptions` from the context's service provider and picks the naming style from the database provider: snake_case on Npgsql, PascalCase on every other provider.
- `ConfigureHeadlessFeatures(FeaturesStorageOptions options, StorageNamingStyle style)` takes options you already hold. Pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` (namespace `Headless.Hosting`) so the style matches the database.
- `FeaturesStorageOptions` defaults: `Schema = "headless"`, the schema every Headless feature shares (see [Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features)). Table names default to `feature_values`, `feature_definitions`, and `feature_group_definitions` on PostgreSQL, and `FeatureValues`, `FeatureDefinitions`, and `FeatureGroupDefinitions` elsewhere. See [Column lengths](#column-lengths).
- Identifier names are validated against SQL Server rules, the stricter set.

### Runtime behavior

- Reads use a fresh context from `IDbContextFactory<TContext>` with `AsNoTracking()`. Writes commit through their own context, so they do not enlist in your outer transaction. Use `unit.Features` for that.
- `CreatedAt` and `UpdatedAt` on each value row are stamped by the Headless audit save-processor on `SaveChanges`.
- Value uniqueness uses two filtered unique indexes, `(Name, ProviderName, ProviderKey)` where `ProviderKey` is not null and `(Name, ProviderName)` where it is null, so values without a key stay unique on databases that treat NULLs as distinct (PostgreSQL, SQLite).

---

## Headless.Features.Storage.PostgreSql

Raw ADO.NET feature storage for PostgreSQL.

### Setup

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

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

`UsePostgreSql()` with no argument uses the connection from `AddPostgreSqlSql`; see [Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features). The other overloads take a connection string, an `IConfiguration` section bound to `PostgreSqlFeaturesOptions`, an `Action<PostgreSqlFeaturesOptions>`, or an `Action<PostgreSqlFeaturesOptions, IServiceProvider>`.

**Verify:** `dotnet app.dll resources provision` creates the tables, and `dotnet app.dll resources verify` then succeeds. A host started before provisioning fails at startup because the schema step is missing.

### Configuration

`PostgreSqlFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | PostgreSQL connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for each command. |

Set schema and table names through `setup.ConfigureStorage(...)`.

### Runtime behavior

- `resources provision` applies two schema steps through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts): `Features/1` (tables) and `Features/2` (indexes). Names are snake_case.
- The host start only verifies the steps. See [Resource provisioning](utilities.md#resource-provisioning).

---

## Headless.Features.Storage.SqlServer

Raw ADO.NET feature storage for SQL Server.

### Setup

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

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

`UseSqlServer()` with no argument uses the connection from `AddSqlServerSql`; see [Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features). The other overloads take a connection string, an `IConfiguration` section bound to `SqlServerFeaturesOptions`, an `Action<SqlServerFeaturesOptions>`, or an `Action<SqlServerFeaturesOptions, IServiceProvider>`.

**Verify:** `dotnet app.dll resources provision` creates the tables, and `dotnet app.dll resources verify` then succeeds. A host started before provisioning fails at startup because the schema step is missing.

### Configuration

`SqlServerFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for each command. |

Set schema and table names through `setup.ConfigureStorage(...)`.

### Runtime behavior

- `resources provision` applies one schema step through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts): `Features/1` (tables, indexes, and table types). Names are PascalCase.
- The host start only verifies the steps. See [Resource provisioning](utilities.md#resource-provisioning).
