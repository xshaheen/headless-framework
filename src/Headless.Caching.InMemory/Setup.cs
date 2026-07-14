// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

[PublicAPI]
public static class SetupInMemoryCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Uses the in-memory cache as the default (unkeyed) <see cref="ICache"/>. Also registers the
        /// <see cref="CacheConstants.MemoryCacheProvider"/> role key.
        /// </summary>
        /// <param name="setupAction">Optional configuration for <see cref="InMemoryCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseInMemory(Action<InMemoryCacheOptions>? setupAction = null)
        {
            setup.RegisterDefaultProvider(
                CacheConstants.MemoryCacheProvider,
                services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses the in-memory cache as the default (unkeyed) <see cref="ICache"/> with service
        /// provider-aware configuration. See <see cref="UseInMemory(HeadlessCachingSetupBuilder, Action{InMemoryCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseInMemory(Action<InMemoryCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(
                CacheConstants.MemoryCacheProvider,
                services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses the in-memory cache as the default (unkeyed) <see cref="ICache"/>, binding
        /// <see cref="InMemoryCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseInMemory(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(
                CacheConstants.MemoryCacheProvider,
                services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration);
                    _AddCacheCore(services, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds the in-memory cache as the local tier of a default hybrid cache: registers
        /// <see cref="IInMemoryCache"/> and the <see cref="CacheConstants.MemoryCacheProvider"/> role key
        /// without touching the default (unkeyed) <see cref="ICache"/>.
        /// </summary>
        /// <param name="setupAction">Optional configuration for <see cref="InMemoryCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddMemoryTier(Action<InMemoryCacheOptions>? setupAction = null)
        {
            setup.RegisterTierProvider(
                CacheConstants.MemoryCacheProvider,
                services =>
                {
                    if (setupAction is null)
                    {
                        services.AddOptions<InMemoryCacheOptions, InMemoryCacheOptionsValidator>();
                    }
                    else
                    {
                        services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    }

                    _AddCacheCore(services, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds the in-memory cache as the local tier of a default hybrid cache with service
        /// provider-aware configuration. See <see cref="AddMemoryTier(HeadlessCachingSetupBuilder, Action{InMemoryCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddMemoryTier(Action<InMemoryCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterTierProvider(
                CacheConstants.MemoryCacheProvider,
                services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    _AddCacheCore(services, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds the in-memory cache as the local tier of a default hybrid cache, binding
        /// <see cref="InMemoryCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddMemoryTier(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterTierProvider(
                CacheConstants.MemoryCacheProvider,
                services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration);
                    _AddCacheCore(services, isDefault: false);
                }
            );

            return setup;
        }
    }

    internal static IServiceCollection AddNamedCacheCore(IServiceCollection services, string name)
    {
        services.AddCacheProvider();

        services.AddKeyedSingleton<ICache>(
            name,
            (provider, _) =>
                new InMemoryCache(
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<IOptionsMonitor<InMemoryCacheOptions>>().Get(name),
                    provider.GetService<ILogger<InMemoryCache>>(),
                    provider.GetService<ICacheFactoryLockProvider>()
                )
        );

        return services;
    }

    private static IServiceCollection _AddCacheCore(IServiceCollection services, bool isDefault)
    {
        // Defensive: this package RESOLVES TimeProvider, so it must also guarantee one exists. Without this,
        // installing the package standalone (no ServiceDefaults, no sibling that happens to register it) throws
        // 'No service for type TimeProvider' at resolve time.
        services.TryAddSingleton(TimeProvider.System);
        services.AddCacheProvider();
        services.AddSingletonOptionValue<InMemoryCacheOptions>();
        services.TryAddSingleton<IInMemoryCache, InMemoryCache>();
        services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            services.AddKeyedSingleton<ICache>(
                CacheConstants.MemoryCacheProvider,
                provider => provider.GetRequiredService<IInMemoryCache>()
            );
        }
        else
        {
            services.TryAddSingleton<ICache>(provider => provider.GetRequiredService<IInMemoryCache>());
            services.AddKeyedSingleton(CacheConstants.MemoryCacheProvider, x => x.GetRequiredService<ICache>());
        }

        return services;
    }
}

/// <summary>
/// Extension members for selecting the in-memory cache as a named cache instance on
/// <see cref="HeadlessCacheInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupInMemoryCacheNamed
{
    extension(HeadlessCacheInstanceBuilder instance)
    {
        /// <summary>
        /// Uses the in-memory cache for this named instance, resolvable as a keyed <see cref="ICache"/>
        /// service or through <see cref="ICacheProvider"/>. Named instances never touch the default
        /// (unkeyed) <see cref="ICache"/> nor the reserved role keys.
        /// </summary>
        /// <param name="setupAction">Configuration action for the instance's <see cref="InMemoryCacheOptions"/>.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseInMemory(Action<InMemoryCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction, name);
                SetupInMemoryCache.AddNamedCacheCore(services, name);
            });

            return instance;
        }

        /// <summary>
        /// Uses the in-memory cache for this named instance with service provider-aware configuration.
        /// See <see cref="UseInMemory(HeadlessCacheInstanceBuilder, Action{InMemoryCacheOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseInMemory(Action<InMemoryCacheOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction, name);
                SetupInMemoryCache.AddNamedCacheCore(services, name);
            });

            return instance;
        }

        /// <summary>
        /// Uses the in-memory cache for this named instance, binding the instance's
        /// <see cref="InMemoryCacheOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder UseInMemory(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration, name);
                SetupInMemoryCache.AddNamedCacheCore(services, name);
            });

            return instance;
        }
    }
}
