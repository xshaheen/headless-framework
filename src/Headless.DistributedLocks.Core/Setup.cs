// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Core;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

[PublicAPI]
public static class SetupDistributedLocks
{
    private const string ProvidersHint = "`UseInMemory`, `UseRedis`, `UsePostgreSql`, or `UseSqlServer`";

    extension(IServiceCollection services)
    {
        public HeadlessDistributedLocksBuilder AddHeadlessDistributedLocks(
            Action<HeadlessDistributedLocksSetupBuilder> configure
        )
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessDistributedLocksSetupBuilder(services);
            configure(setup);

            return _AddDistributedLocksCore(services, setup);
        }
    }

    private static HeadlessDistributedLocksBuilder _AddDistributedLocksCore(
        IServiceCollection services,
        HeadlessDistributedLocksSetupBuilder setup
    )
    {
        _GuardSingleDistributedLocksProvider(
            services,
            setup.Extensions.Count,
            setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown"
        );

        services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(_ => { });

        foreach (var extension in setup.Extensions)
        {
            extension.AddServices(services);
        }

        return new HeadlessDistributedLocksBuilder(services);
    }

    private static void _GuardSingleDistributedLocksProvider(
        IServiceCollection services,
        int extensionCount,
        string extensionTypeName
    )
    {
        if (extensionCount != 1)
        {
            throw new InvalidOperationException(
                extensionCount == 0
                    ? $"Headless.DistributedLocks requires exactly one provider. Call one of {ProvidersHint}."
                    : $"Headless.DistributedLocks requires exactly one provider. Multiple providers were configured; call only one of {ProvidersHint}."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(DistributedLocksProviderRegistration)))
        {
            throw new InvalidOperationException(
                $"Headless.DistributedLocks requires exactly one provider. Multiple providers were configured; call only one of {ProvidersHint}."
            );
        }

        services.AddSingleton(new DistributedLocksProviderRegistration(extensionTypeName));
    }

    private sealed record DistributedLocksProviderRegistration(string Provider);
}

internal static class DistributedLockCoreServiceCollectionExtensions
{
    internal static IServiceCollection AddDistributedLockCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedLockStorage
    {
        services.TryAddSingleton<TStorage>();

        return services.AddDistributedLockCore(static provider => provider.GetRequiredService<TStorage>());
    }

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
            provider.GetService<IOutboxBus>(),
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

        // Auto-register the shared lock-released consumer. AddHeadlessMessaging drains the
        // registration into its consumer registry once, so distributed-lock setup must run before
        // AddHeadlessMessaging when release wake-ups are needed. When messaging is never added,
        // the emitted descriptors are inert.
        DistributedLockConsumerRegistration.TryAddLockReleasedConsumer(services);

        return services;
    }
}

internal static class DistributedReadWriteLockCoreServiceCollectionExtensions
{
    internal static IServiceCollection AddDistributedReadWriteLockCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedReadWriteLockStorage
    {
        services.TryAddSingleton<TStorage>();
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

        services.TryAddSingleton<DistributedReadWriteLock>(provider => new DistributedReadWriteLock(
            provider.GetRequiredService<TStorage>(),
            provider.GetService<IOutboxBus>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedReadWriteLock>>()
        ));

        services.TryAddSingleton<IDistributedReadWriteLock>(sp => sp.GetRequiredService<DistributedReadWriteLock>());

        return services;
    }
}

internal static class DistributedSemaphoreCoreServiceCollectionExtensions
{
    internal static IServiceCollection AddDistributedSemaphoreCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedSemaphoreStorage
    {
        services.TryAddSingleton<TStorage>();
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

        services.TryAddSingleton(provider => new DistributedSemaphoreProvider(
            provider.GetRequiredService<TStorage>(),
            provider.GetService<IOutboxBus>(),
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

        DistributedLockConsumerRegistration.TryAddLockReleasedConsumer(services);

        return services;
    }
}
