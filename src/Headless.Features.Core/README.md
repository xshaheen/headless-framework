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
provider is all you need. Register the required services (`TimeProvider`, `ICache`,
`IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessFeatures`.

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

### Custom Value Provider

```csharp
builder.Services.AddFeatureValueProvider<CustomFeatureValueProvider>();
```

## Configuration

### Options

Tune the management options through `setup.ConfigureManagement(...)` inside the
`AddHeadlessFeatures` block, next to `ConfigureStorage`:

```csharp
services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureManagement(options =>
    {
        options.CrossApplicationsCommonLockKey = "features:common_update_lock";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

The `(options, IServiceProvider)` overload is available when configuration needs resolved
services. `services.Configure<FeatureManagementOptions>(...)` also works and composes with
the auto-registration regardless of order.

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
