# Headless.Features.Storage.EntityFramework

Entity Framework Core storage implementation for feature management.

## Problem Solved

Provides persistent storage for feature definitions and values using Entity Framework Core, enabling database-backed feature management with full CRUD support.

## Key Features

- `IFeaturesDbContext` - DbContext interface for features
- `FeaturesDbContext` - Ready-to-use DbContext
- `FeaturesStorageOptions` - Schema and table-name configuration
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

### Custom Schema / Table Names

```csharp
builder.Services.AddFeaturesManagementDbContextStorage(
    options => options.UseNpgsql(builder.Configuration.GetConnectionString("Features")),
    storage =>
    {
        storage.Schema = "app_features";
        storage.FeatureValuesTableName = "FeatureValues";
        storage.FeatureDefinitionsTableName = "FeatureDefinitions";
        storage.FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions";
    }
);
```

### Using Custom DbContext

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IFeaturesDbContext
{
    public DbSet<FeatureDefinitionRecord> FeatureDefinitions => Set<FeatureDefinitionRecord>();
    public DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions => Set<FeatureGroupDefinitionRecord>();
    public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddFeaturesConfiguration(this);
    }
}

// Registration
builder.Services.AddFeaturesManagementDbContextStorage<AppDbContext>(storage =>
{
    storage.Schema = "app_features";
});
```

## Configuration

`FeaturesStorageOptions` defaults preserve the original physical layout:

- `Schema = "features"`
- `FeatureValuesTableName = "FeatureValues"`
- `FeatureDefinitionsTableName = "FeatureDefinitions"`
- `FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions"`

The storage registration validates these values on startup; schema and table names must be non-empty.

## Dependencies

- `Headless.Features.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` as singleton
- Registers `IFeatureValueRecordRepository` as singleton
- Registers validated `FeaturesStorageOptions`
- Uses pooled DbContext factory for performance
