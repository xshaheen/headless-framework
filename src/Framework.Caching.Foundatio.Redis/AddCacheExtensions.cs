// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        services.ConfigureSingleton<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
        _AddCacheCore(services, isDefault);

        return services;
    }

    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        Action<RedisCacheOptions> setupAction,
        bool isDefault = true
    )
    {
        services.ConfigureSingleton<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);
        _AddCacheCore(services, isDefault);

        return services;
    }

    private static void _AddCacheCore(IServiceCollection builder, bool isDefault)
    {
        builder.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            builder.AddKeyedSingleton<ICache, RedisCachingFoundatioAdapter>(CacheConstants.DistributedCacheProvider);

            return;
        }

        builder.AddSingleton<ICache, RedisCachingFoundatioAdapter>();
        builder.AddKeyedSingleton(
            CacheConstants.DistributedCacheProvider,
            services => services.GetRequiredService<ICache>()
        );
    }
}
