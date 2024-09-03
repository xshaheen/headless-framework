using EasyCaching.Core;
using EasyCaching.Core.Configurations;
using EasyCaching.InMemory;
using EasyCaching.Serialization.MemoryPack;
using EasyCaching.Serialization.SystemTextJson.Configurations;
using Framework.Kernel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Caching.EasyCache;

public static class AddEasyCacheExtensions
{
    private const string _JsonSerializerName = "json";
    private const string _MemSerializerName = "memory";

    public static IHostApplicationBuilder AddInMemoryEasyCache(this IHostApplicationBuilder builder)
    {
        builder.Services.AddEasyCaching(options =>
        {
            options.WithMemoryPack(_MemSerializerName);

            options.UseInMemory(
                configure: config =>
                {
                    config.CacheNulls = false;
                    config.SerializerName = _MemSerializerName;
                },
                name: CacheConstants.RedistCacheProvider
            );
        });

        builder.Services.AddSingleton<ICache>(services =>
        {
            var factory = services.GetRequiredService<IEasyCachingProviderFactory>();
            var cache = factory.GetCachingProvider(CacheConstants.MemoryCacheProvider);

            return new EasyCachingCache(cache);
        });

        builder.Services.AddKeyedSingleton<ICache>(
            CacheConstants.MemoryCacheProvider,
            (services, _) => services.GetRequiredService<ICache>()
        );

        return builder;
    }

    public static IHostApplicationBuilder AddRedisEasyCache(
        this IHostApplicationBuilder builder,
        string connectionString,
        string? keyPrefix = null
    )
    {
        builder.Services.AddEasyCaching(options =>
        {
            options.WithMemoryPack(_MemSerializerName);
            options.WithSystemTextJson(o => PlatformJsonConstants.ConfigureInternalJsonOptions(o), _JsonSerializerName);

            options.UseInMemory(
                configure: config =>
                {
                    config.SerializerName = _MemSerializerName;
                    config.CacheNulls = false;
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

            options.UseRedis(
                configure: config =>
                {
                    var parts = connectionString.Split(':');
                    var host = parts[0];
                    var port = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    config.DBConfig.Endpoints.Add(new ServerEndPoint(host, port));
                    config.DBConfig.Configuration = connectionString;
                    config.DBConfig.KeyPrefix = keyPrefix;
                    config.DBConfig.AllowAdmin = true;
                    config.DBConfig.Database = 0;
                    config.SerializerName = _JsonSerializerName;
                },
                name: CacheConstants.RedistCacheProvider
            );
        });

        builder.Services.AddSingleton<ICache, EasyCachingCache>();

        builder.Services.AddSingleton<ICache>(services =>
        {
            var factory = services.GetRequiredService<IEasyCachingProviderFactory>();
            var cache = factory.GetCachingProvider(CacheConstants.RedistCacheProvider);

            return new EasyCachingCache(cache);
        });

        builder.Services.AddKeyedSingleton<ICache>(
            CacheConstants.MemoryCacheProvider,
            (services, _) =>
            {
                var factory = services.GetRequiredService<IEasyCachingProviderFactory>();
                var cache = factory.GetCachingProvider(CacheConstants.MemoryCacheProvider);

                return new EasyCachingCache(cache);
            }
        );

        builder.Services.AddKeyedSingleton<ICache>(
            CacheConstants.RedistCacheProvider,
            (services, _) => services.GetRequiredService<ICache>()
        );

        return builder;
    }
}
