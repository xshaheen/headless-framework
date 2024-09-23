// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Values;

namespace Framework.Settings.Providers;

/// <summary>Provides setting values from the global store no key is required.</summary>
public sealed class GlobalSettingValueProvider(ISettingStore settingStore) : SettingValueProvider(settingStore)
{
    public const string ProviderName = "Global";

    public override string Name => ProviderName;

    public override Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return SettingStore.GetOrDefaultAsync(setting.Name, Name, providerKey: null);
    }

    public override Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return SettingStore.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, providerKey: null);
    }
}
