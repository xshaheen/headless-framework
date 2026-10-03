// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

internal static class DistributedLockCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers mutex-lock core services using <typeparamref name="TStorage"/> as the
    /// singleton storage backend. Delegates to the factory overload.
    /// </summary>
    internal static IServiceCollection AddDistributedLockCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedLockStorage
    {
        services.TryAddSingleton<TStorage>();

        return services.AddDistributedLockCore(static provider => provider.GetRequiredService<TStorage>());
    }

    /// <summary>
    /// Registers the <see cref="DistributedLock"/> singleton, its <see cref="IDistributedLock"/>
    /// alias, and the <see cref="ICanReceiveLockReleased"/> enumerable entry using the supplied
    /// <paramref name="storageFactory"/>. All registrations are idempotent via
    /// <c>TryAdd*</c> so repeated calls (e.g., multi-provider extension) do not accumulate
    /// duplicate descriptors. Also auto-registers the shared lock-released consumer so
    /// messaging-driven wake-ups work when <c>AddHeadlessMessaging</c> is later called.
    /// </summary>
    internal static IServiceCollection AddDistributedLockCore(
        this IServiceCollection services,
        Func<IServiceProvider, IDistributedLockStorage> storageFactory
    )
    {
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

        // TryAddSingleton on the concrete + the public interface keeps repeated
        // AddHeadlessDistributedLocks(...) calls idempotent inside a single provider extension.
        // Two AddSingleton calls would accumulate descriptors and register two distinct lambdas
        // resolving against the same concrete type.
        services.TryAddSingleton<DistributedLock>(provider => new DistributedLock(
            storageFactory(provider),
            provider.GetService<IBus>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedLock>>()
        ));

        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<DistributedLock>());

        // Register ICanReceiveLockReleased pointing at the same concrete instance so that a
        // decorator wrapped around IDistributedLock does not break the lock-release
        // wake-up signal (the consumer always receives the real DistributedLock).
        // TryAddEnumerable keeps repeated primitive registrations idempotent, and
        // LockReleasedConsumer fans out over the collected IEnumerable<ICanReceiveLockReleased>
        // so mutex and semaphore providers share one decoupled wake-up seam.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICanReceiveLockReleased, DistributedLock>(static sp =>
                sp.GetRequiredService<DistributedLock>()
            )
        );

        // Auto-register the shared lock-released consumer. Messaging applies the contribution when it
        // starts, so it may come before or after AddHeadlessMessaging; without messaging it stays inert.
        DistributedLockConsumerRegistration.AddLockReleasedConsumer(services);

        return services;
    }
}
