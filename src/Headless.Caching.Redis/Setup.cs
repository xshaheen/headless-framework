// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization;
using Headless.Redis;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Caching;

[PublicAPI]
public static class SetupRedisCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Uses the Redis cache as the default (unkeyed) <see cref="ICache"/> and <see cref="IRemoteCache"/>.
        /// Also registers the <see cref="CacheConstants.RemoteCacheProvider"/> role key and the Lua scripts
        /// preload initializer. <see cref="RedisCacheOptions.ConnectionMultiplexer"/> is required.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="RedisCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseRedis(Action<RedisCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(
                CacheConstants.RemoteCacheProvider,
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    services._AddCacheCore(isDefault: true);
                })
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
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    services._AddCacheCore(isDefault: true);
                })
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
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(configuration);
                    services._AddCacheCore(isDefault: true);
                })
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
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    services._AddCacheCore(isDefault: false);
                })
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
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
                    services._AddCacheCore(isDefault: false);
                })
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
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(configuration);
                    services._AddCacheCore(isDefault: false);
                })
            );

            return setup;
        }
    }

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

            instance.RegisterProvider(
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction, name);
                    services._AddNamedCacheCore(name);
                })
            );

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

            instance.RegisterProvider(
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction, name);
                    services._AddNamedCacheCore(name);
                })
            );

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

            instance.RegisterProvider(
                new DelegatingCacheProviderOptionsExtension(services =>
                {
                    services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(configuration, name);
                    services._AddNamedCacheCore(name);
                })
            );

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddNamedCacheCore(string name)
        {
            services._AddSerializerCore();
            services.AddCacheProvider();

            var loaderKey = RedisCacheServiceKeys.NamedScriptsLoader(name);
            var initializerKey = RedisCacheServiceKeys.NamedScriptsInitializer(name);

            services.TryAddKeyedSingleton(
                loaderKey,
                (sp, _) =>
                    new HeadlessRedisScriptsLoader(
                        sp.GetRequiredService<IOptionsMonitor<RedisCacheOptions>>().Get(name).ConnectionMultiplexer,
                        sp.GetService<TimeProvider>(),
                        sp.GetService<ILogger<HeadlessRedisScriptsLoader>>()
                    )
            );

            // Per-instance scripts preload: the same shared singleton is forwarded as IInitializer and
            // IHostedService (mirrors AddInitializerHostedService, which cannot be reused because each named
            // instance needs its own initializer bound to its own loader).
            services.TryAddKeyedSingleton(
                initializerKey,
                (sp, _) =>
                    new RedisCacheScriptsInitializer(sp.GetRequiredKeyedService<HeadlessRedisScriptsLoader>(loaderKey))
            );
            services.AddSingleton<IInitializer>(sp =>
                sp.GetRequiredKeyedService<RedisCacheScriptsInitializer>(initializerKey)
            );
            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<RedisCacheScriptsInitializer>(initializerKey)
            );

            services.AddKeyedSingleton<ICache>(
                name,
                (sp, _) =>
                    new RedisCache(
                        sp.GetRequiredService<ISerializer>(),
                        sp.GetRequiredService<TimeProvider>(),
                        sp.GetRequiredService<IOptionsMonitor<RedisCacheOptions>>().Get(name),
                        sp.GetRequiredKeyedService<HeadlessRedisScriptsLoader>(loaderKey),
                        sp.GetService<ILogger<RedisCache>>(),
                        sp.GetService<ICacheFactoryLockProvider>()
                    )
            );

            return services;
        }

        private IServiceCollection _AddSerializerCore()
        {
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
            services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
                sp.GetRequiredService<IJsonOptionsProvider>()
            ));
            services.TryAddSingleton<ISerializer>(sp => sp.GetRequiredService<IJsonSerializer>());

            return services;
        }

        private IServiceCollection _AddCacheCore(bool isDefault)
        {
            services._AddSerializerCore();
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
            services.AddInitializerHostedService<RedisCacheScriptsInitializer>();
            services.TryAddSingleton<IRemoteCache, RedisCache>();
            services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));
            services.TryAddSingleton(typeof(IRemoteCache<>), typeof(RemoteCache<>));

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
}
