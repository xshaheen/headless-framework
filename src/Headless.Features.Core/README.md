# Headless.Features.Core

Core implementation of feature management with caching, value providers, and definition management.

## Problem Solved

Provides the full feature management implementation including hierarchical value resolution (Tenant > Edition > Default), caching, background initialization, and extensible value providers.

## Key Features

- `FeatureManager` - Full implementation of `IFeatureManager`
- Value providers: Default, Edition, Tenant
- Static and dynamic feature definition stores
- Feature value caching with invalidation
- Background service for feature initialization
- Method invocation feature checking

## Installation

```bash
dotnet add package Headless.Features.Core
dotnet add package Headless.Features.Storage.EntityFramework
```

## Quick Start

`AddHeadlessFeatures(...)` registers the management core automatically, so a storage
provider is all you need — no separate `AddFeaturesManagementCore(...)` call. Register the
required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IGuidGenerator`) first,
then call `AddHeadlessFeatures`.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Requires: TimeProvider, ICache, IDistributedLock, IGuidGenerator

// Register feature definition providers
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// Add management core + storage in one call (Entity Framework shown)
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

For Entity Framework storage, register an `IDbContextFactory<AppDbContext>` and call
`modelBuilder.AddHeadlessFeatures(featuresStorageOptions)` from your DbContext model configuration.

Call `AddFeaturesManagementCore(...)` directly only when you need the management core
without a Headless storage provider, or to set management options (`CacheKeyPrefix`) before
`AddHeadlessFeatures`. It is idempotent with `AddHeadlessFeatures`, so calling both is safe.

### Custom Value Provider

```csharp
builder.Services.AddFeatureValueProvider<CustomFeatureValueProvider>();
```

## Configuration

### Options

```csharp
services.AddFeaturesManagementCore(options =>
{
    options.CacheKeyPrefix = "features:";  // Cache key prefix
});
```

## Dependencies

- `Headless.Features.Abstractions`
- `Headless.Domain`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IFeatureManager` as transient
- Registers feature stores as singletons
- Starts `FeaturesInitializationBackgroundService` hosted service
- Registers cache invalidation handler for feature value changes
