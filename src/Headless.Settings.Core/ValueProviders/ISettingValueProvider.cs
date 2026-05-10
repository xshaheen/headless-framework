// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.ValueProviders;

/// <summary>
/// The setting value provider is used to get the value of a setting from a specific source (e.g. database, file, etc.).
/// </summary>
public interface ISettingValueReadProvider
{
    string Name { get; }

    Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Provides read and write access to setting values from a named source.</summary>
public interface ISettingValueProvider : ISettingValueReadProvider
{
    Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task ClearAsync(SettingDefinition setting, string? providerKey, CancellationToken cancellationToken = default);
}
