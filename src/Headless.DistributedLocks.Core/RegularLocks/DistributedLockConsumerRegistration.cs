// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static class DistributedLockConsumerRegistration
{
    /// <summary>
    /// Auto-registers the single lock-released consumer shared by the mutex and semaphore providers
    /// (both fan out from it via <see cref="ICanReceiveLockReleased"/>). Uses the service-collection
    /// <c>ForMessage</c> seam, whose registration is drained into the consumer registry by
    /// <c>AddHeadlessMessaging</c>. Idempotent across repeated distributed-lock primitive
    /// registrations: once the consumer's <see cref="IConsume{TMessage}"/>
    /// descriptor is present, subsequent calls are no-ops.
    /// </summary>
    /// <remarks>
    /// Ordering constraint: because the consumer registry is built during <c>AddHeadlessMessaging</c>,
    /// distributed-lock primitive setup must run before it. The seam throws
    /// if called afterwards. An order-independent path (runtime subscription) is tracked in #390.
    /// </remarks>
    public static void TryAddLockReleasedConsumer(IServiceCollection services)
    {
        if (services.Any(static d => d.ServiceType == typeof(IConsume<DistributedLockReleased>)))
        {
            return;
        }

        services.ForMessage<DistributedLockReleased>(message =>
            message
                .MessageName("headless.locks.released")
                .OnBus<DistributedLock.LockReleasedConsumer>(consumer => consumer.Concurrency(1))
        );
    }
}
