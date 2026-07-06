# Headless.Features.Storage.EntityFramework

Entity Framework Core storage implementation for feature management.

## Problem Solved

Provides EF Core repository implementations for feature values, feature definitions, and feature group definitions using the consumer's own `DbContext`, with schema managed through EF migrations.

## Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via the `HeadlessFeaturesSetupBuilder`
- `modelBuilder.AddHeadlessFeatures(DbContext context)` — applies entity configurations by resolving `FeaturesStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessFeatures(FeaturesStorageOptions options)` — overload for when you already hold the options
- EF repositories for `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
- `FeatureValueRecord` maps `DateCreated` / `DateUpdated` audit columns (via `ConfigureHeadlessConvention`); the Headless audit save-processor stamps them on `SaveChanges`
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

The registration validates identifier names using cross-provider rules (SQL Server superset). The startup gate inspects the EF model before hosted services start and fails with an actionable message if any features entity is missing.

`InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL. Set it on raw-DDL providers (`Headless.Features.Storage.PostgreSql` / `Headless.Features.Storage.SqlServer`) only.

## Dependencies

- `Headless.Features.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` (`EfFeatureDefinitionRecordRepository<TContext>`) as singleton
- Registers `IFeatureValueRecordRepository` (`EfFeatureValueRecordRepository<TContext>`) as singleton
- Registers validated `FeaturesStorageOptions`
- Registers `FeaturesEntityValidationStartupGate<TContext>` as `IHostedService`
