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
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Requires: TimeProvider, ICache, IResourceLock, IGuidGenerator
builder.Services.AddFeaturesManagementCore(options =>
{
    options.CacheKeyPrefix = "features:";
});

// Register feature definition providers
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// Add storage (e.g., Entity Framework)
builder.Services.AddFeaturesManagementDbContextStorage<AppDbContext>();
```

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
- `Headless.ResourceLocks.Abstractions`

## Side Effects

- Registers `IFeatureManager` as transient
- Registers feature stores as singletons
- Starts `FeaturesInitializationBackgroundService` hosted service
- Registers cache invalidation handler for feature value changes
