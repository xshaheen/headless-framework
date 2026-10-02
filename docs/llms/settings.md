---
domain: Settings
packages: Settings.Abstractions, Settings.Core, Settings.Storage.EntityFramework, Settings.Storage.PostgreSql, Settings.Storage.SqlServer
---

# Settings

> Dynamic, hierarchical application settings with runtime read/write support and multiple value providers (DefaultValue, Configuration, Global, Tenant, User) resolved from lowest to highest priority.

## Orientation

Install three packages: an abstractions package, the core implementation, and exactly one storage provider:

- `Headless.Settings.Abstractions` — interfaces (`ISettingManager`, `ISettingDefinitionProvider`, `SettingDefinition`)
- `Headless.Settings.Core` — full implementation with hierarchical providers, caching, encryption, background init
- one storage provider: `Headless.Settings.Storage.EntityFramework`, `Headless.Settings.Storage.PostgreSql`, or `Headless.Settings.Storage.SqlServer`

Minimal wiring:

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

// AddHeadlessSettings registers the management core automatically.
builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

Define settings via `ISettingDefinitionProvider.Define()`. Read via `ISettingManager.GetAsync()`, write via `SetAsync()`. Provider hierarchy resolves from lowest to highest priority: DefaultValue → Configuration → Global → Tenant → User (User wins).

## Agent Rules

