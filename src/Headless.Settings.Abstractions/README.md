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
