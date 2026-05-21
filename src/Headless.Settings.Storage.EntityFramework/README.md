# Headless.Settings.Storage.EntityFramework

Entity Framework Core storage for settings management.

## Problem Solved

Provides EF Core repository implementations for storing setting definitions and values, with support for both dedicated DbContext and shared application DbContext.

## Key Features

- `EfSettingValueRecordRepository` - Setting value storage
- `EfSettingDefinitionRecordRepository` - Definition record storage
- `SettingsDbContext` - Dedicated settings DbContext
- `ISettingsDbContext` - Interface for shared DbContext integration
- `SettingsStorageOptions` - Schema and table-name configuration
- Model builder extensions for entity configuration

## Installation

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

## Quick Start

### Option 1: Dedicated DbContext

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
builder.Services.AddSettingsManagementCore();
builder.Services.AddSettingsManagementDbContextStorage(options =>
{
    options.UseNpgsql(connectionString);
});
```

### Custom Schema / Table Names

```csharp
builder.Services.AddSettingsManagementDbContextStorage(
    options => options.UseNpgsql(connectionString),
    storage =>
    {
        storage.Schema = "app_settings";
        storage.SettingValuesTableName = "SettingValues";
        storage.SettingDefinitionsTableName = "SettingDefinitions";
    }
);
```

### Option 2: Shared DbContext

```csharp
// In your DbContext
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), ISettingsDbContext
{
    public DbSet<SettingValueRecord> SettingValues => Set<SettingValueRecord>();
    public DbSet<SettingDefinitionRecord> SettingDefinitions => Set<SettingDefinitionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSettingsConfiguration(this);
    }
}

// In Program.cs
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
builder.Services.AddSettingsManagementCore();
builder.Services.AddSettingsManagementDbContextStorage<AppDbContext>(storage =>
{
    storage.Schema = "app_settings";
});
```

## Configuration

Pre-requisite: register string encryption before calling `AddSettingsManagementCore(...)`, for example:

```csharp
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
```

`SettingsStorageOptions` defaults preserve the original physical layout:

- `Schema = "settings"`
- `SettingValuesTableName = "SettingValues"`
- `SettingDefinitionsTableName = "SettingDefinitions"`

The storage registration validates these values on startup; schema and table names must be non-empty.

## Dependencies

- `Headless.Settings.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `ISettingValueRecordRepository` as singleton
- Registers `ISettingDefinitionRecordRepository` as singleton
- Registers validated `SettingsStorageOptions`
- Optionally registers pooled `SettingsDbContext` factory
