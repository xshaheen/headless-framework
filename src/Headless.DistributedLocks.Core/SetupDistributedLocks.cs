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
