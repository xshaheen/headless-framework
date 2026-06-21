---
domain: Feature Management
packages: Features.Abstractions, Features.Core, Features.Storage.EntityFramework, Features.Storage.PostgreSql, Features.Storage.SqlServer
---

# Feature Management

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
  - [Feature Definitions vs. Feature Values](#feature-definitions-vs-feature-values)
  - [Value Providers and Resolution Order](#value-providers-and-resolution-order)
  - [Static Store vs. Dynamic Store](#static-store-vs-dynamic-store)
  - [Feature Value Caching](#feature-value-caching)
  - [Startup Initialization](#startup-initialization)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Features.Abstractions](#headlessfeaturesabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Features.Core](#headlessfeaturescore)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Design Notes](#design-notes)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration-1)
    - [FeatureManagementOptions](#featuremanagementoptions)
    - [FeaturesStorageOptions](#featuresstorageoptions)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Features.Storage.EntityFramework](#headlessfeaturesstorageentityframework)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Features.Storage.PostgreSql](#headlessfeaturesstoragepostgresql)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-2)
  - [Configuration](#configuration-3)
    - [Options](#options)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)
- [Headless.Features.Storage.SqlServer](#headlessfeaturesstoragesqlserver)
  - [Problem Solved](#problem-solved-4)
  - [Key Features](#key-features-4)
  - [Installation](#installation-4)
  - [Quick Start](#quick-start-3)
  - [Configuration](#configuration-4)
    - [Options](#options-1)
  - [Dependencies](#dependencies-4)
  - [Side Effects](#side-effects-4)

> Dynamic feature flags and feature value management with hierarchical resolution (Tenant > Edition > Default), caching, and database persistence via EF Core, PostgreSQL, or SQL Server.

## Quick Orientation

Install `Headless.Features.Abstractions` plus `Headless.Features.Core` and exactly one storage provider:

- `Headless.Features.Abstractions` — interfaces (`IFeatureManager`, `IFeatureDefinitionProvider`, `IFeatureDefinitionManager`)
- `Headless.Features.Core` — full implementation with caching, value providers, and background initialization
- `Headless.Features.Storage.EntityFramework` — EF Core persistence using the consumer's `DbContext`
- `Headless.Features.Storage.PostgreSql` — raw ADO.NET persistence for PostgreSQL (no EF dependency)
- `Headless.Features.Storage.SqlServer` — raw ADO.NET persistence for SQL Server (no EF dependency)

Typical registration:

```csharp
// 1. Register feature definitions
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// 2. Register the management core + storage in one call
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

`AddHeadlessFeatures` requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered before it is called.

## Agent Instructions

- Inject `IFeatureManager` to read or write feature values. Do NOT use `Microsoft.FeatureManagement` — this is a separate system with a different model.
- Define features by implementing `IFeatureDefinitionProvider` and calling `context.AddGroup()` / `group.AddChild()`. Register the provider with `services.AddFeatureDefinitionProvider<T>()`.
- Value resolution order is Tenant > Edition > Default. When no provider is specified, `GetAsync` walks providers from highest to lowest priority and returns the first non-null value.
- `AddHeadlessFeatures(configure)` is the single entry point — it registers the management core automatically alongside the selected storage provider. Only one storage provider (EF / PostgreSQL / SqlServer) can be registered per application.
- To tune management options, call `setup.ConfigureManagement(options => ...)` inside the `AddHeadlessFeatures` block. An `(options, IServiceProvider)` overload is available for late-bound configuration. `services.Configure<FeatureManagementOptions>(...)` also works and composes regardless of call order.
- To tune storage options (schema, table names), call `setup.ConfigureStorage(o => ...)` inside the `AddHeadlessFeatures` block.
- For EF storage: register `AddDbContextFactory<TContext>()` and call `modelBuilder.AddHeadlessFeatures(this)` in `OnModelCreating` before calling `setup.UseEntityFramework<TContext>()`.
- `FeaturesInitializationBackgroundService` runs at startup — do NOT manually initialize features or call `IDynamicFeatureDefinitionStore.SaveAsync` directly.
- Feature value caching is automatic and invalidated via `CacheInvalidationMessage` when values are written through `IFeatureManager`. Do not bypass `IFeatureManager` to write directly to the repository — caching will not be invalidated.
- Custom value providers must implement `IFeatureValueReadProvider` (read-only) or `IFeatureValueProvider` (read-write). Register with `services.AddFeatureValueProvider<T>()`. The last-registered provider has the highest resolution priority.
- `FeatureDefinition.Providers` restricts which providers can read/write a feature. An empty list means all providers are allowed — the most common case.
- Gate HTTP access with `[RequiresFeature("FeatureName")]` on controllers or actions. Use `[DisableFeatureCheck]` on individual action methods to bypass a class-level gate.
- `SetAsync` with `forceToSet: false` (default) skips the write when the supplied value equals the fallback value of the next lower-priority provider. Set `forceToSet: true` when you must persist the value explicitly (e.g., `GrantAsync`/`RevokeAsync` always use `forceToSet: true`).
- `DeleteAsync` removes all feature values for a given provider and key (e.g., all tenant overrides for a deleted tenant). It silently skips read-only providers.
- Set `InitializeOnStartup = false` on `FeaturesStorageOptions` only when the schema is provisioned out-of-band (migrations job, DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so nothing blocks. This flag affects only the raw-DDL providers (PostgreSQL / SqlServer); EF storage uses migrations and ignores it.

## Core Concepts

### Feature Definitions vs. Feature Values

A *feature definition* describes a feature's metadata: its name, default value, visibility, and which value providers are allowed to supply a value. Definitions are registered statically at startup via `IFeatureDefinitionProvider` and optionally persisted to the database by `FeaturesInitializationBackgroundService`. A *feature value* is the resolved runtime state of a feature for a specific subject (e.g., a specific tenant ID or edition ID). Definitions live in `IStaticFeatureDefinitionStore` / `IDynamicFeatureDefinitionStore`; values live in `IFeatureValueStore` backed by `IFeatureValueRecordRepository`.

### Value Providers and Resolution Order

Feature values are resolved by a chain of `IFeatureValueReadProvider` implementations registered in priority order. The three built-in providers, from lowest to highest priority, are: `DefaultValue` (reads from `FeatureDefinition.DefaultValue`), `Edition`, and `Tenant`. `IFeatureManager.GetAsync` walks the chain from highest priority to lowest and returns the first non-null result. The registration order in the DI container controls precedence: the last-added provider has the highest priority. Custom providers added via `services.AddFeatureValueProvider<T>()` are appended after `Tenant` and thus have the highest priority of all.

### Static Store vs. Dynamic Store

The *static store* (`IStaticFeatureDefinitionStore`) builds the feature catalog once at startup by invoking all registered `IFeatureDefinitionProvider` implementations. It is thread-safe and lazily initialized. The *dynamic store* (`IDynamicFeatureDefinitionStore`) reads feature definitions from the database, caches them in-process with a configurable expiry (`DynamicDefinitionsMemoryCacheExpiration`, default 30 seconds), and coordinates cross-instance refreshes via a distributed cache stamp and a distributed lock. The dynamic store is disabled by default (`IsDynamicFeatureStoreEnabled = false`); enable it only when feature definitions must be edited at runtime without redeployment.

### Feature Value Caching

`FeatureValueStore` caches resolved feature values to avoid repeated database reads. The cache is backed by the registered `ICache` (or a named cache instance when `FeatureManagementOptions.FeatureValueCacheName` is set). When `IFeatureManager.SetAsync` writes a value, the framework publishes a `CacheInvalidationMessage` that causes `FeatureValueCacheItemInvalidator` to evict the affected entries across all nodes. Bypassing `IFeatureManager` to write values directly to the repository breaks this invalidation path.

### Startup Initialization

`FeaturesInitializationBackgroundService` runs after the application starts. It saves static feature definitions to the database (idempotent, guarded by a distributed lock; retries up to 10 times with exponential back-off), then pre-caches the dynamic feature definitions if `IsDynamicFeatureStoreEnabled` is true. Dependents can await `WaitForInitializationAsync()` to block until initialization completes. Both tasks are skipped when their governing option flags are disabled — in that case the service signals completion immediately.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Features.Storage.EntityFramework` | You already use EF Core and want schema managed via EF migrations | You need to avoid an EF dependency or want zero-overhead ADO.NET | Portable across any EF-supported DB; startup validates that all feature entities are in the EF model before hosted services start |
| `Headless.Features.Storage.PostgreSql` | You use PostgreSQL and want no EF Core dependency | You run SQL Server or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against PostgreSQL naming rules |
| `Headless.Features.Storage.SqlServer` | You use SQL Server and want no EF Core dependency | You run PostgreSQL or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against SQL Server naming rules |

---

# Headless.Features.Abstractions

Defines the unified interface for feature management and feature flags across different storage providers.

## Problem Solved

Provides a provider-agnostic feature management API, enabling dynamic feature toggling with support for multi-tenancy, editions, and hierarchical feature values without changing application code.

## Key Features

- `IFeatureManager` — reads and writes feature values across the registered provider chain; supports single-feature and bulk queries with optional provider targeting and fallback
- `IFeatureDefinitionProvider` — contributes feature groups and feature definitions at startup via `IFeatureDefinitionContext`
- `IFeatureDefinitionManager` — looks up and enumerates all registered feature definitions
- `FeatureDefinition` — describes a feature's name, default value, display metadata, allowed providers, and child features (tree structure)
- `FeatureGroupDefinition` — organizes related `FeatureDefinition` instances; supports `GetFlatFeatures()` for depth-first enumeration
- `FeatureValue` — record returned by `GetAsync`/`GetAllAsync` carrying the resolved string value and the `FeatureValueProvider` that supplied it
- `FeatureValueProviderNames` — constants `Tenant`, `Edition`, `DefaultValue` for targeting built-in providers
- Extension methods on `IFeatureManager`: `IsEnabledAsync`, `GetAsync<T>`, `EnsureEnabledAsync`, `GrantAsync`, `RevokeAsync`
- Scoped extension methods: `GetForTenantAsync`, `SetForTenantAsync`, `GrantToTenantAsync`, `RevokeFromTenantAsync`, `DeleteForTenantAsync` (tenant); equivalent `*ForEditionAsync` / `*ToEditionAsync` set (edition); `GetDefaultAsync`, `GetAllDefaultAsync` (default provider)
- `RequiresFeatureAttribute` — gates a controller class or action on one or more features; `IsAnd` property controls AND vs. OR policy (default: OR)
- `DisableFeatureCheckAttribute` — bypasses a class-level `[RequiresFeature]` gate on individual action methods

## Installation

```bash
dotnet add package Headless.Features.Abstractions
```

## Usage

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
    public Task GrantPremiumAsync(string tenantId)
        => features.GrantToTenantAsync("EnableReports", tenantId);

    public Task RevokeAsync(string tenantId)
        => features.RevokeFromTenantAsync("EnableReports", tenantId);
}
```

### Defining Features

```csharp
public sealed class MyFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup("App.Features");

        group.AddChild("MaxUsers", defaultValue: "10");
        group.AddChild("EnableReports", defaultValue: "false");

        // Nested child features
        var billingFeature = group.AddChild("Billing", defaultValue: "false");
        billingFeature.AddChild("Billing.Invoices", defaultValue: "false");
    }
}
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.

---

# Headless.Features.Core

Core implementation of feature management with caching, value providers, and definition management.

## Problem Solved

Provides the full feature management implementation including hierarchical value resolution (Tenant > Edition > Default), feature value caching, background initialization that seeds static definitions into the database, and an extensible value-provider pipeline.

## Key Features

- `FeatureManager` — full implementation of `IFeatureManager`; walks the registered provider chain, caches results, and coordinates writes with cache invalidation
- `IFeatureValueProvider` / `IFeatureValueReadProvider` — read-write and read-only contracts for custom value providers
- Built-in value providers: `DefaultValueFeatureValueProvider`, `EditionFeatureValueProvider`, `TenantFeatureValueProvider`
- `IStaticFeatureDefinitionStore` — builds the feature catalog lazily and thread-safely from all registered `IFeatureDefinitionProvider` implementations
- `IDynamicFeatureDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination
- `FeaturesInitializationBackgroundService` — seeds static definitions to the database at startup with exponential-back-off retry; pre-caches dynamic definitions when enabled
- `FeatureManagementOptions` — tuning options for lock keys, cache expiries, dynamic store toggle, and named cache routing
- `FeaturesStorageOptions` — schema and table name configuration shared across all storage providers
- `HeadlessFeaturesSetupBuilder` — fluent builder returned to `AddHeadlessFeatures`; exposes `ConfigureManagement`, `ConfigureStorage`, and `RegisterExtension`
- `services.AddFeatureDefinitionProvider<T>()` — registers a custom `IFeatureDefinitionProvider`
- `services.AddFeatureValueProvider<T>()` — registers a custom `IFeatureValueReadProvider` (idempotent by type)

## Design Notes

- Value providers are registered with the last-added provider having the highest resolution priority. The built-in order is `DefaultValue` → `Edition` → `Tenant` (Tenant wins). Custom providers added via `AddFeatureValueProvider<T>()` are appended after `Tenant` and therefore have the highest priority. This matters when writing custom providers that must override built-in resolution.
- `AddHeadlessFeatures` is guarded on `IFeatureManager` so it is safe to call more than once (only the first call registers the core; the storage extension always applies). However, only one storage provider extension may be registered — a second call with a different provider throws at startup.
- `FeaturesInitializationBackgroundService` implements `IInitializer` so anything that awaits `WaitForInitializationAsync()` blocks until the seed and pre-cache steps complete. If the host is stopped before initialization finishes, the background task is cancelled and the `TaskCompletionSource` is faulted with `OperationCanceledException`.

## Installation

```bash
dotnet add package Headless.Features.Core
```

## Quick Start

Register the required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessFeatures`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register feature definitions
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// Register the management core + storage in one call
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

### Custom Value Provider

```csharp
// T must implement IFeatureValueReadProvider (read-only) or IFeatureValueProvider (read-write)
builder.Services.AddFeatureValueProvider<MyCustomFeatureValueProvider>();
```

## Configuration

### FeatureManagementOptions

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

        // Persist static definitions to the DB on startup (default: true)
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

### FeaturesStorageOptions

Configure schema and table names via `setup.ConfigureStorage(...)`:

```csharp
services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "features";                              // default
        o.FeatureValuesTableName = "FeatureValues";         // default
        o.FeatureDefinitionsTableName = "FeatureDefinitions"; // default
        o.FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions"; // default
        o.InitializeOnStartup = true;                       // default; set false when schema is provisioned out-of-band
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

## Dependencies

- `Headless.Features.Abstractions`
- `Headless.Domain`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IFeatureManager` as transient
- Registers `IStaticFeatureDefinitionStore`, `IDynamicFeatureDefinitionStore`, `IFeatureDefinitionManager`, `IFeatureValueStore`, `IFeatureValueProviderManager` as singletons
- Registers `DefaultValueFeatureValueProvider`, `EditionFeatureValueProvider`, `TenantFeatureValueProvider` as singletons
- Starts `FeaturesInitializationBackgroundService` as a hosted service
- Registers `FeatureValueCacheItemInvalidator` as a `IDomainEventHandler<EntityChangedEventData<FeatureValueRecord>>`
- Registers `IMethodInvocationFeatureCheckerService` as singleton

---

# Headless.Features.Storage.EntityFramework

Entity Framework Core storage implementation for feature management.

## Problem Solved

Provides EF Core repository implementations for feature values, feature definitions, and feature group definitions using the consumer's own `DbContext`, with schema managed through EF migrations.

## Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via the `HeadlessFeaturesSetupBuilder`
- `modelBuilder.AddHeadlessFeatures(DbContext context)` — applies entity configurations by resolving `FeaturesStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessFeatures(FeaturesStorageOptions options)` — overload for when you already hold the options
- EF repositories for `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
- `FeaturesStorageOptions` for schema and table-name configuration (shared with raw-DDL providers)
- Startup validation gate that inspects the EF model before hosted services start and fails with an actionable message if any feature entity is missing from the model

## Installation

```bash
dotnet add package Headless.Features.Storage.EntityFramework
```

## Quick Start

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Resolves FeaturesStorageOptions from the context's service provider —
        // no need to inject IOptions<FeaturesStorageOptions> into the constructor.
        modelBuilder.AddHeadlessFeatures(this);
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

## Configuration

`FeaturesStorageOptions` defaults:

- `Schema = "features"`
- `FeatureValuesTableName = "FeatureValues"`
- `FeatureDefinitionsTableName = "FeatureDefinitions"`
- `FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions"`
- `InitializeOnStartup = true`

The registration validates identifier names using cross-provider rules (SQL Server superset). The startup gate inspects the EF model before hosted services start and fails with an actionable message if any features entity is missing.

`InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL. Set it on raw-DDL providers (PostgreSQL / SqlServer) only.

## Dependencies

- `Headless.Features.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` (`EfFeatureDefinitionRecordRepository<TContext>`) as singleton
- Registers `IFeatureValueRecordRepository` (`EfFeatureValueRecordRecordRepository<TContext>`) as singleton
- Registers validated `FeaturesStorageOptions`
- Registers `FeaturesEntityValidationStartupGate<TContext>` as `IHostedService`

---

# Headless.Features.Storage.PostgreSql

PostgreSQL raw-DDL storage for feature management.

## Problem Solved

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(Action<PostgreSqlFeaturesOptions> configure)` — overload for full option control
- Idempotent schema, table, and index creation at host startup via `PostgreSqlFeaturesStorageInitializer`
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- `PostgreSqlFeaturesOptions` — connection string and command timeout (`CommandTimeout`, default 30 seconds)
- Shares `FeaturesStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)

## Installation

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` registers the management core automatically.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UsePostgreSql(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

### Options

`PostgreSqlFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | PostgreSQL connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `FeaturesStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

## Dependencies

- `Headless.Features.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlFeatureValueRecordRepository` as `IFeatureValueRecordRepository` (singleton)
- Registers `PostgreSqlFeatureDefinitionRecordRepository` as `IFeatureDefinitionRecordRepository` (singleton)

---

# Headless.Features.Storage.SqlServer

SQL Server raw-DDL storage for feature management.

## Problem Solved

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(Action<SqlServerFeaturesOptions> configure)` — overload for full option control
- Idempotent schema, table, and index creation at host startup via `SqlServerFeaturesStorageInitializer`
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- `SqlServerFeaturesOptions` — connection string and command timeout (`CommandTimeout`, default 30 seconds)
- Shares `FeaturesStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)

## Installation

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` registers the management core automatically.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UseSqlServer(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

### Options

`SqlServerFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `FeaturesStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

## Dependencies

- `Headless.Features.Core`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerFeatureValueRecordRepository` as `IFeatureValueRecordRepository` (singleton)
- Registers `SqlServerFeatureDefinitionRecordRepository` as `IFeatureDefinitionRecordRepository` (singleton)
