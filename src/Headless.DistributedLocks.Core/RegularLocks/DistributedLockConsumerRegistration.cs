// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Registers the lock-release consumer that bridges the messaging bus and the in-process distributed-lock providers.
/// </summary>
internal static class DistributedLockConsumerRegistration
{
    /// <summary>
    /// Contributes this assembly's generated <c>MessagingModule</c>, which declares the lock-released consumer shared by
    /// the mutex, semaphore, and reader-writer providers (all fan out from it via <see cref="ICanReceiveLockReleased"/>),
    /// and declares the release signal's message contract. Repeated calls are harmless: messaging registers the module
    /// once and merges the identical contract declarations.
    /// </summary>
    /// <remarks>
    /// The registration is order-independent relative to <c>AddHeadlessMessaging</c> as long as both run during service
    /// configuration, before the provider is built.
    /// </remarks>
    public static void AddLockReleasedConsumer(IServiceCollection services)
    {
        services.ConfigureMessaging(static messaging =>
        {
            messaging.Message<DistributedLockReleased>(DistributedLock.LockReleasedConsumer.MessageName);
            messaging.AddModule<Core.MessagingModule>();
        });
    }
}
