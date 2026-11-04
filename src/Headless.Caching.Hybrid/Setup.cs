// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>DI registration extension methods for hybrid cache.</summary>
/// <remarks>
/// <para><b>Prerequisites for a default hybrid cache:</b></para>
/// <list type="bullet">
/// <item>Local tier: call <c>setup.AddMemoryTier()</c> in the same <c>AddHeadlessCaching</c> setup</item>
/// <item>Remote tier: call <c>setup.AddRedisTier(…)</c> (or register an <see cref="IRemoteCache"/>)</item>
/// <item>
/// Backplane: choose how hosts evict each other's L1 entries, with <c>setup.UseRedisBackplane()</c>,
/// <c>setup.UseMessagingBackplane()</c>, or <c>setup.UseSingleInstanceBackplane()</c>; the host refuses to start
/// without one (see <see cref="IHybridCacheBackplane"/>)
/// </item>
/// </list>
/// <para><b>Example:</b></para>
/// <code>
/// services.AddHeadlessCaching(setup =>
/// {
///     setup.AddMemoryTier();
///     setup.AddRedisTier(options => options.ConnectionMultiplexer = multiplexer);
///     setup.UseHybrid(options => options.DefaultLocalExpiration = TimeSpan.FromMinutes(5));
///     setup.UseRedisBackplane();
/// });
/// </code>
/// <para>
/// When <see cref="HybridCacheOptions.LocalCacheName"/> / <see cref="HybridCacheOptions.RemoteCacheName"/>
/// are set, the corresponding tier is resolved from the keyed <see cref="ICache"/> registration with that
/// name instead of the default <see cref="IInMemoryCache"/> / <see cref="IRemoteCache"/> services.
/// </para>
/// </remarks>
[PublicAPI]
public static class SetupHybridCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Uses the hybrid cache as the default (unkeyed) <see cref="ICache"/>, composing the local and
        /// remote tiers registered in the same setup (see the class-level remarks for the full recipe).
        /// </summary>
        /// <param name="setupAction">Optional configuration for <see cref="HybridCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseHybrid(Action<HybridCacheOptions>? setupAction = null)
        {
            setup.RegisterDefaultProvider(
                CacheConstants.HybridCacheProvider,
                services =>
                {
                    if (setupAction is null)
                    {
                        services.AddOptions<HybridCacheOptions, HybridCacheOptionsValidator>();
                    }
                    else
                    {
                        services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction);
                    }

                    _AddCacheCore(services);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses the hybrid cache as the default (unkeyed) <see cref="ICache"/> with service provider-aware
        /// configuration. See <see cref="UseHybrid(HeadlessCachingSetupBuilder, Action{HybridCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseHybrid(Action<HybridCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(
                CacheConstants.HybridCacheProvider,
                services =>
                {
                    services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses the hybrid cache as the default (unkeyed) <see cref="ICache"/>, binding
        /// <see cref="HybridCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseHybrid(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(
                CacheConstants.HybridCacheProvider,
                services =>
                {
                    services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(configuration);
                    _AddCacheCore(services);
                }
            );

            return setup;
        }

        /// <summary>
        /// Declares that exactly one host runs these hybrid caches, so a write has no peer L1 to invalidate and no
        /// invalidation is sent. Use it for a single-node service, a worker that owns its cache alone, or a test host.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A hybrid cache refuses to start until a backplane is chosen, so this call is the explicit opt-out. On a
        /// second host it is wrong: neither host would evict the L1 entries the other's writes made stale, and both
        /// would serve them until <see cref="HybridCacheOptions.DefaultLocalExpiration"/> runs out. Choose
        /// <c>UseRedisBackplane()</c> (<c>Headless.Caching.Redis</c>) or <c>UseMessagingBackplane()</c>
        /// (<c>Headless.Caching.Hybrid.Messaging</c>) as soon as the service scales out.
        /// </para>
        /// <para>Replaces any backplane registered earlier, and a later <c>Use*Backplane</c> call replaces this one.</para>
        /// </remarks>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseSingleInstanceBackplane()
        {
            setup.RegisterCrossCuttingExtension(static services =>
                services.Replace(
                    ServiceDescriptor.Singleton<IHybridCacheBackplane, SingleInstanceHybridCacheBackplane>()
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// What the startup check and the cache's own resolution tell a host that chose no backplane: every choice, and the
    /// package each one lives in.
    /// </summary>
    internal const string BackplaneRemedy =
        "Choose how hybrid caches on different hosts evict each other's L1 entries, inside AddHeadlessCaching(...): "
        + "setup.UseRedisBackplane() (package Headless.Caching.Redis) sends invalidations over Redis pub/sub; "
        + "setup.UseMessagingBackplane() (package Headless.Caching.Hybrid.Messaging) sends them over the Headless "
        + "messaging bus; setup.UseSingleInstanceBackplane() declares that exactly one host runs these caches, so "
        + "nothing is sent. A custom transport registers its own IHybridCacheBackplane.";

    internal static IServiceCollection AddNamedCacheCore(IServiceCollection services, string name)
    {
        services.AddCacheProvider();

        _AddBackplaneSeam(services);

        services.AddKeyedSingleton<ICache>(
            name,
            (provider, _) =>
                _CreateHybridCache(
                    provider,
                    provider.GetRequiredService<IOptionsMonitor<HybridCacheOptions>>().Get(name)
                )
        );

        // Startup advisor for THIS named instance: it inspects the named options (the default-path advisor in
        // _AddCacheCore only ever sees the default options). AddSingleton (not TryAddEnumerable) so one advisor
        // per named instance coexists with the default one: the factory overload never dedupes.
        services.AddStartupValidator(provider => new HybridCacheBestPracticesAdvisor(
            provider.GetRequiredService<IOptionsMonitor<HybridCacheOptions>>().Get(name),
            provider.GetRequiredService<ILogger<HybridCacheBestPracticesAdvisor>>(),
            instanceName: name
        ));

        return services;
    }

    private static IServiceCollection _AddCacheCore(IServiceCollection services)
    {
        // Defensive: this package RESOLVES TimeProvider, so it must also guarantee one exists. Without this,
        // installing the package standalone (no ServiceDefaults, no sibling that happens to register it) throws
        // 'No service for type TimeProvider' at resolve time.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingletonOptionValue<HybridCacheOptions>();
        services.TryAddSingleton<HybridCache>(provider =>
            _CreateHybridCache(provider, provider.GetRequiredService<HybridCacheOptions>())
        );
        services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));
        services.AddCacheProvider();

        services.TryAddSingleton<ICache>(provider => provider.GetRequiredService<HybridCache>());
        services.AddKeyedSingleton(CacheConstants.HybridCacheProvider, (x, _) => x.GetRequiredService<ICache>());

        _AddBackplaneSeam(services);

        // Startup advisor: logs warnings for questionable-but-valid configurations once at host
        // startup so operators notice misconfigurations before they see unexpected runtime behavior.
        services.AddStartupValidator<HybridCacheBestPracticesAdvisor>();

        return services;
    }

    private static void _AddBackplaneSeam(IServiceCollection services)
    {
        // One router serves every hybrid, default and named, by routing on CacheName, so repeated registrations
        // collapse to one listener.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHybridCacheInvalidationListener, HybridCacheInvalidationRouter>()
        );

        // The backplane is a host-wide choice made by a Use*Backplane call that may come before or after UseHybrid,
        // or by a custom registration outside AddHeadlessCaching, so it is checked when the host starts rather than
        // here. Without one, other hosts keep serving L1 entries this host's writes made stale.
        services.RequireRegisteredService<IHybridCacheBackplane>("Headless hybrid cache", BackplaneRemedy);
    }

    private static HybridCache _CreateHybridCache(IServiceProvider provider, HybridCacheOptions options)
    {
        var l1Cache = _ResolveTier<IInMemoryCache>(
            provider,
            options.LocalCacheName,
            nameof(HybridCacheOptions.LocalCacheName),
            "setup.AddNamed(name, i => i.UseInMemory(…))"
        );

        var l2Cache = _ResolveTier<IRemoteCache>(
            provider,
            options.RemoteCacheName,
            nameof(HybridCacheOptions.RemoteCacheName),
            "setup.AddNamed(name, i => i.UseRedis(…))"
        );

        return new HybridCache(
            l1Cache,
            l2Cache,
            // Also checked here: a container built without the host's startup checks would otherwise fail with a
            // bare missing-service error that names none of the choices.
            provider.GetService<IHybridCacheBackplane>()
                ?? throw new InvalidOperationException("HybridCache has no IHybridCacheBackplane. " + BackplaneRemedy),
            options,
            provider.GetService<ILogger<HybridCache>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ICacheFactoryLockProvider>(),
            provider.GetService<CacheInstrumentationConfig>(),
            provider.GetService<CacheEventsConfig>()
        );
    }

    private static TTier _ResolveTier<TTier>(
        IServiceProvider provider,
        string? name,
        string optionName,
        string registrationHint
    )
        where TTier : class, ICache
    {
        if (name is null)
        {
            return provider.GetService<TTier>()
                ?? throw new InvalidOperationException(
                    $"HybridCache requires a {typeof(TTier).Name} tier, but none is registered. "
                        + $"Register a remote tier (for example {registrationHint}), or set "
                        + $"{nameof(HybridCacheOptions)}.{optionName} to a named instance."
                );
        }

        var cache =
            provider.GetKeyedService<ICache>(name)
            ?? throw new InvalidOperationException(
                $"{nameof(HybridCacheOptions)}.{optionName} is set to '{name}', but no cache is registered "
                    + $"under that name. Register the named instance first, for example {registrationHint}."
            );

        return cache as TTier
            ?? throw new InvalidOperationException(
                $"{nameof(HybridCacheOptions)}.{optionName} is set to '{name}', but the cache registered under "
                    + $"that name ({cache.GetType().Name}) does not implement {typeof(TTier).Name}. Register the "
                    + $"named tier with {registrationHint}."
            );
    }
}

