// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.ValueProviders;

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

    /// <summary>Stores or clears several values scoped to <paramref name="providerKey"/> as one write.</summary>
    /// <remarks>
    /// <c>SettingManager</c> routes every write through this member. The default implementation calls
    /// <see cref="SetAsync"/> and <see cref="ClearAsync"/> once per entry, so a failure part-way leaves the earlier
    /// entries written; override it when the backing source can apply the whole batch atomically, as
    /// <see cref="StoreSettingValueProvider"/> does.
    /// </remarks>
    /// <param name="values">The values to write, each paired with its definition. A <see langword="null"/> value clears the setting.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    async Task SetAllAsync(
        IReadOnlyList<KeyValuePair<SettingDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var (setting, value) in values)
        {
            if (value is null)
            {
                await ClearAsync(setting, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SetAsync(setting, value, providerKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
