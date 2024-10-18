// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Settings.Definitions;
using Framework.Settings.Helpers;
using Framework.Settings.Models;
using Framework.Settings.ValueProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Settings.Values;

/// <summary>Retrieve setting value from <see cref="ISettingValueProvider"/></summary>
public interface ISettingProvider
{
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}

public sealed class SettingDefinitionManager : ISettingProvider
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingEncryptionService _settingEncryptionService;
    private readonly ISettingValueStore _settingValueStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly SettingManagementProvidersOptions _options;
    private readonly Lazy<List<ISettingValueReadProvider>> _lazyProviders;

    public SettingDefinitionManager(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingEncryptionService settingEncryptionService,
        ISettingValueStore settingValueStore,
        IServiceProvider serviceProvider,
        IOptions<SettingManagementProvidersOptions> options
    )
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingEncryptionService = settingEncryptionService;
        _settingValueStore = settingValueStore;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _lazyProviders = new(_CreateProviders, isThreadSafe: true);
    }

    public async Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(providerName);

        var setting =
            await _settingDefinitionManager.GetOrDefaultAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"Undefined setting: {name}");

        var providers = Enumerable
            .Reverse(_lazyProviders.Value)
            .SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        if (providers.Count == 0)
        {
            throw new InvalidOperationException($"Unknown setting value provider: {providerName}");
        }

        if (setting.IsEncrypted)
        {
            value = _settingEncryptionService.Encrypt(setting, value);
        }

        if (providers.Count > 1 && !forceToSet && setting.IsInherited && value != null)
        {
            var fallbackValue = await _CoreGetOrDefaultAsync(
                name,
                providers[1].Name,
                providerKey: null,
                cancellationToken: cancellationToken
            );

            if (string.Equals(fallbackValue, value, StringComparison.Ordinal))
            {
                //Clear the value if it is same as it's fallback value
                value = null;
            }
        }

        // Getting list for case of there are more than one provider with the same providerName
        providers = providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal)).ToList();

        foreach (var provider in providers)
        {
            if (provider is not ISettingValueProvider p)
            {
                throw new InvalidOperationException($"Provider {providerName} is readonly provider");
            }

            if (value == null)
            {
                await p.ClearAsync(setting, providerKey, cancellationToken);
            }
            else
            {
                await p.SetAsync(setting, value, providerKey, cancellationToken);
            }
        }
    }

    public Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(providerName);

        return _CoreGetOrDefaultAsync(name, providerName, providerKey, fallback, cancellationToken);
    }

    public async Task<List<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(providerName);

        var settingDefinitions = await _settingDefinitionManager.GetAllAsync(cancellationToken);

        var providers = Enumerable
            .Reverse(_lazyProviders.Value)
            .SkipWhile(c => !string.Equals(c.Name, providerName, StringComparison.Ordinal));

        if (!fallback)
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        var providerList = providers.Reverse().ToList();

        if (providerList.Count == 0)
        {
            return [];
        }

        var settingValues = new Dictionary<string, SettingValue>(StringComparer.Ordinal);

        foreach (var setting in settingDefinitions)
        {
            string? value = null;

            if (setting.IsInherited)
            {
                foreach (var provider in providerList)
                {
                    var providerValue = await provider.GetOrDefaultAsync(
                        setting,
                        string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null,
                        cancellationToken
                    );

                    if (providerValue is not null)
                    {
                        value = providerValue;
                    }
                }
            }
            else
            {
                value = await providerList[0].GetOrDefaultAsync(setting, providerKey, cancellationToken);
            }

            if (setting.IsEncrypted)
            {
                value = _settingEncryptionService.Decrypt(setting, value);
            }

            if (value is not null)
            {
                settingValues[setting.Name] = new SettingValue(setting.Name, value);
            }
        }

        return [.. settingValues.Values];
    }

    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await _settingValueStore.GetAllAsync(providerName, providerKey, cancellationToken);

        foreach (var setting in settings)
        {
            await _settingValueStore.DeleteAsync(setting.Name, providerName, providerKey, cancellationToken);
        }
    }

    #region Helpers

    private async Task<string?> _CoreGetOrDefaultAsync(
        string name,
        string? providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var definition =
            await _settingDefinitionManager.GetOrDefaultAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"Undefined setting: {name}");

        var valueProviders = Enumerable.Reverse(_lazyProviders.Value);

        if (providerName is not null)
        {
            valueProviders = valueProviders.SkipWhile(c =>
                !string.Equals(c.Name, providerName, StringComparison.Ordinal)
            );
        }

        if (!fallback || !definition.IsInherited)
        {
            valueProviders = valueProviders.TakeWhile(c =>
                string.Equals(c.Name, providerName, StringComparison.Ordinal)
            );
        }

        string? value = null;
        foreach (var provider in valueProviders)
        {
            value = await provider.GetOrDefaultAsync(
                definition,
                string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null,
                cancellationToken
            );

            if (value != null)
            {
                break;
            }
        }

        if (definition.IsEncrypted)
        {
            value = _settingEncryptionService.Decrypt(definition, value);
        }

        return value;
    }

    private List<ISettingValueReadProvider> _CreateProviders()
    {
#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        using var scope = _serviceProvider.CreateScope();
#pragma warning restore MA0045 // Do not use blocking calls in a sync method (need to make calling method async)

        return _options
            .ValueProviders.Select(type => (ISettingValueReadProvider)scope.ServiceProvider.GetRequiredService(type))
            .ToList();
    }

    #endregion
}
