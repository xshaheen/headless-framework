// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

public abstract class StoreSettingValueProvider(ISettingValueStore store) : ISettingValueProvider
{
    public abstract string Name { get; }

    private ISettingValueStore Store { get; } = store;

    public async Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        return await Store
            .GetOrDefaultAsync(setting.Name, Name, NormalizeProviderKey(providerKey), cancellationToken)
            .AnyContext();
    }

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
            .AnyContext();
    }

    public async Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await Store
            .SetAsync(setting.Name, value, Name, NormalizeProviderKey(providerKey), cancellationToken)
            .AnyContext();
    }

    public async Task ClearAsync(
        SettingDefinition setting,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await Store.DeleteAsync(setting.Name, Name, NormalizeProviderKey(providerKey), cancellationToken).AnyContext();
    }

    protected virtual string? NormalizeProviderKey(string? providerKey) => providerKey;
}
