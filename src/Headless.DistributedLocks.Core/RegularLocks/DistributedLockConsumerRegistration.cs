// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Registration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Helpers for registering the lock-release consumer that bridges the messaging bus and the
/// in-process distributed-lock providers.
/// </summary>
internal static class DistributedLockConsumerRegistration
{
    /// <summary>
    /// Auto-registers the single lock-released consumer shared by the mutex and semaphore providers
    /// (both fan out from it via <see cref="ICanReceiveLockReleased"/>). Uses an internal immutable Bus
    /// contribution that messaging bootstrap drains into the consumer registry. Idempotent across repeated
    /// primitive registrations: once the consumer's <see cref="IConsume{TMessage}"/>
    /// descriptor is present, subsequent calls are no-ops.
    /// </summary>
    /// <remarks>
    /// The registration is order-independent relative to <c>AddHeadlessMessaging</c> as long as both
    /// run during service configuration, before the provider is built.
    /// </remarks>
    public static void TryAddLockReleasedConsumer(IServiceCollection services)
    {
        if (services.Any(static d => d.ServiceType == typeof(IConsume<DistributedLockReleased>)))
        {
            return;
        }

        services.AddFrameworkConsumerRegistration<DistributedLockReleased, DistributedLock.LockReleasedConsumer>(
            MessageLane.Bus,
            messageName: "headless.locks.released",
            concurrency: 1
        );
    }
}
