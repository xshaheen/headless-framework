// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.ValueProviders;

/// <summary>Read-only contract for a named source of setting values (e.g. database, configuration, defaults).</summary>
public interface ISettingValueReadProvider
{
    /// <summary>Gets the unique name of this provider.</summary>
    string Name { get; }

    /// <summary>Returns the stored value for <paramref name="setting"/>, or <see langword="null"/> if none is set.</summary>
    /// <param name="setting">The setting definition to look up.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier). Provider implementations determine how this is used.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The stored value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the stored values for all <paramref name="settings"/>.</summary>
    /// <param name="settings">The setting definitions to look up.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="SettingValue"/> entries; a <see langword="null"/> <c>Value</c> indicates no stored entry.</returns>
    Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Provides read and write access to setting values from a named source.</summary>
public interface ISettingValueProvider : ISettingValueReadProvider
{
    /// <summary>Persists <paramref name="value"/> for <paramref name="setting"/> scoped to <paramref name="providerKey"/>.</summary>
    /// <param name="setting">The setting definition to update.</param>
    /// <param name="value">The new value to store.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the stored value for <paramref name="setting"/> scoped to <paramref name="providerKey"/>.</summary>
    /// <param name="setting">The setting definition to clear.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task ClearAsync(SettingDefinition setting, string? providerKey, CancellationToken cancellationToken = default);
}
