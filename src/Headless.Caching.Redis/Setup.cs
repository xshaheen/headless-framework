// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Scripts;
using Headless.Checks;
using Headless.Hosting;
using Headless.Redis;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

[PublicAPI]
public static class SetupRedisCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Uses the Redis cache as the default (unkeyed) <see cref="ICache"/> and <see cref="IRemoteCache"/>.
        /// Also registers the <see cref="CacheConstants.RemoteCacheProvider"/> role key. The cache's Lua scripts load
        /// in a warm-up after the host starts, so neither the start nor the first cache call waits for them.
        /// <see cref="RedisCacheOptions.ConnectionMultiplexer"/> is required.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="RedisCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseRedis(Action<RedisCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(
                CacheConstants.RemoteCacheProvider,
                services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses the Redis cache as the default (unkeyed) <see cref="ICache"/> with service provider-aware
        /// configuration. See <see cref="UseRedis(HeadlessCachingSetupBuilder, Action{RedisCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseRedis(Action<RedisCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(
                CacheConstants.RemoteCacheProvider,
                services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses the Redis cache as the default (unkeyed) <see cref="ICache"/>, binding
        /// <see cref="RedisCacheOptions"/> from configuration. The multiplexer must still be supplied
        /// (for example through an additional <c>Configure</c> call), as it cannot be bound from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseRedis(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(
                CacheConstants.RemoteCacheProvider,
                services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(configuration);
                    _AddCacheCore(services, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds the Redis cache as the remote tier of a default hybrid cache: registers
        /// <see cref="IRemoteCache"/> and the <see cref="CacheConstants.RemoteCacheProvider"/> role key
        /// without touching the default (unkeyed) <see cref="ICache"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="RedisCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddRedisTier(Action<RedisCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds the Redis cache as the remote tier of a default hybrid cache with service provider-aware
        /// configuration. See <see cref="AddRedisTier(HeadlessCachingSetupBuilder, Action{RedisCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddRedisTier(Action<RedisCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds the Redis cache as the remote tier of a default hybrid cache, binding
        /// <see cref="RedisCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddRedisTier(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(configuration);
                    _AddCacheCore(services, isDefault: false);
                }
            );

            return setup;
        }
    }

    internal static IServiceCollection AddNamedCacheCore(
        IServiceCollection services,
        HeadlessCacheInstanceBuilder instance
    )
    {
        _AddSerializerCore(services);
        services.AddCacheProvider();

        var name = instance.Name;

        // Surface the registered instance name on the headless.cache.name telemetry dimension (unless the user
        // set one explicitly). Runs after the user's configure action.
        services.PostConfigure<RedisCacheOptions>(name, options => options.CacheName ??= name);

        var loaderKey = RedisCacheServiceKeys.NamedScriptsLoader(name);

        if (instance.SerializerFactory is { } serializerFactory)
        {
            services.AddKeyedSingleton<ISerializer>(name, (sp, _) => serializerFactory(sp));
        }

        services.TryAddKeyedSingleton(
            loaderKey,
            (sp, _) =>
                new HeadlessRedisScriptsLoader(
                    sp.GetRequiredService<IOptionsMonitor<RedisCacheOptions>>().Get(name).ConnectionMultiplexer,
                    sp.GetService<TimeProvider>(),
                    sp.GetService<ILogger<HeadlessRedisScriptsLoader>>()
                )
        );
        _AddScriptsWarmup(services, loaderKey);

        services.AddHeadlessHealthCheck(
            "cache-redis-" + name,
            HeadlessHealthCheckResource.Shared(provider =>
                provider
                    .GetRequiredService<IOptionsMonitor<RedisCacheOptions>>()
                    .Get(name)
                    .ConnectionMultiplexer.GetEndpointsKey()
            ),
            async (provider, cancellationToken) =>
                await provider
                    .GetRequiredService<IOptionsMonitor<RedisCacheOptions>>()
                    .Get(name)
                    .ConnectionMultiplexer.ProbeEndpointsAsync(cancellationToken)
                    .ConfigureAwait(false),
            HeadlessHealthCheckTags.Redis
        );

        services.AddKeyedSingleton<ICache>(
            name,
            (sp, _) =>
                new RedisCache(
                    sp.GetKeyedService<ISerializer>(name) ?? sp.GetRequiredService<ISerializer>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<IOptionsMonitor<RedisCacheOptions>>().Get(name),
                    sp.GetRequiredKeyedService<HeadlessRedisScriptsLoader>(loaderKey),
                    sp.GetService<ILogger<RedisCache>>(),
                    sp.GetService<ICacheFactoryLockProvider>(),
                    sp.GetService<CacheInstrumentationConfig>(),
                    sp.GetService<CacheEventsConfig>()
                )
        );

        return services;
    }

    // One warm-up loads the scripts onto every registered cache's Redis; each loader is listed once.
    private static void _AddScriptsWarmup(IServiceCollection services, string loaderKey)
    {
        var listed = services.Any(d =>
            d.ImplementationInstance is RedisCacheScriptsLoaderKey existing
            && string.Equals(existing.Key, loaderKey, StringComparison.Ordinal)
        );

        if (!listed)
        {
            services.AddSingleton(new RedisCacheScriptsLoaderKey(loaderKey));
        }

        services.AddWarmup<RedisCacheScriptsWarmup>();
    }

    private static IServiceCollection _AddSerializerCore(IServiceCollection services)
    {
        // Defensive: this package RESOLVES TimeProvider, so it must also guarantee one exists. Without this,
        // installing the package standalone (no ServiceDefaults, no sibling that happens to register it) throws
        // 'No service for type TimeProvider' at resolve time.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
        services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
            sp.GetRequiredService<IJsonOptionsProvider>()
        ));
        services.TryAddSingleton<ISerializer>(sp => sp.GetRequiredService<IJsonSerializer>());

        return services;
    }

    private static IServiceCollection _AddCacheCore(IServiceCollection services, bool isDefault)
    {
        _AddSerializerCore(services);
        services.AddCacheProvider();

        services.AddSingletonOptionValue<RedisCacheOptions>();
        services.TryAddKeyedSingleton(
            RedisCacheServiceKeys.ScriptsLoader,
            (sp, _) =>
                new HeadlessRedisScriptsLoader(
                    sp.GetRequiredService<RedisCacheOptions>().ConnectionMultiplexer,
                    sp.GetService<TimeProvider>(),
                    sp.GetService<ILogger<HeadlessRedisScriptsLoader>>()
                )
        );
        _AddScriptsWarmup(services, RedisCacheServiceKeys.ScriptsLoader);
        services.TryAddSingleton<IRemoteCache, RedisCache>();
        services.AddHeadlessHealthCheck(
            "cache-redis",
            HeadlessHealthCheckResource.Shared(static provider =>
                provider.GetRequiredService<RedisCacheOptions>().ConnectionMultiplexer.GetEndpointsKey()
            ),
            static async (provider, cancellationToken) =>
                await provider
                    .GetRequiredService<RedisCacheOptions>()
                    .ConnectionMultiplexer.ProbeEndpointsAsync(cancellationToken)
                    .ConfigureAwait(false),
            HeadlessHealthCheckTags.Redis
        );
        services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            services.AddKeyedSingleton<ICache>(
                CacheConstants.RemoteCacheProvider,
                provider => provider.GetRequiredService<IRemoteCache>()
            );
        }
        else
        {
            services.TryAddSingleton<ICache>(provider => provider.GetRequiredService<IRemoteCache>());
            services.AddKeyedSingleton(CacheConstants.RemoteCacheProvider, x => x.GetRequiredService<ICache>());
        }

        return services;
    }
}

