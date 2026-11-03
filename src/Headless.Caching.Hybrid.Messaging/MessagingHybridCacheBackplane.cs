// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Headless.Caching;

/// <summary>
/// Sends each hybrid-cache invalidation as a <see cref="CacheInvalidationMessage"/> on the messaging bus, which
/// <see cref="HybridCacheInvalidationConsumer"/> delivers to every host.
/// </summary>
internal sealed class MessagingHybridCacheBackplane(IBus bus) : IHybridCacheBackplane
{
    // Direct: a unit of work open around the cache write must never capture or delay the invalidation, because peers
    // would serve the stale L1 copy until it commits. A system message: invalidations from host scope (catalog lookups,
    // host writes, recovery replay) carry no tenant and must still reach peers when the host requires a tenant on every
    // publish.
    internal static PublishOptions PublishOptions { get; } =
        new() { DeliveryMode = DeliveryMode.Direct, IsSystemMessage = true };

    /// <summary>
    /// Awaits the publish so a broker failure reaches the cache, which logs it and queues it for replay under
    /// auto-recovery.
    /// </summary>
    public async ValueTask PublishAsync(CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        await bus.PublishAsync(message, PublishOptions, cancellationToken).ConfigureAwait(false);
    }
}
