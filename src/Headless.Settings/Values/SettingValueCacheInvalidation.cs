// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Settings;

/// <summary>
/// Keeps a reader's cache fill from overwriting a concurrent write, and writes an enlisted write's values once its unit
/// of work commits.
/// </summary>
/// <remarks>
/// A reader that misses the cache loads the whole provider scope from the repository, then fills the cache. A write that
/// lands between that load and the fill would otherwise be overwritten by the value the reader loaded before it, and
/// stay stale until the entry expires. So a reader fills only entries that are still absent, and every write stores the
/// value it wrote (a delete stores an empty item) instead of removing the entry: an entry present at fill time was
/// written by a writer at least as new as the reader's load.
/// </remarks>
internal static class SettingValueCacheInvalidation
{
    /// <summary>Fills each of <paramref name="cacheItems"/> only where the cache holds no entry yet.</summary>
    public static async ValueTask FillAbsentAsync(
        ICache<SettingValueCacheItem> cache,
        IReadOnlyDictionary<string, SettingValueCacheItem> cacheItems,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        var inserts = new List<Task>(cacheItems.Count);

        foreach (var (key, item) in cacheItems)
        {
            inserts.Add(cache.TryInsertAsync(key, item, expiration, cancellationToken).AsTask());
        }

        await Task.WhenAll(inserts).ConfigureAwait(false);
    }

    /// <summary>Returns the cache entries that describe <paramref name="written"/> and <paramref name="deleted"/> after the write.</summary>
    public static Dictionary<string, SettingValueCacheItem> WrittenItems(
        IEnumerable<SettingValueRecord> written,
        IEnumerable<SettingValueRecord> deleted
    )
    {
        var items = new Dictionary<string, SettingValueCacheItem>(StringComparer.Ordinal);

        foreach (var record in written)
        {
            items[CacheKey(record)] = new SettingValueCacheItem(record.Value);
        }

        foreach (var record in deleted)
        {
            items[CacheKey(record)] = new SettingValueCacheItem(value: null);
        }

        return items;
    }

    /// <summary>Returns the cache key of <paramref name="record"/>.</summary>
    public static string CacheKey(SettingValueRecord record)
    {
        return SettingValueCacheItem.CalculateCacheKey(record.Name, record.ProviderName, record.ProviderKey);
    }

    /// <summary>
    /// Writes <paramref name="cacheItems" /> after <paramref name="unitOfWork" /> commits. A rolled-back unit writes
    /// nothing, so readers never cache a value the rollback discarded. Writing the committed value rather than removing
    /// the entry keeps out a reader that loaded the rows before the commit. A fault is logged, not thrown: the commit is
    /// already durable, and the entry expires on its own.
    /// </summary>
    /// <remarks>
    /// The keys carry the provider name and key, so unlike the permission grant cache no tenant has to be restored.
    /// </remarks>
    public static void UpsertAfterCommit(
        IUnitOfWork unitOfWork,
        ICache<SettingValueCacheItem> cache,
        IDictionary<string, SettingValueCacheItem> cacheItems,
        TimeSpan expiration,
        ILogger logger
    )
    {
        if (cacheItems.Count == 0)
        {
            return;
        }

        unitOfWork.OnCompleted(async () =>
        {
            try
            {
                await cache.UpsertAllAsync(cacheItems, expiration).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The commit is durable; a cache fault must not turn it into a reported failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateSettingCache(ex, cacheItems.Count);
            }
        });
    }
}

internal static partial class SettingValueCacheInvalidationLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToInvalidateSettingCacheAfterCommit",
        Level = LogLevel.Warning,
        Message = "Failed to update {KeyCount} setting value cache entries after the unit of work committed; the write is durable and the entries expire on their own"
    )]
    internal static partial void LogFailedToInvalidateSettingCache(
        this ILogger logger,
        Exception exception,
        int keyCount
    );
}
