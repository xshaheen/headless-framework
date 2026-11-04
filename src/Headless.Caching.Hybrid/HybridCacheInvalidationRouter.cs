// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>
/// The listener every hybrid registration adds: routes a delivered invalidation to the default hybrid cache when
/// <see cref="CacheInvalidationMessage.CacheName"/> is <see langword="null"/>, or to the named hybrid cache it names
/// through <see cref="ICacheProvider"/>, and flushes every hybrid's L1 when the transport's subscription is
/// established. One router serves every hybrid, so a transport needs no knowledge of how many there are.
/// </summary>
internal sealed class HybridCacheInvalidationRouter(
    ICacheProvider cacheProvider,
    ILogger<HybridCacheInvalidationRouter> logger
) : IHybridCacheInvalidationListener
{
    public async ValueTask OnInvalidationAsync(CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(message);

        try
        {
            var target = _ResolveTarget(message);

            if (target is null)
            {
                return;
            }

            await target.HandleInvalidationAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One bad message must not stop the transport's delivery loop; the failure is logged.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogFailedToProcessCacheInvalidation(
                exception,
                message.InstanceId,
                message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
                message.Prefix is not null,
                message.FlushAll
            );
        }
    }

    public async ValueTask OnSubscriptionEstablishedAsync(long generation, CancellationToken cancellationToken)
    {
        if (cacheProvider.GetCacheOrNull(CacheConstants.HybridCacheProvider) is HybridCache defaultCache)
        {
            await defaultCache.FlushLocalAfterSubscriptionGapAsync(generation, cancellationToken).ConfigureAwait(false);
        }

        foreach (var name in cacheProvider.RegisteredNames)
        {
            if (cacheProvider.GetCacheOrNull(name) is HybridCache namedCache)
            {
                await namedCache
                    .FlushLocalAfterSubscriptionGapAsync(generation, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private HybridCache? _ResolveTarget(CacheInvalidationMessage message)
    {
        return cacheProvider.GetCacheOrNull(message.CacheName ?? CacheConstants.HybridCacheProvider) as HybridCache;
    }
}

internal static partial class HybridCacheInvalidationRouterLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToProcessCacheInvalidation",
        Level = LogLevel.Error,
        Message = "Failed to process cache invalidation message (instanceId={InstanceId}, keyCount={KeyCount}, hasPrefix={HasPrefix}, flushAll={FlushAll})"
    )]
    internal static partial void LogFailedToProcessCacheInvalidation(
        this ILogger logger,
        Exception exception,
        string? instanceId,
        int keyCount,
        bool hasPrefix,
        bool flushAll
    );
}
