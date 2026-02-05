// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Exceptions;
using Headless.Settings.Definitions;
using Headless.Settings.Helpers;
using Headless.Settings.Models;
using Headless.Settings.Resources;
using Headless.Settings.ValueProviders;

namespace Headless.Settings.Values;

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

    public async Task<Dictionary<string, SettingValue>> GetAllAsync(
        HashSet<string> settingNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(settingNames);

        var allSettingDefinitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var settingDefinitions = allSettingDefinitions.Where(x => settingNames.Contains(x.Name)).ToList();

        var result = settingDefinitions.ToDictionary(
            x => x.Name,
            x => new SettingValue(x.Name, value: null),
            StringComparer.Ordinal
        );

        var processedNames = new HashSet<string>(StringComparer.Ordinal);
        var definitionMap = settingDefinitions.ToDictionary(x => x.Name, StringComparer.Ordinal);

        foreach (var provider in valueProviderManager.Providers.Reverse())
        {
            var supportedDefinitions = settingDefinitions
                .Where(x => !processedNames.Contains(x.Name))
                .Where(x => x.Providers.Count == 0 || x.Providers.Contains(provider.Name, StringComparer.Ordinal))
                .ToArray();

            var settingValues = await provider
                .GetAllAsync(supportedDefinitions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var notNullValues = settingValues.Where(x => x.Value != null).ToList();

            foreach (var settingValue in notNullValues)
            {
                var settingDefinition = definitionMap[settingValue.Name];

                if (settingDefinition.IsEncrypted)
                {
                    settingValue.Value = encryptionService.Decrypt(settingDefinition, settingValue.Value);
                }

                if (result.TryGetValue(settingValue.Name, out var value) && value.Value == null)
                {
                    value.Value = settingValue.Value;
                }
            }

            foreach (var sv in notNullValues)
            {
                processedNames.Add(sv.Name);
            }

            if (processedNames.Count >= settingDefinitions.Count)
            {
                break;
            }
        }

        return result;
    }

    public async Task<List<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(providerName);

        var settingDefinitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);

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
                    var providerValue = await provider
                        .GetOrDefaultAsync(setting, pk, cancellationToken)
                        .ConfigureAwait(false);

                    if (providerValue is not null)
                    {
                        value = providerValue;
                    }
                }
            }
            else
            {
                value = await providerList[0]
                    .GetOrDefaultAsync(setting, providerKey, cancellationToken)
                    .ConfigureAwait(false);
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
            await definitionManager.FindAsync(settingName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(await errorsDescriptor.NotDefined(settingName).ConfigureAwait(false));

        var providers = valueProviderManager
            .Providers.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        if (providers.Count == 0)
        {
            throw new ConflictException(await errorsDescriptor.ProviderNotFound(providerName).ConfigureAwait(false));
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
                )
                .ConfigureAwait(false);

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
                throw new ConflictException(
                    await errorsDescriptor.ProviderIsReadonly(providerName).ConfigureAwait(false)
                );
            }

            if (value is null)
            {
                await p.ClearAsync(setting, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await p.SetAsync(setting, value, providerKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await valueStore
            .GetAllProviderValuesAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        foreach (var setting in settings)
        {
            await valueStore
                .DeleteAsync(setting.Name, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);
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
            await definitionManager.FindAsync(settingName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(await errorsDescriptor.NotDefined(settingName).ConfigureAwait(false));

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
        var isFromStoreProvider = false;

        foreach (var provider in providers)
        {
            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            value = await provider.GetOrDefaultAsync(definition, pk, cancellationToken).ConfigureAwait(false);

            if (value is not null)
            {
                isFromStoreProvider = provider is StoreSettingValueProvider;
                break;
            }
        }

        if (definition.IsEncrypted && isFromStoreProvider)
        {
            value = encryptionService.Decrypt(definition, value);
        }

        return value;
    }
}
