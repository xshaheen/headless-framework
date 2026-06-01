---
domain: Settings
packages: Settings.Abstractions, Settings.Core, Settings.Storage.EntityFramework, Settings.Storage.PostgreSql, Settings.Storage.SqlServer
---

# Settings

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Settings.Abstractions](#headlesssettingsabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Settings.Core](#headlesssettingscore)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Usage](#usage-1)
    - [Define Settings](#define-settings)
    - [Read/Write Settings](#readwrite-settings)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Settings.Storage.EntityFramework](#headlesssettingsstorageentityframework)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
    - [Option 1: Dedicated DbContext](#option-1-dedicated-dbcontext)
    - [Custom Schema / Table Names](#custom-schema--table-names)
    - [Option 2: Shared DbContext](#option-2-shared-dbcontext)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Settings.Storage.PostgreSql](#headlesssettingsstoragepostgresql)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-3)
  - [Configuration](#configuration-3)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)
- [Headless.Settings.Storage.SqlServer](#headlesssettingsstoragesqlserver)
  - [Problem Solved](#problem-solved-4)
  - [Key Features](#key-features-4)
  - [Installation](#installation-4)
  - [Quick Start](#quick-start-4)
  - [Configuration](#configuration-4)
  - [Dependencies](#dependencies-4)
  - [Side Effects](#side-effects-4)

> Dynamic, hierarchical application settings with runtime read/write support and multiple value providers (default, config, global, tenant, user).

## Quick Orientation

Install the three settings packages:
- `Headless.Settings.Abstractions` -- interfaces (`ISettingManager`, `ISettingDefinitionProvider`, `SettingDefinition`)
- `Headless.Settings.Core` -- implementation with hierarchical providers, caching, encryption, background init
- one storage provider: `Headless.Settings.Storage.EntityFramework`, `Headless.Settings.Storage.PostgreSql`, or `Headless.Settings.Storage.SqlServer`

Minimal wiring:
```csharp
builder.Services.AddCaching();
builder.Services.AddDistributedLock();
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
// AddHeadlessSettings registers the management core automatically.
builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

Define settings via `ISettingDefinitionProvider.Define()`. Read/write via `ISettingManager.FindAsync()` / `SetAsync()`. Provider hierarchy resolves values in order: User > Tenant > Global > Configuration > Default.

## Agent Instructions

- Use this for **runtime-changeable settings**, NOT for static configuration. For static config, use `IOptions<T>` / `IConfiguration`.
- Always install all three packages together. Abstractions alone gives you nothing runnable; Core needs a storage backend.
- `ISettingManager` is the primary entry point. Call `FindAsync(name)` to read, `SetAsync(name, value, providerName, providerKey)` to write.
- Provider names are constants on `SettingValueProviderNames`: `Default`, `Configuration`, `Global`, `Tenant`, `User`.
- For sensitive settings, set `isEncrypted: true` on `SettingDefinition` -- Core handles encryption/decryption automatically.
- Core registers a `SettingsInitializationBackgroundService` hosted service -- do not register your own init logic for settings.
- For EF storage, register `AddDbContextFactory<TContext>()`, call `modelBuilder.AddHeadlessSettings(options)` in `OnModelCreating`, then `AddHeadlessSettings(setup => setup.UseEntityFramework<TContext>())`.
- Dependencies: Core requires `Headless.Caching.Abstractions` and `Headless.DistributedLocks.Abstractions` to be registered.
- `AddHeadlessSettings(...)` is the single entry point — it registers the management core automatically alongside the storage provider. To tune management options, call `setup.ConfigureManagement(options => ...)` inside the setup block (an `(options, IServiceProvider)` overload also exists); `services.Configure<SettingManagementOptions>(...)` works too and composes regardless of order.
- Required services before `AddHeadlessSettings(...)`: `TimeProvider`, caching, distributed lock, and `IStringEncryptionService` (the core throws on startup if encryption is missing). Recommended: `AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"))`.

---
# Headless.Settings.Abstractions

Defines interfaces for dynamic application settings management.

## Problem Solved

Provides a provider-agnostic API for managing application settings with support for multiple value providers (default, configuration, global, tenant, user), enabling hierarchical settings that can be overridden at different levels.

## Key Features

- `ISettingManager` - Core interface for reading/writing settings
- `ISettingDefinitionManager` - Setting definition management
- `ISettingDefinitionProvider` - Define settings in code
- `SettingDefinition` - Setting metadata with validation
- Multiple value provider support (default, config, global, tenant, user)
- Extension methods for provider-specific operations

## Installation

```bash
dotnet add package Headless.Settings.Abstractions
```

## Usage

```csharp
public sealed class NotificationService(ISettingManager settingManager)
{
    public async Task<bool> IsEmailEnabledAsync(CancellationToken ct)
    {
        var value = await settingManager.FindAsync(
            "Notifications.EmailEnabled",
            cancellationToken: ct
        );
        return bool.TryParse(value, out var enabled) && enabled;
    }

    public async Task SetUserPreferenceAsync(
        string userId,
        string settingName,
        string value,
        CancellationToken ct)
    {
        await settingManager.SetAsync(
            settingName,
            value,
            SettingValueProviderNames.User,
            userId,
            cancellationToken: ct
        );
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
---
# Headless.Settings.Core

Core implementation of dynamic settings management with hierarchical value providers.

## Problem Solved

Provides full settings management implementation with multiple value providers (default, configuration, global, tenant, user), caching, encryption for sensitive settings, and automatic initialization via background service.

## Key Features

- `SettingManager` - Full ISettingManager implementation
- `SettingDefinitionManager` - Definition management with static/dynamic stores
- Built-in value providers:
  - `DefaultValueSettingValueProvider` - Default values from definitions
  - `ConfigurationSettingValueProvider` - Values from IConfiguration
  - `GlobalSettingValueProvider` - Application-wide settings
  - `TenantSettingValueProvider` - Tenant-specific settings
  - `UserSettingValueProvider` - User-specific settings
- Setting encryption for sensitive values
- Cache invalidation on changes
- Background initialization service

## Installation

```bash
dotnet add package Headless.Settings.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add required dependencies
builder.Services.AddCaching();
builder.Services.AddDistributedLock();
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);

// Add settings management + storage in one call (EF Core).
// AddHeadlessSettings registers the management core automatically.
builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());

// Register setting definition providers
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

## Usage

### Define Settings

```csharp
public sealed class AppSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(
            name: "App.MaxFileSize",
            displayName: "Maximum File Size",
            defaultValue: "10485760",
            isEncrypted: false
        ));

        context.Add(new SettingDefinition(
            name: "App.ApiKey",
            displayName: "API Key",
            isEncrypted: true
        ));
    }
}
```

### Read/Write Settings

```csharp
public sealed class ConfigService(ISettingManager settings)
{
    public async Task<int> GetMaxFileSizeAsync(CancellationToken ct)
    {
        var value = await settings.FindAsync("App.MaxFileSize", cancellationToken: ct);
        return int.TryParse(value, out var size) ? size : 10485760;
    }

    public async Task SetTenantApiKeyAsync(string tenantId, string apiKey, CancellationToken ct)
    {
        await settings.SetAsync(
            "App.ApiKey",
            apiKey,
            SettingValueProviderNames.Tenant,
            tenantId,
            cancellationToken: ct
        );
    }
}
```

## Configuration

Pre-requisite: configure and register string encryption before settings management:

```json
{
  "Headless": {
    "StringEncryption": {
      "DefaultPassPhrase": "YourPassPhrase123",
      "InitVectorBytes": "WW91ckluaXRWZWN0b3IxNg==",
      "DefaultSalt": "WW91clNhbHQ="
    }
  }
}
```

Then register settings management. Tune management options through `setup.ConfigureManagement(...)` inside the `AddHeadlessSettings` block, next to `ConfigureStorage`:

```csharp
services.AddStringEncryptionService(configuration.GetRequiredSection("Headless:StringEncryption"));

services.AddHeadlessSettings(setup =>
{
    setup.ConfigureManagement(options =>
    {
        // Cache expiration for setting values (default: 5 hours)
        options.ValueCacheExpiration = TimeSpan.FromHours(5);

        // Lock settings for cross-application updates
        options.CrossApplicationsCommonLockKey = "settings:common_update_lock";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

A `(options, IServiceProvider)` overload is available when configuration needs resolved services. `services.Configure<SettingManagementOptions>(...)` also works and composes with the auto-registration regardless of order.

## Dependencies

- `Headless.Settings.Abstractions`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Domain`

## Side Effects

- Registers `ISettingManager` as singleton
- Registers `ISettingDefinitionManager` as singleton
- Registers `SettingsInitializationBackgroundService` as hosted service
- Registers cache invalidation event handler
---
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

The registration validates these values on startup. The startup gate also inspects the EF model before hosted services start and fails with an actionable message if `SettingValueRecord` or `SettingDefinitionRecord` is missing.

Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA), so the raw-DDL startup initializer is skipped (no-op). The initializer still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. This only affects raw-DDL self-initializing providers (PostgreSQL / SqlServer); EF-mode storage uses migrations and ignores the flag.

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(...);
});
```

## Dependencies

- `Headless.Settings.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `ISettingValueRecordRepository` as singleton
- Registers `ISettingDefinitionRecordRepository` as singleton
- Registers validated `SettingsStorageOptions`
- Registers an `IHostedLifecycleService` startup gate for missing entity mappings
---
# Headless.Settings.Storage.PostgreSql

PostgreSQL raw-DDL storage for settings management.

## Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence.

## Key Features

- `AddHeadlessSettings(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for setting values and definitions
- Shares `SettingsStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Settings.Storage.PostgreSql
```

## Quick Start

Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService`. `AddHeadlessSettings` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `SettingsStorageOptions` on the shared settings builder. Configure the connection string through `PostgreSqlSettingsOptions`.

## Dependencies

- `Headless.Settings.Storage.EntityFramework`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of the settings repositories
---
# Headless.Settings.Storage.SqlServer

SQL Server raw-DDL storage for settings management.

## Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence.

## Key Features

- `AddHeadlessSettings(setup => setup.UseSqlServer(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for setting values and definitions
- Shares `SettingsStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

## Quick Start

Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService`. `AddHeadlessSettings` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UseSqlServer(connectionString);
});
```

## Configuration

Configure schema and table names through `SettingsStorageOptions` on the shared settings builder. Configure the connection string through `SqlServerSettingsOptions`.

## Dependencies

- `Headless.Settings.Storage.EntityFramework`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw SQL Server implementations of the settings repositories
