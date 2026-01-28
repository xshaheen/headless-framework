# Headless.Settings.Storage.EntityFramework

Entity Framework Core storage for settings management.

## Problem Solved

Provides EF Core repository implementations for storing setting definitions and values, with support for both dedicated DbContext and shared application DbContext.

## Key Features

- `EfSettingValueRecordRepository` - Setting value storage
- `EfSettingDefinitionRecordRepository` - Definition record storage
- `SettingsDbContext` - Dedicated settings DbContext
- `ISettingsDbContext` - Interface for shared DbContext integration
- Model builder extensions for entity configuration

## Installation

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

## Quick Start

### Option 1: Dedicated DbContext

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSettingsManagementCore();
builder.Services.AddSettingsManagementDbContextStorage(options =>
{
    options.UseNpgsql(connectionString);
});
```

### Option 2: Shared DbContext

```csharp
// In your DbContext
public class AppDbContext : DbContext, ISettingsDbContext
{
    public DbSet<SettingValueRecord> SettingValues => Set<SettingValueRecord>();
    public DbSet<SettingDefinitionRecord> SettingDefinitions => Set<SettingDefinitionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplySettingsConfiguration();
    }
}

// In Program.cs
builder.Services.AddSettingsManagementCore();
builder.Services.AddSettingsManagementDbContextStorage<AppDbContext>();
```

## Configuration

No additional configuration beyond DbContext setup.

## Dependencies

- `Headless.Settings.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `ISettingValueRecordRepository` as singleton
- Registers `ISettingDefinitionRecordRepository` as singleton
- Optionally registers pooled `SettingsDbContext` factory
