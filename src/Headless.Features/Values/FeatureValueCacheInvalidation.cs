// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Features;

/// <summary>
/// Keeps a reader's cache fill from overwriting a concurrent write, and writes an enlisted write's values once its unit
/// of work commits.
/// </summary>
/// <remarks>
/// A reader that misses the cache loads the whole provider scope from the repository, then fills the cache. A write that
/// lands between that load and the fill would otherwise be overwritten by the value the reader loaded before it, and
/// stay stale until the entry expires. So a reader fills only entries that are still absent, and every write stores a
/// value (a delete stores an empty item) instead of removing the entry: an entry present at fill time was written by a
/// writer at least as new as the reader's load. A store write stores what the repository holds after its commit, swapped
/// in only while the entry still holds what it read (<see cref="SyncAsync"/>), so writers whose cache writes land out of
/// commit order still leave the last committed value.
/// </remarks>
internal static class FeatureValueCacheInvalidation
{
    /// <summary>How long a feature value entry lives in the cache.</summary>
    public static readonly TimeSpan Expiration = TimeSpan.FromHours(5);

    /// <summary>Fills each of <paramref name="cacheItems"/> only where the cache holds no entry yet.</summary>
    public static async ValueTask FillAbsentAsync(
        ICache cache,
        IReadOnlyDictionary<string, FeatureValueCacheItem> cacheItems,
        CancellationToken cancellationToken
    )
    {
        var inserts = new List<Task>(cacheItems.Count);

        foreach (var (key, item) in cacheItems)
        {
            inserts.Add(cache.TryInsertAsync(key, item, Expiration, cancellationToken).AsTask());
        }

        await Task.WhenAll(inserts).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings the entries of <paramref name="names"/> under one provider scope to what the repository holds now. A store
    /// write calls it after its commit instead of storing the value it wrote, so two writers whose cache writes land in
    /// the opposite order to their commits still leave the last committed value cached.
    /// A write passes <see cref="CancellationToken.None"/>: its commit is durable, so a caller that stops waiting must
    /// not leave the cache serving the value the write replaced until the entry expires.
    /// </summary>
    public static ValueTask SyncAsync(
        ICache cache,
        IFeatureValueRecordRepository repository,
        IEnumerable<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var keyToName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            keyToName[FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey)] = name;
        }

        return new Cache<FeatureValueCacheItem>(cache).SyncWithSourceAsync(
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

                var stored = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var record in records)
                {
                    stored[record.Name] = record.Value;
                }

                var items = new Dictionary<string, FeatureValueCacheItem>(StringComparer.Ordinal);

                foreach (var key in keys)
                {
                    // No row is cached as an empty item, not removed, so a reader that loaded the row before it was
                    // deleted cannot fill it back: readers fill only an absent entry.
                    items[key] = new FeatureValueCacheItem(stored.GetValueOrDefault(keyToName[key]));
                }

                return items;
            },
            Expiration,
            cancellationToken
        );
    }

    /// <summary>
    /// Runs <see cref="SyncAsync"/> after <paramref name="unitOfWork" /> commits. A rolled-back unit touches nothing, so
    /// readers never cache a value the rollback discarded. A fault is logged, not thrown: the commit is already
    /// durable, and the entry expires on its own.
    /// </summary>
    public static void SyncAfterCommit(
        IUnitOfWork unitOfWork,
        ICache cache,
        IFeatureValueRecordRepository repository,
        IReadOnlyCollection<string> names,
        string providerName,
        string? providerKey,
        ILogger logger
    )
    {
        if (names.Count == 0)
        {
            return;
        }

        unitOfWork.OnCompleted(async () =>
        {
            try
            {
                await SyncAsync(cache, repository, names, providerName, providerKey, CancellationToken.None)
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The commit is durable; a cache fault must not turn it into a reported failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateFeatureCache(ex, names.Count);
            }
        });
    }
}

internal static partial class FeatureValueCacheInvalidationLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToInvalidateFeatureCacheAfterCommit",
        Level = LogLevel.Warning,
        Message = "Failed to update {KeyCount} feature value cache entries after the unit of work committed; the write is durable and the entries expire on their own"
    )]
    internal static partial void LogFailedToInvalidateFeatureCache(
        this ILogger logger,
        Exception exception,
        int keyCount
    );
}