/// <summary>
/// Extension members for selecting the hybrid cache as a named cache instance on
/// <see cref="HeadlessCacheInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupHybridCacheNamed
{
    extension(HeadlessCacheInstanceBuilder instance)
    {
        /// <summary>
        /// Uses the hybrid cache for this named instance, resolvable as a keyed <see cref="ICache"/> service
        /// or through <see cref="ICacheProvider"/>. Tiers are resolved per
        /// <see cref="HybridCacheOptions.LocalCacheName"/> / <see cref="HybridCacheOptions.RemoteCacheName"/>
        /// (falling back to the default <see cref="IInMemoryCache"/> / <see cref="IRemoteCache"/> when unset).
        /// Named instances never touch the default (unkeyed) <see cref="ICache"/> nor the reserved role keys.
        /// </summary>
        /// <param name="setupAction">Configuration action for the instance's <see cref="HybridCacheOptions"/>.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseHybrid(Action<HybridCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction, name);
                services.Configure<HybridCacheOptions>(
                    name,
                    options =>
                    {
                        options.CacheName = name;

                        // Framework-owned routing identity: applied after setupAction (registered above), so a
                        // user attempt to override CacheName in setupAction never wins the routing decision.
                        options.InvalidationRoutingName = name;
                    }
                );
                SetupHybridCache.AddNamedCacheCore(services, name);
            });

            return instance;
        }

        /// <summary>
        /// Uses the hybrid cache for this named instance with service provider-aware configuration.
        /// See <see cref="UseHybrid(HeadlessCacheInstanceBuilder, Action{HybridCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseHybrid(Action<HybridCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction, name);
                services.Configure<HybridCacheOptions>(
                    name,
                    options =>
                    {
                        options.CacheName = name;

                        // Framework-owned routing identity: applied after setupAction (registered above), so a
                        // user attempt to override CacheName in setupAction never wins the routing decision.
                        options.InvalidationRoutingName = name;
                    }
                );
                SetupHybridCache.AddNamedCacheCore(services, name);
            });

            return instance;
        }

        /// <summary>
        /// Uses the hybrid cache for this named instance, binding the instance's
        /// <see cref="HybridCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseHybrid(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(configuration, name);
                services.Configure<HybridCacheOptions>(
                    name,
                    options =>
                    {
                        options.CacheName = name;

                        // Framework-owned routing identity: applied after setupAction (registered above), so a
                        // user attempt to override CacheName in setupAction never wins the routing decision.
                        options.InvalidationRoutingName = name;
                    }
                );
                SetupHybridCache.AddNamedCacheCore(services, name);
            });

            return instance;
        }
    }
}
