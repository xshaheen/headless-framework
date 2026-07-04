// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>
/// Helpers for auto-registering the single <see cref="HybridCacheInvalidationConsumer"/> that receives
/// <see cref="CacheInvalidationMessage"/> from the messaging backplane, so cross-node L1 invalidation is
/// correct by default instead of silently opt-in.
/// </summary>
internal static class HybridCacheInvalidationConsumerRegistration
{
    /// <summary>
    /// Auto-registers the single shared <see cref="HybridCacheInvalidationConsumer"/> through the
    /// service-collection <c>ForMessage</c> seam, whose registration is drained into the consumer registry by
    /// messaging bootstrap. One consumer serves every hybrid (default and named): it routes each incoming
    /// <see cref="CacheInvalidationMessage"/> to the default or the matching named hybrid by
    /// <see cref="CacheInvalidationMessage.CacheName"/> through <see cref="ICacheProvider"/>, so this is called
    /// once per hybrid registration but registers the consumer at most once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Idempotent.</b> Once an <see cref="IConsume{TMessage}"/> descriptor for
    /// <see cref="CacheInvalidationMessage"/> is present — whether emitted by a prior call here or by the
    /// application's own explicit <c>ForMessage&lt;CacheInvalidationMessage&gt;(…)</c> — subsequent calls are
    /// no-ops. So any number of default/named hybrids register exactly one consumer, and an application
    /// registration always wins over the auto one.
    /// </para>
    /// <para>
    /// <b>Bus-gated.</b> The consumer is wired only when a messaging bus (<see cref="IBus"/>) is already present
    /// in the collection. A single-node host with no backplane pays for no idle subscription. Because
    /// <see cref="HybridCache"/> resolves a required <see cref="IBus"/>, a functioning hybrid always has one; the
    /// gate matters for the "caching without messaging" shape and keeps parity with the stated
    /// correct-by-default-when-a-bus-exists contract.
    /// </para>
    /// <para>
    /// <b>Ordering.</b> The gate reads the collection at caching-setup time, so a bus must be registered before
    /// <c>AddHeadlessCaching</c> for the auto-wiring to fire (every documented recipe adds messaging first). If
    /// messaging is added afterwards the consumer is not auto-wired, and the
    /// <see cref="HybridCacheBestPracticesAdvisor"/> Check 5 warning fires at startup so the gap is loud rather
    /// than silent — register the consumer explicitly or order messaging before caching. Once wired, the
    /// registration itself is order-independent: it is drained from the built provider at messaging bootstrap.
    /// </para>
    /// <para>
    /// The consumer shape mirrors the advisor's recommended snippet exactly
    /// (<c>OnBus&lt;HybridCacheInvalidationConsumer&gt;()</c> with the default group and convention-derived
    /// message name) so an application that copied that snippet produces a matching registration the bootstrap
    /// drain merges instead of double-registering.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection the hybrid cache is being registered into.</param>
    public static void TryAddInvalidationConsumer(IServiceCollection services)
    {
        // Idempotent: an application's own ForMessage<CacheInvalidationMessage>, or a prior call here for another
        // hybrid on the same collection, already wired the consumer — never double-register.
        if (services.Any(static d => d.ServiceType == typeof(IConsume<CacheInvalidationMessage>)))
        {
            return;
        }

        // Gate on bus presence: with no backplane bus there is nothing to consume, so do not wire an idle
        // subscription. Bus registration must precede AddHeadlessCaching (see the remarks on ordering).
        if (!services.Any(static d => d.ServiceType == typeof(IBus)))
        {
            return;
        }

        services.ForMessage<CacheInvalidationMessage>(message => message.OnBus<HybridCacheInvalidationConsumer>());
    }
}
