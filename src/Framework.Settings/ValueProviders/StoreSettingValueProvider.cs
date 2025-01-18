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
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return await Store.GetOrDefaultAsync(
            setting.Name,
            Name,
            await NormalizeProviderKey(providerKey),
            cancellationToken
        );
    }

    public async Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return await Store.GetAllAsync(
            settings.Select(x => x.Name).ToArray(),
            Name,
            await NormalizeProviderKey(providerKey),
            cancellationToken
        );
    }

    public async Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await Store.SetAsync(setting.Name, value, Name, await NormalizeProviderKey(providerKey), cancellationToken);
    }

    public async Task ClearAsync(
        SettingDefinition setting,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await Store.DeleteAsync(setting.Name, Name, await NormalizeProviderKey(providerKey), cancellationToken);
    }

    protected virtual ValueTask<string?> NormalizeProviderKey(string? providerKey) => ValueTask.FromResult(providerKey);
}
