// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Headless.Permissions.Grants;

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
    ICurrentTenant currentTenant,
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

            return PermissionGrantStatus.From(existValueCacheItem.Value?.IsGranted);
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

        return result.ConvertAll(x => new PermissionGrant(x.Name, x.IsGranted));
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
            // Update existing record to granted if it was previously denied
            if (!permissionGrant.IsGranted)
            {
                await repository.DeleteAsync(permissionGrant, cancellationToken);
                await repository.InsertAsync(
                    new PermissionGrantRecord(
                        guidGenerator.Create(),
                        name,
                        providerName,
                        providerKey,
                        isGranted: true,
                        tenantId
                    ),
                    cancellationToken
                );
            }
            else
            {
                return;
            }
        }
        else
        {
            await repository.InsertAsync(
                new PermissionGrantRecord(
                    guidGenerator.Create(),
                    name,
                    providerName,
                    providerKey,
                    isGranted: true,
                    tenantId
                ),
                cancellationToken
            );
        }

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

        // Handle records that need to be updated from denied to granted
        var deniedRecords = existGrantRecords.Where(x => !x.IsGranted).ToList();
        if (deniedRecords.Count > 0)
        {
            await repository.DeleteManyAsync(deniedRecords, cancellationToken);
            var updatedRecords = deniedRecords.Select(x => new PermissionGrantRecord(
                guidGenerator.Create(),
                x.Name,
                providerName,
                providerKey,
                isGranted: true,
                tenantId
            ));
            await repository.InsertManyAsync(updatedRecords, cancellationToken);
        }

        var newRecords = distinctNames
            .Where(name => existGrantRecords.TrueForAll(x => !string.Equals(x.Name, name, StringComparison.Ordinal)))
            .Select(name => new PermissionGrantRecord(
                guidGenerator.Create(),
                name,
                providerName,
                providerKey,
                isGranted: true,
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

        if (permissionGrant is not null)
        {
            // Update existing grant to explicit denial
            if (permissionGrant.IsGranted)
            {
                await repository.DeleteAsync(permissionGrant, cancellationToken);
                await repository.InsertAsync(
                    new PermissionGrantRecord(
                        guidGenerator.Create(),
                        name,
                        providerName,
                        providerKey,
                        isGranted: false,
                        permissionGrant.TenantId
                    ),
                    cancellationToken
                );
            }
            else
            {
                return; // Already denied
            }
        }
        else
        {
            // Insert explicit denial
            var tenantId = currentTenant.Id;
            await repository.InsertAsync(
                new PermissionGrantRecord(
                    guidGenerator.Create(),
                    name,
                    providerName,
                    providerKey,
                    isGranted: false,
                    tenantId
                ),
                cancellationToken
            );
        }

        await cache.UpsertAsync(
            cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
            cacheValue: new PermissionGrantCacheItem(isGranted: false),
            expiration: _cacheExpiration,
            cancellationToken: cancellationToken
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

        // Update existing grants to denials
        var grantedRecords = existGrantRecords.Where(x => x.IsGranted).ToList();
        if (grantedRecords.Count > 0)
        {
            await repository.DeleteManyAsync(grantedRecords, cancellationToken);
            var deniedRecords = grantedRecords.Select(x => new PermissionGrantRecord(
                guidGenerator.Create(),
                x.Name,
                providerName,
                providerKey,
                isGranted: false,
                x.TenantId
            ));
            await repository.InsertManyAsync(deniedRecords, cancellationToken);
        }

        // Insert explicit denials for names that don't have records
        var tenantId = currentTenant.Id;
        var newDenials = distinctNames
            .Where(name => existGrantRecords.TrueForAll(x => !string.Equals(x.Name, name, StringComparison.Ordinal)))
            .Select(name => new PermissionGrantRecord(
                guidGenerator.Create(),
                name,
                providerName,
                providerKey,
                isGranted: false,
                tenantId
            ));

        if (newDenials.Any())
        {
            await repository.InsertManyAsync(newDenials, cancellationToken);
        }

        // Update cache with denials
        var cacheValues = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);
        var cacheItem = new PermissionGrantCacheItem(isGranted: false);

        foreach (var name in distinctNames)
        {
            cacheValues[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = cacheItem;
        }

        await cache.UpsertAllAsync(cacheValues, expiration: _cacheExpiration, cancellationToken: cancellationToken);
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

            return cacheItemsMap.ToDictionary(
                x => _GetPermissionNameFormCacheKey(x.Key),
                x => PermissionGrantStatus.From(x.Value.Value?.IsGranted),
                StringComparer.Ordinal
            );
        }

        // Some cache items aren't found in the cache, get them from the database
        logger.LogDebug("Not found in the cache: {@Names}", notCachedNames as object);

        var newCacheItems = await _CacheSomeAsync(notCachedNames, providerName, providerKey, cancellationToken);
        var result = new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal);

        foreach (var cacheKey in cacheKeys)
        {
            var item = newCacheItems.GetOrDefault(cacheKey) ?? cacheItemsMap.GetOrDefault(cacheKey)?.Value;
            var permissionName = _GetPermissionNameFormCacheKey(cacheKey);

            result[permissionName] = PermissionGrantStatus.From(item?.IsGranted);
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

        var grantsLookup = dbPermissionGrants.ToDictionary(g => g.Name, g => g.IsGranted, StringComparer.Ordinal);

        logger.LogDebug("Setting the cache items. Count: {PermissionsCount}", definitions.Length);

        var cacheItems = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

        foreach (var permission in definitions)
        {
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);

            // If there's a record in DB, use its IsGranted value (true or false)
            // If no record, it's undefined (null)
            bool? isGranted = grantsLookup.TryGetValue(permission.Name, out var granted) ? granted : null;
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

        var allPermissionGrants = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var grantsLookup = allPermissionGrants.ToDictionary(g => g.Name, g => g.IsGranted, StringComparer.Ordinal);

        logger.LogDebug("Permissions - Set the cache items. Count: {DefinitionsCount}", definitions.Count);

        Dictionary<string, PermissionGrantCacheItem> cacheItems = new(StringComparer.Ordinal);
        var permissionIsGranted = PermissionGrantStatus.Undefined;

        foreach (var permission in definitions)
        {
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);

            // If there's a record in DB, use its IsGranted value (true or false)
            // If no record, it's undefined (null)
            bool? isGranted = grantsLookup.TryGetValue(permission.Name, out var granted) ? granted : null;
            var cacheItem = new PermissionGrantCacheItem(isGranted);
            cacheItems[cacheKey] = cacheItem;

            if (string.Equals(permission.Name, permissionToFind, StringComparison.Ordinal))
            {
                permissionIsGranted = PermissionGrantStatus.From(isGranted);
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
