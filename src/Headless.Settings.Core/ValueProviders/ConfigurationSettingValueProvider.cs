// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.Values;
using Microsoft.Extensions.Configuration;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values from <see cref="IConfiguration"/> under the <c>Settings:</c> key prefix. This provider is read-only; <see cref="ISettingValueProvider.SetAsync"/> and <see cref="ISettingValueProvider.ClearAsync"/> are not supported.</summary>
public sealed class ConfigurationSettingValueProvider(IConfiguration configuration) : ISettingValueReadProvider
{
    /// <summary>The configuration key prefix used when looking up setting values.</summary>
    public const string ConfigurationNamePrefix = "Settings:";

    /// <summary>The canonical provider name registered in <see cref="SettingValueProviderNames"/>.</summary>
    public const string ProviderName = SettingValueProviderNames.Configuration;

    /// <inheritdoc/>
    public string Name => ProviderName;

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
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
