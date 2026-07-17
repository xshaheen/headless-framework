# Headless.Settings.Storage.EntityFramework

Entity Framework Core storage implementation for settings management.

## Problem Solved

Provides EF Core repository implementations for setting values and definitions using the consumer's own `DbContext`, with schema managed through EF migrations.

## Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessSettingsSetupBuilder`
- `modelBuilder.AddHeadlessSettings(DbContext context)` — applies entity configurations by resolving `SettingsStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessSettings(SettingsStorageOptions options)` — overload for when you already hold the options
- EF repositories for `ISettingValueRecordRepository` and `ISettingDefinitionRecordRepository`
- `SettingsStorageOptions` for schema and table-name configuration (shared with raw-DDL providers)
- Startup validation gate that inspects the EF model before hosted services start and fails with an actionable message if any settings entity is missing

## Design Notes

The package does not ship a dedicated settings `DbContext` or settings-specific `DbContext` interface. Consumers register `AddDbContextFactory<TContext>()`, map the Headless entities in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties. Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`. Writes commit through a fresh context owned by the repository, so they are not enlisted in the consumer's outer transaction.

## Installation

```bash
dotnet add package Headless.Settings.Storage.EntityFramework
```

## Quick Start

`AddHeadlessSettings(...)` registers the settings management core automatically. Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService` (the management core throws on startup if encryption is missing).

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

// AddHeadlessSettings registers the management core automatically.
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
- `InitializeOnStartup = true`

The registration validates identifier names using cross-provider rules (SQL Server superset). The startup gate inspects the EF model before hosted services start and fails with an actionable message if any settings entity is missing. `InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL.

## Dependencies

- `Headless.Settings.Core`
- `Headless.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `ISettingValueRecordRepository` (`EfSettingValueRecordRepository<TContext>`) as singleton
- Registers `ISettingDefinitionRecordRepository` (`EfSettingDefinitionRecordRepository<TContext>`) as singleton
- Registers validated `SettingsStorageOptions`
- Registers `SettingsEntityValidationStartupGate<TContext>` as `IHostedService`
