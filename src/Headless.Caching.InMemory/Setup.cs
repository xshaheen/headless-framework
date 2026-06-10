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
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInMemoryCache(
            Action<InMemoryCacheOptions, IServiceProvider> setupAction,
            bool isDefault = true
        )
        {
            services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);

            return services._AddCacheCore(isDefault);
        }

        public IServiceCollection AddInMemoryCache(IConfiguration configuration, bool isDefault = true)
        {
            services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(configuration);

            return services._AddCacheCore(isDefault);
        }

        public IServiceCollection AddInMemoryCache(
            Action<InMemoryCacheOptions>? setupAction = null,
            bool isDefault = true
        )
        {
            if (setupAction is null)
            {
                services.AddOptions<InMemoryCacheOptions, InMemoryCacheOptionsValidator>();
            }
            else
            {
                services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
            }

            return services._AddCacheCore(isDefault);
        }

        /// <summary>
        /// Adds an independently-configured named in-memory cache instance, resolvable as a keyed
        /// <see cref="ICache"/> service or through <see cref="ICacheProvider"/>. Named instances never touch
        /// the default (unkeyed) <see cref="ICache"/> nor the reserved role keys.
        /// </summary>
        /// <param name="name">The cache instance name. Must be non-empty and not a reserved role key.</param>
        /// <param name="setupAction">Configuration action for the instance's <see cref="InMemoryCacheOptions"/>.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddInMemoryCache(string name, Action<InMemoryCacheOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            return services._AddNamedCache(name, (options, _) => setupAction(options));
        }

        /// <summary>
        /// Adds an independently-configured named in-memory cache instance with service provider-aware
        /// configuration. See <c>AddInMemoryCache(string, Action&lt;InMemoryCacheOptions&gt;)</c>.
        /// </summary>
        /// <param name="name">The cache instance name. Must be non-empty and not a reserved role key.</param>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddInMemoryCache(
            string name,
            Action<InMemoryCacheOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            return services._AddNamedCache(name, setupAction);
        }

        private IServiceCollection _AddNamedCache(
            string name,
            Action<InMemoryCacheOptions, IServiceProvider> setupAction
        )
        {
            _EnsureValidInstanceName(name);

            services.Configure<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction, name);
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

    private static void _EnsureValidInstanceName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        if (CacheConstants.IsReservedProviderKey(name))
        {
            throw new ArgumentException(
                $"The cache name '{name}' is reserved for the role-keyed registrations "
                    + $"('{CacheConstants.MemoryCacheProvider}', '{CacheConstants.RemoteCacheProvider}', "
                    + $"'{CacheConstants.HybridCacheProvider}'). Pick a different instance name.",
                nameof(name)
            );
        }
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
