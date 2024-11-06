// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Humanizer;

namespace Framework.Settings.Values;

/// <summary>Represents a store for setting values.</summary>
public interface ISettingValueStore
{
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllProviderValuesAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllAsync(
        string[] names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class SettingValueStore(
    ISettingValueRecordRepository repository,
    ISettingDefinitionManager settingDefinitionManager,
    IGuidGenerator guidGenerator,
    ICache<SettingValueCacheItem> cache
) : ISettingValueStore
{
    public async Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await cache.GetAsync(cacheKey, cancellationToken);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value?.Value;
        }

        var valueCacheItem = await _CacheAllAndGetAsync(
            providerName,
            providerKey,
            nameToFind: name,
            cancellationToken: cancellationToken
        );

        return valueCacheItem;
    }

    public async Task<List<SettingValue>> GetAllProviderValuesAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await repository.GetListAsync(providerName, providerKey, cancellationToken);

        return settings.ConvertAll(x => new SettingValue(x.Name, x.Value));
    }

    public async Task<List<SettingValue>> GetAllAsync(
        string[] names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);

        if (names.Length == 1)
        {
            var name = names[0];
            var value = await GetOrDefaultAsync(name, providerName, providerKey, cancellationToken);

            return [new SettingValue(name, value)];
        }

        var cacheItems = await _GetCachedItemsAsync(names, providerName, providerKey, cancellationToken);

        return cacheItems;
    }

    public async Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settingValue = await repository.FindAsync(name, providerName, providerKey, cancellationToken);

        if (settingValue is null)
        {
            settingValue = new SettingValueRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await repository.InsertAsync(settingValue, cancellationToken);
        }
        else
        {
            settingValue.Value = value;
            await repository.UpdateAsync(settingValue, cancellationToken);
        }

        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        await cache.UpsertAsync(cacheKey, new SettingValueCacheItem(settingValue.Value), 5.Hours(), cancellationToken);
    }

    public async Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await repository.FindAllAsync(name, providerName, providerKey, cancellationToken);

        if (settings.Count == 0)
        {
            return;
        }

        await repository.DeleteAsync(settings, cancellationToken);

        foreach (var setting in settings)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, setting.ProviderKey);
            await cache.RemoveAsync(cacheKey, cancellationToken);
        }
    }

    #region Helpers

    private async Task<List<SettingValue>> _GetCachedItemsAsync(
        string[] names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var cacheKeys = names.ConvertAll(x => SettingValueCacheItem.CalculateCacheKey(x, providerName, providerKey));
        var existCacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken);
        var existCacheItems = existCacheItemsMap.ToList();

        if (existCacheItems.TrueForAll(x => x.Value.HasValue))
        {
            return existCacheItems.ConvertAll(item => new SettingValue(
                name: _GetSettingNameFormCacheKey(item.Key),
                value: item.Value.Value?.Value
            ));
        }

        // Some cache items aren't found in the cache, get them from the database
        var notCacheNames = existCacheItems
            .Where(x => !x.Value.HasValue)
            .Select(x => _GetSettingNameFormCacheKey(x.Key))
            .ToArray();

        var newCacheItemsMap = await _CacheSomeAsync(notCacheNames, providerName, providerKey, cancellationToken);
        var result = new List<SettingValue>(cacheKeys.Length);

        foreach (var cacheKey in cacheKeys)
        {
            var settingName = _GetSettingNameFormCacheKey(cacheKey);

            if (newCacheItemsMap.TryGetValue(cacheKey, out var newCachedValue))
            {
                result.Add(new SettingValue(settingName, newCachedValue.Value));

                continue;
            }

            if (existCacheItemsMap.TryGetValue(cacheKey, out var cacheItem))
            {
                result.Add(new SettingValue(settingName, cacheItem.Value?.Value));

                continue;
            }

            result.Add(new SettingValue(settingName, value: null));
        }

        return result;
    }

    private async Task<Dictionary<string, SettingValueCacheItem>> _CacheSomeAsync(
        string[] names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await _GetDbSettingDefinitionsAsync(names, cancellationToken);
        var dbValues = await repository.GetListAsync(names, providerName, providerKey, cancellationToken);
        var dbValuesMap = dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        var cacheItems = new Dictionary<string, SettingValueCacheItem>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(definition.Name, providerName, providerKey);
            var settingValue = dbValuesMap.GetOrDefault(definition.Name);
            cacheItems[cacheKey] = new SettingValueCacheItem(settingValue);
        }

        await cache.UpsertAllAsync(cacheItems, 5.Hours(), cancellationToken);

        return cacheItems;
    }

    private async Task<string?> _CacheAllAndGetAsync(
        string providerName,
        string? providerKey,
        string nameToFind,
        CancellationToken cancellationToken
    )
    {
        var definitions = await settingDefinitionManager.GetAllAsync(cancellationToken);
        var dbValues = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var dbValuesMap = dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        Dictionary<string, SettingValueCacheItem> cacheItems = new(StringComparer.Ordinal);
        string? settingValueToFind = null;

        foreach (var settingDefinition in definitions)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(settingDefinition.Name, providerName, providerKey);
            var settingValue = dbValuesMap.GetOrDefault(settingDefinition.Name);
            var settingValueCacheItem = new SettingValueCacheItem(settingValue);
            cacheItems[cacheKey] = settingValueCacheItem;

            if (string.Equals(settingDefinition.Name, nameToFind, StringComparison.Ordinal))
            {
                settingValueToFind = settingValue;
            }
        }

        await cache.UpsertAllAsync(cacheItems, 5.Hours(), cancellationToken);

        return settingValueToFind;
    }

    private static string _GetSettingNameFormCacheKey(string key)
    {
        var settingName = SettingValueCacheItem.GetSettingNameFormCacheKey(key);
        Ensure.True(settingName is not null, $"Invalid setting cache key `{key}` setting name not found");

        return settingName;
    }

    private async Task<IEnumerable<SettingDefinition>> _GetDbSettingDefinitionsAsync(
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Length == 0)
        {
            return Array.Empty<SettingDefinition>();
        }

        var definitions = await settingDefinitionManager.GetAllAsync(cancellationToken);

        return definitions.Where(definition =>
            names.Any(name => string.Equals(name, definition.Name, StringComparison.Ordinal))
        );
    }

    #endregion
}
