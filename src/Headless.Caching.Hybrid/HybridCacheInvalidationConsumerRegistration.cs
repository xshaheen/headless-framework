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
    /// registration that precedes <c>AddHeadlessCaching</c> always wins over the auto one (the guard sees its
    /// descriptor and emits nothing). A customization added <i>after</i> caching setup coexists with the
    /// already-emitted default instead: an identical shape (the documented snippet) merges idempotently at the
    /// bootstrap drain, the same group with diverging settings fails fast at startup naming the conflict — move
    /// the customization before <c>AddHeadlessCaching</c> to defer the default — and a different group adds a
    /// second, separately-grouped subscription.
    /// </para>
    /// <para>
    /// <b>Unconditional and order-independent.</b> The consumer is registered through <c>ForMessage</c> regardless
    /// of whether a bus is present yet: the emitted descriptors are inert until messaging bootstrap drains them
    /// from the built provider, and <c>ForMessage</c> is documented to work before or after
    /// <c>AddHeadlessMessaging</c> as long as both precede host build. Gating on an <see cref="IBus"/> descriptor
    /// here would make correctness depend on registration order (messaging before caching) and silently leave the
    /// backplane publish-only when the order flips; a functioning <see cref="HybridCache"/> resolves a required
    /// <see cref="IBus"/> anyway, so an idle subscription in a genuinely bus-less host cannot run in the first
    /// place — the inert descriptor costs nothing.
    /// </para>
    /// <para>
    /// The consumer shape mirrors the documented explicit snippet exactly
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

        services.ForMessage<CacheInvalidationMessage>(message => message.OnBus<HybridCacheInvalidationConsumer>());
    }
}
