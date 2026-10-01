// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>
/// Registers the single <see cref="HybridCacheInvalidationConsumer"/> that receives <see cref="CacheInvalidationMessage"/>
/// from the messaging backplane, so cross-node L1 invalidation is correct by default instead of silently opt-in.
/// </summary>
internal static class HybridCacheInvalidationConsumerRegistration
{
    /// <summary>
    /// Contributes this assembly's generated <c>MessagingModule</c>, which declares
    /// <see cref="HybridCacheInvalidationConsumer"/> as an every-instance Bus consumer, and declares the invalidation's
    /// message contract. One consumer serves every hybrid, default and named, by routing on
    /// <see cref="CacheInvalidationMessage.CacheName"/>, so this runs once per hybrid registration; messaging registers
    /// the module once and merges the identical contract declarations.
    /// </summary>
    /// <remarks>
    /// The contribution is unconditional and order-independent: it stays inert until messaging starts, and it can come
    /// before or after <c>AddHeadlessMessaging</c>. Gating on an <see cref="IBus"/> descriptor would make correctness depend
    /// on registration order and silently leave the backplane publish-only when the order flips. A host tunes the consumer
    /// by its identity, <see cref="HybridCacheInvalidationConsumer.Identity"/>.
    /// </remarks>
    /// <param name="services">The service collection the hybrid cache is being registered into.</param>
    public static void AddInvalidationConsumer(IServiceCollection services)
    {
        services.ConfigureMessaging(static messaging =>
        {
            // Declared rather than convention-derived so every node agrees on the topic even when the services
            // sharing the broker configure different naming conventions for their own messages.
            messaging.Message<CacheInvalidationMessage>(CacheInvalidationMessage.MessageName, "1");
            messaging.AddModule<Hybrid.MessagingModule>();
        });
    }
}
