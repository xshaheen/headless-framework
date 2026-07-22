// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

[PublicAPI]
public static class SetupCachingCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless caching from a single setup builder. Provider packages contribute through
        /// <c>Use*</c>/<c>Add*Tier</c>/<c>AddNamed</c> extensions on <see cref="HeadlessCachingSetupBuilder"/>;
        /// exactly one default provider is required. All contributions are deferred until the setup gates
        /// pass, so a failed setup leaves the service collection unchanged.
        /// </summary>
        /// <param name="configure">The setup action selecting the providers.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHeadlessCaching(Action<HeadlessCachingSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessCachingSetupBuilder(services);
            configure(setup);

            return _AddCachingCore(services, setup);
        }

        /// <summary>
        /// Registers <see cref="ICacheProvider"/> backed by the container's keyed <see cref="ICache"/>
        /// registrations. Called by every cache provider setup, so the provider is available whenever any
        /// cache is registered. Safe to call multiple times — only the first registration wins, so the
        /// orchestrating <c>AddHeadlessCaching</c> call supplies <paramref name="registeredNames"/> before
        /// any provider contribution runs; the provider packages call this parameterless as a no-op fallback.
        /// </summary>
        /// <param name="registeredNames">
        /// The named cache instance names to expose on <see cref="ICacheProvider.RegisteredNames"/>, or
        /// <see langword="null"/> for an empty set.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        internal IServiceCollection AddCacheProvider(IReadOnlySet<string>? registeredNames = null)
        {
            services.TryAddSingleton<ICacheProvider>(provider => new KeyedServiceCacheProvider(
                provider,
                registeredNames ?? FrozenSet<string>.Empty
            ));

            return services;
        }
    }

    private static IServiceCollection _AddCachingCore(IServiceCollection services, HeadlessCachingSetupBuilder setup)
    {
        if (setup.DefaultExtensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.DefaultExtensions.Count == 0
                    ? "Headless.Caching requires exactly one default cache provider. Call one of `UseInMemory`, `UseRedis`, or `UseHybrid`."
                    : "Headless.Caching requires exactly one default cache provider. Multiple default providers were configured."
            );
        }

        var (defaultRoleKey, defaultAction) = setup.DefaultExtensions[0];

        if (setup.TierExtensions.Exists(tier => string.Equals(tier.RoleKey, defaultRoleKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"The role key '{defaultRoleKey}' is claimed by both the default cache provider and a tier "
                    + "provider. Remove the tier registration or pick a different default provider."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(CachingProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessCaching was already called on this service collection. Configure all cache "
                    + "instances (default, tiers, named) in a single AddHeadlessCaching call."
            );
        }

        services.AddSingleton(new CachingProviderRegistration(defaultRoleKey));

        // Caching-wide instrumentation config (R13): resolved by every provider from DI and threaded into the
        // coordinator / hybrid span emission. Registered once here from the setup builder's flag.
        services.TryAddSingleton(new CacheInstrumentationConfig { IncludeKeyInTraces = setup.IncludeKeyInTraces });

        // Caching-wide event-handler execution config (#385): resolved by every provider from DI and threaded into
        // its CacheEventsHub. Registered once here from the setup builder.
        services.TryAddSingleton(
            new CacheEventsConfig
            {
                SyncHandlers = setup.SyncHandlers,
                HandlerErrorLogLevel = setup.EventHandlerErrorLogLevel,
            }
        );

        // Named instances only — the default and the role keys are resolvable via GetCache but excluded here.
        var registeredNames = setup.InstanceNames.ToFrozenSet(StringComparer.Ordinal);
        services.AddCacheProvider(registeredNames);

        foreach (var (_, action) in setup.TierExtensions)
        {
            action(services);
        }

        defaultAction(services);

        foreach (var (_, action) in setup.NamedExtensions)
        {
            action(services);
        }

        foreach (var action in setup.CrossCuttingExtensions)
        {
            action(services);
        }

        return services;
    }

    private sealed record CachingProviderRegistration(string Provider);
}
