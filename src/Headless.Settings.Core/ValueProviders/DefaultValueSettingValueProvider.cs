// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values from the default value defined on the <see cref="Models.SettingDefinition"/>. This is a read-only, lowest-priority provider used as the final fallback.</summary>
public sealed class DefaultValueSettingValueProvider : ISettingValueReadProvider
{
    /// <summary>The canonical provider name registered in <see cref="SettingValueProviderNames"/>.</summary>
    public const string ProviderName = SettingValueProviderNames.DefaultValue;

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

        return Task.FromResult(setting.DefaultValue);
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
        var settingValues = settings.Select(x => new SettingValue(x.Name, x.DefaultValue)).ToList();

        return Task.FromResult(settingValues);
    }
}
