// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;

namespace Framework.Settings.ValueProviders;

/// <summary>Provides setting values from the default value of the setting definition.</summary>
public sealed class DefaultValueSettingValueProvider : ISettingValueProvider
{
    public const string ProviderName = "DefaultValue";

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return Task.FromResult(setting.DefaultValue);
    }

    public Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        var settingValues = settings.Select(x => new SettingValue(x.Name, x.DefaultValue)).ToList();

        return Task.FromResult(settingValues);
    }
}
