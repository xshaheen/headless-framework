---
domain: Settings
packages: Settings.Abstractions, Settings, Settings.Storage.EntityFramework, Settings.Storage.PostgreSql, Settings.Storage.SqlServer, Settings.Testing
---

# Settings

> Settings an operator or user changes at run time, read through `ISettingManager` from a chain of value providers: DefaultValue, Configuration, Global, Tenant, and User, where the later provider wins.

## Orientation

Install three packages:

- `Headless.Settings.Abstractions`: the contracts application code calls (`ISettingManager`, `ISettingDefinitionProvider`, `ISettingsSnapshot<T>`).
- `Headless.Settings`: the implementation, the value providers, the value cache, encrypted settings, and the definitions seeder.
- Exactly one storage provider: `Headless.Settings.Storage.EntityFramework`, `Headless.Settings.Storage.PostgreSql`, or `Headless.Settings.Storage.SqlServer`.

Minimal wiring:

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

Define settings in an `ISettingDefinitionProvider`. Read with `ISettingManager.GetAsync` and write with `SetAsync`. For an application-wide policy built from Global settings, inject `ISettingsSnapshot<T>`.

**Verify:** `dotnet app.dll check` passes. It fails with `MissingRequiredServiceException` when no caching provider is registered. Run `dotnet app.dll seed` (or `deploy`), then `seed verify` reports the `SettingDefinitions` seeder current. In a test, `GetAsync("App.MaxFileSize")` returns the definition's default with `Provider.Name == SettingValueProviderNames.DefaultValue`.

## Agent Rules

