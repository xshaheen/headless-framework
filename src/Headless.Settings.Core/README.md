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
dotnet add package Headless.Settings.Storage.EntityFramework
```

## Quick Start

`AddHeadlessSettings(...)` registers the management core automatically, so a storage
provider is all you need. Register the required services (`TimeProvider`, caching,
distributed lock, `IStringEncryptionService`) first, then call `AddHeadlessSettings`.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add required dependencies
builder.Services.AddCaching();
builder.Services.AddDistributedLock();
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);

// Add settings management + storage in one call (choose EF Core, PostgreSQL, or SQL Server)
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

Settings management has a prerequisite: register `IStringEncryptionService` before calling `AddHeadlessSettings(...)`. The recommended setup is to bind `Headless:StringEncryption` explicitly:

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

Register encryption first, then configure settings management. Tune the management options
through `setup.ConfigureManagement(...)` inside the `AddHeadlessSettings` block, next to
`ConfigureStorage`:

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

The `(options, IServiceProvider)` overload is available when configuration needs resolved
services. `services.Configure<SettingManagementOptions>(...)` also works and composes with
the auto-registration regardless of order.

## Dependencies

- `Headless.Settings.Abstractions`
- `Headless.Security`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Domain`

## Side Effects

- Registers `ISettingManager` as singleton
- Registers `ISettingDefinitionManager` as singleton
- Registers `SettingsInitializationBackgroundService` as hosted service
- Registers cache invalidation event handler