/// <summary>
/// Extension members for selecting the Redis cache as a named cache instance on
/// <see cref="HeadlessCacheInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupRedisCacheNamed
{
    extension(HeadlessCacheInstanceBuilder instance)
    {
        /// <summary>
        /// Uses the Redis cache for this named instance, resolvable as a keyed <see cref="ICache"/> service
        /// or through <see cref="ICacheProvider"/>. The instance owns its own scripts loader bound to its own
        /// <see cref="RedisCacheOptions.ConnectionMultiplexer"/> and key prefix. Named instances never touch
        /// the default (unkeyed) <see cref="ICache"/> nor the reserved role keys.
        /// </summary>
        /// <param name="setupAction">Configuration action for the instance's <see cref="RedisCacheOptions"/>.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseRedis(Action<RedisCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction, name);
                SetupRedisCache.AddNamedCacheCore(services, instance);
            });

            return instance;
        }

        /// <summary>
        /// Uses the Redis cache for this named instance with service provider-aware configuration.
        /// See <see cref="UseRedis(HeadlessCacheInstanceBuilder, Action{RedisCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseRedis(Action<RedisCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction, name);
                SetupRedisCache.AddNamedCacheCore(services, instance);
            });

            return instance;
        }

        /// <summary>
        /// Uses the Redis cache for this named instance, binding the instance's
        /// <see cref="RedisCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseRedis(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(configuration, name);
                SetupRedisCache.AddNamedCacheCore(services, instance);
            });

            return instance;
        }
    }
}
