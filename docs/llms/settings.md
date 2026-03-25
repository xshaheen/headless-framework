---
domain: Settings
packages: Settings.Abstractions, Settings.Core, Settings.Storage.EntityFramework
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
    - [Option 2: Shared DbContext](#option-2-shared-dbcontext)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)

> Dynamic, hierarchical application settings with runtime read/write support and multiple value providers (default, config, global, tenant, user).

## Quick Orientation

Install the three settings packages:
- `Headless.Settings.Abstractions` -- interfaces (`ISettingManager`, `ISettingDefinitionProvider`, `SettingDefinition`)
- `Headless.Settings.Core` -- implementation with hierarchical providers, caching, encryption, background init
- `Headless.Settings.Storage.EntityFramework` -- EF Core persistence for setting values and definitions

Minimal wiring:
```csharp
builder.Services.AddCaching();
builder.Services.AddDistributedLock();
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
builder.Services.AddSettingsManagementCore(_ => { });
builder.Services.AddSettingsManagementDbContextStorage<AppDbContext>();
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
- For shared DbContext, implement `ISettingsDbContext` and call `modelBuilder.ApplySettingsConfiguration()` in `OnModelCreating`.
- Dependencies: Core requires `Headless.Caching.Abstractions` and `Headless.DistributedLocks.Abstractions` to be registered.
- Pre-requisite: register `IStringEncryptionService` before `AddSettingsManagementCore(...)`. Recommended: `AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"))`.

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

// Add settings management
builder.Services.AddSettingsManagementCore(options =>
{
    options.CacheKeyPrefix = "settings:";
});

// Add storage (EF Core)
builder.Services.AddSettingsManagementDbContextStorage<AppDbContext>();

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

Then register settings management:

```csharp
services.AddStringEncryptionService(configuration.GetRequiredSection("Headless:StringEncryption"));

services.AddSettingsManagementCore(options =>
{
    // Cache expiration for setting values (default: 5 hours)
    options.ValueCacheExpiration = TimeSpan.FromHours(5);

    // Cache expiration for dynamic definitions (default: 30 seconds)
    options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);

    // Lock settings for cross-application updates
    options.CrossApplicationsCommonLockKey = "settings:common_update_lock";
    options.CrossApplicationsCommonLockExpiration = TimeSpan.FromMinutes(10);
});
```

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

builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
builder.Services.AddSettingsManagementCore(_ => { });
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
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);
builder.Services.AddSettingsManagementCore(_ => { });
builder.Services.AddSettingsManagementDbContextStorage<AppDbContext>();
```

## Configuration

Pre-requisite: register string encryption before calling `AddSettingsManagementCore(...)`.

## Dependencies

- `Headless.Settings.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `ISettingValueRecordRepository` as singleton
- Registers `ISettingDefinitionRecordRepository` as singleton
- Optionally registers pooled `SettingsDbContext` factory
