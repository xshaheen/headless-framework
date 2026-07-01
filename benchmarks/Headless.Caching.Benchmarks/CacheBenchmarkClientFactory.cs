// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Caching;
using Headless.Caching.Benchmarks.Adapters;
using Headless.Caching.Benchmarks.Infrastructure;
using Headless.Redis;
using Headless.Serializer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Headless.Caching.Benchmarks;

[SuppressMessage(
    "Performance",
    "CA1859:Use concrete types when possible for improved performance",
    Justification = "Factory-owned caches are disposed through the returned benchmark client."
)]
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Factory-owned caches are disposed through the returned benchmark client."
)]
internal static class CacheBenchmarkClientFactory
{
    private static readonly CacheBenchmarkClientDescriptor[] _BaseDescriptors =
    [
        new(
            BenchmarkProviderIds.HeadlessInMemory,
            "Headless ICache - InMemory",
            CacheBenchmarkBackend.InProcess,
            CacheBenchmarkFeatures.GetOrAdd | CacheBenchmarkFeatures.FailSafe | CacheBenchmarkFeatures.EagerRefresh
        ),
        new(
            BenchmarkProviderIds.HeadlessHybrid,
            "Headless ICache - Hybrid InMemory/L2",
            CacheBenchmarkBackend.InProcess,
            CacheBenchmarkFeatures.GetOrAdd
                | CacheBenchmarkFeatures.Hybrid
                | CacheBenchmarkFeatures.FailSafe
                | CacheBenchmarkFeatures.EagerRefresh
        ),
        new(
            BenchmarkProviderIds.FusionMemory,
            "FusionCache - Memory",
            CacheBenchmarkBackend.InProcess,
            CacheBenchmarkFeatures.GetOrAdd | CacheBenchmarkFeatures.FailSafe | CacheBenchmarkFeatures.EagerRefresh
        ),
        new(
            BenchmarkProviderIds.FoundatioMemory,
            "Foundatio - InMemory",
            CacheBenchmarkBackend.InProcess,
            CacheBenchmarkFeatures.None
        ),
        new(
            BenchmarkProviderIds.MicrosoftMemory,
            "Microsoft IMemoryCache",
            CacheBenchmarkBackend.InProcess,
            CacheBenchmarkFeatures.None
        ),
        new(
            BenchmarkProviderIds.MicrosoftMemoryDistributed,
            "Microsoft IDistributedCache - Memory",
            CacheBenchmarkBackend.InProcess,
            CacheBenchmarkFeatures.None
        ),
    ];

    private static readonly CacheBenchmarkClientDescriptor[] _RedisDescriptors =
    [
        new(
            BenchmarkProviderIds.HeadlessRedis,
            "Headless ICache - Redis",
            CacheBenchmarkBackend.Redis,
            CacheBenchmarkFeatures.GetOrAdd | CacheBenchmarkFeatures.FailSafe | CacheBenchmarkFeatures.EagerRefresh
        ),
        new(
            BenchmarkProviderIds.FusionRedis,
            "FusionCache - Redis L2",
            CacheBenchmarkBackend.Redis,
            CacheBenchmarkFeatures.GetOrAdd
                | CacheBenchmarkFeatures.Hybrid
                | CacheBenchmarkFeatures.FailSafe
                | CacheBenchmarkFeatures.EagerRefresh
        ),
        new(
            BenchmarkProviderIds.FusionRedisDistributed,
            "FusionCache - Redis Distributed Only",
            CacheBenchmarkBackend.Redis,
            CacheBenchmarkFeatures.GetOrAdd | CacheBenchmarkFeatures.FailSafe | CacheBenchmarkFeatures.EagerRefresh
        ),
        new(
            BenchmarkProviderIds.FoundatioRedis,
            "Foundatio - Redis",
            CacheBenchmarkBackend.Redis,
            CacheBenchmarkFeatures.None
        ),
        new(
            BenchmarkProviderIds.MicrosoftRedisDistributed,
            "Microsoft IDistributedCache - Redis",
            CacheBenchmarkBackend.Redis,
            CacheBenchmarkFeatures.None
        ),
    ];

    public static IReadOnlyList<CacheBenchmarkClientDescriptor> GetDescriptors(bool includeRedis = false) =>
        includeRedis ? [.. _BaseDescriptors, .. _RedisDescriptors] : _BaseDescriptors;

    public static bool IsRedisAvailable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEADLESS_CACHE_BENCHMARK_REDIS"));

