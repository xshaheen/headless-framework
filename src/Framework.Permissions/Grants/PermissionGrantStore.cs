// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Caching;
using Framework.Checks;
using Framework.Permissions.Definitions;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Framework.Permissions.Grants;

public interface IPermissionGrantStore
{
    Task<PermissionGrantStatus> IsGrantedAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<Dictionary<string, PermissionGrantStatus>> IsGrantedAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task GrantAsync(
        string name,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );

    Task GrantAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );

    Task RevokeAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task RevokeAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class PermissionGrantStore(
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantRepository repository,
    IGuidGenerator guidGenerator,
    ICache<PermissionGrantCacheItem> cache,
    ILogger<PermissionGrantStore> logger
) : IPermissionGrantStore
{
    private readonly TimeSpan _cacheExpiration = 5.Hours();

    public async Task<PermissionGrantStatus> IsGrantedAsync(
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

            return existValueCacheItem.Value?.IsGranted switch
            {
                true => PermissionGrantStatus.Granted,
                false => PermissionGrantStatus.Prohibited,
                null => PermissionGrantStatus.Undefined,
            };
        }

        logger.LogDebug("Permission not found in the cache: {CacheKey}", cacheKey);

        var valueCacheItem = await _CacheAllAndGetAsync(providerName, providerKey, name, cancellationToken);

        return valueCacheItem;
    }

    public async Task<Dictionary<string, PermissionGrantStatus>> IsGrantedAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);

        if (names.Count == 1)
        {
            var name = names[0];

            return new(StringComparer.Ordinal)
            {
                [name] = await IsGrantedAsync(name, providerName, providerKey, cancellationToken),
            };
        }

        return await _GetCachedItemsAsync(names, providerName, providerKey, cancellationToken);
    }

    public async Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var result = await repository.GetListAsync(providerName, providerKey, cancellationToken);

        return result.ConvertAll(x => new PermissionGrant(x.Name, isGranted: true));
    }

    public async Task GrantAsync(
        string name,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrant = await repository.FindAsync(name, providerName, providerKey, cancellationToken);

        if (permissionGrant is not null)
        {
            return;
        }

        await repository.InsertAsync(
            new PermissionGrantRecord(guidGenerator.Create(), name, providerName, providerKey, tenantId),
            cancellationToken
        );

        await cache.UpsertAsync(
            cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
            cacheValue: new PermissionGrantCacheItem(isGranted: true),
            expiration: _cacheExpiration,
            cancellationToken: cancellationToken
        );
    }

    public async Task GrantAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        Argument.IsNotNullOrEmpty(providerName);
        Argument.IsNotNullOrEmpty(providerKey);

        var distinctNames = names.ToHashSet(StringComparer.Ordinal);

        var existGrantRecords = await repository.GetListAsync(
            distinctNames,
            providerName,
            providerKey,
            cancellationToken
        );

        if (existGrantRecords.Count == distinctNames.Count)
        {
            return;
        }

        var newRecords = distinctNames
            .Where(name => existGrantRecords.TrueForAll(x => !string.Equals(x.Name, name, StringComparison.Ordinal)))
            .Select(name => new PermissionGrantRecord(
                guidGenerator.Create(),
                name,
                providerName,
                providerKey,
                tenantId
            ));

        await repository.InsertManyAsync(newRecords, cancellationToken);

        var cacheValues = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);
        var cacheItem = new PermissionGrantCacheItem(isGranted: true);

        foreach (var name in distinctNames)
        {
            cacheValues[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = cacheItem;
        }

        await cache.UpsertAllAsync(cacheValues, expiration: _cacheExpiration, cancellationToken: cancellationToken);
    }

    public async Task RevokeAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrant = await repository.FindAsync(name, providerName, providerKey, cancellationToken);

        if (permissionGrant is null)
        {
            return;
        }

        await repository.DeleteAsync(permissionGrant, cancellationToken);

        await cache.RemoveAsync(
            cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
            cancellationToken
        );
    }

    public async Task RevokeAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        Argument.IsNotNullOrEmpty(providerName);
        Argument.IsNotNullOrEmpty(providerKey);

        var distinctNames = names.ToHashSet(StringComparer.Ordinal);

        var existGrantRecords = await repository.GetListAsync(
            distinctNames,
            providerName,
            providerKey,
            cancellationToken
        );

        if (existGrantRecords.Count == 0)
        {
            return;
        }

        await repository.DeleteManyAsync(existGrantRecords, cancellationToken);

        foreach (var name in names)
        {
            await cache.RemoveAsync(
                cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
                cancellationToken
            );
        }
    }

    #region Helpers

    private async Task<Dictionary<string, PermissionGrantStatus>> _GetCachedItemsAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        var cacheKeys = names
            .Select(x => PermissionGrantCacheItem.CalculateCacheKey(x, providerName, providerKey))
            .ToList();

        var cacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken);

        var notCachedNames = cacheItemsMap
            .Where(x => !x.Value.HasValue)
            .Select(x => _GetPermissionNameFormCacheKey(x.Key))
            .ToArray();

        logger.LogDebug("PermissionStore._GetCachedItemsAsync: {@CacheKeys}", cacheKeys);

        if (notCachedNames.Length == 0)
        {
            logger.LogDebug("Found in the cache: {@CacheKeys}", cacheKeys);

            return names.ToDictionary(name => name, _ => PermissionGrantStatus.Granted, StringComparer.Ordinal);
        }

        // Some cache items aren't found in the cache, get them from the database
        logger.LogDebug("Not found in the cache: {@Names}", notCachedNames as object);

        var newCacheItems = await _CacheSomeAsync(notCachedNames, providerName, providerKey, cancellationToken);
        var result = new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal);

        foreach (var cacheKey in cacheKeys)
        {
            var item = newCacheItems.GetOrDefault(cacheKey) ?? cacheItemsMap.GetOrDefault(cacheKey)?.Value;
            var permissionName = _GetPermissionNameFormCacheKey(cacheKey);

            result[permissionName] = item?.IsGranted switch
            {
                true => PermissionGrantStatus.Granted,
                false => PermissionGrantStatus.Prohibited,
                null => PermissionGrantStatus.Undefined,
            };
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

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken);
        logger.LogDebug("Finished setting the cache items. Count: {PermissionsCount}", definitions.Length);

        return cacheItems;
    }

    private async Task<PermissionGrantStatus> _CacheAllAndGetAsync(
        string providerName,
        string providerKey,
        string permissionToFind,
        CancellationToken cancellationToken
    )
    {
        var definitions = await permissionDefinitionManager.GetPermissionsAsync(cancellationToken);

        logger.LogDebug(
            "Getting all granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}",
            providerName,
            providerKey
        );

        var dbRecords = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var grantedPermissions = dbRecords.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        logger.LogDebug("Permissions - Set the cache items. Count: {DefinitionsCount}", definitions.Count);

        Dictionary<string, PermissionGrantCacheItem> cacheItems = new(StringComparer.Ordinal);
        var permissionIsGranted = PermissionGrantStatus.Undefined;

        foreach (var permission in definitions)
        {
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);
            var isGranted = grantedPermissions.Contains(permission.Name);
            var cacheItem = new PermissionGrantCacheItem(isGranted);
            cacheItems[cacheKey] = cacheItem;

            if (string.Equals(permission.Name, permissionToFind, StringComparison.Ordinal))
            {
                permissionIsGranted = isGranted ? PermissionGrantStatus.Granted : PermissionGrantStatus.Prohibited;
            }
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken);

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

        var definitions = await permissionDefinitionManager.GetPermissionsAsync(cancellationToken);

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
