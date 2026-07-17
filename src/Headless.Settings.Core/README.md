# Headless.Settings.Core

Core implementation of dynamic settings management with hierarchical value providers, caching, encryption, and background initialization.

## Problem Solved

Provides the full settings management implementation including hierarchical value resolution (User > Tenant > Global > Configuration > DefaultValue), setting value caching with distributed invalidation, transparent encryption for sensitive settings, and background startup initialization that seeds static definitions to the database.

## Key Features

- `SettingManager` — full implementation of `ISettingManager`; walks the registered provider chain, caches results, and coordinates writes with cache invalidation
- `ISettingValueReadProvider` / `ISettingValueProvider` — read-only and read-write contracts for custom value providers; register with `services.AddSettingValueProvider<T>()`
- Built-in value providers (lowest to highest priority): `DefaultValueSettingValueProvider`, `ConfigurationSettingValueProvider`, `GlobalSettingValueProvider`, `TenantSettingValueProvider`, `UserSettingValueProvider`
- `IStaticSettingDefinitionStore` — builds the setting catalog lazily from all registered `ISettingDefinitionProvider` implementations
- `IDynamicSettingDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination
- `SettingsInitializationBackgroundService` — seeds static definitions to the database at startup with exponential-back-off retry; pre-caches dynamic definitions when enabled
- `SettingManagementOptions` — tuning options for lock keys, cache expiries, dynamic store toggle
- `SettingsStorageOptions` — schema and table name configuration shared across all storage providers
- `HeadlessSettingsSetupBuilder` — fluent builder returned to `AddHeadlessSettings`; exposes `ConfigureManagement`, `ConfigureStorage`, and `RegisterExtension`
- `services.AddSettingDefinitionProvider<T>()` — registers a custom `ISettingDefinitionProvider`
- `services.AddSettingValueProvider<T>()` — registers a custom value provider (idempotent by type)

## Design Notes

Value providers are registered with the last-added provider having the highest resolution priority. The built-in order (from setup) is `DefaultValue → Configuration → Global → Tenant → User` — User wins. Custom providers added via `AddSettingValueProvider<T>()` are appended after `User` and therefore have the highest priority of all.

`AddHeadlessSettings` is guarded on `ISettingManager` so it is safe to call more than once (only the first call registers the core). However, only one storage provider extension may be registered — a second call with a different provider throws at startup.

## Installation

```bash
dotnet add package Headless.Settings.Core
```

## Quick Start

Register the required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IStringEncryptionService`) first, then call `AddHeadlessSettings`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Required dependencies
builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

// Register management core + storage in one call
builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<AppDbContext>());

// Register setting definition providers
builder.Services.AddSettingDefinitionProvider<AppSettingDefinitionProvider>();
```

### Define Settings

```csharp
public sealed class AppSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinitionCreateOptions("App.MaxFileSize")
        {
            DisplayName = "Maximum File Size",
            DefaultValue = "10485760",
        });

        context.Add(new SettingDefinitionCreateOptions("App.ApiKey")
        {
            DisplayName = "API Key",
            IsEncrypted = true,
        });
    }
}
```

### Read and Write Settings

```csharp
public sealed class ConfigService(ISettingManager settings)
{
    public async Task<int> GetMaxFileSizeAsync(CancellationToken ct)
    {
        // GetAsync returns a never-null SettingValue; Value is null on a miss.
        var setting = await settings.GetAsync("App.MaxFileSize", cancellationToken: ct);
        return int.TryParse(setting.Value, out var size) ? size : 10485760;
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

Tune management options through `setup.ConfigureManagement(...)` inside the `AddHeadlessSettings` block:

```csharp
services.AddStringEncryptionService(configuration.GetRequiredSection("Headless:StringEncryption"));

services.AddHeadlessSettings(setup =>
{
    setup.ConfigureManagement(options =>
    {
        // Distributed lock key coordinating cross-application definition saves (default: "settings:common_update_lock")
        options.CrossApplicationsCommonLockKey = "settings:common_update_lock";

        // Lifetime of cached setting values in the distributed cache (default: 5 hours)
        options.ValueCacheExpiration = TimeSpan.FromHours(5);

        // Enable database-backed dynamic setting definition store (default: false)
        options.IsDynamicSettingStoreEnabled = false;

        // Persist static definitions to the DB on startup (default: true)
        options.SaveStaticSettingsToDatabase = true;

        // How long dynamic definitions stay in-process before the distributed stamp is re-checked (default: 30 seconds)
        options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);
    });
    setup.ConfigureStorage(o =>
    {
        o.Schema = "settings"; // default
        o.SettingValuesTableName = "SettingValues"; // default
        o.SettingDefinitionsTableName = "SettingDefinitions"; // default
        o.InitializeOnStartup = true; // default
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

The `(options, IServiceProvider)` overload is available for `ConfigureManagement` when configuration needs resolved services. `services.Configure<SettingManagementOptions>(...)` also works and composes with the auto-registration regardless of order.

## Dependencies

- `Headless.Settings.Abstractions`
- `Headless.Security`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Domain`

## Side Effects

- Registers `ISettingManager` as singleton
- Registers `ISettingDefinitionManager`, `IStaticSettingDefinitionStore`, `IDynamicSettingDefinitionStore`, `ISettingValueStore`, `ISettingValueProviderManager` as singletons
- Registers `DefaultValueSettingValueProvider`, `ConfigurationSettingValueProvider`, `GlobalSettingValueProvider`, `TenantSettingValueProvider`, `UserSettingValueProvider` as singletons
- Registers `SettingsInitializationBackgroundService` as hosted service
