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
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        string name,
        string value,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class SettingValueStore(
    ISettingValueRecordRepository repository,
    ISettingDefinitionManager settingDefinitionManager,
    IGuidGenerator guidGenerator,
    ICache<SettingValueCacheItem> distributedCache
) : ISettingValueStore
{
    public async Task<string?> GetOrDefaultAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var item = await _GetCachedItemAsync(name, providerName, providerKey);

        return item.Value;
    }

    public async Task<List<SettingValue>> GetAllAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await repository.GetListAsync(providerName, providerKey);

        return settings.ConvertAll(x => new SettingValue(x.Name, x.Value));
    }

    public async Task<List<SettingValue>> GetAllAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);

        if (names.Length == 1)
        {
            var name = names[0];
            var value = await GetOrDefaultAsync(name, providerName, providerKey);

            return [new SettingValue(name, value)];
        }

        var cacheItems = await _GetCachedItemsAsync(names, providerName, providerKey);

        return cacheItems
            .Select(item => new SettingValue(_GetSettingNameFormCacheKey(item.Key), item.Value.Value))
            .ToList();
    }

    public async Task SetAsync(
        string name,
        string value,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var setting = await repository.FindAsync(name, providerName, providerKey);

        if (setting is null)
        {
            setting = new SettingValueRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await repository.InsertAsync(setting);
        }
        else
        {
            setting.Value = value;
            await repository.UpdateAsync(setting);
        }

        await distributedCache.UpsertAsync(
            SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey),
            new SettingValueCacheItem(setting.Value),
            5.Hours()
        );
    }

    public async Task DeleteAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var setting = await repository.FindAsync(name, providerName, providerKey);

        if (setting is null)
        {
            return;
        }

        await repository.DeleteAsync(setting);

        await distributedCache.RemoveAsync(SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey));
    }

    #region Cache Helpers

    private async Task<SettingValueCacheItem> _GetCachedItemAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = _CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await distributedCache.GetAsync(cacheKey);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value ?? new SettingValueCacheItem(value: null);
        }

        var valueCacheItem = await _CacheAllAndGetAsync(providerName, providerKey, nameToFind: name);

        return valueCacheItem;
    }

    private async Task<Dictionary<string, SettingValueCacheItem>> _GetCachedItemsAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKeys = names.ConvertAll(x => _CalculateCacheKey(x, providerName, providerKey));
        var cacheItems = await distributedCache.GetAllAsync(cacheKeys);

        if (cacheItems.All(x => x.Value.HasValue))
        {
            return cacheItems.ToDictionary(x => x.Key, x => x.Value.Value ?? new(value: null), StringComparer.Ordinal);
        }

        // Some cache items aren't found in the cache, get them from the database
        var notCacheNames = cacheItems
            .Where(x => !x.Value.HasValue)
            .Select(x => _GetSettingNameFormCacheKey(x.Key))
            .ToArray();

        var newCacheItems = await _CacheSomeAsync(notCacheNames, providerName, providerKey);

        var result = new Dictionary<string, SettingValueCacheItem>(StringComparer.Ordinal);

        foreach (var key in cacheKeys)
        {
            result[key] =
                newCacheItems.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal)).Value
                ?? cacheItems.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal)).Value.Value
                ?? new SettingValueCacheItem(value: null);
        }

        return result;
    }

    private async Task<SettingValueCacheItem> _CacheAllAndGetAsync(
        string? providerName,
        string? providerKey,
        string nameToFind,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await _GetDbSettingDefinitionsAsync();
        var values = await _GetDbSettingValuesAsync(providerName, providerKey);

        Dictionary<string, SettingValueCacheItem> cacheItems = new(StringComparer.Ordinal);
        SettingValueCacheItem? settingToFind = null;

        foreach (var settingDefinition in definitions)
        {
            var cacheKey = _CalculateCacheKey(settingDefinition.Name, providerName, providerKey);
            var settingValue = values.GetOrDefault(settingDefinition.Name);
            var settingValueCacheItem = new SettingValueCacheItem(settingValue);
            cacheItems[cacheKey] = settingValueCacheItem;

            if (string.Equals(settingDefinition.Name, nameToFind, StringComparison.Ordinal))
            {
                settingToFind = settingValueCacheItem;
            }
        }

        await distributedCache.UpsertAllAsync(cacheItems, 5.Hours());

        return settingToFind ?? new SettingValueCacheItem(value: null);
    }

    private async Task<Dictionary<string, SettingValueCacheItem>> _CacheSomeAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await _GetDbSettingDefinitionsAsync(names);
        var values = await _GetDbSettingValuesAsync(providerName, providerKey, names);

        var cacheItems = new Dictionary<string, SettingValueCacheItem>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var cacheKey = _CalculateCacheKey(definition.Name, providerName, providerKey);
            var settingValue = values.GetOrDefault(definition.Name);
            cacheItems[cacheKey] = new SettingValueCacheItem(settingValue);
        }

        await distributedCache.UpsertAllAsync(cacheItems, 5.Hours());

        return cacheItems;
    }

    private static string _CalculateCacheKey(string settingName, string? providerName, string? providerKey)
    {
        return SettingValueCacheItem.CalculateCacheKey(settingName, providerName, providerKey);
    }

    private static string _GetSettingNameFormCacheKey(string key)
    {
        var settingName = SettingValueCacheItem.GetSettingNameFormCacheKey(key);
        Ensure.True(settingName is not null, $"Invalid setting cache key `{key}` setting name not found");

        return settingName;
    }

    #endregion

    #region DB Helpers

    private async Task<Dictionary<string, string>> _GetDbSettingValuesAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var values = await repository.GetListAsync(providerName, providerKey);

        return values.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>> _GetDbSettingValuesAsync(
        string? providerName,
        string? providerKey,
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        var values = await repository.GetListAsync(names, providerName, providerKey);

        return values.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
    }

    private async Task<IEnumerable<SettingDefinition>> _GetDbSettingDefinitionsAsync()
    {
        return await settingDefinitionManager.GetAllAsync();
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

        var definitions = await settingDefinitionManager.GetAllAsync();

        return definitions.Where(definition =>
            names.Any(name => string.Equals(name, definition.Name, StringComparison.Ordinal))
        );
    }

    #endregion
}
