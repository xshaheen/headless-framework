// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

/// <summary>DI registration for the messaging-backed hybrid cache backplane.</summary>
[PublicAPI]
public static class SetupHybridCacheMessaging
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Sends hybrid-cache invalidations to every host over the messaging bus: each write publishes a
        /// <see cref="CacheInvalidationMessage"/>, and an every-instance consumer on each host evicts the L1 entries the
        /// write made stale. One backplane serves the default and every named hybrid cache.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Requires <c>AddHeadlessMessaging</c>, before or after <c>AddHeadlessCaching</c>, on a transport with
        /// every-instance delivery. The message is published under <see cref="CacheInvalidationMessage.MessageName"/> at
        /// contract version <c>1</c> with <c>DeliveryMode.Direct</c> as a system message, and the consumer's identity is
        /// <see cref="HybridCacheInvalidationConsumer.Identity"/>, so hosts on earlier versions and this one still
        /// invalidate each other.
        /// </para>
        /// <para>Replaces any backplane registered earlier, and a later <c>Use*Backplane</c> call replaces this one.</para>
        /// </remarks>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseMessagingBackplane()
        {
            setup.RegisterCrossCuttingExtension(static services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IHybridCacheBackplane, MessagingHybridCacheBackplane>());

                // Messaging applies the contribution when it starts, so it may come before or after
                // AddHeadlessMessaging. The message name is declared rather than convention-derived so every host
                // agrees on the topic whatever naming conventions it configures for its own messages.
                services.ConfigureMessaging(static messaging =>
                {
                    messaging.Message<CacheInvalidationMessage>(CacheInvalidationMessage.MessageName, "1");
                    messaging.AddModule<Hybrid.Messaging.MessagingModule>();
                });
            });

            return setup;
        }
    }
}
