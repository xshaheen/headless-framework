---
domain: Feature Management
packages: Features.Abstractions, Features.Core, Features.Storage.EntityFramework
---

# Feature Management

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Features.Abstractions](#headlessfeaturesabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Usage](#usage)
        - [Defining Features](#defining-features)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Features.Core](#headlessfeaturescore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start)
        - [Custom Value Provider](#custom-value-provider)
    - [Configuration](#configuration-1)
        - [Options](#options)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Features.Storage.EntityFramework](#headlessfeaturesstorageentityframework)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-1)
        - [Using Built-in DbContext](#using-built-in-dbcontext)
        - [Using Custom DbContext](#using-custom-dbcontext)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Dynamic feature flags and feature value management with hierarchical resolution (Tenant > Edition > Default), caching, and EF Core persistence.

## Quick Orientation

Install all three packages for a complete setup:

- `Headless.Features.Abstractions` — interfaces (`IFeatureManager`, `IFeatureDefinitionProvider`)
- `Headless.Features.Core` — implementation with caching, value providers, background initialization
- `Headless.Features.Storage.EntityFramework` — database persistence via EF Core

Typical registration:

```csharp
builder.Services.AddFeaturesManagementCore(options => { options.CacheKeyPrefix = "features:"; });
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();
builder.Services.AddFeaturesManagementDbContextStorage<AppDbContext>();
```

Core requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered.

## Agent Instructions

- Inject `IFeatureManager` to read/write feature values. Do NOT use Microsoft.FeatureManagement — this is a separate system.
- Define features by implementing `IFeatureDefinitionProvider` and calling `context.AddGroup()` / `group.AddFeature()`.
- Value resolution order: Tenant > Edition > Default. Custom providers via `AddFeatureValueProvider<T>()`.
- Storage registration: use `AddFeaturesManagementDbContextStorage<TDbContext>()` for custom DbContext, or the overload with `Action<DbContextOptionsBuilder>` for a standalone context.
- For custom DbContext, implement `IFeaturesDbContext` and call `modelBuilder.ConfigureFeatureManagement()` in `OnModelCreating`.
- Feature caching is automatic; invalidation is handled via `CacheInvalidationMessage`. Ensure caching and distributed lock infrastructure is registered.
- `FeaturesInitializationBackgroundService` runs at startup — do not manually initialize features.
- Gate access with `RequiresFeatureAttribute` on controllers/actions.

---

# Headless.Features.Abstractions

Defines the unified interface for feature management and feature flags across different storage providers.

## Problem Solved

Provides a provider-agnostic feature management API, enabling dynamic feature toggling with support for multi-tenancy, editions, and hierarchical feature values without changing application code.

## Key Features

- `IFeatureManager` - Core interface for getting/setting feature values
- `IFeatureDefinitionProvider` - Define features in code
- `IFeatureDefinitionManager` - Manage feature definitions
- Feature value providers (Default, Edition, Tenant)
- `RequiresFeatureAttribute` - Attribute-based feature gating
- Hierarchical feature definitions with groups

## Installation

```bash
dotnet add package Headless.Features.Abstractions
```

## Usage

```csharp
public sealed class BillingService(IFeatureManager features)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var maxUsers = await features.GetAsync("MaxUsers", cancellationToken: ct).ConfigureAwait(false);

        if (int.Parse(maxUsers.Value ?? "10") > 100)
        {
            // Premium feature logic
        }
    }
}
```

### Defining Features

```csharp
public class MyFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup("App.Features");

        group.AddFeature("MaxUsers", defaultValue: "10");
        group.AddFeature("EnableReports", defaultValue: "false");
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

## None.

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

// Requires: TimeProvider, ICache, IDistributedLock, IGuidGenerator
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
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IFeatureManager` as transient
- Registers feature stores as singletons
- Starts `FeaturesInitializationBackgroundService` hosted service
- Registers cache invalidation handler for feature value changes

---

# Headless.Features.Storage.EntityFramework

Entity Framework Core storage implementation for feature management.

## Problem Solved

Provides persistent storage for feature definitions and values using Entity Framework Core, enabling database-backed feature management with full CRUD support.

## Key Features

- `IFeaturesDbContext` - DbContext interface for features
- `FeaturesDbContext` - Ready-to-use DbContext
- EF repositories for feature definitions and values
- Model builder extensions for custom DbContext integration
- Pooled DbContext factory support

## Installation

```bash
dotnet add package Headless.Features.Storage.EntityFramework
```

## Quick Start

### Using Built-in DbContext

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeaturesManagementDbContextStorage(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Features"))
);
```

### Using Custom DbContext

```csharp
public class AppDbContext : DbContext, IFeaturesDbContext
{
    public DbSet<FeatureDefinitionRecord> FeatureDefinitions => Set<FeatureDefinitionRecord>();
    public DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions => Set<FeatureGroupDefinitionRecord>();
    public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureFeatureManagement();
    }
}

// Registration
builder.Services.AddFeaturesManagementDbContextStorage<AppDbContext>();
```

## Configuration

No additional configuration required beyond DbContext setup.

## Dependencies

- `Headless.Features.Core`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` as singleton
- Registers `IFeatureValueRecordRepository` as singleton
- Uses pooled DbContext factory for performance
