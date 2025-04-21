// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Exceptions;
using Framework.Settings.Definitions;
using Framework.Settings.Helpers;
using Framework.Settings.Models;
using Framework.Settings.Resources;
using Framework.Settings.ValueProviders;

namespace Framework.Settings.Values;

public sealed class SettingManager(
    ISettingDefinitionManager definitionManager,
    ISettingValueStore valueStore,
    ISettingValueProviderManager valueProviderManager,
    ISettingEncryptionService encryptionService,
    ISettingsErrorsDescriptor errorsDescriptor
) : ISettingManager
{
    public Task<string?> FindAsync(
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return _CoreGetOrDefaultAsync(settingName, providerName, providerKey, fallback, cancellationToken);
    }

    public async Task<List<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(providerName);

        var settingDefinitions = await definitionManager.GetAllAsync(cancellationToken);

        var providers = valueProviderManager.Providers.SkipWhile(c =>
            !string.Equals(c.Name, providerName, StringComparison.Ordinal)
        );

        if (!fallback)
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        var providerList = providers.ToList();

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
                    var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
                    var providerValue = await provider.GetOrDefaultAsync(setting, pk, cancellationToken);

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

            if (
                setting.IsEncrypted
                && !string.Equals(providerName, DefaultValueSettingValueProvider.ProviderName, StringComparison.Ordinal)
            )
            {
                value = encryptionService.Decrypt(setting, value);
            }

            if (value is not null)
            {
                settingValues[setting.Name] = new SettingValue(setting.Name, value);
            }
        }

        return [.. settingValues.Values];
    }

    public async Task SetAsync(
        string settingName,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(settingName);
        Argument.IsNotNull(providerName);

        var setting =
            await definitionManager.GetOrDefaultAsync(settingName, cancellationToken)
            ?? throw new ConflictException(await errorsDescriptor.NotDefined(settingName));

        var providers = valueProviderManager
            .Providers.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        if (providers.Count == 0)
        {
            throw new ConflictException(await errorsDescriptor.ProviderNotFound(providerName));
        }

        if (setting.IsEncrypted)
        {
            value = encryptionService.Encrypt(setting, value);
        }

        if (providers.Count > 1 && !forceToSet && setting.IsInherited && value is not null)
        {
            var fallbackValue = await _CoreGetOrDefaultAsync(
                settingName,
                providers[1].Name,
                providerKey: null,
                cancellationToken: cancellationToken
            );

            if (string.Equals(fallbackValue, value, StringComparison.Ordinal))
            {
                // Clear the value if it is same as it's fallback value
                value = null;
            }
        }

        // Getting list for case of there are more than one provider with the same providerName
        providers = providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal)).ToList();

        foreach (var provider in providers)
        {
            if (provider is not ISettingValueProvider p)
            {
                throw new ConflictException(await errorsDescriptor.ProviderIsReadonly(providerName));
            }

            if (value is null)
            {
                await p.ClearAsync(setting, providerKey, cancellationToken);
            }
            else
            {
                await p.SetAsync(setting, value, providerKey, cancellationToken);
            }
        }
    }

    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await valueStore.GetAllProviderValuesAsync(providerName, providerKey, cancellationToken);

        foreach (var setting in settings)
        {
            await valueStore.DeleteAsync(setting.Name, providerName, providerKey, cancellationToken);
        }
    }

    private async Task<string?> _CoreGetOrDefaultAsync(
        string settingName,
        string? providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(settingName);

        if (!fallback)
        {
            Argument.IsNotNull(providerName);
        }

        var definition =
            await definitionManager.GetOrDefaultAsync(settingName, cancellationToken)
            ?? throw new ConflictException(await errorsDescriptor.NotDefined(settingName));

        IEnumerable<ISettingValueReadProvider> providers = valueProviderManager.Providers;

        if (providerName is not null)
        {
            providers = providers.SkipWhile(c => !string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        if (!fallback || !definition.IsInherited)
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        string? value = null;

        foreach (var provider in providers)
        {
            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            value = await provider.GetOrDefaultAsync(definition, pk, cancellationToken);

            if (value is not null)
            {
                break;
            }
        }

        if (definition.IsEncrypted)
        {
            value = encryptionService.Decrypt(definition, value);
        }

        return value;
    }
}
