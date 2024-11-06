// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Framework.Permissions.Values;

public interface IPermissionValueStore
{
    Task<PermissionGrantResult> IsGrantedAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class PermissionValueStore(
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantRepository repository,
    IGuidGenerator guidGenerator,
    ICache<PermissionGrantCacheItem> cache,
    ILogger<PermissionValueStore> logger
) : IPermissionValueStore
{
    public async Task<PermissionGrantResult> IsGrantedAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);

        logger.LogDebug("PermissionStore.GetCacheItemAsync: {CacheKey}", cacheKey);

        var existValueCacheItem = await cache.GetAsync(cacheKey, cancellationToken);

        if (existValueCacheItem.HasValue)
        {
            logger.LogDebug("Permission found in the cache: {CacheKey}", cacheKey);

            return existValueCacheItem.Value?.IsGranted ?? false
                ? PermissionGrantResult.Granted
                : PermissionGrantResult.Prohibited;
        }

        logger.LogDebug("Permission not found in the cache: {CacheKey}", cacheKey);

        var valueCacheItem = await _CacheAllAndGetAsync(providerName, providerKey, name, cancellationToken);

        return valueCacheItem;
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);

        if (names.Length == 1)
        {
            var name = names[0];
            var result = await IsGrantedAsync(name, providerName, providerKey, cancellationToken);

            return new() { Result = { [name] = result } };
        }

        return await _GetCachedItemsAsync(names, providerName, providerKey, cancellationToken);
    }

    #region Helpers

    private async Task<MultiplePermissionGrantResult> _GetCachedItemsAsync(
        string[] names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        var cacheKeys = names.ConvertAll(x => PermissionGrantCacheItem.CalculateCacheKey(x, providerName, providerKey));
        logger.LogDebug("PermissionStore._GetCachedItemsAsync: {@CacheKeys}", cacheKeys as object);
        var cacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken);

        var notCachedNames = cacheItemsMap
            .Where(x => !x.Value.HasValue)
            .Select(x => _GetPermissionNameFormCacheKey(x.Key))
            .ToArray();

        if (notCachedNames.Length == 0)
        {
            logger.LogDebug("Found in the cache: {@CacheKeys}", cacheKeys as object);

            return new MultiplePermissionGrantResult(names, PermissionGrantResult.Granted);
        }

        // Some cache items aren't found in the cache, get them from the database
        logger.LogDebug("Not found in the cache: {@Names}", notCachedNames as object);
        var newCacheItems = await _CacheSomeAsync(notCachedNames, providerName, providerKey, cancellationToken);

        foreach (var key in cacheKeys)
        {
            var item = newCacheItems.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));

            if (item.Value == null)
            {
                item = cacheItems.FirstOrDefault(x => x.Key == key);
            }

            result.Add(new KeyValuePair<string, PermissionGrantCacheItem>(key, item.Value));
        }

        return result;
    }

    private async Task<Dictionary<string, PermissionGrantCacheItem>> _CacheSomeAsync(
        string[] names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        logger.LogDebug(
            "Getting not cache granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}",
            providerName,
            providerKey
        );

        var definitions = await _GetDbPermissionsDefinitionsAsync(names, cancellationToken);
        var dbPermissionGrants = await repository.GetListAsync(names, providerName, providerKey, cancellationToken);
        var dbPermissionGrantNames = dbPermissionGrants.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        logger.LogDebug("Setting the cache items. Count: {PermissionsCount}", definitions.Length);

        var cacheItems = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

        foreach (var permission in definitions)
        {
            var isGranted = dbPermissionGrantNames.Contains(permission.Name);
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);
            cacheItems[cacheKey] = new PermissionGrantCacheItem(isGranted);
        }

        await cache.UpsertAllAsync(cacheItems, 30.Days(), cancellationToken);

        logger.LogDebug("Finished setting the cache items. Count: {PermissionsCount}", definitions.Length);

        return cacheItems;
    }

    private async Task<PermissionGrantResult> _CacheAllAndGetAsync(
        string providerName,
        string providerKey,
        string permissionToFind,
        CancellationToken cancellationToken
    )
    {
        var definitions = await permissionDefinitionManager.GetAllPermissionsAsync(cancellationToken);

        logger.LogDebug(
            "Getting all granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}",
            providerName,
            providerKey
        );

        var dbRecords = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var grantedPermissions = dbRecords.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        logger.LogDebug("Permissions - Set the cache items. Count: {DefinitionsCount}", definitions.Count);

        Dictionary<string, PermissionGrantCacheItem> cacheItems = new(StringComparer.Ordinal);
        var permissionIsGranted = PermissionGrantResult.Undefined;

        foreach (var permission in definitions)
        {
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);
            var isGranted = grantedPermissions.Contains(permission.Name);
            var cacheItem = new PermissionGrantCacheItem(isGranted);
            cacheItems[cacheKey] = cacheItem;

            if (string.Equals(permission.Name, permissionToFind, StringComparison.Ordinal))
            {
                permissionIsGranted = isGranted ? PermissionGrantResult.Granted : PermissionGrantResult.Prohibited;
            }
        }

        await cache.UpsertAllAsync(cacheItems, 5.Hours(), cancellationToken);

        logger.LogDebug("Finished setting the cache items. Count: {DefinitionsCount}", definitions.Count);

        return permissionIsGranted;
    }

    private async Task<PermissionDefinition[]> _GetDbPermissionsDefinitionsAsync(
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Length == 0)
        {
            return [];
        }

        var definitions = await permissionDefinitionManager.GetAllPermissionsAsync(cancellationToken);

        return definitions
            .Where(definition => names.Exists(name => string.Equals(name, definition.Name, StringComparison.Ordinal)))
            .ToArray();
    }

    private static string _GetPermissionNameFormCacheKey(string key)
    {
        var permissionName = PermissionGrantCacheItem.GetPermissionNameFormCacheKeyOrDefault(key);
        Ensure.True(permissionName is not null, $"Invalid permission cache key `{key}` permission name not found");

        return permissionName;
    }

    #endregion
}
