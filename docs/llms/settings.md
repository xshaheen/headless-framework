---
domain: Settings
packages: Settings.Abstractions, Settings.Core, Settings.Storage.EntityFramework, Settings.Storage.PostgreSql, Settings.Storage.SqlServer
---

# Settings

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Setting Definitions vs. Setting Values](#setting-definitions-vs-setting-values)
    - [Value Providers and Resolution Order](#value-providers-and-resolution-order)
    - [Static Store vs. Dynamic Store](#static-store-vs-dynamic-store)
    - [Setting Value Caching](#setting-value-caching)
    - [Startup Initialization](#startup-initialization)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Settings.Abstractions](#headlesssettingsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Settings.Core](#headlesssettingscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Settings.Storage.EntityFramework](#headlesssettingsstorageentityframework)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Settings.Storage.PostgreSql](#headlesssettingsstoragepostgresql)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Settings.Storage.SqlServer](#headlesssettingsstoragesqlserver)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Dynamic, hierarchical application settings with runtime read/write support and multiple value providers (DefaultValue, Configuration, Global, Tenant, User) resolved from lowest to highest priority.

## Quick Orientation

Install three packages: an abstractions package, the core implementation, and exactly one storage provider:

- `Headless.Settings.Abstractions` — interfaces (`ISettingManager`, `ISettingDefinitionProvider`, `SettingDefinition`)
- `Headless.Settings.Core` — full implementation with hierarchical providers, caching, encryption, background init
- one storage provider: `Headless.Settings.Storage.EntityFramework`, `Headless.Settings.Storage.PostgreSql`, or `Headless.Settings.Storage.SqlServer`

Minimal wiring:

```csharp
builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

// AddHeadlessSettings registers the management core automatically.
builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

Define settings via `ISettingDefinitionProvider.Define()`. Read via `ISettingManager.GetAsync()`, write via `SetAsync()`. Provider hierarchy resolves from lowest to highest priority: DefaultValue → Configuration → Global → Tenant → User (User wins).

## Agent Instructions

- Use this for **runtime-changeable settings**, not for static configuration. For static config, use `IOptions<T>` / `IConfiguration`.
- Always install all three packages together. `Headless.Settings.Abstractions` alone gives nothing runnable; `Headless.Settings.Core` requires a storage backend.
- `ISettingManager` is the primary entry point. Call `GetAsync(name)` to read; `SetAsync(name, value, providerName, providerKey)` to write. `GetAsync` returns a never-`null` `SettingValue(Name, Value, Provider)`: on a miss both `Value` and `Provider` are `null`; on a hit `Provider` (a `SettingValueProvider(Name, Key)`) identifies the resolving provider and its per-provider key. It still throws `ConflictException` when the setting is undefined.
- Provider names are constants on `SettingValueProviderNames`: `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User`. Note: the default-value constant is `DefaultValue`, not `Default`.
- Scoped extension members are available for each provider scope: `GetForTenantAsync` / `SetForTenantAsync`, `GetForUserAsync` / `SetForUserAsync`, `GetGlobalAsync` / `SetGlobalAsync`, `GetDefaultAsync`, `GetInConfigurationAsync`. The raw (non-generic) `Get*` scoped helpers return the unwrapped `string?` value; the generic `Get*<T>` helpers deserialize the JSON value to `T`. Use `IsTrueAsync` / `IsFalseAsync` / `GetAsync<T>` / `SetAsync<T>` on `ISettingManager` for typed or boolean reads.
- For sensitive settings, set `isEncrypted: true` on `SettingDefinition` — Core handles encryption/decryption via `IStringEncryptionService` automatically.
- Core registers a `SettingsInitializationBackgroundService` hosted service — do not register your own init logic for settings.
- `AddHeadlessSettings(...)` is the single entry point — it registers the management core automatically alongside the storage provider. Only one storage provider (EF / PostgreSQL / SqlServer) may be registered; a second registration throws at startup.
- To tune management options, call `setup.ConfigureManagement(options => ...)` inside the `AddHeadlessSettings` block. An `(options, IServiceProvider)` overload is available for late-bound configuration. `services.Configure<SettingManagementOptions>(...)` also works and composes regardless of call order.
- To tune schema and table names, call `setup.ConfigureStorage(o => ...)` inside the same block.
- For EF storage: register `AddDbContextFactory<TContext>()` and call `modelBuilder.AddHeadlessSettings(this)` in `OnModelCreating` before calling `setup.UseEntityFramework<TContext>()`. The `(SettingsStorageOptions)` overload exists when you already hold the options object.
- Required services before `AddHeadlessSettings(...)`: `TimeProvider`, caching (`ICache`), distributed lock (`IDistributedLock`), and `IStringEncryptionService`. The core throws `InvalidOperationException` on startup if encryption is missing.
- `DeleteAsync(providerName, providerKey)` removes all setting values for a given provider and key — use it when cleaning up a deleted tenant or user.
- Both `ISettingManager` and direct `ISettingValueRecordRepository` writes invalidate cached values (the repository removes the affected key after `SaveChangesAsync`). Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale.
- `SettingDefinition.IsInherited = false` disables fallback for that setting: if no value exists at the requested provider, `GetAsync` returns a `SettingValue` with a `null` `Value` regardless of lower-priority providers.
- `SettingDefinition` instances are minted through the `ISettingDefinitionContext.Add(options)` factory (the constructor is `internal`). The factory returns the created definition so you can then mutate `Providers` or `ExtraProperties` on it.
- Custom value providers must implement `ISettingValueReadProvider` (read-only) or `ISettingValueProvider` (read-write). Register with `services.AddSettingValueProvider<T>()`. The last-registered provider has the highest resolution priority.

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

### Problem Solved

Provides a storage-independent API for managing application settings with support for multiple value providers (DefaultValue, Configuration, Global, Tenant, User), enabling hierarchical settings that can be overridden at different levels without changing application code.

### Key Features

- `ISettingManager` — reads and writes setting values across the registered provider chain; supports single and bulk queries with optional provider targeting and fallback
- `ISettingDefinitionManager` — looks up and enumerates all registered setting definitions
- `ISettingDefinitionProvider` — contributes setting definitions at startup via `ISettingDefinitionContext`
- `SettingDefinition` — describes a setting's name, default value, display metadata, encryption flag, inheritance flag, client-visibility flag, allowed providers, and custom properties
- `SettingDefinitionCreateOptions` — initializer-based setting metadata with a required `Name`; optional values remain additive without constructor churn
- `SettingValue` — immutable record `SettingValue(string Name, string? Value, SettingValueProvider? Provider = null)` returned by `GetAsync` and `GetAllAsync`; `Provider` attributes the resolving value provider (or `null` on a miss)
- `SettingValueProvider` — immutable record `SettingValueProvider(string Name, string? Key)` identifying the provider name and its per-provider key
- `ISettingDefinitionContext` — context passed to `ISettingDefinitionProvider.Define()`; exposes the factory `Add(SettingDefinitionCreateOptions options)` (creates, registers, and returns the definition), plus `GetOrDefault(name)` and `GetAll()`
- `SettingValueProviderNames` — constants `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User` for targeting built-in providers
- General extension members on `ISettingManager`: `IsTrueAsync`, `IsFalseAsync`, `GetAsync<T>` (deserializes JSON), `SetAsync<T>` (serializes to JSON)
- Scoped extension members: `GetForTenantAsync` / `SetForTenantAsync` / `GetAllForTenantAsync` (and `*ForCurrentTenant*` variants), equivalent `*ForUser*` / `*ForCurrentUser*` set, `GetGlobalAsync` / `SetGlobalAsync` / `GetAllGlobalAsync`, `GetDefaultAsync` / `GetAllDefaultAsync`, `GetInConfigurationAsync` / `GetAllInConfigurationAsync`. The `GetAll*` helpers return `IReadOnlyList<SettingValue>`

### Installation

```bash
dotnet add package Headless.Settings.Abstractions
```

### Quick Start

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

### Dependencies

None.

### Side Effects

None.

---

## Headless.Settings.Core

Core implementation of dynamic settings management with hierarchical value providers, caching, encryption, and background initialization.

### Problem Solved

Provides the full settings management implementation including hierarchical value resolution (User > Tenant > Global > Configuration > DefaultValue), setting value caching with distributed invalidation, transparent encryption for sensitive settings, and background startup initialization that seeds static definitions to the database.

### Key Features

- `SettingManager` — full implementation of `ISettingManager`; walks the registered provider chain, caches results, and coordinates writes with cache invalidation
- `ISettingValueReadProvider` / `ISettingValueProvider` — read-only and read-write contracts for custom value providers; register with `services.AddSettingValueProvider<T>()`
- Built-in value providers (lowest to highest priority): `DefaultValueSettingValueProvider`, `ConfigurationSettingValueProvider`, `GlobalSettingValueProvider`, `TenantSettingValueProvider`, `UserSettingValueProvider`
- `IStaticSettingDefinitionStore` — builds the setting catalog lazily from all registered `ISettingDefinitionProvider` implementations
- `IDynamicSettingDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination
- `SettingsInitializationBackgroundService` — seeds static definitions with up to 10 jittered exponential-back-off retries capped at 30 seconds; pre-caches dynamic definitions when enabled
- `SettingManagementOptions` — tuning options for lock keys, cache expiries, dynamic store toggle
- `SettingsStorageOptions` — schema and table name configuration shared across all storage providers
- `HeadlessSettingsSetupBuilder` — fluent builder returned to `AddHeadlessSettings`; exposes `ConfigureManagement`, `ConfigureStorage`, and `RegisterExtension`
- `services.AddSettingDefinitionProvider<T>()` — registers a custom `ISettingDefinitionProvider`
- `services.AddSettingValueProvider<T>()` — registers a custom value provider (idempotent by type)

### Design Notes

Value providers are registered with the last-added provider having the highest resolution priority. The built-in order (from setup) is `DefaultValue → Configuration → Global → Tenant → User` — User wins. Custom providers added via `AddSettingValueProvider<T>()` are appended after `User` and therefore have the highest priority of all. This matters when writing custom providers that must override built-in resolution.

`AddHeadlessSettings` is guarded on `ISettingManager` so it is safe to call more than once (only the first call registers the core). However, only one storage provider extension may be registered — a second call with a different provider throws at startup.

`SettingsInitializationBackgroundService` implements `IInitializer` so anything that awaits `WaitForInitializationAsync()` blocks until the seed and pre-cache steps complete. Cancellation, `ArgumentException`, and `NotSupportedException` fail immediately without retry; other failures retain 10 retries, and the terminal exception is surfaced to every waiter. If the host is stopped before initialization finishes, the background task and waiters are cancelled.

### Installation

```bash
dotnet add package Headless.Settings.Core
```

### Quick Start

Register the required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IStringEncryptionService`) first, then call `AddHeadlessSettings`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Required dependencies
builder.Services.AddCaching();
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
        o.Schema = "settings"; // default
        o.SettingValuesTableName = "SettingValues"; // default
        o.SettingDefinitionsTableName = "SettingDefinitions"; // default
        o.InitializeOnStartup = true; // default; set false when schema is provisioned out-of-band
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

### Dependencies

- `Headless.Settings.Abstractions`
- `Headless.Security`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Domain`

### Side Effects

- Registers `ISettingManager` as singleton
- Registers `ISettingDefinitionManager`, `IStaticSettingDefinitionStore`, `IDynamicSettingDefinitionStore`, `ISettingValueStore`, `ISettingValueProviderManager` as singletons
- Registers `DefaultValueSettingValueProvider`, `ConfigurationSettingValueProvider`, `GlobalSettingValueProvider`, `TenantSettingValueProvider`, `UserSettingValueProvider` as singletons
- Registers `SettingsInitializationBackgroundService` as hosted service

---

## Headless.Settings.Storage.EntityFramework

Entity Framework Core storage implementation for settings management.

### Problem Solved

Provides EF Core repository implementations for setting values and definitions using the consumer's own `DbContext`, with schema managed through EF migrations.

### Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessSettingsSetupBuilder`
- `modelBuilder.AddHeadlessSettings(DbContext context)` — applies entity configurations by resolving `SettingsStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessSettings(SettingsStorageOptions options)` — overload for when you already hold the options
- EF repositories for `ISettingValueRecordRepository` and `ISettingDefinitionRecordRepository`
- `SettingsStorageOptions` for schema and table-name configuration (shared with raw-DDL providers)
- Startup validation gate that inspects the EF model before hosted services start and fails with an actionable message if any settings entity is missing

### Design Notes

The package does not ship a dedicated settings `DbContext` or settings-specific `DbContext` interface. Consumers register `AddDbContextFactory<TContext>()`, map the Headless entities in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties. Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`. Writes commit through a fresh context owned by the repository, so they are not enlisted in the consumer's outer transaction.

### Installation

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

### Quick Start

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

builder.Services.AddCaching();
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
        storage.SettingValuesTableName = "SettingValues";
        storage.SettingDefinitionsTableName = "SettingDefinitions";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

### Configuration

`SettingsStorageOptions` defaults:

- `Schema = "settings"`
- `SettingValuesTableName = "SettingValues"`
- `SettingDefinitionsTableName = "SettingDefinitions"`
- `InitializeOnStartup = true`

The registration validates identifier names using cross-provider rules (SQL Server superset). The startup gate inspects the EF model before hosted services start and fails with an actionable message if any settings entity is missing. `InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL.

### Dependencies

- `Headless.Settings.Core`
- `Headless.EntityFramework`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Registers `ISettingValueRecordRepository` (`EfSettingValueRecordRepository<TContext>`) as singleton
- Registers `ISettingDefinitionRecordRepository` (`EfSettingDefinitionRecordRepository<TContext>`) as singleton
- Registers validated `SettingsStorageOptions`
- Registers `SettingsEntityValidationStartupGate<TContext>` as `IHostedService`

---

## Headless.Settings.Storage.PostgreSql

PostgreSQL raw-DDL storage for settings management.

### Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence. All schema is created idempotently at host startup via raw ADO.NET.

### Key Features

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — overload that binds `PostgreSqlSettingsOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlSettingsOptions> configure)` — overload for full option control
- `setup.UsePostgreSql(Action<PostgreSqlSettingsOptions, IServiceProvider> configure)` — overload for late-bound configuration
- Idempotent schema, table, and index creation at host startup via `PostgreSqlSettingsStorageInitializer`
- Raw ADO.NET repositories for setting values and definitions
- `PostgreSqlSettingsOptions` — connection string and command timeout
- Shares `SettingsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)

### Installation

```bash
dotnet add package Headless.Settings.Storage.PostgreSql
```

### Quick Start

Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService`. `AddHeadlessSettings` then registers the management core automatically.

```csharp
builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UsePostgreSql(connectionString);
});

// Or with full option control:
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

### Dependencies

- `Headless.Settings.Core`
- `Headless.Serializer.Json`
- `Npgsql`

### Side Effects

- Registers `PostgreSqlSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlSettingValueRecordRepository` as `ISettingValueRecordRepository` (singleton)
- Registers `PostgreSqlSettingDefinitionRecordRepository` as `ISettingDefinitionRecordRepository` (singleton)

---

## Headless.Settings.Storage.SqlServer

SQL Server raw-DDL storage for settings management.

### Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence. All schema is created idempotently at host startup via raw ADO.NET.

### Key Features

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — overload that binds `SqlServerSettingsOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerSettingsOptions> configure)` — overload for full option control
- `setup.UseSqlServer(Action<SqlServerSettingsOptions, IServiceProvider> configure)` — overload for late-bound configuration
- Idempotent schema, table, and index creation at host startup via `SqlServerSettingsStorageInitializer`
- Raw ADO.NET repositories for setting values and definitions
- `SqlServerSettingsOptions` — connection string and command timeout
- Shares `SettingsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)

### Installation

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

### Quick Start

Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService`. `AddHeadlessSettings` then registers the management core automatically.

```csharp
builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UseSqlServer(connectionString);
});

// Or with full option control:
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

### Dependencies

- `Headless.Settings.Core`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

### Side Effects

- Registers `SqlServerSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerSettingValueRecordRepository` as `ISettingValueRecordRepository` (singleton)
- Registers `SqlServerSettingDefinitionRecordRepository` as `ISettingDefinitionRecordRepository` (singleton)