    public static ICacheBenchmarkClient Create(string providerId, string keyPrefix)
    {
        return providerId switch
        {
            BenchmarkProviderIds.HeadlessInMemory => _CreateHeadlessInMemory(keyPrefix),
            BenchmarkProviderIds.HeadlessRedis => _CreateHeadlessRedis(keyPrefix),
            BenchmarkProviderIds.HeadlessHybrid => _CreateHeadlessHybrid(keyPrefix),
            BenchmarkProviderIds.FusionMemory => _CreateFusionMemory(keyPrefix),
            BenchmarkProviderIds.FusionRedis => _CreateFusionRedis(keyPrefix),
            BenchmarkProviderIds.FusionRedisDistributed => _CreateFusionRedisDistributed(keyPrefix),
            BenchmarkProviderIds.FoundatioMemory => _CreateFoundatioMemory(keyPrefix),
            BenchmarkProviderIds.FoundatioRedis => _CreateFoundatioRedis(keyPrefix),
            BenchmarkProviderIds.MicrosoftMemory => _CreateMicrosoftMemory(keyPrefix),
            BenchmarkProviderIds.MicrosoftMemoryDistributed => _CreateMicrosoftMemoryDistributed(keyPrefix),
            BenchmarkProviderIds.MicrosoftRedisDistributed => _CreateMicrosoftRedisDistributed(keyPrefix),
            _ => throw new ArgumentOutOfRangeException(
                nameof(providerId),
                providerId,
                "Unknown cache benchmark provider."
            ),
        };
    }

    public static IEnumerable<string> MemoryOnlyProviderIds() =>
        [
            BenchmarkProviderIds.HeadlessInMemory,
            BenchmarkProviderIds.FusionMemory,
            BenchmarkProviderIds.FoundatioMemory,
            BenchmarkProviderIds.MicrosoftMemory,
        ];

    public static IEnumerable<string> DistributedOnlyProviderIds(bool includeRedis = false)
    {
        if (!includeRedis)
        {
            return [BenchmarkProviderIds.MicrosoftMemoryDistributed];
        }

        return
        [
            BenchmarkProviderIds.HeadlessRedis,
            BenchmarkProviderIds.FusionRedisDistributed,
            BenchmarkProviderIds.FoundatioRedis,
            BenchmarkProviderIds.MicrosoftRedisDistributed,
        ];
    }

    private static ICacheBenchmarkClient _CreateHeadlessInMemory(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.HeadlessInMemory);
        var cache = new InMemoryCache(
            TimeProvider.System,
            new InMemoryCacheOptions
            {
                KeyPrefix = keyPrefix,
                DefaultEntryOptions = _CreateHeadlessOptions(TimeSpan.FromMinutes(1)),
            }
        );

        return new HeadlessCacheBenchmarkClient(descriptor, cache);
    }

    private static ICacheBenchmarkClient _CreateHeadlessRedis(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.HeadlessRedis);
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
        var multiplexer = ConnectionMultiplexer.Connect(_GetRequiredRedisConnectionString());
#pragma warning restore MA0045
        var scriptsLoader = new HeadlessRedisScriptsLoader(multiplexer);
        var cache = new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
            new RedisCacheOptions
            {
                ConnectionMultiplexer = multiplexer,
                KeyPrefix = keyPrefix,
                DefaultEntryOptions = _CreateHeadlessOptions(TimeSpan.FromMinutes(1)),
            },
            scriptsLoader
        );

        return new HeadlessCacheBenchmarkClient(descriptor, cache, scriptsLoader, multiplexer);
    }

    private static ICacheBenchmarkClient _CreateHeadlessHybrid(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.HeadlessHybrid);
        var local = new InMemoryCache(
            TimeProvider.System,
            new InMemoryCacheOptions
            {
                KeyPrefix = keyPrefix + "l1:",
                DefaultEntryOptions = _CreateHeadlessOptions(TimeSpan.FromMinutes(1)),
            }
        );
        var remote = new InMemoryCache(
            TimeProvider.System,
            new InMemoryCacheOptions
            {
                KeyPrefix = keyPrefix + "l2:",
                DefaultEntryOptions = _CreateHeadlessOptions(TimeSpan.FromMinutes(1)),
            }
        );
        var hybrid = new HybridCache(
            local,
            new BenchmarkRemoteCacheAdapter(remote),
            new NoOpBus(),
            new HybridCacheOptions
            {
                KeyPrefix = keyPrefix,
                DefaultLocalExpiration = TimeSpan.FromMinutes(1),
                DefaultEntryOptions = _CreateHeadlessOptions(TimeSpan.FromMinutes(1)),
            },
            timeProvider: TimeProvider.System
        );

        return new HeadlessCacheBenchmarkClient(descriptor, hybrid);
    }

    private static ICacheBenchmarkClient _CreateFusionMemory(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.FusionMemory);
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        services
            .AddFusionCache()
            .WithCacheKeyPrefix(keyPrefix)
            .WithDefaultEntryOptions(_CreateFusionOptions(TimeSpan.FromMinutes(1)));

        var provider = services.BuildServiceProvider();

        return new FusionCacheBenchmarkClient(descriptor, provider.GetRequiredService<IFusionCache>(), provider);
    }

    private static ICacheBenchmarkClient _CreateFusionRedisDistributed(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.FusionRedisDistributed);
        var redisConnection = _GetRequiredRedisConnectionString();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        services.AddFusionCacheSystemTextJsonSerializer();
        services
            .AddFusionCache()
            .WithCacheKeyPrefix(keyPrefix)
            .WithDefaultEntryOptions(_CreateFusionDistributedOptions(TimeSpan.FromMinutes(1)))
            .WithRegisteredDistributedCache(ignoreMemoryDistributedCache: false);

        var provider = services.BuildServiceProvider();

        return new FusionCacheBenchmarkClient(
            descriptor,
            provider.GetRequiredService<IFusionCache>(),
            provider,
            _CreateFusionDistributedOptions
        );
    }

    private static ICacheBenchmarkClient _CreateFusionRedis(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.FusionRedis);
        var redisConnection = _GetRequiredRedisConnectionString();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        services.AddFusionCacheSystemTextJsonSerializer();
        services
            .AddFusionCache()
            .WithCacheKeyPrefix(keyPrefix)
            .WithDefaultEntryOptions(_CreateFusionOptions(TimeSpan.FromMinutes(1)))
            .WithRegisteredDistributedCache(ignoreMemoryDistributedCache: false);

        var provider = services.BuildServiceProvider();

        return new FusionCacheBenchmarkClient(descriptor, provider.GetRequiredService<IFusionCache>(), provider);
    }

    private static ICacheBenchmarkClient _CreateFoundatioMemory(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.FoundatioMemory);
        var cache = new ScopedCacheClient(new InMemoryCacheClient(), keyPrefix);

        return new FoundatioCacheBenchmarkClient(descriptor, cache);
    }

    private static ICacheBenchmarkClient _CreateFoundatioRedis(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.FoundatioRedis);
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
        var multiplexer = ConnectionMultiplexer.Connect(_GetRequiredRedisConnectionString());
