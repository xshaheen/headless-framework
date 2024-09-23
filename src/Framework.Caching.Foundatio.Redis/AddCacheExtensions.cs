// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Caching;

[PublicAPI]
public static class AddCacheExtensions
{
    public static IHostApplicationBuilder AddRedisCache(
        this IHostApplicationBuilder builder,
        Action<RedisCacheOptions, IServiceProvider> setupAction,
        bool isDefault = true
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        _AddCore(builder, isDefault);

        return builder;
    }

    public static IHostApplicationBuilder AddRedisCache(
        this IHostApplicationBuilder builder,
        Action<RedisCacheOptions> setupAction,
        bool isDefault = true
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        _AddCore(builder, isDefault);

        return builder;
    }

    private static void _AddCore(IHostApplicationBuilder builder, bool isDefault)
    {
        builder.Services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            builder.Services.AddKeyedSingleton<ICache, RedisCachingFoundatioAdapter>(
                CacheConstants.DistributedCacheProvider
            );

            return;
        }

        builder.Services.AddSingleton<ICache, RedisCachingFoundatioAdapter>();
        builder.Services.AddKeyedSingleton(
            CacheConstants.DistributedCacheProvider,
            services => services.GetRequiredService<ICache>()
        );
    }
}
