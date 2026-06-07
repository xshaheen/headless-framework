# Headless.Settings.Storage.EntityFramework

Entity Framework Core storage for settings management.

## Problem Solved

Provides EF Core repository implementations for setting definitions and values using the consumer's own `DbContext`.

## Key Features

- `AddHeadlessSettings(setup => setup.UseEntityFramework<TContext>())` storage registration
- `modelBuilder.AddHeadlessSettings(this)` entity mapping for shared contexts (resolves `SettingsStorageOptions` from the context's service provider; an `(options)` overload exists for when you already hold the options)
- `EfSettingValueRecordRepository` for setting values
- `EfSettingDefinitionRecordRepository` for definition records
- `SettingsStorageOptions` for schema and table-name configuration

## Design Notes

The package no longer ships a dedicated settings DbContext or settings-specific DbContext interface. Consumers register `AddDbContextFactory<TContext>()`, map the Headless entities in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties.

Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`. Writes commit through a fresh context owned by the repository, so they are not enlisted in the consumer's outer transaction.

## Installation

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

## Quick Start

`AddHeadlessSettings(...)` registers the settings management core automatically. Register
the required services first — `TimeProvider`, caching, distributed lock, and
`IStringEncryptionService` (the management core throws on startup if encryption is missing).

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Resolves SettingsStorageOptions from the context's service provider —
        // no need to inject IOptions<SettingsStorageOptions> into the constructor.
        modelBuilder.AddHeadlessSettings(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);

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
