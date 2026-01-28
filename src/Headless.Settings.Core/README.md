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
builder.Services.AddResourceLock();

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

```csharp
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
- `Headless.ResourceLocks.Abstractions`
- `Headless.Domain`

## Side Effects

- Registers `ISettingManager` as singleton
- Registers `ISettingDefinitionManager` as singleton
- Registers `SettingsInitializationBackgroundService` as hosted service
- Registers cache invalidation event handler
