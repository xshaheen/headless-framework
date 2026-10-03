// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

internal static class DistributedSemaphoreCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="DistributedSemaphoreProvider"/> singleton and its
    /// <see cref="IDistributedSemaphoreProvider"/> alias using <typeparamref name="TStorage"/>
    /// as the storage backend. Also registers the semaphore under
    /// <see cref="ICanReceiveLockReleased"/> so messaging-driven wake-ups wake semaphore
    /// waiters alongside mutex waiters. Registrations are idempotent via <c>TryAdd*</c>.
    /// </summary>
    internal static IServiceCollection AddDistributedSemaphoreCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedSemaphoreStorage
    {
        services.TryAddSingleton<TStorage>();
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

        services.TryAddSingleton(provider => new DistributedSemaphoreProvider(
            provider.GetRequiredService<TStorage>(),
            provider.GetService<IBus>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedSemaphoreProvider>>()
        ));

        services.TryAddSingleton<IDistributedSemaphoreProvider>(sp =>
            sp.GetRequiredService<DistributedSemaphoreProvider>()
        );

        // Register under ICanReceiveLockReleased so LockReleasedConsumer wakes semaphore waiters
        // alongside mutex waiters. TryAddEnumerable keeps repeated registrations idempotent.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICanReceiveLockReleased, DistributedSemaphoreProvider>(static sp =>
                sp.GetRequiredService<DistributedSemaphoreProvider>()
            )
        );

        DistributedLockConsumerRegistration.AddLockReleasedConsumer(services);

        return services;
    }
}
