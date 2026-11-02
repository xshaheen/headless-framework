// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Permissions;

public sealed class PermissionGrantStore(
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantRepository repository,
    IGuidGenerator guidGenerator,
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant,
    IOptions<PermissionManagementOptions> managementOptions,
    ILogger<PermissionGrantStore> logger
) : IPermissionGrantStore
{
    private readonly TimeSpan _cacheExpiration = managementOptions.Value.GrantCacheExpiration;

    private static void _EnsureKey(string? providerName, string? providerKey, string? name = null)
    {
        Argument.IsPortableKey(providerName);
        Argument.IsPortableKey(providerKey);
        Argument.IsPortableKey(name);
    }

    private static void _EnsureNames(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            Argument.IsPortableKey(name);
        }
    }

    public async Task<PermissionGrantStatus> IsGrantedAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureKey(providerName, providerKey, name);

        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);

        logger.LogGetCacheItem(cacheKey);

        var existValueCacheItem = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        if (existValueCacheItem.HasValue)
        {
            logger.LogPermissionFoundInCache(cacheKey);

            return PermissionGrantStatus.From(existValueCacheItem.Value?.IsGranted);
        }

        logger.LogPermissionNotFoundInCache(cacheKey);

        return await _CacheAllAndGetAsync(providerName, providerKey, name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, PermissionGrantStatus>> IsGrantedAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        _EnsureKey(providerName, providerKey);
        _EnsureNames(names);

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

    public async Task<Dictionary<string, Dictionary<string, PermissionGrantStatus>>> IsGrantedAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        Argument.IsNotNullOrEmpty(providerKeys);
        _EnsureKey(providerName, providerKey: null);
        _EnsureNames(names);
        _EnsureNames(providerKeys);

        // One cache read across every (name, key) pair, then one repository read for the misses: the shape
        // this replaces, one IsGrantedAsync call per key, cost one cache round-trip per target, and one
        // repository load per target with any miss.
        var distinctNames = names.ToHashSet(StringComparer.Ordinal);
        var distinctKeys = providerKeys.ToHashSet(StringComparer.Ordinal);

        var cacheKeys = new List<string>(distinctNames.Count * distinctKeys.Count);

        foreach (var providerKey in distinctKeys)
        {
            foreach (var name in distinctNames)
            {
                cacheKeys.Add(PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey));
            }
        }

        var cacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken).ConfigureAwait(false);

        logger.LogGetCachedItems(cacheKeys);

        // Misses grouped per provider key, so the repository load can name the exact names that key lacks.
        var missedByKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var providerKey in distinctKeys)
        {
            foreach (var name in distinctNames)
            {
                var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);

                if (cacheItemsMap.TryGetValue(cacheKey, out var item) && item.HasValue)
                {
                    continue;
                }

                if (!missedByKey.TryGetValue(providerKey, out var missedNames))
                {
                    missedNames = [];
                    missedByKey[providerKey] = missedNames;
                }

                _ = missedNames.Add(name);
            }
        }

        var newCacheItems =
            missedByKey.Count == 0
                ? new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal)
                : await _CacheSomeForKeysAsync(missedByKey, providerName, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<string, Dictionary<string, PermissionGrantStatus>>(
            distinctKeys.Count,
            StringComparer.Ordinal
        );

        foreach (var providerKey in distinctKeys)
        {
            var statuses = new Dictionary<string, PermissionGrantStatus>(distinctNames.Count, StringComparer.Ordinal);
            result[providerKey] = statuses;

            foreach (var name in distinctNames)
            {
                var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);
                var item =
                    newCacheItems.GetValueOrDefault(cacheKey)
                    ?? (cacheItemsMap.TryGetValue(cacheKey, out var cached) && cached.HasValue ? cached.Value : null);

                statuses[name] = PermissionGrantStatus.From(item?.IsGranted);
            }
        }

        return result;
    }

    /// <summary>Loads the missed (name, provider key) pairs from the repository in one read and caches them.</summary>
    private async Task<Dictionary<string, PermissionGrantCacheItem>> _CacheSomeForKeysAsync(
        Dictionary<string, HashSet<string>> missedByKey,
        string providerName,
        CancellationToken cancellationToken
    )
    {
        var allMissedNames = missedByKey.Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);

        var definitions = await _GetDbPermissionsDefinitionsAsync([.. allMissedNames], cancellationToken)
            .ConfigureAwait(false);

        var dbPermissionGrants = await repository
            .GetListAsync([.. allMissedNames], providerName, missedByKey.Keys, cancellationToken)
            .ConfigureAwait(false);

        // (name, key) to IsGranted for every row the repository returned.
        var grantsLookup = new Dictionary<(string Name, string Key), bool>();

        foreach (var grant in dbPermissionGrants)
        {
            grantsLookup[(grant.Name, grant.ProviderKey)] = grant.IsGranted;
        }

        // Only defined permissions get a cache entry, same as the single-key path: an undefined name stays
        // uncached so it resolves fresh if its definition appears later.
        var definedNames = definitions.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);

        logger.LogSettingCacheItems(definitions.Length);

        var cacheItems = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

        foreach (var (providerKey, missedNames) in missedByKey)
        {
            foreach (var name in missedNames)
            {
                if (!definedNames.Contains(name))
                {
                    continue;
                }

                // A row decides granted or prohibited; no row is undefined, and the cache stores that distinction.
                bool? isGranted = grantsLookup.TryGetValue((name, providerKey), out var granted) ? granted : null;
                cacheItems[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = new(
                    isGranted
                );
            }
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);
        logger.LogFinishedSettingCacheItems(definitions.Length);

        return cacheItems;
    }

    public async Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureKey(providerName, providerKey);

        var result = await repository.GetListAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false);

        return result.ConvertAll(x => new PermissionGrant(x.Name, x.IsGranted));
    }

    public async Task GrantAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureKey(providerName, providerKey, name);

        var tenantId = _GrantTenantId();

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
        CancellationToken cancellationToken = default
    )
    {
        var distinctNames = _EnsureBatch(names, providerName, providerKey);

        await _SetCoreAsync(
                unitOfWork: null,
                distinctNames,
                providerName,
                providerKey,
                isGranted: true,
                _GrantTenantId(),
                cancellationToken
            )
            .ConfigureAwait(false);

        await _UpsertCacheAsync(distinctNames, providerName, providerKey, isGranted: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RevokeAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureKey(providerName, providerKey, name);

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
        var distinctNames = _EnsureBatch(names, providerName, providerKey);

        await _SetCoreAsync(
                unitOfWork: null,
                distinctNames,
                providerName,
                providerKey,
                isGranted: false,
                currentTenant.Id,
                cancellationToken
            )
            .ConfigureAwait(false);

        await _UpsertCacheAsync(distinctNames, providerName, providerKey, isGranted: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureKey(providerName, providerKey);

        var grants = await repository.GetListAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false);

        if (grants.Count == 0)
        {
            return [];
        }

        await repository.DeleteManyAsync(grants, cancellationToken).ConfigureAwait(false);

        var names = grants.ConvertAll(x => x.Name);
        await cache
            .RemoveAllAsync(_CacheKeys(names, providerName, providerKey), cancellationToken)
            .ConfigureAwait(false);

        return names;
    }

    public async Task GrantEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);
        var distinctNames = _EnsureBatch(names, providerName, providerKey);

        await _SetCoreAsync(
                unitOfWork,
                distinctNames,
                providerName,
                providerKey,
                isGranted: true,
                _GrantTenantId(),
                cancellationToken
            )
            .ConfigureAwait(false);

        _RemoveFromCacheAfterCommit(unitOfWork, distinctNames, providerName, providerKey);
    }

    public async Task RevokeEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);
        var distinctNames = _EnsureBatch(names, providerName, providerKey);

        await _SetCoreAsync(
                unitOfWork,
                distinctNames,
                providerName,
                providerKey,
                isGranted: false,
                currentTenant.Id,
                cancellationToken
            )
            .ConfigureAwait(false);

        _RemoveFromCacheAfterCommit(unitOfWork, distinctNames, providerName, providerKey);
    }

    public async Task<IReadOnlyList<string>> DeleteEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);
        _EnsureKey(providerName, providerKey);

        var grants = await repository
            .GetListEnlistedAsync(unitOfWork, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (grants.Count == 0)
        {
            return [];
        }

        await repository.DeleteManyEnlistedAsync(unitOfWork, grants, cancellationToken).ConfigureAwait(false);

        var names = grants.ConvertAll(x => x.Name);
        _RemoveFromCacheAfterCommit(unitOfWork, names, providerName, providerKey);

        return names;
    }

    #region Helpers

    /// <summary>
    /// The tenant stamped on written grants. The repository lookup and the cache are both scoped to the current
    /// tenant, so the record must carry that same tenant: any other value writes a row the lookup never sees and leaves
    /// a cache entry that misreports it.
    /// </summary>
    private string? _GrantTenantId()
    {
        // SQL Server ignores trailing spaces when comparing keys, so a padded tenant id would share a tenant's rows.
        return Argument.IsPortableKey(currentTenant.Id, paramName: "tenantId");
    }

    private static HashSet<string> _EnsureBatch(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey
    )
    {
        Argument.IsNotNullOrEmpty(names);
        Argument.IsNotNullOrEmpty(providerName);
        Argument.IsNotNullOrEmpty(providerKey);
        _EnsureKey(providerName, providerKey);
        _EnsureNames(names);

        return names.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes <paramref name="isGranted" /> for every name: a record holding the opposite state is replaced (grants
    /// are insert-only, so replacing is delete then insert), a name with no record gets one, and a name already in that
    /// state is left alone. With a unit, every read and write runs inside its transaction.
    /// </summary>
    /// <param name="newTenantId">
    /// The tenant stamped on new records. A grant also stamps it on a replaced denial; a revoke keeps the replaced
    /// grant's own tenant.
    /// </param>
    private async Task _SetCoreAsync(
        IUnitOfWork? unitOfWork,
        HashSet<string> distinctNames,
        string providerName,
        string providerKey,
        bool isGranted,
        string? newTenantId,
        CancellationToken cancellationToken
    )
    {
        var existGrantRecords = unitOfWork is null
            ? await repository
                .GetListAsync(distinctNames, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false)
            : await repository
                .GetListEnlistedAsync(unitOfWork, distinctNames, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);

        // A name that already has a record may hold the opposite state, so the record count alone cannot decide
        // whether there is work to do — every name has to be classified.
        var flippedRecords = existGrantRecords.Where(x => x.IsGranted != isGranted).ToList();

        if (flippedRecords.Count > 0)
        {
            var replacements = flippedRecords.ConvertAll(x => new PermissionGrantRecord(
                guidGenerator.Create(),
                x.Name,
                providerName,
                providerKey,
                isGranted,
                isGranted ? newTenantId : x.TenantId
            ));

            if (unitOfWork is null)
            {
                await repository.DeleteManyAsync(flippedRecords, cancellationToken).ConfigureAwait(false);
                await repository.InsertManyAsync(replacements, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await repository
                    .DeleteManyEnlistedAsync(unitOfWork, flippedRecords, cancellationToken)
                    .ConfigureAwait(false);
                await repository
                    .InsertManyEnlistedAsync(unitOfWork, replacements, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var newRecords = distinctNames
            .Where(name => existGrantRecords.TrueForAll(x => !string.Equals(x.Name, name, StringComparison.Ordinal)))
            .Select(name => new PermissionGrantRecord(
                guidGenerator.Create(),
                name,
                providerName,
                providerKey,
                isGranted,
                newTenantId
            ))
            .AsArray();

        if (newRecords.Length == 0)
        {
            return;
        }

        if (unitOfWork is null)
        {
            await repository.InsertManyAsync(newRecords, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await repository.InsertManyEnlistedAsync(unitOfWork, newRecords, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _UpsertCacheAsync(
        HashSet<string> distinctNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken
    )
    {
        var cacheValues = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);
        var cacheItem = new PermissionGrantCacheItem(isGranted);

        foreach (var name in distinctNames)
        {
            cacheValues[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = cacheItem;
        }

        await cache
            .UpsertAllAsync(cacheValues, expiration: _cacheExpiration, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private void _RemoveFromCacheAfterCommit(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey
    )
    {
        PermissionGrantCacheInvalidation.RemoveAfterCommit(
            unitOfWork,
            cache,
            currentTenant,
            _CacheKeys(names, providerName, providerKey),
            logger
        );
    }

    private static string[] _CacheKeys(IEnumerable<string> names, string providerName, string providerKey)
    {
        return [.. names.Select(name => PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey))];
    }

    private async Task<Dictionary<string, PermissionGrantStatus>> _GetCachedItemsAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        var keyToName = new Dictionary<string, string>(names.Count, StringComparer.Ordinal);
        var cacheKeys = new List<string>(names.Count);

        foreach (var name in names)
        {
            var key = PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);
            cacheKeys.Add(key);
            keyToName[key] = name;
        }

        var cacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken).ConfigureAwait(false);

        var notCachedNames = cacheItemsMap
            .Where(x => !x.Value.HasValue)
            .Select(x => keyToName.TryGetValue(x.Key, out var n) ? n : _GetPermissionNameFormCacheKey(x.Key))
            .ToArray();

        logger.LogGetCachedItems(cacheKeys);

        if (notCachedNames.Length == 0)
        {
            logger.LogFoundInCache(cacheKeys);

            return cacheItemsMap.ToDictionary(
                x => keyToName.TryGetValue(x.Key, out var n) ? n : _GetPermissionNameFormCacheKey(x.Key),
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
            var permissionName = keyToName.TryGetValue(cacheKey, out var n)
                ? n
                : _GetPermissionNameFormCacheKey(cacheKey);

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
        var statuses = await _CacheAllAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false);

        return statuses.TryGetValue(permissionToFind, out var isGranted)
            ? PermissionGrantStatus.From(isGranted)
            : PermissionGrantStatus.Undefined;
    }

    public async Task RefreshAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(providerName);
        Argument.IsNotNullOrWhiteSpace(providerKey);
        _EnsureKey(providerName, providerKey);

        await _CacheAllAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads every grant for the provider target, writes one cache entry per known definition (a missing row caches
    /// as undefined), and returns the per-name grant value so callers can answer a lookup from the same load.
    /// </summary>
    private async Task<Dictionary<string, bool?>> _CacheAllAsync(
        string providerName,
        string providerKey,
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
        Dictionary<string, bool?> statuses = new(StringComparer.Ordinal);

        foreach (var permission in definitions)
        {
            var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permission.Name, providerName, providerKey);

            // A row decides granted or prohibited; no row is undefined, and the cache stores that distinction.
            bool? isGranted = grantsLookup.TryGetValue(permission.Name, out var granted) ? granted : null;
            cacheItems[cacheKey] = new PermissionGrantCacheItem(isGranted);
            statuses[permission.Name] = isGranted;
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);

        logger.LogFinishedSettingCacheItemsForDefinitions(definitions.Count);

        return statuses;
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

        // HashSet lookup instead of a names.Exists scan per definition (O(D x N) -> O(D + N)).
        var nameSet = names.ToHashSet(StringComparer.Ordinal);

        return [.. definitions.Where(definition => nameSet.Contains(definition.Name))];
    }

    private static string _GetPermissionNameFormCacheKey(string key)
    {
        var permissionName = PermissionGrantCacheItem.GetPermissionNameFormCacheKeyOrDefault(key);
        Ensure.True(permissionName is not null, $"Invalid permission cache key `{key}` permission name not found");

        return permissionName;
    }

    #endregion Helpers
}

/// <summary>
/// Caching read/write layer over the permission grant repository. Reads are served from a
/// tenant-scoped cache (<see cref="PermissionManagementOptions.GrantCacheExpiration"/>, 5 hours by default) and
/// populated on first miss by loading all grants for the provider in a single repository call. Writes update both
/// the repository and the cache atomically.
/// <para>
/// An explicit <c>Revoke</c> writes a denial record (<c>IsGranted = false</c>) — it does NOT delete
/// the row — so the cache can distinguish <c>Prohibited</c> from <c>Undefined</c>.
/// </para>
/// <para>
/// Every member throws <see cref="ArgumentException"/> before touching storage when a permission name, provider name,
/// provider key, or tenant id is text some provider would not keep unchanged: surrounding white space (SQL Server ignores trailing spaces when
/// it compares keys, so <c>"acme"</c> and <c>"acme "</c> would address one row there and two on PostgreSQL), a NUL
/// character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (SqlClient rewrites it to U+FFFD, merging
/// distinct keys). See <c>Argument.IsPortableKey</c>.
/// </para>
/// <para>
/// New members arrive as default interface members. The enlisted members (<c>*EnlistedAsync</c>) default to throwing
/// <see cref="NotSupportedException" />.
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

    /// <summary>
    /// Returns the grant status for each (permission name, provider key) pair the request covers, in one
    /// batched read per provider key, so a caller checking several targets of one provider (for example
    /// every role a user holds) pays one call instead of one per target.
    /// </summary>
    /// <remarks>
    /// The result is keyed by provider key, then by permission name. Every requested name appears for every
    /// requested key: a name with no record anywhere resolves to <see cref="PermissionGrantStatus.Undefined"/>.
    /// Must agree with <see cref="IsGrantedAsync(string, string, string, CancellationToken)"/>: a (name, key)
    /// pair resolvable through the single-target read resolves identically here, and vice versa.
    /// </remarks>
    /// <param name="names">The permission names to resolve. Must not be empty.</param>
    /// <param name="providerName">The provider name the keys belong to.</param>
    /// <param name="providerKeys">The provider targets to resolve the names for. Must not be empty.</param>
    Task<Dictionary<string, Dictionary<string, PermissionGrantStatus>>> IsGrantedAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all grant records (granted and denied) for the given provider target.</summary>
    Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants a permission for the provider target in the current tenant. If a denial record already exists it is
    /// replaced with a grant; if the record is already granted the call is a no-op. Updates the cache on success.
    /// </summary>
    /// <remarks>
    /// Like <see cref="RevokeAsync(string, string, string, CancellationToken)"/>, the lookup, the written record, and
    /// the cache entry all use <see cref="ICurrentTenant.Id"/>. To grant for another tenant, switch the current tenant
    /// first with <c>currentTenant.Change(tenantId)</c>.
    /// </remarks>
    Task GrantAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch overload of <see cref="GrantAsync(string, string, string, CancellationToken)"/>, in the current tenant.
    /// Skips names that already have an active grant record. Converts denial records to grants.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
    Task GrantAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
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

    /// <summary>
    /// Reloads every grant for the provider target from the repository and replaces the cached statuses, so a
    /// long-lived consumer can observe a change made outside <see cref="IPermissionManager"/> and the repository
    /// before <see cref="PermissionManagementOptions.GrantCacheExpiration"/> elapses.
    /// </summary>
    Task RefreshAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every grant and denial of the provider target and removes their cache entries.
    /// </summary>
    /// <returns>The names whose records were deleted; empty when the target had none.</returns>
    /// <exception cref="NotSupportedException">This store does not implement the member.</exception>
    Task<IReadOnlyList<string>> DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not implement deleting a provider target's grants.");
    }

    /// <summary>
    /// Batch grant inside <paramref name="unitOfWork" />'s transaction: the same writes as
    /// <see cref="GrantAsync(IReadOnlyCollection{string}, string, string, CancellationToken)"/>, with the cache
    /// cleared after the unit commits instead of updated at write time.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="names">Must not be empty.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider target.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the writes.</exception>
    /// <exception cref="NotSupportedException">This store or its repository cannot run inside a unit of work.</exception>
    Task GrantEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Batch revoke inside <paramref name="unitOfWork" />'s transaction: the same writes as
    /// <see cref="RevokeAsync(IReadOnlyCollection{string}, string, string, CancellationToken)"/>, with the cache cleared
    /// after the unit commits instead of updated at write time.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="names">Must not be empty.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider target.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the writes.</exception>
    /// <exception cref="NotSupportedException">This store or its repository cannot run inside a unit of work.</exception>
    Task RevokeEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Deletes every grant and denial of the provider target inside <paramref name="unitOfWork" />'s transaction, and
    /// clears their cache entries after the unit commits.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the delete.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider target.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The names whose records were deleted; empty when the target had none.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">This store or its repository cannot run inside a unit of work.</exception>
    Task<IReadOnlyList<string>> DeleteEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    private static NotSupportedException _NotEnlistable()
    {
        return new NotSupportedException("The configured permission grant store cannot run inside a unit of work.");
    }
}

internal static partial class PermissionGrantStoreLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GetCacheItem",
        Level = LogLevel.Debug,
        Message = "PermissionStore.GetCacheItemAsync: {CacheKey}"
    )]
    internal static partial void LogGetCacheItem(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 2,
        EventName = "PermissionFoundInCache",
        Level = LogLevel.Debug,
        Message = "Permission found in the cache: {CacheKey}"
    )]
    internal static partial void LogPermissionFoundInCache(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 3,
        EventName = "PermissionNotFoundInCache",
        Level = LogLevel.Debug,
        Message = "Permission not found in the cache: {CacheKey}"
    )]
    internal static partial void LogPermissionNotFoundInCache(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 4,
        EventName = "GetCachedItems",
        Level = LogLevel.Debug,
        Message = "PermissionStore._GetCachedItemsAsync: {@CacheKeys}"
    )]
    internal static partial void LogGetCachedItems(this ILogger logger, IReadOnlyList<string> cacheKeys);

    [LoggerMessage(
        EventId = 5,
        EventName = "FoundInCache",
        Level = LogLevel.Debug,
        Message = "Found in the cache: {@CacheKeys}"
    )]
    internal static partial void LogFoundInCache(this ILogger logger, IReadOnlyList<string> cacheKeys);

    [LoggerMessage(
        EventId = 6,
        EventName = "NotFoundInCache",
        Level = LogLevel.Debug,
        Message = "Not found in the cache: {@Names}"
    )]
    internal static partial void LogNotFoundInCache(this ILogger logger, string[] names);

    [LoggerMessage(
        EventId = 7,
        EventName = "GettingNotCachedGrantedPermissions",
        Level = LogLevel.Debug,
        Message = "Getting not cache granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}"
    )]
    internal static partial void LogGettingNotCachedGrantedPermissions(
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
    internal static partial void LogSettingCacheItems(this ILogger logger, int permissionsCount);

    [LoggerMessage(
        EventId = 9,
        EventName = "FinishedSettingCacheItems",
        Level = LogLevel.Debug,
        Message = "Finished setting the cache items. Count: {PermissionsCount}"
    )]
    internal static partial void LogFinishedSettingCacheItems(this ILogger logger, int permissionsCount);

    [LoggerMessage(
        EventId = 10,
        EventName = "GettingAllGrantedPermissions",
        Level = LogLevel.Debug,
        Message = "Getting all granted permissions from the repository for this provider name,key: {ProviderName},{ProviderKey}"
    )]
    internal static partial void LogGettingAllGrantedPermissions(
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
    internal static partial void LogSettingCacheItemsForDefinitions(this ILogger logger, int definitionsCount);

    [LoggerMessage(
        EventId = 12,
        EventName = "FinishedSettingCacheItemsForDefinitions",
        Level = LogLevel.Debug,
        Message = "Finished setting the cache items. Count: {DefinitionsCount}"
    )]
    internal static partial void LogFinishedSettingCacheItemsForDefinitions(this ILogger logger, int definitionsCount);
}
