// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
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

        foreach (var action in setup.CrossCuttingExtensions)
        {
            action(services);
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
    /// alias, and the <see cref="IDistributedLockReleaseListener"/> enumerable entry using the supplied
    /// <paramref name="storageFactory"/>. All registrations are idempotent via
    /// <c>TryAdd*</c> so repeated calls (e.g., multi-provider extension) do not accumulate
    /// duplicate descriptors.
    /// </summary>
    internal static IServiceCollection AddDistributedLockCore(
        this IServiceCollection services,
        Func<IServiceProvider, IDistributedLockStorage> storageFactory
    )
    {
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();
        services.AddLocalReleaseSignal();

        // TryAddSingleton on the concrete + the public interface keeps repeated
        // AddHeadlessDistributedLocks(...) calls idempotent inside a single provider extension.
        // Two AddSingleton calls would accumulate descriptors and register two distinct lambdas
        // resolving against the same concrete type.
        services.TryAddSingleton<DistributedLock>(provider => new DistributedLock(
            storageFactory(provider),
            provider.GetRequiredService<IDistributedLockReleaseSignal>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedLock>>()
        ));

        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<DistributedLock>());

        // Register the listener pointing at the same concrete instance so that a decorator wrapped around
        // IDistributedLock does not break the lock-release wake-up signal (the transport always reaches the real
        // DistributedLock). TryAddEnumerable keeps repeated primitive registrations idempotent, and the transport fans
        // out over the collected IEnumerable<IDistributedLockReleaseListener>, so the mutex, semaphore, and
        // reader-writer providers share one wake-up seam.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDistributedLockReleaseListener, DistributedLock>(static sp =>
                sp.GetRequiredService<DistributedLock>()
            )
        );

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
        services.AddLocalReleaseSignal();

        services.TryAddSingleton<DistributedReadWriteLock>(provider => new DistributedReadWriteLock(
            provider.GetRequiredService<TStorage>(),
            provider.GetRequiredService<IDistributedLockReleaseSignal>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedReadWriteLock>>()
        ));

        services.TryAddSingleton<IDistributedReadWriteLock>(sp => sp.GetRequiredService<DistributedReadWriteLock>());

        // Join the lock-released fan-out with the same singleton so a release wakes reader-writer waiters too.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDistributedLockReleaseListener, DistributedReadWriteLock>(static sp =>
                sp.GetRequiredService<DistributedReadWriteLock>()
            )
        );

        return services;
    }
}

internal static class DistributedSemaphoreCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="DistributedSemaphoreProvider"/> singleton and its
    /// <see cref="IDistributedSemaphoreProvider"/> alias using <typeparamref name="TStorage"/>
    /// as the storage backend. Also registers the semaphore under
    /// <see cref="IDistributedLockReleaseListener"/> so release wake-ups reach semaphore
    /// waiters alongside mutex waiters. Registrations are idempotent via <c>TryAdd*</c>.
    /// </summary>
    internal static IServiceCollection AddDistributedSemaphoreCore<TStorage>(this IServiceCollection services)
        where TStorage : class, IDistributedSemaphoreStorage
    {
        services.TryAddSingleton<TStorage>();
        services.AddSingletonOptionValue<DistributedLockOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();
        services.AddLocalReleaseSignal();

        services.TryAddSingleton(provider => new DistributedSemaphoreProvider(
            provider.GetRequiredService<TStorage>(),
            provider.GetRequiredService<IDistributedLockReleaseSignal>(),
            provider.GetRequiredService<DistributedLockOptions>(),
            provider.GetRequiredService<IGuidGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DistributedSemaphoreProvider>>()
        ));

        services.TryAddSingleton<IDistributedSemaphoreProvider>(sp =>
            sp.GetRequiredService<DistributedSemaphoreProvider>()
        );

        // Register as a release listener so a release signal wakes semaphore waiters
        // alongside mutex waiters. TryAddEnumerable keeps repeated registrations idempotent.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDistributedLockReleaseListener, DistributedSemaphoreProvider>(static sp =>
                sp.GetRequiredService<DistributedSemaphoreProvider>()
            )
        );

        return services;
    }
}

internal static class DistributedLockReleaseSignalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default release signal, which wakes this process's waiters and logs once that other hosts poll.
    /// <c>TryAdd</c> keeps a signal registered first, such as the in-process provider's silent one, and a cross-host
    /// transport replaces it from a cross-cutting extension that runs after the provider.
    /// </summary>
    internal static IServiceCollection AddLocalReleaseSignal(this IServiceCollection services)
    {
        services.TryAddSingleton<IDistributedLockReleaseSignal>(static sp => new LocalDistributedLockReleaseSignal(
            sp,
            sp.GetRequiredService<ILogger<LocalDistributedLockReleaseSignal>>()
        ));

        return services;
    }

    /// <summary>
    /// Registers the in-process release signal without its log line, for a provider that only ever runs in one process,
    /// where a release has no other host to reach.
    /// </summary>
    internal static IServiceCollection AddSingleProcessReleaseSignal(this IServiceCollection services)
    {
        services.TryAddSingleton<IDistributedLockReleaseSignal>(static sp => new LocalDistributedLockReleaseSignal(
            sp,
            logger: null
        ));

        return services;
    }
}
