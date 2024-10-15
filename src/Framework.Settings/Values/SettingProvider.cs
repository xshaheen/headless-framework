// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Helpers;
using Framework.Settings.Models;
using Framework.Settings.ValueProviders;

namespace Framework.Settings.Values;

/// <summary>Retrieve setting value from <see cref="ISettingValueProvider"/></summary>
public interface ISettingProvider
{
    Task<string?> GetOrDefaultAsync(string name);

    Task<List<SettingValue>> GetAllAsync(string[] names);

    Task<List<SettingValue>> GetAllAsync();
}

public sealed class SettingProvider(
    ISettingDefinitionManager settingDefinitionManager,
    ISettingValueProviderManager settingValueProviderManager,
    ISettingEncryptionService settingEncryptionService
) : ISettingProvider
{
    public async Task<string?> GetOrDefaultAsync(string name)
    {
        var definition = await settingDefinitionManager.GetOrDefaultAsync(name);

        if (definition is null)
        {
            return null;
        }

        var valueProviders = Enumerable.Reverse(settingValueProviderManager.Providers);

        // filter providers by definition allowed providers if present
        if (definition.Providers.Count != 0)
        {
            valueProviders = valueProviders.Where(p => definition.Providers.Contains(p.Name, StringComparer.Ordinal));
        }

        // TODO: How to implement setting.IsInherited?
        var value = await _FindValueFromValueProvidersAsync(valueProviders, definition);

        if (value is not null && definition.IsEncrypted)
        {
            value = settingEncryptionService.Decrypt(definition, value);
        }

        return value;
    }

    public async Task<List<SettingValue>> GetAllAsync(string[] names)
    {
        var settingDefinitions = (await settingDefinitionManager.GetAllAsync())
            .Where(x => names.Contains(x.Name, StringComparer.Ordinal))
            .ToList();

        var result = settingDefinitions.ToDictionary(
            definition => definition.Name,
            definition => new SettingValue(definition.Name),
            StringComparer.Ordinal
        );

        foreach (var provider in Enumerable.Reverse(settingValueProviderManager.Providers))
        {
            var settingValues = await provider.GetAllAsync(
                settingDefinitions
                    .Where(x => x.Providers.Count == 0 || x.Providers.Contains(provider.Name, StringComparer.Ordinal))
                    .ToArray()
            );

            var notNullValues = settingValues.Where(x => x.Value is not null).ToList();
            foreach (var settingValue in notNullValues)
            {
                var settingDefinition = settingDefinitions.First(x =>
                    string.Equals(x.Name, settingValue.Name, StringComparison.Ordinal)
                );
                if (settingDefinition.IsEncrypted)
                {
                    settingValue.Value = settingEncryptionService.Decrypt(settingDefinition, settingValue.Value);
                }

                if (result.TryGetValue(settingValue.Name, out var value) && value.Value is null)
                {
                    value.Value = settingValue.Value;
                }
            }

            settingDefinitions.RemoveAll(x =>
                notNullValues.Exists(v => string.Equals(v.Name, x.Name, StringComparison.Ordinal))
            );

            if (settingDefinitions.Count == 0)
            {
                break;
            }
        }

        return [.. result.Values];
    }

    public async Task<List<SettingValue>> GetAllAsync()
    {
        var settingDefinitions = await settingDefinitionManager.GetAllAsync();
        var settingValues = new List<SettingValue>();

        foreach (var setting in settingDefinitions)
        {
            var value = await GetOrDefaultAsync(setting.Name);
            settingValues.Add(new SettingValue(setting.Name, value));
        }

        return settingValues;
    }

    private static async Task<string?> _FindValueFromValueProvidersAsync(
        IEnumerable<ISettingValueProvider> valueProviders,
        SettingDefinition definition
    )
    {
        foreach (var valueProvider in valueProviders)
        {
            var value = await valueProvider.GetOrDefaultAsync(definition);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
