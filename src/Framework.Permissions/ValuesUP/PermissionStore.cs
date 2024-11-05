// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Kernel.Checks;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Permissions.Values;

public interface IPermissionStore
{
    Task<bool> IsGrantedAsync(string name, string providerName, string? providerKey);

    Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names, string providerName, string? providerKey);
}

public sealed class PermissionStore(
    IPermissionGrantRepository permissionGrantRepository,
    ICache<PermissionGrantCacheItem> cache,
    IPermissionDefinitionManager permissionDefinitionManager
) : IPermissionStore
{
    private readonly ILogger<PermissionStore> _logger = NullLogger<PermissionStore>.Instance;

    public async Task<bool> IsGrantedAsync(string name, string providerName, string? providerKey)
    {
        return (await _GetCacheItemAsync(name, providerName, providerKey)).IsGranted;
    }

    private async Task<PermissionGrantCacheItem> _GetCacheItemAsync(
        string name,
        string providerName,
        string? providerKey
    )
    {
        var cacheKey = _CalculateCacheKey(name, providerName, providerKey);

        _logger.LogDebug($"PermissionStore.GetCacheItemAsync: {cacheKey}");

        var cacheItem = await cache.GetAsync(cacheKey);

        if (cacheItem != null)
        {
            _logger.LogDebug($"Found in the cache: {cacheKey}");
            return cacheItem;
        }

        _logger.LogDebug($"Not found in the cache: {cacheKey}");

        cacheItem = new PermissionGrantCacheItem(false);

        await _SetCacheItemsAsync(providerName, providerKey, name, cacheItem);

        return cacheItem;
    }

    private async Task _SetCacheItemsAsync(
        string providerName,
        string providerKey,
        string currentName,
        PermissionGrantCacheItem currentCacheItem
    )
    {
        var permissions = await permissionDefinitionManager.GetAllPermissionsAsync();

        _logger.LogDebug(
            $"Getting all granted permissions from the repository for this provider name,key: {providerName},{providerKey}"
        );

        var grantedPermissionsHashSet = new HashSet<string>(
            (await permissionGrantRepository.GetListAsync(providerName, providerKey)).Select(p => p.Name)
        );

        _logger.LogDebug($"Setting the cache items. Count: {permissions.Count}");

        var cacheItems = new List<KeyValuePair<string, PermissionGrantCacheItem>>();

        foreach (var permission in permissions)
        {
            var isGranted = grantedPermissionsHashSet.Contains(permission.Name);

            cacheItems.Add(
                new KeyValuePair<string, PermissionGrantCacheItem>(
                    _CalculateCacheKey(permission.Name, providerName, providerKey),
                    new PermissionGrantCacheItem(isGranted)
                )
            );

            if (permission.Name == currentName)
            {
                currentCacheItem.IsGranted = isGranted;
            }
        }

        await cache.SetManyAsync(cacheItems);

        _logger.LogDebug($"Finished setting the cache items. Count: {permissions.Count}");
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        string providerName,
        string? providerKey
    )
    {
        Check.NotNullOrEmpty(names, nameof(names));

        var result = new MultiplePermissionGrantResult();

        if (names.Length == 1)
        {
            var name = names.First();
            result.Result.Add(
                name,
                await IsGrantedAsync(names.First(), providerName, providerKey)
                    ? PermissionGrantResult.Granted
                    : PermissionGrantResult.Undefined
            );
            return result;
        }

        var cacheItems = await _GetCacheItemsAsync(names, providerName, providerKey);
        foreach (var item in cacheItems)
        {
            result.Result.Add(
                _GetPermissionNameFormCacheKey(item.Key),
                item.Value != null && item.Value.IsGranted
                    ? PermissionGrantResult.Granted
                    : PermissionGrantResult.Undefined
            );
        }

        return result;
    }

    private async Task<List<KeyValuePair<string, PermissionGrantCacheItem>>> _GetCacheItemsAsync(
        string[] names,
        string providerName,
        string providerKey
    )
    {
        var cacheKeys = names.Select(x => _CalculateCacheKey(x, providerName, providerKey)).ToList();

        _logger.LogDebug("PermissionStore.GetCacheItemAsync: {@CacheKeys}", cacheKeys);

        var cacheItems = (await cache.GetAllAsync(cacheKeys)).ToList();

        if (cacheItems.TrueForAll(x => x.Value != null))
        {
            _logger.LogDebug($"Found in the cache: {string.Join(",", cacheKeys)}");
            return cacheItems;
        }

        var notCacheKeys = cacheItems.Where(x => x.Value == null).Select(x => x.Key).ToList();

        _logger.LogDebug($"Not found in the cache: {string.Join(",", notCacheKeys)}");

        var newCacheItems = await _SetCacheItemsAsync(providerName, providerKey, notCacheKeys);

        var result = new List<KeyValuePair<string, PermissionGrantCacheItem>>();
        foreach (var key in cacheKeys)
        {
            var item = newCacheItems.FirstOrDefault(x => x.Key == key);
            if (item.Value == null)
            {
                item = cacheItems.FirstOrDefault(x => x.Key == key);
            }

            result.Add(new KeyValuePair<string, PermissionGrantCacheItem>(key, item.Value));
        }

        return result;
    }

    private async Task<Dictionary<string, PermissionGrantCacheItem>> _SetCacheItemsAsync(
        string providerName,
        string providerKey,
        List<string> notCacheKeys
    )
    {
        var permissions = (await permissionDefinitionManager.GetAllPermissionsAsync())
            .Where(x => notCacheKeys.Any(k => _GetPermissionNameFormCacheKey(k) == x.Name))
            .ToList();

        _logger.LogDebug(
            "Getting not cache granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}",
            providerName,
            providerKey
        );

        var grantedPermissionsHashSet = new HashSet<string>(
            (
                await permissionGrantRepository.GetListAsync(
                    notCacheKeys.Select(_GetPermissionNameFormCacheKey).ToArray(),
                    providerName,
                    providerKey
                )
            ).Select(p => p.Name),
            StringComparer.Ordinal
        );

        _logger.LogDebug("Setting the cache items. Count: {PermissionsCount}", permissions.Count);

        var cacheItems = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            var isGranted = grantedPermissionsHashSet.Contains(permission.Name);
            var cacheKey = _CalculateCacheKey(permission.Name, providerName, providerKey);
            cacheItems[cacheKey] = new PermissionGrantCacheItem(isGranted);
        }

        await cache.UpsertAllAsync(cacheItems, 30.Days());

        _logger.LogDebug("Finished setting the cache items. Count: {PermissionsCount}", permissions.Count);

        return cacheItems;
    }

    private static string _CalculateCacheKey(string name, string providerName, string providerKey)
    {
        return PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);
    }

    private static string _GetPermissionNameFormCacheKey(string key)
    {
        var permissionName = PermissionGrantCacheItem.GetPermissionNameFormCacheKeyOrDefault(key);
        Ensure.True(permissionName is not null, $"Invalid permission cache key `{key}` setting name not found");

        return permissionName;
    }
}
