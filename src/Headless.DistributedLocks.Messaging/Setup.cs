// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.DistributedLocks;

/// <summary>DI registration for the messaging-backed lock release signal.</summary>
[PublicAPI]
public static class SetupDistributedLocksMessaging
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>
        /// Pushes lock-release wake-ups to every host over the messaging bus: each confirmed release of a lock,
        /// semaphore slot, or reader-writer lease publishes a <see cref="DistributedLockReleased"/> message, and an
        /// every-instance consumer on each host wakes its local waiters. Without it, waiters wake on their backoff
        /// poll.
        /// </summary>
        /// <remarks>
        /// Requires <c>AddHeadlessMessaging</c>, before or after <c>AddHeadlessDistributedLocks</c>: the lock
        /// providers resolve <see cref="IBus"/> when they are first built. The message is published under
        /// <see cref="DistributedLockReleased.MessageName"/> at contract version <c>1</c>, and the consumer's identity
        /// is <c>headless.distributed-locks.release</c>. Replaces any release signal registered earlier.
        /// </remarks>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
        public HeadlessDistributedLocksSetupBuilder UseMessagingReleaseSignal()
        {
            setup.RegisterCrossCuttingExtension(static services =>
            {
                services.Replace(
                    ServiceDescriptor.Singleton<IDistributedLockReleaseSignal, MessagingDistributedLockReleaseSignal>()
                );

                // Messaging applies the contribution when it starts, so it may come before or after
                // AddHeadlessMessaging. The message name is declared rather than convention-derived so every host
                // agrees on the topic whatever naming conventions it configures for its own messages.
                services.ConfigureMessaging(static messaging =>
                {
                    messaging.Message<DistributedLockReleased>(DistributedLockReleased.MessageName, "1");
                    messaging.AddModule<Messaging.MessagingModule>();
                });
            });

            return setup;
        }
    }
}
