// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Permissions;

/// <summary>Clears grant cache entries once a unit of work commits, never at write time.</summary>
internal static class PermissionGrantCacheInvalidation
{
    /// <summary>
    /// Removes <paramref name="cacheKeys" /> after <paramref name="unitOfWork" /> commits. A rolled-back unit removes
    /// nothing, so readers never cache a value the rollback discarded. A removal fault is logged, not thrown: the commit
    /// is already durable, and the entry expires on its own.
    /// </summary>
    public static void RemoveAfterCommit(
        IUnitOfWork unitOfWork,
        ICache<PermissionGrantCacheItem> cache,
        ICurrentTenant currentTenant,
        IReadOnlyCollection<string> cacheKeys,
        ILogger logger
    )
    {
        if (cacheKeys.Count == 0)
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
                    await cache.RemoveAllAsync(cacheKeys).ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // The commit is durable; a cache fault must not turn it into a reported failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateGrantCache(ex, cacheKeys.Count);
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
        Message = "Failed to clear {KeyCount} permission grant cache entries after the unit of work committed; the write is durable and the entries expire on their own"
    )]
    internal static partial void LogFailedToInvalidateGrantCache(
        this ILogger logger,
        Exception exception,
        int keyCount
    );
}
