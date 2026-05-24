# Headless.Settings.Storage.EntityFramework

Entity Framework Core storage for settings management.

## Problem Solved

Provides EF Core repository implementations for setting definitions and values using the consumer's own `DbContext`.

## Key Features

- `AddHeadlessSettings(setup => setup.UseEntityFramework<TContext>())` storage registration
- `modelBuilder.AddHeadlessSettings(options)` entity mapping for shared contexts
- `EfSettingValueRecordRepository` for setting values
- `EfSettingDefinitionRecordRepository` for definition records
- `SettingsStorageOptions` for schema and table-name configuration

## Design Notes

The package no longer ships a dedicated a dedicated settings DbContext or settings-specific DbContext interface. Consumers register `AddDbContextFactory<TContext>()`, map the Headless entities in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties.

Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`. Writes commit through a fresh context owned by the repository, so they are not enlisted in the consumer's outer transaction.

## Installation

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

## Quick Start

```csharp
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IOptions<SettingsStorageOptions> settingsStorage
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessSettings(settingsStorage.Value);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
builder.Services.AddSettingsManagementCore(_ => { });
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage =>
    {
        storage.Schema = "app_settings";
        storage.SettingValuesTableName = "SettingValues";
        storage.SettingDefinitionsTableName = "SettingDefinitions";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

## Configuration

`SettingsStorageOptions` defaults:

- `Schema = "settings"`
- `SettingValuesTableName = "SettingValues"`
- `SettingDefinitionsTableName = "SettingDefinitions"`

The registration validates these values on startup. The startup gate also inspects the EF model before hosted services start and fails with an actionable message if `SettingValueRecord` or `SettingDefinitionRecord` is missing.

## Dependencies

- `Headless.Settings.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `ISettingValueRecordRepository` as singleton
- Registers `ISettingDefinitionRecordRepository` as singleton
- Registers validated `SettingsStorageOptions`
- Registers an `IHostedLifecycleService` startup gate for missing entity mappings
