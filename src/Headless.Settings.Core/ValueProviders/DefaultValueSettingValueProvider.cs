// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values from the default value of the setting definition.</summary>
public sealed class DefaultValueSettingValueProvider : ISettingValueReadProvider
{
    public const string ProviderName = SettingValueProviderNames.DefaultValue;

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(setting.DefaultValue);
    }

    public Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settingValues = settings.Select(x => new SettingValue(x.Name, x.DefaultValue)).ToList();

        return Task.FromResult(settingValues);
    }
}
