// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Base class for <see cref="ISettingValueProvider"/> implementations that delegate persistence to an <see cref="ISettingValueStore"/>.</summary>
public abstract class StoreSettingValueProvider(ISettingValueStore store) : ISettingValueProvider
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    private ISettingValueStore Store { get; } = store;

    /// <inheritdoc/>
    public async Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        return await Store
            .GetOrDefaultAsync(setting.Name, Name, NormalizeProviderKey(providerKey), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        return await Store
            .GetAllAsync(
                settings.Select(x => x.Name).ToHashSet(StringComparer.Ordinal),
                Name,
                NormalizeProviderKey(providerKey),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await Store
            .SetAsync(setting.Name, value, Name, NormalizeProviderKey(providerKey), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(
        SettingDefinition setting,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await Store
            .DeleteAsync(setting.Name, Name, NormalizeProviderKey(providerKey), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Normalizes the provider key before it is forwarded to the store. Override to apply provider-specific scoping logic.</summary>
    /// <param name="providerKey">The raw provider key supplied by the caller.</param>
    /// <returns>The normalized key to use when accessing the store, or <see langword="null"/> if not scoped.</returns>
    protected virtual string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey;
    }
}
