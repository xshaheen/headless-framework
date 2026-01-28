// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.Values;
using Microsoft.Extensions.Configuration;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values from the <see cref="IConfiguration"/> with prefix <see cref="ConfigurationNamePrefix"/>.</summary>
public sealed class ConfigurationSettingValueProvider(IConfiguration configuration) : ISettingValueReadProvider
{
    public const string ConfigurationNamePrefix = "Settings:";
    public const string ProviderName = SettingValueProviderNames.Configuration;

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = configuration[ConfigurationNamePrefix + setting.Name];

        return Task.FromResult(value);
    }

    public Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settingValues = settings
            .Select(x => new SettingValue(x.Name, configuration[ConfigurationNamePrefix + x.Name]))
            .ToList();

        return Task.FromResult(settingValues);
    }
}
