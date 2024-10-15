// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Humanizer;

namespace Framework.Settings.Values;

/// <summary>Represents a store for setting values.</summary>
public interface ISettingValueStore
{
    Task<string?> GetOrDefaultAsync(string name, string? providerName, string? providerKey);

    Task<List<SettingValue>> GetAllAsync(string[] names, string? providerName, string? providerKey);
}

public sealed class SettingValueStore(
    ISettingValueRecordRepository settingValueRecordRepository,
    ISettingDefinitionManager settingDefinitionManager,
    IGuidGenerator guidGenerator,
    ICache<SettingValueCacheItem> distributedCache
) : ISettingValueStore
{
    public Task<string?> GetOrDefaultAsync(string name, string? providerName, string? providerKey)
    {
        throw new NotImplementedException();
    }

    public Task<List<SettingValue>> GetAllAsync(string[] names, string? providerName, string? providerKey)
    {
        throw new NotImplementedException();
    }

    #region Helpers

    private async Task<SettingValueCacheItem> _GetCacheItemAsync(string name, string? providerName, string? providerKey)
    {
        var cacheKey = _CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await distributedCache.GetAsync(cacheKey);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value ?? new SettingValueCacheItem(value: null);
        }

        var valueCacheItem = await _SetCacheItemsAsync(providerName, providerKey, requiredSettingValueName: name);

        return valueCacheItem;
    }

    private async Task<SettingValueCacheItem> _SetCacheItemsAsync(
        string? providerName,
        string? providerKey,
        string requiredSettingValueName
    )
    {
        var settingDefinitions = await settingDefinitionManager.GetAllAsync();

        var dbRecords = await settingValueRecordRepository.GetListAsync(providerName, providerKey);
        var dbRecordsMap = dbRecords.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        Dictionary<string, SettingValueCacheItem> cacheItems = new(StringComparer.Ordinal);

        SettingValueCacheItem? requiredSettingValueCacheItem = null;

        foreach (var settingDefinition in settingDefinitions)
        {
            var cacheKey = _CalculateCacheKey(settingDefinition.Name, providerName, providerKey);
            var settingValue = dbRecordsMap.GetOrDefault(settingDefinition.Name);
            var settingValueCacheItem = new SettingValueCacheItem(settingValue);
            cacheItems[cacheKey] = settingValueCacheItem;

            if (string.Equals(settingDefinition.Name, requiredSettingValueName, StringComparison.Ordinal))
            {
                requiredSettingValueCacheItem = settingValueCacheItem;
            }
        }

        await distributedCache.UpsertAllAsync(cacheItems, 5.Hours());

        return requiredSettingValueCacheItem ?? new SettingValueCacheItem(value: null);
    }

    private async Task<Dictionary<string, SettingValueCacheItem>> _SetCacheItemsAsync(
        List<string> notCacheKeys,
        string? providerName,
        string? providerKey
    )
    {
        // Get not cache setting definitions
        var definitions = (await settingDefinitionManager.GetAllAsync()).Where(definition =>
            notCacheKeys.Exists(noCacheKey =>
                string.Equals(_GetSettingNameFormCacheKey(noCacheKey), definition.Name, StringComparison.Ordinal)
            )
        );

        // Get not cache items db records
        var noCacheSettingNames = notCacheKeys.Select(_GetSettingNameFormCacheKey).ToArray();

        var values = (
            await settingValueRecordRepository.GetListAsync(noCacheSettingNames, providerName, providerKey)
        ).ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        var cacheItems = new Dictionary<string, SettingValueCacheItem>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(definition.Name, providerName, providerKey);
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
}
