# Headless.Features.Storage.EntityFramework

Entity Framework Core storage implementation for feature management.

## Problem Solved

Provides EF Core repository implementations for feature values, feature definitions, and feature group definitions using the consumer's own `DbContext`.

## Key Features

- `AddHeadlessFeatures(setup => setup.UseEntityFramework<TContext>())` storage registration
- `modelBuilder.AddHeadlessFeatures(this)` entity mapping for shared contexts (resolves `FeaturesStorageOptions` from the context's service provider; an `(options)` overload exists for when you already hold the options)
- `EfFeatureValueRecordRecordRepository` for feature values
- `EfFeatureDefinitionRecordRepository` for feature definitions and groups
- `FeaturesStorageOptions` for schema and table-name configuration

## Installation

```bash
dotnet add package Headless.Features.Storage.EntityFramework
```

## Quick Start

`AddHeadlessFeatures(...)` registers the features management core automatically. Register
the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and
`IGuidGenerator`.

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

The registration validates these values on startup. The startup gate also inspects the EF model before hosted services start and fails with an actionable message if any features entity is missing.

## Dependencies

- `Headless.Features.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` as singleton
- Registers `IFeatureValueRecordRepository` as singleton
- Registers validated `FeaturesStorageOptions`
- Registers an `IHostedLifecycleService` startup gate for missing entity mappings
