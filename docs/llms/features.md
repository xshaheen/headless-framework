---
domain: Feature Management
packages: Features.Abstractions, Features.Core, Features.Storage.EntityFramework, Features.Storage.PostgreSql, Features.Storage.SqlServer
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
        - [Custom Schema / Table Names](#custom-schema--table-names)
        - [Using Custom DbContext](#using-custom-dbcontext)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Features.Storage.PostgreSql](#headlessfeaturesstoragepostgresql)
- [Headless.Features.Storage.SqlServer](#headlessfeaturesstoragesqlserver)

> Dynamic feature flags and feature value management with hierarchical resolution (Tenant > Edition > Default), caching, and EF Core persistence.

## Quick Orientation

Install all three packages for a complete setup:

- `Headless.Features.Abstractions` — interfaces (`IFeatureManager`, `IFeatureDefinitionProvider`)
- `Headless.Features.Core` — implementation with caching, value providers, background initialization
- `Headless.Features.Storage.EntityFramework` — database persistence via EF Core
- `Headless.Features.Storage.PostgreSql` — raw PostgreSQL persistence
- `Headless.Features.Storage.SqlServer` — raw SQL Server persistence

Typical registration:

```csharp
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();
// AddHeadlessFeatures registers the management core automatically.
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

Core requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered.

## Agent Instructions

- Inject `IFeatureManager` to read/write feature values. Do NOT use Microsoft.FeatureManagement — this is a separate system.
- Define features by implementing `IFeatureDefinitionProvider` and calling `context.AddGroup()` / `group.AddChild()`.
- Value resolution order: Tenant > Edition > Default. Custom providers via `AddFeatureValueProvider<T>()`.
- `AddHeadlessFeatures(...)` is the single entry point — it registers the management core automatically alongside the storage provider. To tune management options, call `setup.ConfigureManagement(options => ...)` inside the setup block (an `(options, IServiceProvider)` overload also exists); `services.Configure<FeatureManagementOptions>(...)` works too and composes regardless of order.
- Storage registration: register `AddDbContextFactory<TContext>()`, call `modelBuilder.AddHeadlessFeatures(this)` in `OnModelCreating` (resolves `FeaturesStorageOptions` from the context's service provider — no `IOptions<>` constructor injection needed; an `(options)` overload exists when you already hold them), then use `AddHeadlessFeatures(setup => setup.UseEntityFramework<TContext>())`.
- Raw storage registration: use `AddHeadlessFeatures(setup => setup.UsePostgreSql(connectionString))` or `UseSqlServer(connectionString)`.
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

        group.AddChild("MaxUsers", defaultValue: "10");
        group.AddChild("EnableReports", defaultValue: "false");
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

// Register feature definition providers
builder.Services.AddFeatureDefinitionProvider<MyFeatureDefinitionProvider>();

// Add management core + storage in one call (e.g., Entity Framework).
// AddHeadlessFeatures registers the management core automatically.
builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<AppDbContext>());
```

### Custom Value Provider

```csharp
builder.Services.AddFeatureValueProvider<CustomFeatureValueProvider>();
```

## Configuration

### Options

Tune the management options through `setup.ConfigureManagement(...)` inside the `AddHeadlessFeatures` block, next to `ConfigureStorage`:

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

A `(options, IServiceProvider)` overload is available when configuration needs resolved services. `services.Configure<FeatureManagementOptions>(...)` also works and composes with the auto-registration regardless of order.

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

Provides EF Core repository implementations for feature values, feature definitions, and feature group definitions using the consumer's own `DbContext`.

## Key Features

- `AddHeadlessFeatures(setup => setup.UseEntityFramework<TContext>())` storage registration
- `modelBuilder.AddHeadlessFeatures(this)` entity mapping for shared contexts (resolves `FeaturesStorageOptions` from the context's service provider; an `(options)` overload exists for when you already hold the options)
- EF repositories for feature definitions and values
- `FeaturesStorageOptions` for schema and table-name configuration

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

`FeaturesStorageOptions` defaults preserve the original physical layout:

- `Schema = "features"`
- `FeatureValuesTableName = "FeatureValues"`
- `FeatureDefinitionsTableName = "FeatureDefinitions"`
- `FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions"`
- `InitializeOnStartup = true`

The registration validates these values on startup. The startup gate also inspects the EF model before hosted services start and fails with an actionable message if any features entity is missing.

Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA), so the raw-DDL startup initializer is skipped (no-op). The initializer still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. This only affects raw-DDL self-initializing providers (PostgreSQL / SqlServer); EF-mode storage uses migrations and ignores the flag.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(...);
});
```

## Dependencies

- `Headless.Features.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` as singleton
- Registers `IFeatureValueRecordRepository` as singleton
- Registers validated `FeaturesStorageOptions`
- Registers an `IHostedLifecycleService` startup gate for missing entity mappings
---
# Headless.Features.Storage.PostgreSql

PostgreSQL raw-DDL storage for feature management.

## Problem Solved

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence.

## Key Features

- `AddHeadlessFeatures(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- Shares `FeaturesStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `FeaturesStorageOptions` on the shared features builder. Configure the connection string through `PostgreSqlFeaturesOptions`.

## Dependencies

- `Headless.Features.Storage.EntityFramework`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
---
# Headless.Features.Storage.SqlServer

SQL Server raw-DDL storage for feature management.

## Problem Solved

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence.

## Key Features

- `AddHeadlessFeatures(setup => setup.UseSqlServer(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- Shares `FeaturesStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UseSqlServer(connectionString);
});
```

## Configuration

Configure schema and table names through `FeaturesStorageOptions` on the shared features builder. Configure the connection string through `SqlServerFeaturesOptions`.

## Dependencies

- `Headless.Features.Storage.EntityFramework`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw SQL Server implementations of `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