- Use this for **runtime-changeable settings**, not for static configuration. For static config, use `IOptions<T>` / `IConfiguration`.
- Always install all three packages together. `Headless.Settings.Abstractions` alone gives nothing runnable; `Headless.Settings.Core` requires a storage backend.
- `ISettingManager` is the primary entry point. Call `GetAsync(name)` to read; `SetAsync(name, value, providerName, providerKey)` to write. `GetAsync` returns a never-`null` `SettingValue(Name, Value, Provider)`: on a miss both `Value` and `Provider` are `null`; on a hit `Provider` (a `SettingValueProvider(Name, Key)`) identifies the resolving provider and its per-provider key. It still throws `ConflictException` when the setting is undefined.
- To write several settings at once, call `SetAsync(values, providerName, providerKey)` with an `IReadOnlyDictionary<string, string?>` keyed by setting name; a `null` value clears that setting. It checks every name, the provider, and its writability before writing anything, so an undefined name or a read-only provider rejects the whole batch with `ConflictException` and changes nothing. The built-in stores (EF, PostgreSQL, SQL Server) then write the batch in one transaction, so a failed write leaves every value as it was, and a successful one publishes a single `SettingChangedMessage` listing every name. An empty dictionary writes and announces nothing. The single-name `SetAsync` is the one-entry case of this call. Clearing a value removes only the row stored under the exact provider key the provider resolves, not that setting under every key of the provider. Atomicity holds per provider: when several registered providers share `providerName`, each writes the batch in its own transaction. The store-backed providers open their own connection and transaction, so the write does not join a unit of work the caller has open, and rolling that unit back does not undo it. When a concurrent writer inserts or deletes one of the batch's rows between the store's read and its save, the store reads again and retries (up to three attempts); the last writer's value wins.
- Provider names are constants on `SettingValueProviderNames`: `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User`. Note: the default-value constant is `DefaultValue`, not `Default`.
- Scoped extension members are available for each provider scope: `GetForTenantAsync` / `SetForTenantAsync`, `GetForUserAsync` / `SetForUserAsync`, `GetGlobalAsync` / `SetGlobalAsync`, `GetDefaultAsync`, `GetInConfigurationAsync`. The raw (non-generic) `Get*` scoped helpers return the unwrapped `string?` value; the generic `Get*<T>` helpers deserialize the JSON value to `T`. Use `IsTrueAsync` / `IsFalseAsync` / `GetAsync<T>` / `SetAsync<T>` on `ISettingManager` for typed or boolean reads.
- For sensitive settings, set `isEncrypted: true` on `SettingDefinition` — Core handles encryption/decryption via `IStringEncryptionService` automatically. Only values persisted by a store-backed provider (`Global`, `Tenant`, `User`) are decrypted on read; a plaintext `DefaultValue` or `IConfiguration` value resolved through fallback is returned as-is, on every read path (`GetAsync`, both `GetAllAsync` overloads).
- Core registers a `SettingsInitializationBackgroundService` hosted service — do not register your own init logic for settings. A host that must not touch the store at startup (a test host, a read-only replica) calls `setup.DisableStartupInitialization()`; static definitions stay available in memory and nothing else changes.
- `AddHeadlessSettings(...)` is the single entry point — it registers the management core automatically alongside the storage provider. Only one storage provider (EF / PostgreSQL / SqlServer) may be registered; a second registration throws at startup.
- To tune management options, call `setup.ConfigureManagement(options => ...)` inside the `AddHeadlessSettings` block. An `(options, IServiceProvider)` overload is available for late-bound configuration. `services.Configure<SettingManagementOptions>(...)` also works and composes regardless of call order.
- To tune schema and table names, call `setup.ConfigureStorage(o => ...)` inside the same block. The `IConfiguration` overload binds the `Headless:Settings:Storage` section instead.
- For EF storage: register `AddDbContextFactory<TContext>()` and call `modelBuilder.AddHeadlessSettings(this)` in `OnModelCreating` before calling `setup.UseEntityFramework<TContext>()`. The factory must be a singleton (the `AddDbContextFactory` / `AddPooledDbContextFactory` default): the EF repositories are singletons that would capture a scoped or transient factory for the life of the host, so startup refuses one with `InvalidServiceLifetimeException`. The `(SettingsStorageOptions)` overload exists when you already hold the options object.
- Required services before `AddHeadlessSettings(...)`: `TimeProvider`, caching (`ICache`), distributed lock (`IDistributedLock`), and `IStringEncryptionService`. The core throws `InvalidOperationException` on startup if encryption is missing.
- To keep an application-wide policy built from Global settings in memory, such as a rate limit an operator tunes at runtime, register `services.AddSettingsSnapshot<T>(...)` and inject `ISettingsSnapshot<T>`. Do not hand-write a cache, a `SettingChangedMessage` consumer, a poller, and a revision for it; see [Settings snapshot](#settings-snapshot).
- `DeleteAsync(providerName, providerKey)` removes all setting values for a given provider and key — use it when cleaning up a deleted tenant or user.
- Both `ISettingManager` and direct `ISettingValueRecordRepository` writes invalidate cached values (the repository removes the affected key after `SaveChangesAsync`). Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale.
- `SettingDefinition.IsInherited = false` disables fallback for that setting: if no value exists at the requested provider, `GetAsync` returns a `SettingValue` with a `null` `Value` regardless of lower-priority providers.
- `SettingDefinition` instances are minted through the `ISettingDefinitionContext.Add(options)` factory (the constructor is `internal`). The factory returns the created definition so you can then mutate `Providers` or `ExtraProperties` on it.
- Custom value providers must implement `ISettingValueReadProvider` (read-only) or `ISettingValueProvider` (read-write). Register with `services.AddSettingValueProvider<T>()`. The last-registered provider has the highest resolution priority. `SettingManager` writes through `ISettingValueProvider.SetAllAsync`; its default implementation calls `SetAsync` / `ClearAsync` once per entry, so a custom provider overrides it when its source can apply a batch atomically.

## Core Concepts

### Setting Definitions vs. Setting Values

A *setting definition* describes a setting's metadata: its name, default value, whether it is encrypted, whether it inherits from lower-priority providers (`IsInherited`), and whether clients may read it (`IsVisibleToClients`). Definitions are registered statically at startup via `ISettingDefinitionProvider` and optionally persisted to the database by `SettingsInitializationBackgroundService`. A *setting value* is the resolved runtime state of a setting for a specific subject (e.g., a specific tenant or user). Definitions live in `IStaticSettingDefinitionStore` / `IDynamicSettingDefinitionStore`; values live in the storage backend behind `ISettingValueStore` and `ISettingValueRecordRepository`.

### Value Providers and Resolution Order

Setting values are resolved by a chain of `ISettingValueReadProvider` implementations registered in priority order. The five built-in providers, from lowest to highest priority, are: `DefaultValue` (reads `SettingDefinition.DefaultValue`), `Configuration` (reads from `IConfiguration`), `Global` (application-wide store), `Tenant` (tenant-scoped store), and `User` (user-scoped store). `ISettingManager.GetAsync` walks the chain from highest to lowest priority and returns the first non-null result wrapped in a `SettingValue` whose `Provider` names the resolving provider. The last-registered provider has the highest priority. Custom providers added via `services.AddSettingValueProvider<T>()` are appended after `User` and therefore have the highest resolution priority.

### Tenancy Model

Settings do **not** carry a first-class `TenantId` column or implement `IMultiTenant`. Tenancy (and every other scope) is expressed uniformly through `ProviderName` / `ProviderKey` on `SettingValueRecord`: a tenant-scoped value is simply `ProviderName == "Tenant"` with the tenant id in `ProviderKey`. This is a deliberate divergence from `PermissionGrantRecord` (which does carry `TenantId`), not drift — one scoping value provider expresses tenant, user, global, and custom scopes without a dedicated column.

### Static Store vs. Dynamic Store

The *static store* (`IStaticSettingDefinitionStore`) builds the setting catalog once at startup by invoking all registered `ISettingDefinitionProvider` implementations. It is thread-safe and lazily initialized. The *dynamic store* (`IDynamicSettingDefinitionStore`) reads definitions from the database, caches them in-process with a configurable expiry (`DynamicDefinitionsMemoryCacheExpiration`, default 30 seconds), and coordinates cross-instance refreshes via a distributed cache stamp and a distributed lock. The dynamic store is disabled by default (`IsDynamicSettingStoreEnabled = false`); enable it only when setting definitions must be edited at runtime without redeployment.

### Setting Value Caching

`SettingValueStore` caches resolved setting values to avoid repeated database reads. The cache is backed by the registered `ICache`. When `ISettingManager.SetAsync` or `DeleteAsync` writes or removes a value, `SettingValueStore` updates or evicts the affected cache entries directly through `ICache` (a distributed cache propagates the eviction across nodes via `CacheInvalidationMessage`). Direct `ISettingValueRecordRepository` writes also evict the affected cache entry (removed after `SaveChangesAsync`), so a repository-level write is reflected on the next read. Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale.

### Reacting to a change

For a typed value bound from Global settings, use a [settings snapshot](#settings-snapshot): it is this consumer, a backstop, and a revision, already written. Write a consumer of your own only for state a snapshot does not fit, such as a value per tenant.

`SettingManager` publishes one `SettingChangedMessage` over `IBus` after each successful `SetAsync` or `DeleteAsync`, listing every name that call changed, so an instance holding a resolved value learns it is stale instead of polling for it. The state it refreshes lives in each process, so consume it with an [every-instance consumer](messaging.md#every-instance-bus-delivery): every process receives every announcement instead of one replica taking the only copy. Delivery is at most once, so reload after a gap in the subscription too. Wire contract: `AddHeadlessSettings` declares the message as `headless.settings.changed`, contract version `1` (`SettingChangedMessage.MessageName`), so the consumer below needs no `Message<T>` declaration of its own. The host's naming conventions (`UseConventions`) do not rename it; only the host-wide `MessagingOptions.MessageNamePrefix` applies, as it does to every message.

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

The message carries setting names and the scope they were written at, never values. Values would put the plaintext of an `IsEncrypted` setting on the broker and make delivery order load-bearing, so a receiver re-reads instead, which is idempotent and order-free. `OriginHostName` names the host that wrote the change (`IHostIdentityAccessor.HostName`) for logs and telemetry; do not filter on it. The writing process holds copies too, since the manager knows nothing about the field a consumer copied a value into, so the origin must re-read like every other instance.

`IBus` is optional. A host that never calls `AddHeadlessMessaging` writes settings exactly as before and publishes nothing, and a failed publish is logged and never fails the write that already succeeded. In both cases a peer keeps its copy until it re-reads for its own reasons, so a consumer that must converge without a bus still needs a periodic refresh.

The announcement follows a committed write. A setting write never joins a unit of work the caller has open: the EF store saves through a fresh context from `IDbContextFactory<TContext>`, and the PostgreSQL and SQL Server stores open their own connection and transaction. `SetAsync` commits before it returns, even inside a caller's `RunAsync(db, …)`, and the message goes out after that commit, so a peer that re-reads on it loads the new value. The write is not atomic with the caller's other writes: a `SetAsync` inside a unit that later rolls back leaves the setting changed.

This is separate from cache coherence, which is already handled: a store-backed write evicts its own cache entry, and a hybrid cache broadcasts that eviction through `CacheInvalidationMessage`. The change signal exists for state the framework cannot see, such as a value a consumer copied into a field of its own.

The two messages travel on separate subscriptions, and nothing orders them. With a hybrid cache, a peer can receive `SettingChangedMessage` before its own `CacheInvalidationMessage`, re-read, and get the old value from its local tier. A consumer that re-reads once on the announcement then stays stale until its next refresh. Re-read again a few seconds later when the first read shows no change, or rely on a periodic refresh.

### Settings snapshot

A settings snapshot holds a typed value bound from a fixed set of Global settings, in memory, in every process, and keeps it current. Register one per type with `AddSettingsSnapshot<T>` and inject `ISettingsSnapshot<T>`:

```csharp
builder.Services.AddSettingsSnapshot<RateLimitPolicy>(snapshot => snapshot
    .Names(RateLimitSettings.PublicPerMinute, RateLimitSettings.AuthenticatedPerMinute)
    .Bind(values => new RateLimitPolicy(
        int.Parse(values[RateLimitSettings.PublicPerMinute] ?? "60", CultureInfo.InvariantCulture),
        int.Parse(values[RateLimitSettings.AuthenticatedPerMinute] ?? "600", CultureInfo.InvariantCulture)))
    .Backstop(TimeSpan.FromMinutes(1)));

public sealed class RateLimitPartitioner(ISettingsSnapshot<RateLimitPolicy> policy)
{
    public RateLimitPolicy Current => policy.Current;

    // Moves only when a value changed, so a partition keyed on it is not reset by a reload that changed nothing.
    public long Revision => policy.Revision;
}
```

- **Scope.** Values resolve at `Global` scope with the usual fallback to configuration and the definition default, so a tenant or user value never shadows them. A setting defined with `IsInherited = false` reads the `Global` value only. A per-tenant value is not a snapshot's job; write a consumer of your own.
- **Bind.** `Bind` receives every tracked name mapped to its resolved string, `null` when the setting has no value and no default. It runs only when a value changed.
- **Revision.** `Revision` is `1` after the first load and moves by one each time a resolved value changes. A reload that reads the same values keeps the same `Current` instance and revision. The comparison is on the raw setting values, so it holds whatever equality `T` has, including a record holding a collection.
- **Startup.** Every snapshot loads in its hosted service's `StartAsync`, after every `StartingAsync` hook, so the relational settings schema exists whatever order the host registered settings and snapshots in. An undefined name or a `Bind` exception there fails the host: a process does not serve on a policy it never read. Reading `Current` before the load throws `InvalidOperationException`.
- **Bad values later.** A `Bind` exception after startup is logged with the setting names, never their values, and the snapshot keeps its last good value and revision.
- **Listeners.** `OnChange((value, revision) => ...)` runs after `Current` and `Revision` show the change. A listener exception is logged and does not stop the other listeners. Dispose the returned handle to unregister.
- **Change announcements.** One framework consumer, identity `headless.settings.snapshot`, receives every `SettingChangedMessage` in every process and reloads the snapshots tracking an announced name at `Global` scope. It never filters on `OriginHostName`, so the writing process refreshes too. It reloads every snapshot each time its subscription is established, first or after a gap, because delivery is at most once. When a reload does not yet see the announced value, it re-reads a few times over the next few seconds, which closes the cache ordering window described above.
- **Transport.** `AddSettingsSnapshot` contributes that consumer, and only when the host uses messaging. It is an [every-instance consumer](messaging.md#every-instance-bus-delivery), so a host on a transport without every-instance delivery (AWS SNS/SQS, or Azure Service Bus without `AutoProvision`) fails at startup once it registers a snapshot. Such a host runs the snapshot without messaging. A host that never calls `AddSettingsSnapshot` is unaffected.
- **Backstop.** Each process re-reads every snapshot on its backstop interval, one minute by default and at most 30 days (`SettingsSnapshotBuilder<T>.MaxBackstop`), with up to 10% of jitter. This is the only refresh without messaging, and it catches announcements that were lost or never sent, such as configuration changes. The re-read goes through the setting cache: a write that evicts the cache is seen at the next tick, and a write that bypasses eviction (raw SQL, or a write through the PostgreSQL or SQL Server value repository directly) is seen only after `SettingManagementOptions.ValueCacheExpiration`.
- **Several replicas.** Convergence across processes needs a shared (Redis) or hybrid setting cache. With a process-local cache (`UseInMemory`), each process's cache learns only its own writes, so another replica reads its cached value until it expires, through announcement, re-read, and backstop alike.

### Startup Initialization

`SettingsInitializationBackgroundService` runs after the application starts. It saves static setting definitions to the database (idempotent and guarded by a distributed lock), with up to 10 jittered exponential-back-off retries capped at 30 seconds, then pre-caches dynamic setting definitions when `IsDynamicSettingStoreEnabled` is true. Cancellation, `ArgumentException`, and `NotSupportedException` fail immediately without retry; other terminal failures surface through `WaitForInitializationAsync()`. Both tasks are skipped when their governing option flags are disabled — in that case the service signals completion immediately. If the host is stopped before initialization finishes, the background task and waiters are cancelled.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Settings.Storage.EntityFramework` | You already use EF Core and want schema managed via EF migrations | You need to avoid an EF dependency or want zero-overhead ADO.NET | Portable across any EF-supported DB; startup validates that all settings entities are in the EF model before hosted services start |
| `Headless.Settings.Storage.PostgreSql` | You use PostgreSQL and want no EF Core dependency | You run SQL Server or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against PostgreSQL naming rules |
| `Headless.Settings.Storage.SqlServer` | You use SQL Server and want no EF Core dependency | You run PostgreSQL or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against SQL Server naming rules |

---

## Headless.Settings.Abstractions

Defines the provider-agnostic interfaces for dynamic application settings management.

### API and behavior

- `ISettingManager` — reads and writes setting values across the registered provider chain; supports single and bulk queries with optional provider targeting and fallback
- `ISettingDefinitionManager` — looks up and enumerates all registered setting definitions
- `ISettingDefinitionProvider` — contributes setting definitions at startup via `ISettingDefinitionContext`
- `SettingDefinition` — describes a setting's name, default value, display metadata, encryption flag, inheritance flag, client-visibility flag, allowed providers, and custom properties
- `SettingDefinitionCreateOptions` — initializer-based setting metadata with a required `Name`; optional values remain additive without constructor churn
- `SettingValue` — immutable record `SettingValue(string Name, string? Value, SettingValueProvider? Provider = null)` returned by `GetAsync` and `GetAllAsync`; `Provider` attributes the resolving value provider (or `null` on a miss)
- `SettingValueProvider` — immutable record `SettingValueProvider(string Name, string? Key)` identifying the provider name and its per-provider key
- `ISettingDefinitionContext` — context passed to `ISettingDefinitionProvider.Define()`; exposes the factory `Add(SettingDefinitionCreateOptions options)` (creates, registers, and returns the definition), plus `GetOrDefault(name)` and `GetAll()`
- `ISettingsSnapshot<T>` — a typed value bound from Global settings, held in memory: `Current`, `Revision` (moves only when a value changed), and `OnChange(listener)`. Registered with `AddSettingsSnapshot<T>` from `Headless.Settings.Core`; see [Settings snapshot](#settings-snapshot)
- `SettingValueProviderNames` — constants `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User` for targeting built-in providers
- General extension members on `ISettingManager`: `IsTrueAsync`, `IsFalseAsync`, `GetAsync<T>` (deserializes JSON), `SetAsync<T>` (serializes to JSON)
- Scoped extension members: `GetForTenantAsync` / `SetForTenantAsync` / `GetAllForTenantAsync` (and `*ForCurrentTenant*` variants), equivalent `*ForUser*` / `*ForCurrentUser*` set, `GetGlobalAsync` / `SetGlobalAsync` / `GetAllGlobalAsync`, `GetDefaultAsync` / `GetAllDefaultAsync`, `GetInConfigurationAsync` / `GetAllInConfigurationAsync`. The `GetAll*` helpers return `IReadOnlyList<SettingValue>`
- `GetAllAsync(settingNames, providerName, providerKey, fallback)` and its `GetAllGlobalAsync(settingNames, fallback)` helper resolve a named set within one scope. Reach for these over `GetAllAsync(settingNames)` whenever the values are application-wide policy: the name-only overload walks the whole provider chain, so a Tenant, Account, or User value shadows the Global one, and a caller reading a limit or a quota gets the reader's own value instead of the platform's. They also stop a caller that needs a handful of settings from paying for every setting the application defines

### Install

```bash
dotnet add package Headless.Settings.Abstractions
```

### Setup and use

```csharp
public sealed class NotificationService(ISettingManager settingManager)
{
    public async Task<bool> IsEmailEnabledAsync(CancellationToken ct)
    {
        // IsTrueAsync is a convenience extension on ISettingManager
        return await settingManager.IsTrueAsync("Notifications.EmailEnabled", cancellationToken: ct);
    }

    public async Task SetTenantPreferenceAsync(string tenantId, string settingName, string value, CancellationToken ct)
    {
        // Scoped extension: sets Tenant provider value for the given tenantId
        await settingManager.SetForTenantAsync(tenantId, settingName, value, cancellationToken: ct);
    }

    public async Task SetUserPreferenceAsync(string userId, string settingName, string value, CancellationToken ct)
    {
        await settingManager.SetAsync(
            settingName,
            value,
            SettingValueProviderNames.User,
            userId,
            cancellationToken: ct
        );
    }
}
```

#### Defining Settings

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
            IsVisibleToClients = false,
        });
    }
}
```

### Configuration

None. This is an abstractions-only package.

### Runtime behavior

None.

---

## Headless.Settings.Core

Core implementation of dynamic settings management with hierarchical value providers, caching, encryption, and background initialization.

### API and behavior

- `SettingManager` — full implementation of `ISettingManager`; walks the registered provider chain, caches results, and coordinates writes with cache invalidation
- `ISettingValueReadProvider` / `ISettingValueProvider` — read-only and read-write contracts for custom value providers; register with `services.AddSettingValueProvider<T>()`
- Built-in value providers (lowest to highest priority): `DefaultValueSettingValueProvider`, `ConfigurationSettingValueProvider`, `GlobalSettingValueProvider`, `TenantSettingValueProvider`, `UserSettingValueProvider`
- `IStaticSettingDefinitionStore` — builds the setting catalog lazily from all registered `ISettingDefinitionProvider` implementations
- `IDynamicSettingDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination
- `SettingsInitializationBackgroundService` — seeds static definitions with up to 10 jittered exponential-back-off retries capped at 30 seconds; pre-caches dynamic definitions when enabled
- `SettingManagementOptions` — tuning options for lock keys, cache expiries, dynamic store toggle
- `SettingsStorageOptions` — schema and table name configuration shared across all storage providers
- `RelationalSettingsOptions` — base of `PostgreSqlSettingsOptions` and `SqlServerSettingsOptions` (`ConnectionString`, `CommandTimeout`). Core also holds the one relational value and definition repository both raw providers run, written over the `ISqlDialect` statement kit; each provider package supplies only its options, DDL, and registration
- `HeadlessSettingsSetupBuilder` — fluent builder returned to `AddHeadlessSettings`; exposes `ConfigureManagement`, `ConfigureStorage`, and `RegisterExtension`
- `services.AddSettingDefinitionProvider<T>()` — registers a custom `ISettingDefinitionProvider`
- `services.AddSettingValueProvider<T>()` — registers a custom value provider (idempotent by type)
- `services.AddSettingsSnapshot<T>(snapshot => ...)` — registers an `ISettingsSnapshot<T>` configured through `SettingsSnapshotBuilder<T>` (`Names`, `Bind`, `Backstop`, at most `MaxBackstop`), its startup load and backstop, and the every-instance change consumer when the host uses messaging. Registering the same `T` twice, or omitting `Names` or `Bind`, throws `InvalidOperationException`; see [Settings snapshot](#settings-snapshot)
- `IClientVisibleSettingsReader` (`Headless.Settings.ClientVisibility`) — `GetAsync(PrincipalContext, …)` returns the value of every setting whose definition is `IsVisibleToClients`, keyed by name, through one `GetAllAsync(settingNames)` read, for example to include in the configuration an application returns to its front end. Headless ships no endpoint; see the client-config recipe in `docs/llms/permissions.md`

### Design constraints

`IClientVisibleSettingsReader` resolves for the principal and tenant in its `PrincipalContext`, not the ambient ones: it switches `ICurrentPrincipalAccessor` and `ICurrentTenant` to the context for the duration of the read, so the `User` and `Tenant` providers read the context's user and tenant, and restores them afterwards. `IsVisibleToClients` defaults to `false`, and an encrypted setting marked visible is returned decrypted, so mark a secret visible only when the client is meant to read it.

Value providers are registered with the last-added provider having the highest resolution priority. The built-in order (from setup) is `DefaultValue → Configuration → Global → Tenant → User` — User wins. Custom providers added via `AddSettingValueProvider<T>()` are appended after `User` and therefore have the highest priority of all. This matters when writing custom providers that must override built-in resolution. `ISettingValueProviderManager.Providers` exposes the reversed (highest priority first) list, which every read path — `GetAsync`, `GetAllAsync(settingNames)`, and `GetAllAsync(providerName)` — walks forward, taking the first non-null value.

Encrypted settings (`isEncrypted: true`) are decrypted only when the resolving provider is store-backed (`Global`, `Tenant`, `User`). A plaintext `DefaultValue` or `IConfiguration` value resolved through fallback is returned as-is rather than fed to the decryptor.

**Keys must be text every provider keeps unchanged.** Every `ISettingValueStore` entry point throws `ArgumentException` before touching storage when a setting name, provider name, or provider key starts or ends with white space, on reads as well as writes. SQL Server ignores trailing spaces when it compares keys, so `"acme "` would read and overwrite the `"acme"` row there while PostgreSQL keeps the two apart. Every provider must keep a key unchanged, so the store also refuses a NUL character (PostgreSQL cannot store it) and an unpaired UTF-16 surrogate (SqlClient sends it as U+FFFD, so SQL Server would merge keys that differ only in which lone surrogate they carry). The rule is `Argument.IsPortableKey`. Normalize keys at your own boundary; the store refuses rather than trims.

`AddHeadlessSettings` is guarded on `ISettingManager` so it is safe to call more than once (only the first call registers the core). However, only one storage provider extension may be registered — a second call with a different provider throws at startup.

`SettingsInitializationBackgroundService` implements `IInitializer` so anything that awaits `WaitForInitializationAsync()` blocks until the seed and pre-cache steps complete. Cancellation, `ArgumentException`, and `NotSupportedException` fail immediately without retry; other failures retain 10 retries, and the terminal exception is surfaced to every waiter. If the host is stopped before initialization finishes, the background task and waiters are cancelled.

### Install

```bash
dotnet add package Headless.Settings.Core
```

### Setup and use

Register the required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IStringEncryptionService`) first, then call `AddHeadlessSettings`:

> A caching provider is a hard prerequisite. This package references `Headless.Caching.Abstractions` only, so the `ICache<SettingValueCacheItem>` that `SettingValueStore` reads through comes from `AddHeadlessCaching(...)` with a provider (`UseInMemory` / `UseRedis` / `UseHybrid`). `AddHeadlessSettings` declares it via `Headless.Hosting`'s `RequireRegisteredService<T>`, so a host without one is refused at startup with a `MissingRequiredServiceException` rather than failing on the first setting read. Registration order does not matter — the check runs at host start.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Required dependencies
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

// Register management core + storage in one call
builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());

// Register setting definition providers
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

#### Define Settings

```csharp
public sealed class AppSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinitionCreateOptions
        {
            Name = "App.MaxFileSize",
            DisplayName = "Maximum File Size",
            DefaultValue = "10485760",
        });

        context.Add(new SettingDefinitionCreateOptions
        {
            Name = "App.ApiKey",
            DisplayName = "API Key",
            IsEncrypted = true,
        });
    }
}
```

#### Read and Write Settings

```csharp
public sealed class ConfigService(ISettingManager settings)
{
    public async Task<int> GetMaxFileSizeAsync(CancellationToken ct)
    {
        var setting = await settings.GetAsync("App.MaxFileSize", cancellationToken: ct);
        return int.TryParse(setting.Value, out var size) ? size : 10485760;
    }

    public async Task SetTenantApiKeyAsync(string tenantId, string apiKey, CancellationToken ct)
    {
        await settings.SetAsync(
            "App.ApiKey",
            apiKey,
            SettingValueProviderNames.Tenant,
            tenantId,
            cancellationToken: ct
        );
    }
}
```

#### Register a Settings Snapshot

```csharp
builder.Services.AddSettingsSnapshot<UploadPolicy>(snapshot => snapshot
    .Names("App.MaxFileSize")
    .Bind(values => new UploadPolicy(
        long.Parse(values["App.MaxFileSize"] ?? "10485760", CultureInfo.InvariantCulture))));

public sealed class UploadValidator(ISettingsSnapshot<UploadPolicy> policy)
{
    public bool IsAllowed(long size) => size <= policy.Current.MaxFileSize;
}
```

#### Custom Value Provider

```csharp
// T must implement ISettingValueReadProvider (read-only) or ISettingValueProvider (read-write)
builder.Services.AddSettingValueProvider<MyCustomSettingValueProvider>();
```

### Configuration

Pre-requisite: configure and register string encryption before settings management:

```json
{
  "Headless": {
    "StringEncryption": {
      "DefaultPassPhrase": "YourPassPhrase123",
      "InitVectorBytes": "WW91ckluaXRWZWN0b3IxNg==",
      "DefaultSalt": "WW91clNhbHQ="
    }
  }
}
```

#### SettingManagementOptions

Configure via `setup.ConfigureManagement(...)` or `services.Configure<SettingManagementOptions>(...)`:

```csharp
services.AddHeadlessSettings(setup =>
{
    setup.ConfigureManagement(options =>
    {
        // Distributed lock key coordinating cross-application definition saves (default: "settings:common_update_lock")
        options.CrossApplicationsCommonLockKey = "settings:common_update_lock";

        // Lifetime of cached setting values in the distributed cache (default: 5 hours)
        options.ValueCacheExpiration = TimeSpan.FromHours(5);

        // Enable database-backed dynamic setting definition store (default: false)
        options.IsDynamicSettingStoreEnabled = false;

        // Persist static definitions to the DB on startup (default: true)
        options.SaveStaticSettingsToDatabase = true;

        // How long dynamic definitions stay in-process before the distributed stamp is re-checked (default: 30 seconds)
        options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

All lock- and cache-expiry options default to reasonable production values. The validator rejects empty lock keys and zero/negative expiry spans.

#### SettingsStorageOptions

Configure schema and table names via `setup.ConfigureStorage(...)`:

```csharp
services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "headless"; // default, shared by every Headless feature
        o.SettingValuesTableName = null; // default: setting_values on PostgreSQL, SettingValues elsewhere
        o.SettingDefinitionsTableName = null; // default: setting_definitions / SettingDefinitions
        o.InitializeOnStartup = true; // default; set false when schema is provisioned out-of-band
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

Every object follows its database's naming convention. On PostgreSQL the tables, columns, primary keys, and indexes are snake_case (`setting_values`, `provider_key`, `pk_setting_values`, `ix_setting_values_name_provider_name_provider_key`); on SQL Server and other databases they are PascalCase (`SettingValues`, `ProviderKey`, `PK_SettingValues`, `IX_SettingValues_Name_ProviderName_ProviderKey`). A table-name option left `null` takes that convention's default. A table name you set is used verbatim, and its key and index names derive from it (`pk_MyValues`). The raw providers and the EF mapping produce the same names on the same database. Because PostgreSQL silently truncates identifiers longer than 63 bytes, every provider refuses a configured table name whose longest derived PostgreSQL key or index name would exceed that: at most 23 characters for the values table and 55 for definitions.

### Runtime behavior

- Registers `ISettingManager` and `IClientVisibleSettingsReader` as singletons
- Registers `ISettingDefinitionManager`, `IStaticSettingDefinitionStore`, `IDynamicSettingDefinitionStore`, `ISettingValueStore`, `ISettingValueProviderManager` as singletons
- Registers `DefaultValueSettingValueProvider`, `ConfigurationSettingValueProvider`, `GlobalSettingValueProvider`, `TenantSettingValueProvider`, `UserSettingValueProvider` as singletons
- Registers `SettingsInitializationBackgroundService` as hosted service
- `AddSettingsSnapshot<T>` registers `ISettingsSnapshot<T>` as a singleton, one hosted service that loads and re-reads every snapshot, and, with messaging, the every-instance consumer `headless.settings.snapshot`

---

## Headless.Settings.Storage.EntityFramework

Entity Framework Core storage implementation for settings management.

### API and behavior

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessSettingsSetupBuilder`
- `modelBuilder.AddHeadlessSettings(DbContext context)` — applies entity configurations by resolving `SettingsStorageOptions` from the context's service provider (no constructor injection required) and the naming style from `context.Database.ProviderName`: snake_case on Npgsql, PascalCase on every other provider
- `modelBuilder.AddHeadlessSettings(SettingsStorageOptions options, StorageNamingStyle style)` — overload for when you already hold the options; pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` (namespace `Headless.Hosting.Initialization`) so the style matches the database
- EF repositories for `ISettingValueRecordRepository` and `ISettingDefinitionRecordRepository`
- `SettingsStorageOptions` for schema and table-name configuration (shared with raw-DDL providers)
- Startup validation gate that inspects the EF model before hosted services start and fails with an actionable message if any settings entity is missing
- Value uniqueness declared as a pair of filtered unique indexes — `(Name, ProviderName, ProviderKey) WHERE "ProviderKey" IS NOT NULL` and `(Name, ProviderName) WHERE "ProviderKey" IS NULL` — matching the raw-DDL providers, so global (NULL-key) values stay unique on databases that treat NULLs as distinct (PostgreSQL, SQLite)

### Design constraints

The package does not ship a dedicated settings `DbContext` or settings-specific `DbContext` interface. Consumers register `AddDbContextFactory<TContext>()`, map the Headless entities in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties. Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`. Writes commit through a fresh context owned by the repository, so they are not enlisted in the consumer's outer transaction.

### Install

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

### Setup and use

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Resolves SettingsStorageOptions from the context's service provider —
        // no need to inject IOptions<SettingsStorageOptions> into the constructor.
        modelBuilder.AddHeadlessSettings(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);

// AddHeadlessSettings registers the management core automatically.
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage =>
    {
        storage.Schema = "app_settings";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

### Configuration

`SettingsStorageOptions` defaults:

- `Schema = "headless"`, the schema every Headless feature shares (see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features))
- `SettingValuesTableName = null`: `setting_values` on PostgreSQL, `SettingValues` elsewhere
- `SettingDefinitionsTableName = null`: `setting_definitions` on PostgreSQL, `SettingDefinitions` elsewhere
- `InitializeOnStartup = true`

The registration validates identifier names using cross-provider rules (SQL Server superset). The startup gate inspects the EF model before hosted services start and fails with an actionable message if any settings entity is missing. `InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL.

### Runtime behavior

- Registers `ISettingValueRecordRepository` (`EfSettingValueRecordRepository<TContext>`) as singleton
- Registers `ISettingDefinitionRecordRepository` (`EfSettingDefinitionRecordRepository<TContext>`) as singleton
- Registers validated `SettingsStorageOptions`
- Registers `SettingsEntityStartupValidator<TContext>` as an `IHeadlessStartupValidator`

---

## Headless.Settings.Storage.PostgreSql

PostgreSQL raw-DDL storage for settings management.

### API and behavior

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — overload that binds `PostgreSqlSettingsOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlSettingsOptions> configure)` — overload for full option control
- `setup.UsePostgreSql(Action<PostgreSqlSettingsOptions, IServiceProvider> configure)` — overload for late-bound configuration
- `setup.UsePostgreSql()` — reads the connection registered by `AddPostgreSqlSql`, so one connection string serves every feature; see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features)
- Table and index creation at host startup as schema steps (`Settings/1` tables, `Settings/2` indexes) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), with snake_case tables, columns, keys, and indexes (`setting_values`, `provider_key`, `ix_setting_values_name_provider_name_provider_key`)
- Raw ADO.NET repositories for setting values and definitions
- `PostgreSqlSettingsOptions` — connection string and command timeout, inherited from `RelationalSettingsOptions`
- Shares `SettingsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)

### Install

```bash
dotnet add package Headless.Settings.Storage.PostgreSql
```

### Setup and use

Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService`. `AddHeadlessSettings` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

