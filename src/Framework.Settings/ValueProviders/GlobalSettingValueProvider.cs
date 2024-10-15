// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

/// <summary>Provides setting values from the global store no key is required.</summary>
public sealed class GlobalSettingValueProvider(ISettingValueStore settingValueStore) : ISettingValueProvider
{
    public const string ProviderName = "Global";

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return settingValueStore.GetOrDefaultAsync(setting.Name, Name, providerKey: null);
    }

    public Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return settingValueStore.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, providerKey: null);
    }
}
