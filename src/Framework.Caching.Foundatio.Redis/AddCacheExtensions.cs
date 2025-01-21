// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Caching;

[PublicAPI]
public static class AddCacheExtensions
{
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        Action<RedisCacheOptions, IServiceProvider> setupAction,
        bool isDefault = true
    )
    {
        services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);

        return _AddCacheCore(services, isDefault);
    }

    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        Action<RedisCacheOptions> setupAction,
        bool isDefault = true
    )
    {
        services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);

        return _AddCacheCore(services, isDefault);
    }

    private static IServiceCollection _AddCacheCore(IServiceCollection services, bool isDefault)
    {
        services.AddSingletonOptionValue<RedisCacheOptions>();
        services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            services.AddKeyedSingleton<ICache, RedisCachingFoundatioAdapter>(CacheConstants.DistributedCacheProvider);

            return services;
        }

        services.TryAddSingleton<ISerializer, SystemJsonSerializer>();
        services.AddSingleton<ICache, RedisCachingFoundatioAdapter>();
        services.AddKeyedSingleton(CacheConstants.DistributedCacheProvider, x => x.GetRequiredService<ICache>());

        return services;
    }
}
