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

/// <summary>
/// Caching read/write layer over the permission grant repository. Reads are served from a
/// tenant-scoped cache (5-hour TTL) and populated on first miss by loading all grants for the
/// provider in a single repository call. Writes update both the repository and the cache atomically.
/// <para>
/// An explicit <c>Revoke</c> writes a denial record (<c>IsGranted = false</c>) — it does NOT delete
/// the row — so the cache can distinguish <c>Prohibited</c> from <c>Undefined</c>.
/// </para>
/// </summary>
public interface IPermissionGrantStore
{
    /// <summary>
    /// Returns the grant status of a single permission for the given provider target. A cache miss triggers
    /// a full load of all grants for that provider target so subsequent lookups are served from cache.
    /// </summary>
    Task<PermissionGrantStatus> IsGrantedAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the grant status for each of the requested permission names for the given provider target.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
    Task<Dictionary<string, PermissionGrantStatus>> IsGrantedAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all grant records (granted and denied) for the given provider target.</summary>
    Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants a permission for the provider target. If a denial record already exists it is replaced with
    /// a grant; if the record is already granted the call is a no-op. Updates the cache on success.
    /// </summary>
    Task GrantAsync(
        string name,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch overload of <see cref="GrantAsync(string, string, string, string?, CancellationToken)"/>.
    /// Skips names that already have an active grant record. Converts denial records to grants.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
    Task GrantAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes an explicit denial (Prohibited) for the provider target. If a grant record already exists it
    /// is replaced with a denial; if the record is already denied the call is a no-op. Updates the cache.
    /// </summary>
    Task RevokeAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch overload of <see cref="RevokeAsync(string, string, string, CancellationToken)"/>.
    /// Converts grant records to denials and inserts denial records for names with no existing record.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
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

        logger.LogGetCacheItem(cacheKey);

        var existValueCacheItem = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        if (existValueCacheItem.HasValue)
        {
            logger.LogPermissionFoundInCache(cacheKey);

            return PermissionGrantStatus.From(existValueCacheItem.Value?.IsGranted);
        }

        logger.LogPermissionNotFoundInCache(cacheKey);

        var valueCacheItem = await _CacheAllAndGetAsync(providerName, providerKey, name, cancellationToken)
            .ConfigureAwait(false);

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
                [name] = await IsGrantedAsync(name, providerName, providerKey, cancellationToken).ConfigureAwait(false),
            };
        }

        return await _GetCachedItemsAsync(names, providerName, providerKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var result = await repository.GetListAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false);

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
        var permissionGrant = await repository
            .FindAsync(name, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (permissionGrant is not null)
        {
            // Update existing record to granted if it was previously denied
            if (!permissionGrant.IsGranted)
            {
                await repository.DeleteAsync(permissionGrant, cancellationToken).ConfigureAwait(false);
                await repository
                    .InsertAsync(
                        new PermissionGrantRecord(
                            guidGenerator.Create(),
                            name,
                            providerName,
                            providerKey,
                            isGranted: true,
                            tenantId
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                return;
            }
        }
        else
        {
            await repository
                .InsertAsync(
                    new PermissionGrantRecord(
                        guidGenerator.Create(),
                        name,
                        providerName,
                        providerKey,
                        isGranted: true,
                        tenantId
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await cache
            .UpsertAsync(
                cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
                cacheValue: new PermissionGrantCacheItem(isGranted: true),
                expiration: _cacheExpiration,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
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

        var existGrantRecords = await repository
            .GetListAsync(distinctNames, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (existGrantRecords.Count == distinctNames.Count)
        {
            return;
        }

        // Handle records that need to be updated from denied to granted
        var deniedRecords = existGrantRecords.Where(x => !x.IsGranted).ToList();
        if (deniedRecords.Count > 0)
        {
            await repository.DeleteManyAsync(deniedRecords, cancellationToken).ConfigureAwait(false);
            var updatedRecords = deniedRecords.Select(x => new PermissionGrantRecord(
                guidGenerator.Create(),
                x.Name,
                providerName,
                providerKey,
                isGranted: true,
                tenantId
            ));
            await repository.InsertManyAsync(updatedRecords, cancellationToken).ConfigureAwait(false);
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

        await repository.InsertManyAsync(newRecords, cancellationToken).ConfigureAwait(false);

        var cacheValues = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);
        var cacheItem = new PermissionGrantCacheItem(isGranted: true);

        foreach (var name in distinctNames)
        {
            cacheValues[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = cacheItem;
        }

        await cache
            .UpsertAllAsync(cacheValues, expiration: _cacheExpiration, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RevokeAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrant = await repository
            .FindAsync(name, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (permissionGrant is not null)
        {
            // Update existing grant to explicit denial
            if (permissionGrant.IsGranted)
            {
                await repository.DeleteAsync(permissionGrant, cancellationToken).ConfigureAwait(false);
                await repository
                    .InsertAsync(
                        new PermissionGrantRecord(
                            guidGenerator.Create(),
                            name,
                            providerName,
                            providerKey,
                            isGranted: false,
                            permissionGrant.TenantId
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
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
            await repository
                .InsertAsync(
                    new PermissionGrantRecord(
                        guidGenerator.Create(),
                        name,
                        providerName,
                        providerKey,
                        isGranted: false,
                        tenantId
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await cache
            .UpsertAsync(
                cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
                cacheValue: new PermissionGrantCacheItem(isGranted: false),
                expiration: _cacheExpiration,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
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

        var existGrantRecords = await repository
            .GetListAsync(distinctNames, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        // Update existing grants to denials
        var grantedRecords = existGrantRecords.Where(x => x.IsGranted).ToList();
        if (grantedRecords.Count > 0)
        {
            await repository.DeleteManyAsync(grantedRecords, cancellationToken).ConfigureAwait(false);
            var deniedRecords = grantedRecords.Select(x => new PermissionGrantRecord(
                guidGenerator.Create(),
                x.Name,
                providerName,
                providerKey,
                isGranted: false,
                x.TenantId
            ));
            await repository.InsertManyAsync(deniedRecords, cancellationToken).ConfigureAwait(false);
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

        var permissionGrantRecords = newDenials as PermissionGrantRecord[] ?? newDenials.ToArray();

        if (permissionGrantRecords.Length > 0)
        {
            await repository.InsertManyAsync(permissionGrantRecords, cancellationToken).ConfigureAwait(false);
        }

        // Update cache with denials
        var cacheValues = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);
        var cacheItem = new PermissionGrantCacheItem(isGranted: false);

        foreach (var name in distinctNames)
        {
            cacheValues[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = cacheItem;
        }

        await cache
            .UpsertAllAsync(cacheValues, expiration: _cacheExpiration, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
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

        var cacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken).ConfigureAwait(false);

        var notCachedNames = cacheItemsMap
            .Where(x => !x.Value.HasValue)
            .Select(x => _GetPermissionNameFormCacheKey(x.Key))
            .ToArray();

        logger.LogGetCachedItems(cacheKeys);

        if (notCachedNames.Length == 0)
        {
            logger.LogFoundInCache(cacheKeys);

            return cacheItemsMap.ToDictionary(
                x => _GetPermissionNameFormCacheKey(x.Key),
                x => PermissionGrantStatus.From(x.Value.Value?.IsGranted),
                StringComparer.Ordinal
            );
        }

        // Some cache items aren't found in the cache, get them from the database
        logger.LogNotFoundInCache(notCachedNames);

        var newCacheItems = await _CacheSomeAsync(notCachedNames, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
        var result = new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal);

        foreach (var cacheKey in cacheKeys)
        {
            var cachedItem = cacheItemsMap.GetOrDefault(cacheKey);
            var item = newCacheItems.GetOrDefault(cacheKey) ?? (cachedItem.HasValue ? cachedItem.Value : null);
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
        logger.LogGettingNotCachedGrantedPermissions(providerName, providerKey);

        var definitions = await _GetDbPermissionsDefinitionsAsync(names, cancellationToken).ConfigureAwait(false);
        var dbPermissionGrants = await repository
            .GetListAsync(names, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        var grantsLookup = dbPermissionGrants.ToDictionary(g => g.Name, g => g.IsGranted, StringComparer.Ordinal);

        logger.LogSettingCacheItems(definitions.Length);

        var cacheItems = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

        foreach (var permission in definitions)
        {
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);

            // If there's a record in DB, use its IsGranted value (true or false)
            // If no record, it's undefined (null)
            bool? isGranted = grantsLookup.TryGetValue(permission.Name, out var granted) ? granted : null;
            cacheItems[cacheKey] = new PermissionGrantCacheItem(isGranted);
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);
        logger.LogFinishedSettingCacheItems(definitions.Length);

        return cacheItems;
    }

    private async Task<PermissionGrantStatus> _CacheAllAndGetAsync(
        string providerName,
        string providerKey,
        string permissionToFind,
        CancellationToken cancellationToken
    )
    {
        var definitions = await permissionDefinitionManager
            .GetPermissionsAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogGettingAllGrantedPermissions(providerName, providerKey);

        var allPermissionGrants = await repository
            .GetListAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
        var grantsLookup = allPermissionGrants.ToDictionary(g => g.Name, g => g.IsGranted, StringComparer.Ordinal);

        logger.LogSettingCacheItemsForDefinitions(definitions.Count);

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

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);

        logger.LogFinishedSettingCacheItemsForDefinitions(definitions.Count);

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

        var definitions = await permissionDefinitionManager
            .GetPermissionsAsync(cancellationToken)
            .ConfigureAwait(false);

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

internal static partial class PermissionGrantStoreLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GetCacheItem",
        Level = LogLevel.Debug,
        Message = "PermissionStore.GetCacheItemAsync: {CacheKey}"
    )]
    public static partial void LogGetCacheItem(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 2,
        EventName = "PermissionFoundInCache",
        Level = LogLevel.Debug,
        Message = "Permission found in the cache: {CacheKey}"
    )]
    public static partial void LogPermissionFoundInCache(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 3,
        EventName = "PermissionNotFoundInCache",
        Level = LogLevel.Debug,
        Message = "Permission not found in the cache: {CacheKey}"
    )]
    public static partial void LogPermissionNotFoundInCache(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 4,
        EventName = "GetCachedItems",
        Level = LogLevel.Debug,
        Message = "PermissionStore._GetCachedItemsAsync: {@CacheKeys}"
    )]
    public static partial void LogGetCachedItems(this ILogger logger, IReadOnlyList<string> cacheKeys);

    [LoggerMessage(
        EventId = 5,
        EventName = "FoundInCache",
        Level = LogLevel.Debug,
        Message = "Found in the cache: {@CacheKeys}"
    )]
    public static partial void LogFoundInCache(this ILogger logger, IReadOnlyList<string> cacheKeys);

    [LoggerMessage(
        EventId = 6,
        EventName = "NotFoundInCache",
        Level = LogLevel.Debug,
        Message = "Not found in the cache: {@Names}"
    )]
    public static partial void LogNotFoundInCache(this ILogger logger, string[] names);

    [LoggerMessage(
        EventId = 7,
        EventName = "GettingNotCachedGrantedPermissions",
        Level = LogLevel.Debug,
        Message = "Getting not cache granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}"
    )]
    public static partial void LogGettingNotCachedGrantedPermissions(
        this ILogger logger,
        string providerName,
        string providerKey
    );

    [LoggerMessage(
        EventId = 8,
        EventName = "SettingCacheItems",
        Level = LogLevel.Debug,
        Message = "Setting the cache items. Count: {PermissionsCount}"
    )]
    public static partial void LogSettingCacheItems(this ILogger logger, int permissionsCount);

    [LoggerMessage(
        EventId = 9,
        EventName = "FinishedSettingCacheItems",
        Level = LogLevel.Debug,
        Message = "Finished setting the cache items. Count: {PermissionsCount}"
    )]
    public static partial void LogFinishedSettingCacheItems(this ILogger logger, int permissionsCount);

    [LoggerMessage(
        EventId = 10,
        EventName = "GettingAllGrantedPermissions",
        Level = LogLevel.Debug,
        Message = "Getting all granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}"
    )]
    public static partial void LogGettingAllGrantedPermissions(
        this ILogger logger,
        string providerName,
        string providerKey
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "SettingCacheItemsForDefinitions",
        Level = LogLevel.Debug,
        Message = "Permissions - Set the cache items. Count: {DefinitionsCount}"
    )]
    public static partial void LogSettingCacheItemsForDefinitions(this ILogger logger, int definitionsCount);

    [LoggerMessage(
        EventId = 12,
        EventName = "FinishedSettingCacheItemsForDefinitions",
        Level = LogLevel.Debug,
        Message = "Finished setting the cache items. Count: {DefinitionsCount}"
    )]
    public static partial void LogFinishedSettingCacheItemsForDefinitions(this ILogger logger, int definitionsCount);
}
