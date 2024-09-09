using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Framework.Settings.Values;
using Humanizer;

namespace Framework.Settings.Repositories;

public interface ISettingManagementStore
{
    Task<string?> GetOrNullAsync(string name, string? providerName, string? providerKey);

    Task<List<SettingValue>> GetListAsync(string? providerName, string? providerKey);

    Task<List<SettingValue>> GetListAsync(string[] names, string? providerName, string? providerKey);

    Task SetAsync(string name, string value, string? providerName, string? providerKey);

    Task DeleteAsync(string name, string? providerName, string? providerKey);
}

public sealed class SettingManagementStore(
    ISettingRecordRepository settingRecordRepository,
    ISettingDefinitionManager settingDefinitionManager,
    IGuidGenerator guidGenerator,
    ICache<SettingCacheItem> cache
) : ISettingManagementStore
{
    public async Task<string?> GetOrNullAsync(string name, string? providerName, string? providerKey)
    {
        return (await _GetCacheItemAsync(name, providerName, providerKey)).Value;
    }

    public async Task SetAsync(string name, string value, string? providerName, string? providerKey)
    {
        var setting = await settingRecordRepository.FindAsync(name, providerName, providerKey);

        if (setting is null)
        {
            setting = new SettingRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await settingRecordRepository.InsertAsync(setting);
        }
        else
        {
            setting.Value = value;
            await settingRecordRepository.UpdateAsync(setting);
        }

        await cache.SetAsync(
            _CalculateCacheKey(name, providerName, providerKey),
            new SettingCacheItem(setting.Value),
            5.Hours()
        );
    }

    public async Task<List<SettingValue>> GetListAsync(string? providerName, string? providerKey)
    {
        var settings = await settingRecordRepository.GetListAsync(providerName, providerKey);

        return settings.ConvertAll(s => new SettingValue(s.Name, s.Value));
    }

    public async Task DeleteAsync(string name, string? providerName, string? providerKey)
    {
        var setting = await settingRecordRepository.FindAsync(name, providerName, providerKey);

        if (setting is not null)
        {
            await settingRecordRepository.DeleteAsync(setting);
            await cache.RemoveAsync(_CalculateCacheKey(name, providerName, providerKey));
        }
    }

    public async Task<List<SettingValue>> GetListAsync(string[] names, string? providerName, string? providerKey)
    {
        Argument.IsNotNullOrEmpty(names);

        var result = new List<SettingValue>();

        if (names.Length == 1)
        {
            var name = names[0];
            result.Add(new SettingValue(name, (await _GetCacheItemAsync(name, providerName, providerKey)).Value));

            return result;
        }

        var cacheItems = await _GetCacheItemsAsync(names, providerName, providerKey);

        result.AddRange(
            cacheItems.Select(item => new SettingValue(_GetSettingNameFormCacheKey(item.Key), item.Value.Value))
        );

        return result;
    }

    #region Helpers

    private async Task<Dictionary<string, SettingCacheItem>> _SetCacheItemsAsync(
        string? providerName,
        string? providerKey,
        List<string> notCacheKeys
    )
    {
        var settingDefinitions = (await settingDefinitionManager.GetAllAsync()).Where(definition =>
            notCacheKeys.Exists(noCacheKey =>
                string.Equals(_GetSettingNameFormCacheKey(noCacheKey), definition.Name, StringComparison.Ordinal)
            )
        );

        var settingRecords = await settingRecordRepository.GetListAsync(
            notCacheKeys.Select(_GetSettingNameFormCacheKey).ToArray(),
            providerName,
            providerKey
        );

        var settingsDictionary = settingRecords.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        var cacheItems = new Dictionary<string, SettingCacheItem>(StringComparer.Ordinal);

        foreach (var settingDefinition in settingDefinitions)
        {
            var cacheKey = _CalculateCacheKey(settingDefinition.Name, providerName, providerKey);
            var settingValue = settingsDictionary.GetOrDefault(settingDefinition.Name);
            cacheItems[cacheKey] = new SettingCacheItem(settingValue);
        }

        await cache.SetAllAsync(cacheItems, 5.Hours());

        return cacheItems;
    }

    private async Task _SetCacheItemsAsync(
        string? providerName,
        string? providerKey,
        string currentName,
        SettingCacheItem currentCacheItem
    )
    {
        var settingDefinitions = await settingDefinitionManager.GetAllAsync();

        var settingsDictionary = (await settingRecordRepository.GetListAsync(providerName, providerKey)).ToDictionary(
            s => s.Name,
            s => s.Value,
            StringComparer.Ordinal
        );

        var cacheItems = new Dictionary<string, SettingCacheItem>(StringComparer.Ordinal);

        foreach (var settingDefinition in settingDefinitions)
        {
            var cacheKey = _CalculateCacheKey(settingDefinition.Name, providerName, providerKey);
            var settingValue = settingsDictionary.GetOrDefault(settingDefinition.Name);
            cacheItems[cacheKey] = new SettingCacheItem(settingValue);

            if (string.Equals(settingDefinition.Name, currentName, StringComparison.Ordinal))
            {
                currentCacheItem.Value = settingValue;
            }
        }

        await cache.SetAllAsync(cacheItems, 5.Hours());
    }

    private async Task<Dictionary<string, SettingCacheItem>> _GetCacheItemsAsync(
        string[] names,
        string? providerName,
        string? providerKey
    )
    {
        var cacheKeys = names.Select(x => _CalculateCacheKey(x, providerName, providerKey)).ToList();
        var cacheItems = await cache.GetAllAsync(cacheKeys);

        if (cacheItems.All(x => x.Value.HasValue))
        {
            return cacheItems.ToDictionary(
                x => x.Key,
                x => x.Value.Value ?? new SettingCacheItem(value: null),
                StringComparer.Ordinal
            );
        }

        // Some cache items are not found in the cache, get them from the database
        var notCacheKeys = cacheItems.Where(x => !x.Value.HasValue).Select(x => x.Key).ToList();
        var newCacheItems = await _SetCacheItemsAsync(providerName, providerKey, notCacheKeys);

        var result = new Dictionary<string, SettingCacheItem>(StringComparer.Ordinal);

        foreach (var key in cacheKeys)
        {
            result[key] =
                newCacheItems.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal)).Value
                ?? cacheItems.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal)).Value.Value
                ?? new SettingCacheItem(value: null);
        }

        return result;
    }

    private async Task<SettingCacheItem> _GetCacheItemAsync(string name, string? providerName, string? providerKey)
    {
        var cacheKey = _CalculateCacheKey(name, providerName, providerKey);
        var cacheValue = await cache.GetAsync(cacheKey);

        if (cacheValue.HasValue)
        {
            return cacheValue.Value ?? new SettingCacheItem(value: null);
        }

        var cacheItem = new SettingCacheItem(value: null);

        await _SetCacheItemsAsync(providerName, providerKey, name, cacheItem);

        return cacheItem;
    }

    private static string _CalculateCacheKey(string name, string? providerName, string? providerKey)
    {
        return SettingCacheItem.CalculateCacheKey(name, providerName, providerKey);
    }

    private static string _GetSettingNameFormCacheKey(string key)
    {
        var settingName = SettingCacheItem.GetSettingNameFormCacheKey(key);
        Ensure.True(settingName is not null, $"Invalid setting cache key `{key}` setting name not found");

        return settingName;
    }

    #endregion
}
