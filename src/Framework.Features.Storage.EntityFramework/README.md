# Framework.Features.Storage.EntityFramework

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
dotnet add package Framework.Features.Storage.EntityFramework
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

- `Framework.Features.Core`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IFeatureDefinitionRecordRepository` as singleton
- Registers `IFeatureValueRecordRepository` as singleton
- Uses pooled DbContext factory for performance