#pragma warning restore MA0045
        var cache = new ScopedCacheClient(
            new RedisCacheClient(options => options.ConnectionMultiplexer(multiplexer)),
            keyPrefix
        );

        return new FoundatioCacheBenchmarkClient(descriptor, cache, multiplexer);
    }

    private static ICacheBenchmarkClient _CreateMicrosoftMemory(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.MicrosoftMemory);
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        return new MicrosoftMemoryCacheBenchmarkClient(descriptor, cache, keyPrefix);
    }

    private static ICacheBenchmarkClient _CreateMicrosoftMemoryDistributed(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.MicrosoftMemoryDistributed);
        var cache = new PrefixDistributedCache(
            keyPrefix,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
        );

        return new MicrosoftDistributedCacheBenchmarkClient(descriptor, cache);
    }

    private static ICacheBenchmarkClient _CreateMicrosoftRedisDistributed(string keyPrefix)
    {
        var descriptor = _GetDescriptor(BenchmarkProviderIds.MicrosoftRedisDistributed);
        var cache = new PrefixDistributedCache(
            keyPrefix,
            new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache(
                Options.Create(
                    new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions
                    {
                        Configuration = _GetRequiredRedisConnectionString(),
                    }
                )
            )
        );

        return new MicrosoftDistributedCacheBenchmarkClient(descriptor, cache);
    }

    private static CacheBenchmarkClientDescriptor _GetDescriptor(string providerId) =>
        GetDescriptors(includeRedis: true).Single(x => string.Equals(x.Id, providerId, StringComparison.Ordinal));

    private static string _GetRequiredRedisConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("HEADLESS_CACHE_BENCHMARK_REDIS");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set HEADLESS_CACHE_BENCHMARK_REDIS to a Redis connection string before running Redis cache benchmarks."
            );
        }

        return connectionString;
    }

    private static CacheEntryOptions _CreateHeadlessOptions(TimeSpan duration)
    {
        return new()
        {
            Duration = duration,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(5),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
            EagerRefreshThreshold = 0.8f,
        };
    }

    private static FusionCacheEntryOptions _CreateFusionOptions(TimeSpan duration)
    {
        return new FusionCacheEntryOptions(duration)
            .SetFailSafe(isEnabled: true, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1))
            .SetEagerRefresh(0.8f);
    }

    private static FusionCacheEntryOptions _CreateFusionDistributedOptions(TimeSpan duration)
    {
        return _CreateFusionOptions(duration).SetSkipMemoryCache(skip: true);
    }

    private sealed class PrefixDistributedCache(string prefix, IDistributedCache inner) : IDistributedCache, IDisposable
    {
        public byte[]? Get(string key) => inner.Get(prefix + key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            inner.GetAsync(prefix + key, token);

        public void Refresh(string key) => inner.Refresh(prefix + key);

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            inner.RefreshAsync(prefix + key, token);

        public void Remove(string key) => inner.Remove(prefix + key);

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            inner.RemoveAsync(prefix + key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            inner.Set(prefix + key, value, options);

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        ) => inner.SetAsync(prefix + key, value, options, token);

        public void Dispose()
        {
            if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
