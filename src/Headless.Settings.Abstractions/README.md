# Headless.Settings.Abstractions

Defines the provider-agnostic interfaces for dynamic application settings management.

## Problem Solved

Provides a storage-independent API for managing application settings with support for multiple value providers (DefaultValue, Configuration, Global, Tenant, User), enabling hierarchical settings that can be overridden at different levels without changing application code.

## Key Features

- `ISettingManager` — reads and writes setting values across the registered provider chain; supports single and bulk queries with optional provider targeting and fallback
- `ISettingDefinitionManager` — looks up and enumerates all registered setting definitions
- `ISettingDefinitionProvider` — contributes setting definitions at startup via `ISettingDefinitionContext`
- `SettingDefinition` — describes a setting's name, default value, display metadata, encryption flag, inheritance flag, client-visibility flag, allowed providers, and custom properties
- `SettingValue` — immutable record `SettingValue(string Name, string? Value, SettingValueProvider? Provider = null)` returned by `GetAsync` and `GetAllAsync`; `Provider` attributes the resolving value provider (or `null` on a miss)
- `SettingValueProvider` — immutable record `SettingValueProvider(string Name, string? Key)` identifying the provider name and its per-provider key
- `ISettingDefinitionContext` — context passed to `ISettingDefinitionProvider.Define()`; exposes the factory `Add(name, defaultValue?, displayName?, description?, isVisibleToClients?, isInherited?, isEncrypted?)` (creates, registers, and returns the definition), plus `GetOrDefault(name)` and `GetAll()`
- `SettingValueProviderNames` — constants `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User` for targeting built-in providers
- General extension members on `ISettingManager`: `IsTrueAsync`, `IsFalseAsync`, `GetAsync<T>` (deserializes JSON), `SetAsync<T>` (serializes to JSON)
- Scoped extension members: `GetForTenantAsync` / `SetForTenantAsync` / `GetAllForTenantAsync` (and `*ForCurrentTenant*` variants), equivalent `*ForUser*` / `*ForCurrentUser*` set, `GetGlobalAsync` / `SetGlobalAsync` / `GetAllGlobalAsync`, `GetDefaultAsync` / `GetAllDefaultAsync`, `GetInConfigurationAsync` / `GetAllInConfigurationAsync`. The `GetAll*` helpers return `IReadOnlyList<SettingValue>`

## Installation

```bash
dotnet add package Headless.Settings.Abstractions
```

## Quick Start

```csharp
public sealed class NotificationService(ISettingManager settingManager)
{
    public async Task<bool> IsEmailEnabledAsync(CancellationToken ct)
    {
        // IsTrueAsync is a convenience extension on ISettingManager
        return await settingManager.IsTrueAsync("Notifications.EmailEnabled", cancellationToken: ct);
    }

    public async Task SetTenantPreferenceAsync(string tenantId, string settingName, string value, CancellationToken ct)
    {
        // Scoped extension: sets Tenant provider value for the given tenantId
        await settingManager.SetForTenantAsync(tenantId, settingName, value, cancellationToken: ct);
    }

    public async Task SetUserPreferenceAsync(string userId, string settingName, string value, CancellationToken ct)
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

### Defining Settings

```csharp
public sealed class AppSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        context.Add(name: "App.MaxFileSize", defaultValue: "10485760", displayName: "Maximum File Size");

        context.Add(
            name: "App.ApiKey",
            displayName: "API Key",
            isEncrypted: true,
            isVisibleToClients: false
        );
    }
}
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
