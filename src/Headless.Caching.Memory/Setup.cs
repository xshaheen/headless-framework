// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

[PublicAPI]
public static class InMemoryCacheSetup
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

        private IServiceCollection _AddCacheCore(bool isDefault)
        {
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
                // Distributed Cache adapter
                services.TryAddSingleton<IDistributedCache>(provider => new InMemoryCacheDistributedCacheAdapter(
                    provider.GetRequiredService<IInMemoryCache>()
                ));
                services.TryAddSingleton(typeof(IDistributedCache<>), typeof(DistributedCache<>));
                services.AddKeyedSingleton(
                    CacheConstants.DistributedCacheProvider,
                    x => x.GetRequiredService<IDistributedCache>()
                );
            }

            return services;
        }
    }

    private sealed class InMemoryCacheDistributedCacheAdapter(IInMemoryCache inMemoryCache) : IDistributedCache
    {
        public Task<bool> UpsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.UpsertAsync(key, value, expiration, cancellationToken);
        }

        public Task<int> UpsertAllAsync<T>(
            IDictionary<string, T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.UpsertAllAsync(value, expiration, cancellationToken);
        }

        public Task<bool> TryInsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.TryInsertAsync(key, value, expiration, cancellationToken);
        }

        public Task<bool> TryReplaceAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.TryReplaceAsync(key, value, expiration, cancellationToken);
        }

        public Task<bool> TryReplaceIfEqualAsync<T>(
            string key,
            T? expected,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
        }

        public Task<double> IncrementAsync(
            string key,
            double amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.IncrementAsync(key, amount, expiration, cancellationToken);
        }

        public Task<long> IncrementAsync(
            string key,
            long amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.IncrementAsync(key, amount, expiration, cancellationToken);
        }

        public Task<double> SetIfHigherAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.SetIfHigherAsync(key, value, expiration, cancellationToken);
        }

        public Task<long> SetIfHigherAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.SetIfHigherAsync(key, value, expiration, cancellationToken);
        }

        public Task<double> SetIfLowerAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.SetIfLowerAsync(key, value, expiration, cancellationToken);
        }

        public Task<long> SetIfLowerAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.SetIfLowerAsync(key, value, expiration, cancellationToken);
        }

        public Task<long> SetAddAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.SetAddAsync(key, value, expiration, cancellationToken);
        }

        public Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.GetAllAsync<T>(cacheKeys, cancellationToken);
        }

        public Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
            string prefix,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.GetByPrefixAsync<T>(prefix, cancellationToken);
        }

        public Task<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
            string prefix,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
        }

        public Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.GetAsync<T>(key, cancellationToken);
        }

        public Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
        {
            return inMemoryCache.GetCountAsync(prefix, cancellationToken);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.ExistsAsync(key, cancellationToken);
        }

        public Task<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.GetExpirationAsync(key, cancellationToken);
        }

        public Task<CacheValue<ICollection<T>>> GetSetAsync<T>(
            string key,
            int? pageIndex = null,
            int pageSize = 100,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.RemoveAsync(key, cancellationToken);
        }

        public Task<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.RemoveIfEqualAsync(key, expected, cancellationToken);
        }

        public Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.RemoveAllAsync(cacheKeys, cancellationToken);
        }

        public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            return inMemoryCache.RemoveByPrefixAsync(prefix, cancellationToken);
        }

        public Task<long> SetRemoveAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        )
        {
            return inMemoryCache.SetRemoveAsync(key, value, expiration, cancellationToken);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return inMemoryCache.FlushAsync(cancellationToken);
        }
    }
}
