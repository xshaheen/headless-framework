// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

namespace Headless.Caching;

/// <summary>
/// Message consumer that hands cache invalidations from other instances to every
/// <see cref="IHybridCacheInvalidationListener"/>, which applies them to this process's L1. <c>UseMessagingBackplane()</c>
/// registers it (see <see cref="SetupHybridCacheMessaging"/>).
/// </summary>
/// <remarks>
/// The consumer is every-instance: each L1 lives in one process, so every process must see every invalidation rather than
/// share one copy with its replicas. Delivery is at most once, so each time the subscription is established the consumer
/// has the listeners flush every hybrid's L1 instead of trusting entries whose invalidation may never have arrived.
/// </remarks>
[PublicAPI]
[BusConsumer(Identity, EveryInstance = true)]
public sealed class HybridCacheInvalidationConsumer(IEnumerable<IHybridCacheInvalidationListener> listeners)
    : IConsume<CacheInvalidationMessage>,
        IOnSubscriptionEstablished
{
    /// <summary>The consumer identity that <c>Tune</c> and <c>ConsumeOnly</c> refer to.</summary>
    public const string Identity = "headless.caching.hybrid.invalidation";

    /// <inheritdoc />
    public async ValueTask ConsumeAsync(
        ConsumeContext<CacheInvalidationMessage> context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(context);

        foreach (var listener in listeners)
        {
            await listener.OnInvalidationAsync(context.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Has every listener flush the L1 of the default and every named hybrid cache each time the subscription is
    /// established, because invalidations published while this process had no live subscription were never delivered
    /// to it.
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

        foreach (var listener in listeners)
        {
            await listener.OnSubscriptionEstablishedAsync(context.Generation, cancellationToken).ConfigureAwait(false);
        }
    }
}
