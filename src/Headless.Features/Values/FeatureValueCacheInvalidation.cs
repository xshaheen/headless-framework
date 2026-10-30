// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Features;

/// <summary>Clears feature value cache entries once a unit of work commits, never at write time.</summary>
internal static class FeatureValueCacheInvalidation
{
    /// <summary>
    /// Removes <paramref name="cacheKeys" /> after <paramref name="unitOfWork" /> commits. A rolled-back unit removes
    /// nothing, so readers never cache a value the rollback discarded. A removal fault is logged, not thrown: the commit
    /// is already durable, and the entry expires on its own.
    /// </summary>
    /// <remarks>
    /// The keys carry the provider name and key, so unlike the permission grant cache no tenant has to be restored.
    /// </remarks>
    public static void RemoveAfterCommit(
        IUnitOfWork unitOfWork,
        ICache cache,
        IReadOnlyCollection<string> cacheKeys,
        ILogger logger
    )
    {
        if (cacheKeys.Count == 0)
        {
            return;
        }

        unitOfWork.OnCompleted(async () =>
        {
            try
            {
                await cache.RemoveAllAsync(cacheKeys).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The commit is durable; a cache fault must not turn it into a reported failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateFeatureCache(ex, cacheKeys.Count);
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
        Message = "Failed to clear {KeyCount} feature value cache entries after the unit of work committed; the write is durable and the entries expire on their own"
    )]
    internal static partial void LogFailedToInvalidateFeatureCache(
        this ILogger logger,
        Exception exception,
        int keyCount
    );
}
