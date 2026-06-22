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

Configure management options via `setup.ConfigureManagement(...)` or `services.Configure<FeatureManagementOptions>(...)`:

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

        // How long dynamic definitions stay in the in-process cache before the stamp is re-checked (default: 30 seconds)
        options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

Configure schema and table names via `setup.ConfigureStorage(...)`:

```csharp
services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "features";                               // default
        o.FeatureValuesTableName = "FeatureValues";          // default
        o.FeatureDefinitionsTableName = "FeatureDefinitions"; // default
        o.FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions"; // default
        o.InitializeOnStartup = true;                        // default; set false when schema is provisioned out-of-band
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
