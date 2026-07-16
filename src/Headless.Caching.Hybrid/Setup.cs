// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
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
/// <item>Messaging: configure messaging with <c>AddHeadlessMessaging()</c></item>
/// </list>
/// <para><b>Example:</b></para>
/// <code>
/// services.AddHeadlessMessaging(...);
/// services.AddHeadlessCaching(setup =>
/// {
///     setup.AddMemoryTier();
///     setup.AddRedisTier(options => options.ConnectionMultiplexer = multiplexer);
///     setup.UseHybrid(options => options.DefaultLocalExpiration = TimeSpan.FromMinutes(5));
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
    }

    internal static IServiceCollection AddNamedCacheCore(IServiceCollection services, string name)
    {
        services.AddCacheProvider();

        // Auto-register the shared invalidation consumer so this named hybrid receives peer L1 invalidations
        // by default (idempotent + unconditional; one consumer routes to every hybrid by CacheName).
        HybridCacheInvalidationConsumerRegistration.TryAddInvalidationConsumer(services);

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
        // per named instance coexists with the default one instead of being deduped by implementation type.
        services.AddSingleton<IHostedService>(provider => new HybridCacheBestPracticesAdvisor(
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

        // Auto-register the invalidation consumer so peer L1 caches are evicted by default (idempotent +
        // unconditional, drained by messaging bootstrap in either registration order). Without this the
        // backplane was silently publish-only.
        HybridCacheInvalidationConsumerRegistration.TryAddInvalidationConsumer(services);

        // Startup advisor: logs warnings for questionable-but-valid configurations once at host
        // startup so operators notice misconfigurations before they see unexpected runtime behavior.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, HybridCacheBestPracticesAdvisor>(
                provider => new HybridCacheBestPracticesAdvisor(
                    provider.GetRequiredService<HybridCacheOptions>(),
                    provider.GetRequiredService<ILogger<HybridCacheBestPracticesAdvisor>>()
                )
            )
        );

        return services;
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
            provider.GetRequiredService<IBus>(),
            options,
            provider.GetService<ILogger<HybridCache>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ICacheFactoryLockProvider>(),
            provider.GetService<CacheInstrumentationConfig>()
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
