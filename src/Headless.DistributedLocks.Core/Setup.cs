// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// Entry point for registering Headless Distributed Locks in the DI container.
/// Call <c>services.AddHeadlessDistributedLocks(setup => setup.Use…(…))</c> once per
/// application to wire a backend provider and the shared lock infrastructure.
/// </summary>
[PublicAPI]
public static class SetupDistributedLocks
{
    private const string _ProvidersHint = "`UseInMemory`, `UseRedis`, `UsePostgreSql`, or `UseSqlServer`";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless Distributed Locks and a single backend provider with the DI
        /// container. The <paramref name="configure"/> callback must call exactly one
        /// <c>Use*</c> provider extension (e.g., <c>setup.UseRedis(…)</c>); zero or multiple
        /// provider registrations throw <see cref="InvalidOperationException"/> at setup time.
        /// </summary>
        /// <param name="configure">
        /// Delegate that configures the setup builder, including selecting and configuring the
        /// backend provider via a <c>Use*</c> extension method.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when zero or more than one provider is configured, or when
        /// <c>AddHeadlessDistributedLocks</c> is called more than once for the same provider.
        /// </exception>
        public IServiceCollection AddHeadlessDistributedLocks(Action<HeadlessDistributedLocksSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessDistributedLocksSetupBuilder(services);
            configure(setup);

            return _AddDistributedLocksCore(services, setup);
        }
    }

    private static IServiceCollection _AddDistributedLocksCore(
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

        return services;
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
                    ? $"Headless.DistributedLocks requires exactly one provider. Call one of {_ProvidersHint}."
                    : $"Headless.DistributedLocks requires exactly one provider. Multiple providers were configured; call only one of {_ProvidersHint}."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(DistributedLocksProviderRegistration)))
        {
            throw new InvalidOperationException(
                $"Headless.DistributedLocks requires exactly one provider. Multiple providers were configured; call only one of {_ProvidersHint}."
            );
        }

        services.AddSingleton(new DistributedLocksProviderRegistration(extensionTypeName));
    }

    private sealed record DistributedLocksProviderRegistration(string Provider);
}

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
    /// <summary>
    /// Registers the <see cref="DistributedReadWriteLock"/> singleton and its
    /// <see cref="IDistributedReadWriteLock"/> alias using <typeparamref name="TStorage"/> as
    /// the storage backend. Registrations are idempotent via <c>TryAdd*</c>.
    /// </summary>
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
