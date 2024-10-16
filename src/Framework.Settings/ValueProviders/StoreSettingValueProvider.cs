// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

public abstract class StoreSettingValueProvider(ISettingValueStore store) : ISettingValueProvider
{
    public abstract string Name { get; }

    private ISettingValueStore Store { get; } = store;

    public async Task<string?> GetOrDefaultAsync(SettingDefinition setting, string? providerKey)
    {
        return await Store.GetOrDefaultAsync(setting.Name, Name, NormalizeProviderKey(providerKey));
    }

    public Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings, string? providerKey)
    {
        return Store.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, NormalizeProviderKey(providerKey));
    }

    public async Task SetAsync(SettingDefinition setting, string value, string? providerKey)
    {
        await Store.SetAsync(setting.Name, value, Name, NormalizeProviderKey(providerKey));
    }

    public async Task ClearAsync(SettingDefinition setting, string? providerKey)
    {
        await Store.DeleteAsync(setting.Name, Name, NormalizeProviderKey(providerKey));
    }

    protected virtual string? NormalizeProviderKey(string? providerKey) => providerKey;
}