builder.Services.AddPostgreSqlSql(connectionString);
builder.Services.AddHeadlessSettings(setup => setup.UsePostgreSql());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_settings");
    setup.UsePostgreSql(connectionString);
});
```

Or with full option control:

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

### Configuration

#### Options

`PostgreSqlSettingsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | PostgreSQL connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `SettingsStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (migrations job, DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

### Runtime behavior

- Registers the settings schema contribution; the one schema runner applies it at startup
- Registers the shared relational repositories from `Headless.Settings.Core`, over the PostgreSQL dialect, as `ISettingValueRecordRepository` and `ISettingDefinitionRecordRepository` (singletons)

---

## Headless.Settings.Storage.SqlServer

SQL Server raw-DDL storage for settings management.

### API and behavior

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — overload that binds `SqlServerSettingsOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerSettingsOptions> configure)` — overload for full option control
- `setup.UseSqlServer(Action<SqlServerSettingsOptions, IServiceProvider> configure)` — overload for late-bound configuration
- `setup.UseSqlServer()` — reads the connection registered by `AddSqlServerSql`, so one connection string serves every feature; see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features)
- Table and index creation at host startup as schema steps (`Settings/1` tables, `Settings/2` indexes) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), with PascalCase tables, columns, keys, and indexes (`SettingValues`, `ProviderKey`, `IX_SettingValues_Name_ProviderName_ProviderKey`)
- Raw ADO.NET repositories for setting values and definitions
- `SqlServerSettingsOptions` — connection string and command timeout, inherited from `RelationalSettingsOptions`
- Shares `SettingsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)

### Install

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

### Setup and use

Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService`. `AddHeadlessSettings` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

builder.Services.AddSqlServerSql(connectionString);
builder.Services.AddHeadlessSettings(setup => setup.UseSqlServer());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_settings");
    setup.UseSqlServer(connectionString);
});
```

Or with full option control:

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

### Configuration

#### Options

`SqlServerSettingsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `SettingsStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (migrations job, DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

### Runtime behavior

- Registers the settings schema contribution; the one schema runner applies it at startup
- Registers the shared relational repositories from `Headless.Settings.Core`, over the SQL Server dialect, as `ISettingValueRecordRepository` and `ISettingDefinitionRecordRepository` (singletons)
