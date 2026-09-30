// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>
/// Message consumer that applies cache invalidation messages from other instances to this process's L1. Every hybrid
/// registration adds it (see <see cref="SetupHybridCache"/>). Routes to the default hybrid cache when
/// <see cref="CacheInvalidationMessage.CacheName"/> is <see langword="null"/>, or to the matching named hybrid cache
/// through <see cref="ICacheProvider"/> when set.
/// </summary>
/// <remarks>
/// The consumer is every-instance: each L1 lives in one process, so every process must see every invalidation rather than
/// share one copy with its replicas. Delivery is at most once, so each time the subscription is established the consumer flushes
/// every hybrid's L1 instead of trusting entries whose invalidation may never have arrived.
/// </remarks>
[PublicAPI]
[BusConsumer(Identity, EveryInstance = true)]
public sealed class HybridCacheInvalidationConsumer(
    ICacheProvider cacheProvider,
    ILogger<HybridCacheInvalidationConsumer> logger
) : IConsume<CacheInvalidationMessage>, IOnSubscriptionEstablished
{
    /// <summary>The consumer identity that <c>Tune</c> and <c>ConsumeOnly</c> refer to.</summary>
    public const string Identity = "headless.caching.hybrid.invalidation";

    /// <inheritdoc />
    public async ValueTask ConsumeAsync(
        ConsumeContext<CacheInvalidationMessage> context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var target = _ResolveTarget(context.Message);

            if (target is null)
            {
                return;
            }

            await target.HandleInvalidationAsync(context.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, don't log as error
            throw;
        }
        catch (Exception ex)
        {
            var msg = context.Message;
            logger.LogFailedToProcessCacheInvalidation(
                ex,
                msg.InstanceId,
                msg.Keys?.Length ?? (msg.Key is not null ? 1 : 0),
                msg.Prefix is not null,
                msg.FlushAll
            );
        }
    }

    /// <summary>
    /// Flushes the L1 of the default and every named hybrid cache each time the subscription is established, because
    /// invalidations published while this process had no live subscription were never delivered to it.
    /// </summary>
    /// <remarks>
    /// The first establishment flushes too: L1 can be warmed before the subscription is live (host startup reads, or a
    /// broker outage at startup that delays it), and any invalidation published in that window is lost. Flushing an
    /// empty L1 on a clean start costs nothing.
    /// </remarks>
    /// <param name="context">The establishment, first or re-established.</param>
    /// <param name="cancellationToken">Cancelled when the subscription stops.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when every L1 is flushed.</returns>
    public async ValueTask OnSubscriptionEstablishedAsync(
        SubscriptionEstablishedContext context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(context);

        if (cacheProvider.GetCacheOrNull(CacheConstants.HybridCacheProvider) is HybridCache defaultCache)
        {
            await defaultCache
                .FlushLocalAfterSubscriptionGapAsync(context.Generation, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var name in cacheProvider.RegisteredNames)
        {
            if (cacheProvider.GetCacheOrNull(name) is HybridCache namedCache)
            {
                await namedCache
                    .FlushLocalAfterSubscriptionGapAsync(context.Generation, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private HybridCache? _ResolveTarget(CacheInvalidationMessage message)
    {
        if (message.CacheName is null)
        {
            return cacheProvider.GetCacheOrNull(CacheConstants.HybridCacheProvider) as HybridCache;
        }

        return cacheProvider.GetCacheOrNull(message.CacheName) as HybridCache;
    }
}

internal static partial class HybridCacheInvalidationConsumerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToProcessCacheInvalidation",
        Level = LogLevel.Error,
        Message = "Failed to process cache invalidation message (instanceId={InstanceId}, keyCount={KeyCount}, hasPrefix={HasPrefix}, flushAll={FlushAll})"
    )]
    public static partial void LogFailedToProcessCacheInvalidation(
        this ILogger logger,
        Exception exception,
        string? instanceId,
        int keyCount,
        bool hasPrefix,
        bool flushAll
    );
}
