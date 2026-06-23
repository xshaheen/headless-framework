# Headless.Settings.Abstractions

Defines the provider-agnostic interfaces for dynamic application settings management.

## Problem Solved

Provides a storage-independent API for managing application settings with support for multiple value providers (DefaultValue, Configuration, Global, Tenant, User), enabling hierarchical settings that can be overridden at different levels without changing application code.

## Key Features

- `ISettingManager` — reads and writes setting values across the registered provider chain; supports single and bulk queries with optional provider targeting and fallback
- `ISettingDefinitionManager` — looks up and enumerates all registered setting definitions
- `ISettingDefinitionProvider` — contributes setting definitions at startup via `ISettingDefinitionContext`
- `SettingDefinition` — describes a setting's name, default value, display metadata, encryption flag, inheritance flag, client-visibility flag, allowed providers, and custom properties
- `SettingValue` — record returned by `GetAllAsync` carrying the resolved string value
- `ISettingDefinitionContext` — context passed to `ISettingDefinitionProvider.Define()`; exposes `Add(params ReadOnlySpan<SettingDefinition>)`, `GetOrDefault(name)`, and `GetAll()`
- `SettingValueProviderNames` — constants `DefaultValue`, `Configuration`, `Global`, `Tenant`, `User` for targeting built-in providers
- General extension members on `ISettingManager`: `IsTrueAsync`, `IsFalseAsync`, `FindAsync<T>` (deserializes JSON), `SetAsync<T>` (serializes to JSON)
- Scoped extension members: `FindForTenantAsync` / `SetForTenantAsync` / `GetAllForTenantAsync` (and `*ForCurrentTenant*` variants), equivalent `*ForUser*` / `*ForCurrentUser*` set, `FindGlobalAsync` / `SetGlobalAsync` / `GetAllGlobalAsync`, `FindDefaultAsync` / `GetAllDefaultAsync`, `FindInConfigurationAsync` / `GetAllInConfigurationAsync`

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
        context.Add(
            new SettingDefinition(
                name: "App.MaxFileSize",
                defaultValue: "10485760",
                displayName: "Maximum File Size"
            ),
            new SettingDefinition(
                name: "App.ApiKey",
                displayName: "API Key",
                isEncrypted: true,
                isVisibleToClients: false
            )
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
