// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Caching;

[PublicAPI]
public static class AddCacheExtensions
{
    public static IHostApplicationBuilder AddInMemoryCache(
        this IHostApplicationBuilder builder,
        Action<RedisCacheOptions> setupAction,
        bool isDefault = true
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        builder.Services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (isDefault)
        {
            builder.Services.AddSingleton<ICache, RedisCachingFoundatioAdapter>();
        }

        builder.Services.AddKeyedSingleton(
            CacheConstants.DistributedCacheProvider,
            (services, _) => services.GetRequiredKeyedService<ICache>(CacheConstants.DistributedCacheProvider)
        );

        return builder;
    }
}
