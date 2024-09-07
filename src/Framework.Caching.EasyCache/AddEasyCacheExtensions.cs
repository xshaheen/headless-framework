using EasyCaching.Core;
using EasyCaching.Core.Configurations;
using EasyCaching.InMemory;
using EasyCaching.Serialization.MemoryPack;
using EasyCaching.Serialization.SystemTextJson.Configurations;
using Framework.Kernel.BuildingBlocks.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Caching.EasyCache;

[PublicAPI]
public static class AddEasyCacheExtensions
{
    private const string _JsonSerializerName = "json";
    private const string _MemSerializerName = "memory";

    public static IHostApplicationBuilder AddInMemoryEasyCache(
        this IHostApplicationBuilder builder,
        Action<CacheOptions> setupAction
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        builder.Services.AddSingleton(typeof(ICache<>), typeof(Cache<>));
        builder.Services.AddKeyedSingleton(CacheConstants.MemoryCacheProvider, _CreateMemoryCache);
        builder.Services.AddKeyedSingleton(CacheConstants.DistributedCacheProvider, _CreateMemoryCache);

        builder.Services.AddSingleton<ICache>(services =>
            services.GetRequiredKeyedService<ICache>(CacheConstants.DistributedCacheProvider)
        );

        builder.Services.AddEasyCaching(options =>
        {
            options.WithMemoryPack(_MemSerializerName);
            options._UseInMemory();
        });

        return builder;
    }

    public static IHostApplicationBuilder AddRedisEasyCache(
        this IHostApplicationBuilder builder,
        Action<RedisCacheOptions> setupAction
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        builder.Services.ConfigureSingleton<CacheOptions>(config =>
        {
            var redis = new RedisCacheOptions();
            setupAction(redis);
            config.KeyPrefix = redis.KeyPrefix;
        });
        builder.Services.AddSingleton(typeof(ICache<>), typeof(Cache<>));
        builder.Services.AddKeyedSingleton(CacheConstants.MemoryCacheProvider, _CreateMemoryCache);
        builder.Services.AddKeyedSingleton(CacheConstants.DistributedCacheProvider, _CreateRedisCache);

        builder.Services.AddSingleton<ICache>(services =>
            services.GetRequiredKeyedService<ICache>(CacheConstants.DistributedCacheProvider)
        );

        builder.Services.AddEasyCaching(
            (provider, options) =>
            {
                options.WithMemoryPack(_MemSerializerName);
                options._UseInMemory();
                options.WithSystemTextJson(
                    o => PlatformJsonConstants.ConfigureInternalJsonOptions(o),
                    _JsonSerializerName
                );
                options._UseRedis(provider.GetRequiredService<RedisCacheOptions>());
            }
        );

        return builder;
    }

    private static ICache _CreateRedisCache(IServiceProvider services, object? key)
    {
        var factory = services.GetRequiredService<IEasyCachingProviderFactory>();
        var cache = factory.GetCachingProvider(CacheConstants.DistributedCacheProvider);

        return new EasyCachingCache(cache);
    }

    private static ICache _CreateMemoryCache(IServiceProvider services, object? key)
    {
        var factory = services.GetRequiredService<IEasyCachingProviderFactory>();
        var cache = factory.GetCachingProvider(CacheConstants.MemoryCacheProvider);
        var cacheOptions = services.GetRequiredService<CacheOptions>();

        return string.IsNullOrEmpty(cacheOptions.KeyPrefix)
            ? new EasyCachingCache(cache)
            : new ScopedEasyCachingCache(cache, cacheOptions.KeyPrefix);
    }

    private static void _UseRedis(this EasyCachingOptions options, RedisCacheOptions cacheOptions)
    {
        options.UseRedis(
            configure: config =>
            {
                var parts = cacheOptions.ConnectionString.Split(':');
                var host = parts[0];
                var port = int.Parse(parts[1], CultureInfo.InvariantCulture);
                config.DBConfig.Endpoints.Add(new ServerEndPoint(host, port));
                config.DBConfig.Configuration = cacheOptions.ConnectionString;
                config.DBConfig.KeyPrefix = cacheOptions.KeyPrefix;
                config.DBConfig.Database = cacheOptions.Database;
                config.DBConfig.AllowAdmin = true;
                config.SerializerName = _JsonSerializerName;
            },
            name: CacheConstants.DistributedCacheProvider
        );
    }

    private static void _UseInMemory(this EasyCachingOptions options)
    {
        options.UseInMemory(
            configure: config =>
            {
                config.SerializerName = _MemSerializerName;
                config.CacheNulls = true;
                config.EnableLogging = false;
                // The max random second will be added to cache's expiration, default value is 120
                config.MaxRdSecond = 120;
                // Mutex key's alive time(ms), default is 5000
                config.LockMs = 5000;
                // When mutex key alive, it will sleep some time, default is 300
                config.SleepMs = 300;

                config.DBConfig = new InMemoryCachingOptions
                {
                    // Scan time, default value is 60s
                    ExpirationScanFrequency = 60,
                    // Total count of cache items, default value is 10000
                    SizeLimit = 1000,
                    // Enable deep clone when reading object from cache or not, default value is true.
                    EnableReadDeepClone = true,
                    // Enable deep clone when writing object to cache or not, default value is false.
                    EnableWriteDeepClone = false,
                };
            },
            name: CacheConstants.MemoryCacheProvider
        );
    }
}