- Use settings for values that change at run time. For static configuration, use `IOptions<T>` or `IConfiguration`.
- Install all three packages. `Headless.Settings.Abstractions` alone runs nothing, and `Headless.Settings` needs a storage provider.
- Register a caching provider (`AddHeadlessCaching`), an `IDistributedLock` provider, and a `TimeProvider` before the host starts. A missing cache fails the start with `MissingRequiredServiceException`.
- Call `AddHeadlessSettings(...)` with exactly one storage provider. A second storage provider throws at startup. Calling `AddHeadlessSettings` again registers the core only once.
- Read with `GetAsync(name)`. It returns a non-null `SettingValue(Name, Value, Provider)`. On a miss, `Value` and `Provider` are both `null`. On a hit, `Provider` is a `SettingValueProvider(Name, Key)` naming the provider that resolved the value. `GetAsync` throws `ConflictException` (`setting:not_defined`) for an undefined setting.
- Write with `SetAsync(name, value, providerName, providerKey)`, or a scoped helper such as `SetForTenantAsync`. To write several values that form one policy (a limit and its window), call the dictionary `SetAsync` or `SetAllGlobalAsync` once, not one `SetGlobalAsync` per value. See [Writing values](#writing-values).
- Name providers with the `SettingValueProviderNames` constants: `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User`. The default-value constant is `DefaultValue`, not `Default`.
- To read application-wide policy, use `GetAllGlobalAsync(settingNames, fallback)` or `GetAllAsync(settingNames, providerName, providerKey, fallback)`. The name-only `GetAllAsync(settingNames)` walks the whole chain, so a Tenant or User value shadows the Global one and the caller gets its own value instead of the platform's.
- To hold a Global policy in memory, register `AddSettingsSnapshot<T>(...)` and inject `ISettingsSnapshot<T>`. Do not hand-write a cache, a `SettingChangedMessage` consumer, a poller, and a revision. See [Settings snapshot](#settings-snapshot).
- For an edit form that must not overwrite another admin's save, read with `GetVersionedAsync(names, providerName, providerKey)` and save with `SetVersionedAsync(values, providerName, providerKey, version)`. Do not compare values yourself and then call `SetAsync`: the check and the write are not atomic. See [Editing a group with optimistic concurrency](#editing-a-group-with-optimistic-concurrency).
- An `ISettingManager` write commits on its own connection and does not join a unit of work the caller has open. To write inside the unit, call `unit.Settings.SetAsync(...)`. See [Writing settings inside a unit of work](#writing-settings-inside-a-unit-of-work).
- For a secret, set `IsEncrypted = true` on the definition and register `AddHeadlessEncryption(...)`. Reading or writing an encrypted setting without it throws `InvalidOperationException`. See [Encrypted settings](#encrypted-settings).
- Do not call `IDynamicSettingDefinitionStore.SaveAsync` to persist definitions. The `SettingDefinitions` seeder does that when the deployment runs `seed`. See [Definition Seeding](#definition-seeding).
- To remove every value of a deleted tenant or user, call `DeleteAsync(providerName, providerKey)`.
- Write through `ISettingManager`, `unit.Settings`, or the EF `ISettingValueRecordRepository` only. A raw SQL write, a direct `DbContext` write, or a direct call to the PostgreSQL or SQL Server repository leaves the cache stale until `ValueCacheExpiration` (5 hours by default).
- Normalize setting names, provider names, and provider keys at your own boundary. Every read and write throws `ArgumentException` for a key with leading or trailing white space, a NUL character, or an unpaired UTF-16 surrogate.
- Create definitions through `ISettingDefinitionContext.Add(new SettingDefinitionCreateOptions { … })`. The `SettingDefinition` constructor is not public. `Add` returns the definition, so you can then change its `Providers` or `ExtraProperties`.

## Core Concepts

### Definitions and values

A setting definition holds a setting's metadata: `Name`, `DefaultValue`, `DisplayName`, `Description`, `IsEncrypted`, `IsInherited` (default `true`), `IsVisibleToClients` (default `false`), `Providers`, and `ExtraProperties`. An `ISettingDefinitionProvider` adds definitions at startup. A setting value is the stored value for one provider and key, such as one tenant or one user.

- `IsInherited = false` stops fallback below a provider the caller names. When that provider holds no value, `GetAsync(name, providerName, …)` returns a `null` `Value`, whatever lower providers hold. `GetAsync(name)` with no provider named still resolves the whole chain, as `GetAllAsync(names)` does.
- `Providers` restricts which providers may hold a value. An empty list allows every provider. A write through another provider throws `ConflictException` with `setting:provider_not_allowed`, and every read skips a value another provider holds.
- Tenancy is a provider key, not a column. A tenant value is a value stored under provider `Tenant` with the tenant id as its key.

### Resolution order

The built-in providers, from lowest to highest priority:

| Provider | Reads | Writable |
| --- | --- | --- |
| `DefaultValue` | `SettingDefinition.DefaultValue` | no |
| `Configuration` | `IConfiguration["Settings:<name>"]` | no |
| `Global` | the application-wide stored value | yes |
| `Tenant` | the stored value for the current tenant, or the tenant id you pass | yes |
| `User` | the stored value for the current user, or the user id you pass | yes |

`GetAsync` walks the chain from the highest priority down and returns the first non-null value. A read that names a provider starts at that provider and falls back to every provider below it.

Place a custom provider with one of two calls. Both work whether they run before or after `AddHeadlessSettings`:

- `services.AddSettingValueProvider<T>()` adds `T` above `User`, so it has the highest priority.
- `services.AddSettingValueProviderBelow<T, THigher>()` adds `T` directly below `THigher`. A read at `THigher` falls back to `T`, and a read at `T` falls back to the providers below it. For example, `AddSettingValueProviderBelow<AccountSettingValueProvider, UserSettingValueProvider>()` gives `User`, `Account`, `Tenant`, `Global`, `Configuration`, `DefaultValue`.

Do not insert into `SettingManagementProvidersOptions.ValueProviders` from `PostConfigure` by hand. `AddSettingValueProviderBelow` does that, moves a provider already in the chain, and throws `InvalidOperationException` when the settings options are first resolved if `THigher` is not in the chain.

The Tenant level always resolves from the ambient tenant. A read or write for a named user from host scope (no current tenant) therefore resolves the user's fallback against the host's chain, not the user's tenant. To see what a user sees, read inside the user's tenant (`currentTenant.Change(tenantId)`).

### Writing values

- `SetAsync(values, providerName, providerKey)` takes an `IReadOnlyDictionary<string, string?>` keyed by setting name. A `null` value clears that setting. The single-name `SetAsync` is the one-entry case. `SetAllGlobalAsync(values)` is the same call with the Global provider filled in.
- The batch checks every name, the provider, and its writability before writing. An undefined name (`setting:not_defined`), an unknown provider (`setting:provider_not_found`), or a read-only provider (`setting:provider_is_readonly`) rejects the whole batch with `ConflictException` and changes nothing.
- The built-in stores write the batch in one transaction. A failed write leaves every value as it was. A successful write publishes one `SettingChangedMessage` listing every name. An empty dictionary writes and announces nothing.
- Atomicity holds per provider. When several registered providers share `providerName`, each writes the batch in its own transaction.
- A value written through `SetGlobalAsync<T>` or `SetAsync<T>` is JSON. To put a typed value in a batch, serialize it with `JsonSerializer.Serialize(value, JsonConstants.DefaultStorageJsonOptions)`.
- Clearing a value removes only the row stored under the exact key the provider resolves, not the setting under every key of that provider.
- When the value written equals what the next provider below supplies, the manager stores nothing, so the setting keeps inheriting. A write for a named user from host scope skips that comparison and stores the value. To clear a user value that equals what the user inherits, write inside the user's tenant.
- When a concurrent writer inserts or deletes one of the batch's rows during the write, the store reads again and retries, up to three attempts. The last writer wins.

Scoped helpers on `ISettingManager`:

- `GetForTenantAsync`, `SetForTenantAsync`, `GetAllForTenantAsync`, and their `*ForCurrentTenant*` variants.
- `GetForUserAsync`, `SetForUserAsync`, `GetAllForUserAsync`, and their `*ForCurrentUser*` variants.
- `GetGlobalAsync`, `SetGlobalAsync`, `GetAllGlobalAsync`, `SetAllGlobalAsync`.
- `GetDefaultAsync`, `GetAllDefaultAsync`, `GetInConfigurationAsync`, `GetAllInConfigurationAsync`.
- `IsTrueAsync`, `IsFalseAsync`, `GetAsync<T>` (deserializes JSON), and `SetAsync<T>` (serializes to JSON).

A non-generic scoped `Get*` helper returns the raw `string?`. A generic `Get*<T>` helper deserializes the JSON value to `T`. The `GetAll*` helpers return `IReadOnlyList<SettingValue>`.

### Value caching

Resolved values are cached in the registered `ICache` for `ValueCacheExpiration` (5 hours by default).

- A write through `ISettingManager`, the EF `ISettingValueRecordRepository`, or `unit.Settings` updates the cache, so the next read sees it. A `unit.Settings` write updates the cache only after its unit commits. A unit that rolls back leaves the cache as it was. A cache fault after the commit is logged, not thrown.
- The cache keeps the last committed value even when two writers' cache updates land in the opposite order to their commits. Each write costs one extra repository read for that.
- A hybrid cache's peers learn of a write through its backplane, so the window between nodes is the backplane's.

**Use the hybrid cache in production.** Every request that reads a setting reads this cache. With `UseHybrid`, a warm read costs no network trip. With a distributed-only cache (`UseRedis`), each setting read is one round trip on the request path, and a second read of the same name in the same request repeats it. [Permissions](permissions.md#grant-store-and-caching) gives the round-trip count of a whole tenant-scoped request.

### Static and dynamic definition stores

Static definitions come from the registered `ISettingDefinitionProvider` implementations and are built once, on first use.

The dynamic store reads definitions from the database, so definitions can change without a redeploy. It is off by default (`IsDynamicSettingStoreEnabled = false`). When it is on:

- Definitions are cached in process for `DynamicDefinitionsMemoryCacheExpiration` (30 seconds by default), then re-checked against a distributed stamp.
- The `SettingDefinitions` [warm-up](utilities.md#warm-up) loads the store after the host starts. The `warmup` readiness check reports `Unhealthy` until that load succeeds, and a failed load is retried in the background.

With the dynamic store off, the warm-up does nothing and does not hold readiness.

### Reacting to a change

For a typed value bound from Global settings, use a [settings snapshot](#settings-snapshot). It already includes this consumer, a backstop, and a revision. Write your own consumer only for state a snapshot does not fit, such as a value per tenant.

After each successful `SetAsync` or `DeleteAsync`, `ISettingManager` publishes one `SettingChangedMessage` over `IBus`, listing every name that call changed. The state a consumer refreshes lives in each process, so consume the message with an [every-instance consumer](messaging/consumers.md#every-instance-bus-delivery). Delivery is at most once, so reload after a gap in the subscription too.

`AddHeadlessSettings` declares the message as `headless.settings.changed`, contract version `1` (`SettingChangedMessage.MessageName`), so your consumer needs no `Message<T>` declaration. The host's naming conventions (`UseConventions`) do not rename it. Only `MessagingOptions.MessageNamePrefix` applies, as it does to every message.

```csharp
[BusConsumer("app.reload-limits", EveryInstance = true)]
public sealed class ReloadLimits(MyPolicyCache cache) : IConsume<SettingChangedMessage>, IOnSubscriptionEstablished
{
    public async ValueTask ConsumeAsync(ConsumeContext<SettingChangedMessage> context, CancellationToken ct)
    {
        var message = context.Message;

        // Scope matters: a per-user override must not invalidate application-wide policy.
        if (!string.Equals(message.ProviderName, SettingValueProviderNames.Global, StringComparison.Ordinal))
        {
            return;
        }

        if (message.SettingNames.Any(cache.Tracks))
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

**Verify:** in a test, write a Global value through `ISettingManager` and assert the consumer reloaded.

- **Contents.** The message carries `SettingNames`, `ProviderName`, `ProviderKey`, and `OriginHostName`, never values. Values would put the plaintext of an encrypted setting on the broker. A receiver re-reads instead, which works in any delivery order.
- **Origin.** `OriginHostName` is for logs and telemetry. Do not filter on it. The writing process holds copies too, so it must re-read like every other instance.
- **Without a bus.** `IBus` is optional. A host that never calls `AddHeadlessMessaging` publishes nothing. A failed publish is logged and never fails the write. In both cases a peer keeps its copy until it re-reads, so a consumer that must converge without a bus needs a periodic refresh.
- **After the commit.** An `ISettingManager` write commits before it returns, even inside a caller's `RunAsync(db, …)`, and the message goes out after that commit. A `unit.Settings` write publishes after its unit commits, under the tenant that made the write. A unit that rolls back publishes nothing. A crash between the commit and the publish loses the announcement.
- **Cache order.** With a hybrid cache, a peer can receive `SettingChangedMessage` before the cache invalidation, re-read, and get the old value from its local tier. If the first re-read shows no change, re-read again a few seconds later, or rely on a periodic refresh.

### Writing settings inside a unit of work

`unit.Settings` writes settings inside a unit of work. `Headless.Settings.Abstractions` adds it to `IUnitOfWork` (namespace `Headless.UnitOfWork`). It offers `SetAsync` (one name, a typed `SetAsync<T>`, or a dictionary batch), `SetGlobalAsync`, `SetForTenantAsync`, `SetForUserAsync`, and `DeleteAsync(providerName, providerKey)`. Each call runs on the unit's connection and transaction, so the value commits or rolls back with the caller's other writes:

```csharp
await factory.RunAsync(db, async (unit, ct) =>
{
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);
    await unit.Settings.SetForTenantAsync(tenant.Id, "App.Theme", "dark", cancellationToken: ct);
}, ct);
```

**Verify:** in a test, throw after `unit.Settings.SetForTenantAsync` inside `RunAsync` and assert `GetForTenantAsync` still returns the old value.

- **Which unit joins.** Any unit with a live relational transaction on the database the storage uses: an EF unit (`BeginAsync(db)`, `RunAsync(db, …)`) or a raw ADO.NET unit (`BeginAsync(connection)`), on any of the three stores. A unit on another database, a unit with no resource, or a completed unit throws `InvalidOperationException` before any write, and the unit stays usable. `unit.Settings` also throws `InvalidOperationException` when `AddHeadlessSettings` was not called.
- **Reads in the unit.** Fallback reads from a store-backed provider run inside the unit's transaction, so they see a value the same unit already wrote.
- **One attempt.** An enlisted batch is not retried on a concurrent-write collision. A failed statement aborts the unit's transaction, so the collision surfaces to the unit's owner, which decides whether to run the whole unit again.
- **After commit.** The committed values go to the cache and `SettingChangedMessage` is published. A rolled-back unit does neither.
- **Validation and encryption.** Definition and provider checks throw `ConflictException` before the unit is touched. An encrypted setting is encrypted as `ISettingManager.SetAsync` encrypts it.
- **Replay.** Inside an owned `RunAsync` block, the writes replay with the block. Inside an observed unit (a domain-event handler in the `HeadlessDbContext` save pipeline), the first write calls `IUnitOfWork.PreventRetry()`, because the pipeline's replay cannot restore rows written outside its change tracker.
- **EF with a retrying execution strategy.** A context configured with `EnableRetryOnFailure` joins only from inside `RunAsync(db, …)`. From a raw ADO.NET unit it throws `InvalidOperationException` naming `RunAsync(db, …)`.
- **Custom providers.** A custom `ISettingValueProvider` joins a unit by overriding `SetAllEnlistedAsync`, and a custom `ISettingValueRecordRepository` by overriding its `*EnlistedAsync` members. The defaults throw `NotSupportedException`. A custom read provider that reads the unit's database overrides `GetOrDefaultEnlistedAsync`. A host that replaces `ISettingManager` keeps the framework's enlisted path.

### Editing a group with optimistic concurrency

An edit form reads a group of settings for one scope, lets an admin change them, and saves them back. `GetVersionedAsync` returns the values and the group's version. `SetVersionedAsync` saves only while the group still has that version. Otherwise it throws `ConflictException` with the error code `g:setting_version_mismatch` and writes nothing. Over HTTP, the version is the `ETag`, and the save takes it in `If-Match`. The Headless API exception handler maps the `ConflictException` to 409.

```csharp
HashSet<string> branding = ["App.Theme", "App.LogoUrl"];

app.MapGet("/tenants/{tenantId}/settings/branding", async (string tenantId, ISettingManager settings, HttpResponse response, CancellationToken ct) =>
{
    var group = await settings.GetVersionedAsync(branding, SettingValueProviderNames.Tenant, tenantId, cancellationToken: ct);
    response.Headers.ETag = $"\"{group.Version}\"";

    return Results.Ok(group.Values.ToDictionary(x => x.Name, x => x.Value));
});

app.MapPut("/tenants/{tenantId}/settings/branding", async (string tenantId, Dictionary<string, string?> body, HttpRequest request, HttpResponse response, ISettingManager settings, CancellationToken ct) =>
{
    var version = request.Headers.IfMatch.ToString().Trim('"');

    if (version.Length == 0)
    {
        return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
    }

    // Every name of the group, so an omitted name clears its value instead of failing the version check.
    var values = branding.ToDictionary(name => name, name => body.GetValueOrDefault(name), StringComparer.Ordinal);
    var savedVersion = await settings.SetVersionedAsync(values, SettingValueProviderNames.Tenant, tenantId, version, forceToSet: true, cancellationToken: ct);
    response.Headers.ETag = $"\"{savedVersion}\"";

    return Results.NoContent();
});
```

**Verify:** in a test, call `GetVersionedAsync`, write one of the names with `SetForTenantAsync`, then call `SetVersionedAsync` with the version you read. It throws `ConflictException` whose `Errors[0].Code` is `g:setting_version_mismatch`, and `GetForTenantAsync` still returns the value the other write stored.

- **The group.** The version covers the names you pass, in one provider scope (`providerName` and the key the provider resolves). Two forms over different names do not block each other. To lock a whole scope, pass every name the form shows.
- **Same names on save.** Save exactly the names you read. Another name set has another version, so a save with fewer or more names throws `g:setting_version_mismatch`.
- **Stored representation.** The version covers the current local rows, not write history. An unchanged save writes nothing and returns the same token. An absent → set → cleared sequence can reuse its original token.
- **Scope-local editing.** `GetVersionedAsync` defaults to `fallback: false`, so it returns only what the scope stores. Use `forceToSet: true` on saves as above. The save then preserves explicit local values even when they equal an inherited value. Do not pass `fallback: true` for an edit form: the version never covers inherited values, so a form that saves an inherited value back unchanged stores it as a local override, even after the inherited value changed, and no version mismatch reports it. If you display inherited values, read them separately and never submit them as local values.
- **Read from storage.** `GetVersionedAsync` reads the scope's values from the database, not the cache, so the values and the version come from one read. Encrypted values come back decrypted.
- **Atomicity.** The check and the write run in one serializable transaction. Of two saves that change the same group from one version, at most one succeeds. A serialization, deadlock, or unique-violation failure, including one reported at commit, triggers a fresh storage read. A changed version produces a version conflict. An unchanged version permits another attempt, up to three attempts. When the third attempt also conflicts, the save throws the same version-mismatch `ConflictException`, so the client re-reads and retries instead of receiving a 500. A lost commit reply never triggers replay. If storage confirms the intended representation, the save returns its version. Otherwise, the original failure propagates.
- **Returned version.** `SetVersionedAsync` returns the resulting token computed inside its transaction. Return that token as the PUT response's ETag, rather than issuing a later GET that could observe another writer. A cache-maintenance failure after a confirmed commit is logged and does not reject the save. Stale cache entries expire normally.
- **After the commit.** Every write, versioned or not, refreshes the cache and announces the change to other hosts even when the caller's cancellation token fires after the commit. A client that disconnects mid-save therefore cannot leave hosts serving the old value.
- **Not in a unit of work.** `unit.Settings` has no versioned write, because the save owns its serializable transaction, its retries, and its commit. Do not wrap `SetVersionedAsync` in your own transaction or `TransactionScope`: it would not join it, and a rollback of yours would not undo it. A versioned save commits on its own connection.
- **Supported stores.** The EF Core, PostgreSQL, and SQL Server storage support it. A custom `ISettingValueProvider` must implement `GetAllVersionedAsync` and `SetAllVersionedAsync`, and a custom `ISettingValueRecordRepository` must implement `SaveIfVersionAsync` and compute the version with `SettingValueVersion.Compute`. Their defaults throw `NotSupportedException`, so a versioned call never silently runs unversioned. More than one provider registered under the same name also throws `NotSupportedException`.

### Settings snapshot

A settings snapshot holds a typed value bound from a fixed set of Global settings, in memory in every process, and keeps it current. Register one per type with `AddSettingsSnapshot<T>` and inject `ISettingsSnapshot<T>`:

```csharp
builder.Services.AddSettingsSnapshot<RateLimitPolicy>(snapshot => snapshot
    .Names(RateLimitSettings.PublicPerMinute, RateLimitSettings.AuthenticatedPerMinute)
    .Bind(values => new RateLimitPolicy(
        int.Parse(values[RateLimitSettings.PublicPerMinute] ?? "60", CultureInfo.InvariantCulture),
        int.Parse(values[RateLimitSettings.AuthenticatedPerMinute] ?? "600", CultureInfo.InvariantCulture)))
    .Backstop(TimeSpan.FromMinutes(1)));

public sealed class RateLimitPartitioner(ISettingsSnapshot<RateLimitPolicy> policy)
{
    // Completes synchronously once the snapshot has loaded; before that it waits for the first load.
    public ValueTask<RateLimitPolicy> GetPolicyAsync(CancellationToken ct) => policy.GetAsync(ct);

    // Moves only when a value changed, so a partition keyed on it is not reset by a reload that changed nothing.
    public long Revision => policy.Revision;
}
```

**Verify:** in a test, `await snapshot.GetAsync(ct)` returns the bound value and `Revision` is `1`. After `SetAllGlobalAsync` changes a tracked name, `Revision` moves to `2`.

- **Registration.** Registering the same `T` twice, or omitting `Names` or `Bind`, throws `InvalidOperationException`. A snapshot without `AddHeadlessSettings` fails the start with `MissingRequiredServiceException`.
- **Scope.** Values resolve at `Global` scope, with fallback to configuration and the definition default, so a tenant or user value never shadows them. A setting with `IsInherited = false` reads the `Global` value only. A per-tenant value is not a snapshot's job. Write your own consumer for it.
- **Bind.** `Bind` receives every tracked name mapped to its resolved string, or `null` when the setting has no value and no default. It runs only when a value changed.
- **Revision.** `Revision` is `1` after the first load and moves by one each time a resolved value changes. A reload that reads the same values keeps the same instance and revision. The comparison is on the raw setting values, so it does not depend on the equality of `T`.
- **Loading.** A snapshot never blocks host startup. The first load starts in the background when the host runs. A failed first load is retried with jittered exponential back-off from one second up to the backstop interval, and each failure logs a warning. Names defined only in the dynamic store fail until a seed run writes their definitions. An undefined setting name makes every load throw, naming it.
- **Reads.** `GetAsync` returns the value, and waits for (or starts) the first load when it has not finished. A failed first load throws to that caller, and the next call tries again. `TryGetCurrent(out value)` is the synchronous read for code that cannot await, such as a rate-limiter partition factory. It returns `false` until the first load completes and never throws, so the caller picks a safe built-in default before then.
- **Bad values later.** A `Bind` exception after the first load is logged with the setting names, never their values. The snapshot keeps its last good value and revision.
- **Listeners.** `OnChange((value, revision) => ...)` runs after `TryGetCurrent` and `Revision` show the change. A listener exception is logged and does not stop the other listeners. Dispose the returned handle to unregister.
- **Change announcements.** With messaging, the framework adds the every-instance consumer `headless.settings.snapshot`. It reloads every snapshot that tracks an announced name at `Global` scope, in every process including the writer. It reloads every loaded snapshot each time its subscription is established. Each announced name whose value stayed the same keeps the snapshot rereading for a few seconds, even when another announced name already changed. See [Reacting to a change](#reacting-to-a-change).
- **Transport.** That consumer needs every-instance delivery. A host on a transport without it (AWS SNS/SQS, or Azure Service Bus without `AutoProvision`) fails at startup once it registers a snapshot. Run such a host's snapshots without messaging.
- **Backstop.** Each process re-reads every snapshot on its backstop interval: one minute by default, at most 30 days (`SettingsSnapshotBuilder<T>.MaxBackstop`), jittered by ±10%. It is the only refresh without messaging, and it catches lost announcements and configuration changes. The re-read goes through the value cache, so a write that bypasses the cache shows only after `ValueCacheExpiration`.
- **Several replicas.** Convergence across processes needs a shared (Redis) or hybrid cache. With a process-local cache (`UseInMemory`), another replica reads its cached value until it expires, through announcement, re-read, and backstop alike.
- **Tests.** Replace the snapshot with `TestSettingsSnapshot<T>` from [Headless.Settings.Testing](#headlesssettingstesting), so the test sets the value instead of loading it.

### Encrypted settings

A definition with `IsEncrypted = true` stores its value through `IFieldEncryptor` from [Encryption](encryption.md), so the host registers `AddHeadlessEncryption(...)` with a key store and a key-encryption key. A host with no encrypted setting needs no encryption setup. The first read or write of an encrypted setting without it throws `InvalidOperationException` naming `AddHeadlessEncryption`.

- **Purpose.** Each value is bound to `settings:{name}`, so a value copied to another setting's row fails to decrypt.
- **Key scope.** A value stored by the `Tenant` provider uses that tenant's key: the tenant id passed to `SetForTenantAsync` or `GetForTenantAsync`, else the current tenant. `Global`, `User`, and custom providers use the host key. With no tenant id and no current tenant, a Tenant value throws `MissingTenantContextException`.
- **Shredding.** `IDataKeyManager.ShredAsync(tenantId)` also destroys the tenant's encrypted settings. Reading one afterwards throws `ConflictException` with `setting:decryption_failed`, the same error a corrupt or foreign value gets. Writing one throws `DataKeyScopeShreddedException` until `IDataKeyManager.EnsureAsync(tenantId)` reopens the scope.
- **Stored size.** The value column holds the frame as base64url text: 46 bytes plus the UTF-8 value, before encoding. An encrypted value fits the default 2,000-character value column only up to 1,454 UTF-8 bytes. Each write draws a fresh nonce, so the stored text changes on every write of the same value.
- **Only stored values decrypt.** A plaintext `DefaultValue` or `IConfiguration` value resolved through fallback is returned as it is, on every read path.
- **Fallback clearing.** For an encrypted setting, the inherited-value comparison runs on the plaintext.

To change how values are protected, replace `ISettingEncryptionService`. Its `EncryptAsync` and `DecryptAsync` take the definition, the value, and the provider name and key that decide the scope.

### Definition Seeding

`AddHeadlessSettings` registers an `ISeeder` named `SettingDefinitions`. When `SaveStaticSettingsToDatabase` is `true` (the default), it saves the static definitions to the database under a distributed lock. Re-seeding unchanged definitions leaves the database untouched and repairs a missed cache stamp update after a committed save.

- **Retries.** A failure is retried up to 10 times with jittered exponential back-off capped at 30 seconds, on the system clock even when the host registers a `FakeTimeProvider`. Cancellation, `ArgumentException`, and `NotSupportedException` fail at once. Any other failure fails the seed run after the last retry.
- **When it runs.** The host start does not seed. In production, run `seed` (or `deploy`) in the deployment. In development, call `services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None)`. `HeadlessTestServer` seeds by default. See [Database Seeders](utilities.md#database-seeders).
- **Extra properties.** `ExtraProperties` keys keep their case. Values read back as `string`, `bool`, `long`, `decimal`, or `JsonElement`. Read them with `GetProperty<T>(name)`.
- **Deleting definitions.** Add the name to `SettingManagementProvidersOptions.DeletedSettings`. The seeder removes it on its next run.

**Verify:** `dotnet app.dll seed verify` reports `SettingDefinitions` current.

A host that never seeds still serves its static definitions from memory. But definitions in `DeletedSettings` stay in the database, and other applications sharing the database keep serving stale definitions.

### Client-visible settings

`IClientVisibleSettingsReader.GetAsync(PrincipalContext, …)` returns the value of every setting whose definition has `IsVisibleToClients = true`, keyed by name, in one read. Use it to build the configuration an application returns to its front end. Headless ships no endpoint. See the [client-config recipe](permissions.md#client-config-for-spas).

- It resolves for the principal and tenant in its `PrincipalContext`, not the ambient ones, and restores the ambient ones afterwards.
- An encrypted setting marked visible is returned decrypted. Mark a secret visible only when the client is meant to read it.

### Custom value providers

Implement `ISettingValueReadProvider` (read-only) or `ISettingValueProvider` (read-write), and register it with `services.AddSettingValueProvider<T>()` (highest priority) or `services.AddSettingValueProviderBelow<T, THigher>()` (directly below `THigher`), as [Resolution order](#resolution-order) describes. `AddSettingValueProvider<T>()` is idempotent by type, and among several such calls the last one has the highest priority. `ISettingManager` writes through `ISettingValueProvider.SetAllAsync`. Its default calls `SetAsync` or `ClearAsync` once per entry, so override it when your source can apply a batch atomically.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Settings.Storage.EntityFramework` | You use EF Core and manage the schema with EF migrations | You want no EF Core dependency | Works on any EF Core database. You add the tables through your own migrations |
| `Headless.Settings.Storage.PostgreSql` | You use PostgreSQL without EF Core | You run SQL Server or manage the schema with EF migrations | `resources provision` creates the tables |
| `Headless.Settings.Storage.SqlServer` | You use SQL Server without EF Core | You run PostgreSQL or manage the schema with EF migrations | `resources provision` creates the tables |

---

## Headless.Settings.Abstractions

The contracts application code calls: `ISettingManager` and its scoped helpers, `ISettingDefinitionProvider`, `ISettingDefinitionContext`, `SettingDefinitionCreateOptions`, `SettingValue`, `SettingValueProvider`, `SettingValueProviderNames`, `ISettingsSnapshot<T>`, `SettingChangedMessage`, and the `unit.Settings` accessor.

```bash
dotnet add package Headless.Settings.Abstractions
```

```csharp
public sealed class AppSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinitionCreateOptions
        {
            Name = "App.MaxFileSize",
            DefaultValue = "10485760",
            DisplayName = "Maximum File Size",
        });

        context.Add(new SettingDefinitionCreateOptions
        {
            Name = "App.ApiKey",
            DisplayName = "API Key",
            IsEncrypted = true,
        });
    }
}

public sealed class NotificationService(ISettingManager settingManager)
{
    public Task<bool> IsEmailEnabledAsync(CancellationToken ct) =>
        settingManager.IsTrueAsync("Notifications.EmailEnabled", cancellationToken: ct);

    public Task SetTenantPreferenceAsync(string tenantId, string settingName, string value, CancellationToken ct) =>
        settingManager.SetForTenantAsync(tenantId, settingName, value, cancellationToken: ct);

    public Task SetUserPreferenceAsync(string userId, string settingName, string value, CancellationToken ct) =>
        settingManager.SetAsync(settingName, value, SettingValueProviderNames.User, userId, cancellationToken: ct);
}
```

**Verify:** none in this package. The registration in [Headless.Settings](#headlesssettings) supplies the implementation.

---

## Headless.Settings

The `ISettingManager` implementation, the built-in value providers, the value cache, encrypted settings, snapshots, and the `SettingDefinitions` seeder.

```bash
dotnet add package Headless.Settings
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();

builder.Services.AddSettingsSnapshot<UploadPolicy>(snapshot => snapshot
    .Names("App.MaxFileSize")
    .Bind(values => new UploadPolicy(
        long.Parse(values["App.MaxFileSize"] ?? "10485760", CultureInfo.InvariantCulture))));
```

**Verify:** `dotnet app.dll check` passes. Without `AddHeadlessCaching`, it fails with `MissingRequiredServiceException` naming the cache. With `IsDynamicSettingStoreEnabled = true`, the `warmup` readiness check reports `Healthy` once the definitions load.

The caching provider is required because this package references only `Headless.Caching.Abstractions`. Registration order does not matter, because the check runs at host start.

`AddHeadlessSettings` also registers `IUnitOfWorkFactory` and an AsyncLocal-backed `ICurrentTenant` fallback, which any real tenancy registration replaces.

### Configuration

Encrypted settings take their configuration from [Encryption](encryption.md). Settings itself reads no encryption section.

#### SettingManagementOptions

Set them with `setup.ConfigureManagement(options => …)` (or its `(options, IServiceProvider)` overload) inside `AddHeadlessSettings`, or with `services.Configure<SettingManagementOptions>(...)`:

| Option | Default | Notes |
| --- | --- | --- |
| `ValueCacheExpiration` | 5 hours | Lifetime of a cached value. Bounds how long a write that bypasses the cache stays invisible |
| `IsDynamicSettingStoreEnabled` | `false` | Read definitions from the database. See [Static and dynamic definition stores](#static-and-dynamic-definition-stores) |
| `SaveStaticSettingsToDatabase` | `true` | The `SettingDefinitions` seeder saves static definitions when the host seeds |
| `DynamicDefinitionsMemoryCacheExpiration` | 30 seconds | How long dynamic definitions stay in process before the distributed stamp is re-checked |
| `CrossApplicationsCommonLockKey` | `settings:common_update_lock` | Distributed lock key for definition saves shared across applications |
| `CrossApplicationsCommonLockExpiration`, `ApplicationSaveLockExpiration` | 10 minutes | Lock lifetimes for definition saves |
| `CrossApplicationsCommonLockAcquireTimeout`, `ApplicationSaveLockAcquireTimeout` | 5 minutes | How long a definition save waits for its lock |

The validator rejects an empty lock key and a zero or negative expiry.

#### SettingsStorageOptions

Set schema and table names with `setup.ConfigureStorage(o => …)`, or bind them with `setup.ConfigureStorage(configurationSection)`:

```csharp
services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "headless"; // default, shared by every Headless feature
        o.SettingValuesTableName = null; // default: setting_values on PostgreSQL, SettingValues elsewhere
        o.SettingDefinitionsTableName = null; // default: setting_definitions or SettingDefinitions
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

Every object follows its database's naming convention. On PostgreSQL, tables, columns, keys, and indexes are snake_case (`setting_values`, `provider_key`, `ix_setting_values_name_provider_name_provider_key`). On SQL Server and other databases they are PascalCase (`SettingValues`, `ProviderKey`, `IX_SettingValues_Name_ProviderName_ProviderKey`). A table name you set is used as written, and its key and index names derive from it (`pk_MyValues`). The raw providers and the EF Core mapping produce the same names on the same database. PostgreSQL truncates identifiers longer than 63 bytes, so every provider refuses a table name whose longest derived name would exceed that: at most 23 characters for the values table and 55 for the definitions table.

##### Column lengths

Each string column's maximum length is an option on `SettingsStorageOptions`:

| Option | Column | Default |
| --- | --- | --- |
| `SettingValuesNameMaxLength` | values `Name` | 128 |
| `SettingValuesValueMaxLength` | values `Value` | 2000 |
| `SettingValuesProviderNameMaxLength` | values `ProviderName` | 64 |
| `SettingValuesProviderKeyMaxLength` | values `ProviderKey` | 64 |
| `SettingDefinitionsNameMaxLength` | definitions `Name` | 128 |
| `SettingDefinitionsDisplayNameMaxLength` | definitions `DisplayName` | 256 |
| `SettingDefinitionsDescriptionMaxLength` | definitions `Description` | 512 |
| `SettingDefinitionsDefaultValueMaxLength` | definitions `DefaultValue` | 2000 |
| `SettingDefinitionsProvidersMaxLength` | definitions `Providers` | 1024 |

```csharp
setup.ConfigureStorage(o => o.SettingValuesProviderKeyMaxLength = 128);
```

- Each value must be between 1 and 4000. Anything else throws when the options are first read.
- One option sizes the EF Core model (`HasMaxLength`), the raw providers' tables, and their query parameters. Set it before the tables are created.
- A definition that does not fit throws `ArgumentOutOfRangeException` when definitions are saved, not at the database.
- The values table's unique indexes cover `Name`, `ProviderName`, and `ProviderKey`, and the definitions table indexes `Name`. SQL Server limits an index key to 1,700 bytes (850 `nvarchar` characters), so the three values lengths together, and the definitions `Name` length, must each total at most 850. The registration refuses a wider configuration when the options are first read, on every provider, so one configuration stays valid on each database. `Value` is not indexed.
- `SettingDefinitionsDefaultValueMaxLength` is separate from `SettingValuesValueMaxLength`. Raise both when values and defaults grow together.

**Changing a length after the tables exist needs a migration in your application.** The option does not alter an existing column. Change the option and the column in the same release:

- **EF Core storage:** change the option, then add a migration (`dotnet ef migrations add WidenSettingValueProviderKeyColumn`). The migration contains the `AlterColumn`.
- **Raw PostgreSQL or SQL Server storage:** run the `ALTER` with the tool that owns your schema, then deploy the option. `resources provision` does not widen a column.

  ```sql
  -- PostgreSQL: widening a varchar changes metadata only and keeps its indexes
  ALTER TABLE headless.setting_values ALTER COLUMN provider_key TYPE character varying(128);

  -- SQL Server: restate the nullability, or the column takes the session default
  ALTER TABLE [headless].[SettingValues] ALTER COLUMN [ProviderKey] nvarchar(128) NULL;
  ```

Widen the column before, or together with, the code that writes longer values. Narrowing a column that holds longer values fails on both databases, so shorten the data first.

---

## Headless.Settings.Storage.EntityFramework

EF Core storage for setting values and definitions, in your own `DbContext`.

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureHeadlessSettings(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_settings");
    setup.UseEntityFramework<AppDbContext>();
});
```

Then add an EF Core migration for the settings tables and apply it as you apply every migration. This provider writes no DDL.

**Verify:** `dotnet app.dll check` passes. Without `ConfigureHeadlessSettings(this)`, it fails with `InvalidOperationException`: "the registered DbContext … does not contain …. Call `modelBuilder.ConfigureHeadlessSettings(this)` in your `OnModelCreating`." A scoped or transient context factory fails with `InvalidServiceLifetimeException`.

### Configuration

- Register a singleton `IDbContextFactory<TContext>`: `AddDbContextFactory<TContext>()` or `AddPooledDbContextFactory<TContext>()` for a plain `DbContext`, or `AddHeadlessDbContext<TContext>()` or `AddHeadlessDbContextPool<TContext>()` for a `HeadlessDbContext`. All four register a singleton factory by default.
- `modelBuilder.ConfigureHeadlessSettings(this)` reads `SettingsStorageOptions` from the context's service provider and picks the naming style from the database provider: snake_case on Npgsql, PascalCase elsewhere. When you already hold the options, call `ConfigureHeadlessSettings(options, HeadlessStorageNaming.ForProvider(Database.ProviderName))` (namespace `Headless.Hosting`).
- `SettingsStorageOptions` defaults: `Schema = "headless"`, shared by every Headless feature (see [Shared connection and schema](sql.md#shared-connection-and-schema-for-storage-features)). Table names default by convention. See [Column lengths](#column-lengths).
- Identifier names are validated against cross-provider rules.

### Design and runtime behavior

- Reads use a fresh context from the factory, with `AsNoTracking()`.
- Writes commit through a fresh context the repository owns, so they do not join your outer transaction. To join, use [`unit.Settings`](#writing-settings-inside-a-unit-of-work).
- Value uniqueness is a pair of filtered unique indexes: on (`Name`, `ProviderName`, `ProviderKey`) where the key is not null, and on (`Name`, `ProviderName`) where it is null. Global values (null key) stay unique on databases that treat nulls as distinct.

---

## Headless.Settings.Storage.PostgreSql

PostgreSQL storage for settings, with no EF Core dependency. `resources provision` creates its tables.

```bash
dotnet add package Headless.Settings.Storage.PostgreSql
```

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

builder.Services.AddPostgreSqlSql(connectionString);
builder.Services.AddHeadlessSettings(setup => setup.UsePostgreSql());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_settings");
    setup.UsePostgreSql(connectionString);
});
```

**Verify:** `dotnet app.dll resources provision` creates the tables, and `resources verify` then passes. A host started without provisioning fails, naming the missing step (see [Resource provisioning](utilities.md#resource-provisioning)).

### Configuration

`UsePostgreSql()` reads the connection registered by `AddPostgreSqlSql`, so one connection string serves every feature (see [Shared connection and schema](sql.md#shared-connection-and-schema-for-storage-features)). The other overloads take a connection string, an `IConfiguration` section, an `Action<PostgreSqlSettingsOptions>`, or an `Action<PostgreSqlSettingsOptions, IServiceProvider>`:

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

| Option | Default | Description |
| --- | --- | --- |
| `ConnectionString` | `""` | PostgreSQL connection string. Required |
| `CommandTimeout` | 30 seconds | Timeout for each command |

Set schema and table names through `setup.ConfigureStorage(...)`.

### Design and runtime behavior

- The tables and indexes are two schema steps, `Settings/1` (tables) and `Settings/2` (indexes), that `resources provision` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). Names are snake_case.
- A host start only verifies the steps and fails fast when one is missing.
- A direct call to this provider's value repository bypasses the value cache. Write through `ISettingManager` or `unit.Settings`.

---

## Headless.Settings.Storage.SqlServer

SQL Server storage for settings, with no EF Core dependency. `resources provision` creates its tables.

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

builder.Services.AddSqlServerSql(connectionString);
builder.Services.AddHeadlessSettings(setup => setup.UseSqlServer());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_settings");
    setup.UseSqlServer(connectionString);
});
```

**Verify:** `dotnet app.dll resources provision` creates the tables, and `resources verify` then passes. A host started without provisioning fails, naming the missing step.

### Configuration

`UseSqlServer()` reads the connection registered by `AddSqlServerSql`. The other overloads take a connection string, an `IConfiguration` section, an `Action<SqlServerSettingsOptions>`, or an `Action<SqlServerSettingsOptions, IServiceProvider>`:

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

| Option | Default | Description |
| --- | --- | --- |
| `ConnectionString` | `""` | SQL Server connection string. Required |
| `CommandTimeout` | 30 seconds | Timeout for each command |

Set schema and table names through `setup.ConfigureStorage(...)`.

### Design and runtime behavior

- The tables and indexes are two schema steps, `Settings/1` (tables) and `Settings/2` (indexes), that `resources provision` applies. Names are PascalCase.
- A host start only verifies the steps and fails fast when one is missing.
- SQL Server ignores trailing spaces when it compares keys. The store refuses such keys on every provider, so a value never resolves differently between PostgreSQL and SQL Server.
- A direct call to this provider's value repository bypasses the value cache. Write through `ISettingManager` or `unit.Settings`.

---

## Headless.Settings.Testing

Test doubles for settings.

```bash
dotnet add package Headless.Settings.Testing
```

```csharp
var snapshot = new TestSettingsSnapshot<RateLimitPolicy>(new RateLimitPolicy(PermitLimit: 10));

// Replace the snapshot the application registered with AddSettingsSnapshot<RateLimitPolicy>.
builder.Services.RemoveAll<ISettingsSnapshot<RateLimitPolicy>>();
builder.Services.AddSingleton<ISettingsSnapshot<RateLimitPolicy>>(snapshot);

// Simulate an operator changing the setting.
snapshot.Set(new RateLimitPolicy(PermitLimit: 20));
```

**Verify:** the code under test reads `PermitLimit` 20 after `snapshot.Set`, and `snapshot.Revision` is `2`.

### Design and runtime behavior

- `new TestSettingsSnapshot<T>()` starts unloaded: `TryGetCurrent` returns `false` and `Revision` is `0`. `new TestSettingsSnapshot<T>(value)` starts loaded at revision `1`.
- `Set(value)` publishes the value, moves `Revision` by one, calls each `OnChange` listener with the new value and revision, and returns the new revision. Every call counts as a change, including one with an equal value.
- `GetAsync` throws `InvalidOperationException` before a value is set, where the real snapshot waits for its first load. A test that forgot to set a value fails at once instead of hanging.
- A listener exception propagates from `Set` as an `AggregateException` after every listener ran, where the real snapshot logs it.
