// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.Logging;

namespace Headless.MultiTenancy;

/// <summary>
/// The fault rules every tenancy read-through cache shares, so the catalog and the data-placement cache cannot
/// drift apart: a cache read fault degrades to a miss, a cache write fault is swallowed because the store-derived
/// result is authoritative, and <see cref="OperationCanceledException"/> always propagates. Store faults never pass
/// through here, so they propagate unwrapped from the caller.
/// </summary>
internal static class TenantCacheOperations
{
    /// <summary>
    /// Reads from <paramref name="cache"/>, degrading a read fault to a miss so the caller falls through to the
    /// store. <see cref="OperationCanceledException"/> is never a cache fault and always propagates unchanged.
    /// </summary>
    public static async Task<CacheValue<T>> TryGetAsync<T>(
        ILogger logger,
        ICache<T> cache,
        string cacheKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cache read faults degrade to a miss by design; the store is the source of truth and is consulted next. OperationCanceledException is excluded so caller cancellation still propagates.
        catch (Exception fault) when (fault is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogTenancyCacheReadFaultedDegradingToMiss(fault, typeof(T).Name);

            return CacheValue<T>.NoValue;
        }
    }

    /// <summary>
    /// Writes to <paramref name="cache"/>, swallowing a write fault so the outcome already derived from the store
    /// stays unchanged. <see cref="OperationCanceledException"/> always propagates unchanged.
    /// </summary>
    public static async Task TryUpsertAsync<T>(
        ILogger logger,
        ICache<T> cache,
        string cacheKey,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await cache.UpsertAsync(cacheKey, value, expiration, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cache write faults must never surface to the caller: the store-derived outcome already computed is authoritative regardless of whether the cache write succeeds. OperationCanceledException is excluded so caller cancellation still propagates.
        catch (Exception fault) when (fault is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogTenancyCacheWriteFaulted(fault, typeof(T).Name);
        }
    }
}

internal static partial class TenantCacheLog
{
    [LoggerMessage(
        EventId = 10,
        EventName = "TenancyCacheReadFaultedDegradingToMiss",
        Level = LogLevel.Warning,
        Message = "Tenancy cache read of {CacheItemType} faulted; degrading to a cache miss and falling through to the store."
    )]
    public static partial void LogTenancyCacheReadFaultedDegradingToMiss(
        this ILogger logger,
        Exception exception,
        string cacheItemType
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "TenancyCacheWriteFaulted",
        Level = LogLevel.Warning,
        Message = "Tenancy cache write of {CacheItemType} faulted; the resolved outcome is unaffected."
    )]
    public static partial void LogTenancyCacheWriteFaulted(
        this ILogger logger,
        Exception exception,
        string cacheItemType
    );
}
