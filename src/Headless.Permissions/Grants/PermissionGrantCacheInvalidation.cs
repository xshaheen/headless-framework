// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Permissions;

/// <summary>Rewrites grant cache entries with the status a write stored, once a unit of work commits.</summary>
internal static class PermissionGrantCacheInvalidation
{
    /// <summary>
    /// The cache entries a repository write leaves behind: each written record's status, and an undefined status for
    /// each deleted record. A deleted record is stored as undefined rather than removed, so a reader that loaded the row
    /// before the delete cannot fill it back: readers fill only an absent entry.
    /// </summary>
    public static Dictionary<string, PermissionGrantCacheItem> WrittenItems(
        IEnumerable<PermissionGrantRecord> written,
        IEnumerable<PermissionGrantRecord> deleted
    )
    {
        var items = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

        foreach (var record in deleted)
        {
            items[CacheKey(record)] = new PermissionGrantCacheItem(isGranted: null);
        }

        // After the deletes, so a record written in the same call wins over a deleted one with the same key.
        foreach (var record in written)
        {
            items[CacheKey(record)] = new PermissionGrantCacheItem(record.IsGranted);
        }

        return items;
    }

    /// <summary>Returns the cache key of <paramref name="record"/>.</summary>
    public static string CacheKey(PermissionGrantRecord record)
    {
        return PermissionGrantCacheItem.CalculateCacheKey(record.Name, record.ProviderName, record.ProviderKey);
    }

    /// <summary>
    /// Brings the entries of <paramref name="names"/> for one provider target to what the repository holds now, under
    /// the current tenant. A store write calls it after its commit instead of storing the status it wrote, so two
    /// writers whose cache writes land in the opposite order to their commits still leave the last committed status
    /// cached.
    /// </summary>
    public static ValueTask SyncAsync(
        ICache<PermissionGrantCacheItem> cache,
        IPermissionGrantRepository repository,
        IEnumerable<string> names,
        string providerName,
        string providerKey,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        var keyToName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            keyToName[PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey)] = name;
        }

        return cache.SyncWithSourceAsync(
            keyToName.Keys,
            async (keys, token) =>
            {
                var records = await repository
                    .GetListAsync(
                        keys.Select(key => keyToName[key]).ToHashSet(StringComparer.Ordinal),
                        providerName,
                        providerKey,
                        token
                    )
                    .ConfigureAwait(false);

                var stored = new Dictionary<string, bool>(StringComparer.Ordinal);

                foreach (var record in records)
                {
                    stored[record.Name] = record.IsGranted;
                }

                var items = new Dictionary<string, PermissionGrantCacheItem>(StringComparer.Ordinal);

                foreach (var key in keys)
                {
                    // No row is cached as undefined, not removed, so a reader that loaded the row before it was deleted
                    // cannot fill it back: readers fill only an absent entry.
                    items[key] = new PermissionGrantCacheItem(
                        stored.TryGetValue(keyToName[key], out var isGranted) ? isGranted : null
                    );
                }

                return items;
            },
            expiration,
            cancellationToken
        );
    }

    /// <summary>
    /// Runs <see cref="SyncAsync"/> after <paramref name="unitOfWork" /> commits, under the tenant that made the write.
    /// A rolled-back unit touches nothing, so readers never cache a status the rollback discarded. A fault is logged,
    /// not thrown: the commit is already durable, and the entry expires on its own.
    /// </summary>
    public static void SyncAfterCommit(
        IUnitOfWork unitOfWork,
        ICache<PermissionGrantCacheItem> cache,
        IPermissionGrantRepository repository,
        ICurrentTenant currentTenant,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        TimeSpan expiration,
        ILogger logger
    )
    {
        if (names.Count == 0)
        {
            return;
        }

        var tenantId = currentTenant.Id;
        var tenantName = currentTenant.Name;

        unitOfWork.OnCompleted(async () =>
        {
            try
            {
                // The cache and the repository are tenant-scoped through the current tenant, which the code completing
                // the unit may no longer hold.
                using (currentTenant.Change(tenantId, tenantName))
                {
                    await SyncAsync(
                            cache,
                            repository,
                            names,
                            providerName,
                            providerKey,
                            expiration,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // The commit is durable; a cache fault must not turn it into a reported failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateGrantCache(ex, names.Count);
            }
        });
    }

    /// <summary>
    /// Writes <paramref name="cacheValues" /> after <paramref name="unitOfWork" /> commits. A rolled-back unit writes
    /// nothing. Writing the committed status rather than removing the entry keeps out a reader that loaded the rows
    /// before the commit, because readers fill only an absent entry. A write fault is logged, not thrown: the commit
    /// is already durable, and the entry expires on its own.
    /// </summary>
    public static void UpsertAfterCommit(
        IUnitOfWork unitOfWork,
        ICache<PermissionGrantCacheItem> cache,
        ICurrentTenant currentTenant,
        IDictionary<string, PermissionGrantCacheItem> cacheValues,
        TimeSpan expiration,
        ILogger logger
    )
    {
        if (cacheValues.Count == 0)
        {
            return;
        }

        var tenantId = currentTenant.Id;
        var tenantName = currentTenant.Name;

        unitOfWork.OnCompleted(async () =>
        {
            try
            {
                // The cache is tenant-scoped through the current tenant, which the code completing the unit may no
                // longer hold.
                using (currentTenant.Change(tenantId, tenantName))
                {
                    await cache.UpsertAllAsync(cacheValues, expiration).ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // The commit is durable; a cache fault must not turn it into a reported failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateGrantCache(ex, cacheValues.Count);
            }
        });
    }
}

internal static partial class PermissionGrantCacheInvalidationLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToInvalidateGrantCacheAfterCommit",
        Level = LogLevel.Warning,
        Message = "Failed to update {KeyCount} permission grant cache entries after the unit of work committed; the write is durable and the entries expire on their own"
    )]
    internal static partial void LogFailedToInvalidateGrantCache(
        this ILogger logger,
        Exception exception,
        int keyCount
    );
}
