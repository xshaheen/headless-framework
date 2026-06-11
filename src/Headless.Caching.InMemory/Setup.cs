// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Caching;

[PublicAPI]
public static class SetupInMemoryCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Uses the in-memory cache as the default (unkeyed) <see cref="ICache"/>. Also registers the
        /// <see cref="CacheConstants.MemoryCacheProvider"/> role key and an <see cref="IRemoteCache"/>
        /// adapter over the same store for single-node hosts.
        /// </summary>
        /// <param name="setupAction">Optional configuration for <see cref="InMemoryCacheOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseInMemory(Action<InMemoryCacheOptions>? setupAction = null)
        {
            setup.RegisterDefaultProvider(
                CacheConstants.MemoryCacheProvider,
                new InMemoryCacheOptionsExtension(services =>
                {
                    if (setupAction is null)
                    {
                        services.AddOptions<InMemoryCacheOptions, InMemoryCacheOptionsValidator>();
                    }
                    else
                    {
                        services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    }

                    services._AddCacheCore(isDefault: true);
                })
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
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    services._AddCacheCore(isDefault: true);
                })
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
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration);
                    services._AddCacheCore(isDefault: true);
                })
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
                new InMemoryCacheOptionsExtension(services =>
                {
                    if (setupAction is null)
                    {
                        services.AddOptions<InMemoryCacheOptions, InMemoryCacheOptionsValidator>();
                    }
                    else
                    {
                        services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    }

                    services._AddCacheCore(isDefault: false);
                })
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
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
                    services._AddCacheCore(isDefault: false);
                })
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
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration);
                    services._AddCacheCore(isDefault: false);
                })
            );

            return setup;
        }
    }

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

            instance.RegisterProvider(
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction, name);
                    services._AddNamedCacheCore(name);
                })
            );

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

            instance.RegisterProvider(
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction, name);
                    services._AddNamedCacheCore(name);
                })
            );

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

            instance.RegisterProvider(
                new InMemoryCacheOptionsExtension(services =>
                {
                    services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration, name);
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

        private IServiceCollection _AddCacheCore(bool isDefault)
        {
            services.AddCacheProvider();
            services.AddSingletonOptionValue<InMemoryCacheOptions>();
            services.TryAddSingleton<IInMemoryCache, InMemoryCache>();
            services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));
            services.TryAddSingleton(typeof(IInMemoryCache<>), typeof(InMemoryCache<>));

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
                // Remote cache adapter
                services.TryAddSingleton<IRemoteCache>(provider => new InMemoryRemoteCacheAdapter(
                    provider.GetRequiredService<IInMemoryCache>()
                ));
                services.TryAddSingleton(typeof(IRemoteCache<>), typeof(RemoteCache<>));
                services.AddKeyedSingleton(
                    CacheConstants.RemoteCacheProvider,
                    x => x.GetRequiredService<IRemoteCache>()
                );
            }

            return services;
        }
    }

    private sealed class InMemoryCacheOptionsExtension(Action<IServiceCollection> apply)
        : ICacheProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) => apply(services);
    }

    private sealed class InMemoryRemoteCacheAdapter(IInMemoryCache inMemoryCache) : IRemoteCache
    {
        public CacheEntryOptions? DefaultEntryOptions => inMemoryCache.DefaultEntryOptions;

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T?>> factory,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.GetOrAddAsync(key, factory, options, cancellationToken);

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.GetOrAddAsync(key, factory, options, cancellationToken);

        public ValueTask<bool> UpsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.UpsertAsync(key, value, expiration, cancellationToken);

        public ValueTask<bool> UpsertEntryAsync<T>(
            string key,
            T? value,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.UpsertEntryAsync(key, value, options, cancellationToken);

        public ValueTask<int> UpsertAllAsync<T>(
            IDictionary<string, T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.UpsertAllAsync(value, expiration, cancellationToken);

        public ValueTask<bool> TryInsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.TryInsertAsync(key, value, expiration, cancellationToken);

        public ValueTask<bool> TryReplaceAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.TryReplaceAsync(key, value, expiration, cancellationToken);

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(
            string key,
            T? expected,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);

        public ValueTask<double> IncrementAsync(
            string key,
            double amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.IncrementAsync(key, amount, expiration, cancellationToken);

        public ValueTask<long> IncrementAsync(
            string key,
            long amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.IncrementAsync(key, amount, expiration, cancellationToken);

        public ValueTask<double> SetIfHigherAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.SetIfHigherAsync(key, value, expiration, cancellationToken);

        public ValueTask<long> SetIfHigherAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.SetIfHigherAsync(key, value, expiration, cancellationToken);

        public ValueTask<double> SetIfLowerAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.SetIfLowerAsync(key, value, expiration, cancellationToken);

        public ValueTask<long> SetIfLowerAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.SetIfLowerAsync(key, value, expiration, cancellationToken);

        public ValueTask<long> SetAddAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.SetAddAsync(key, value, expiration, cancellationToken);

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.GetAllAsync<T>(cacheKeys, cancellationToken);

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
            string prefix,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.GetByPrefixAsync<T>(prefix, cancellationToken);

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
            string prefix,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.GetAllKeysByPrefixAsync(prefix, cancellationToken);

        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            inMemoryCache.GetAsync<T>(key, cancellationToken);

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
            inMemoryCache.GetCountAsync(prefix, cancellationToken);

        public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            inMemoryCache.ExistsAsync(key, cancellationToken);

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
            inMemoryCache.GetExpirationAsync(key, cancellationToken);

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
            string key,
            int? pageIndex = null,
            int pageSize = 100,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);

        public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            inMemoryCache.RemoveAsync(key, cancellationToken);

        public ValueTask<bool> RemoveIfEqualAsync<T>(
            string key,
            T? expected,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.RemoveIfEqualAsync(key, expected, cancellationToken);

        public ValueTask<int> RemoveAllAsync(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.RemoveAllAsync(cacheKeys, cancellationToken);

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            inMemoryCache.RemoveByPrefixAsync(prefix, cancellationToken);

        public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            inMemoryCache.RemoveByTagAsync(tag, cancellationToken);

        public ValueTask<long> SetRemoveAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => inMemoryCache.SetRemoveAsync(key, value, expiration, cancellationToken);

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            inMemoryCache.FlushAsync(cancellationToken);
    }
}
