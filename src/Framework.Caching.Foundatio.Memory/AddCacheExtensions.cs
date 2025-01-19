// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Caching;

[PublicAPI]
public static class AddCacheExtensions
{
    public static IServiceCollection AddInMemoryCache(
        this IServiceCollection services,
        Action<InMemoryCacheOptions, IServiceProvider> setupAction,
        bool isDefault = true
    )
    {
        services.ConfigureSingleton<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);

        return _AddCacheCore(services, isDefault);
    }

    public static IServiceCollection AddInMemoryCache(
        this IServiceCollection services,
        Action<InMemoryCacheOptions>? setupAction = null,
        bool isDefault = true
    )
    {
        if (setupAction is null)
        {
            services.AddSingletonOptions<InMemoryCacheOptions, InMemoryCacheOptionsValidator>();
        }
        else
        {
            services.ConfigureSingleton<InMemoryCacheOptions, InMemoryCacheOptionsValidator>(setupAction);
        }

        return _AddCacheCore(services, isDefault);
    }

    private static IServiceCollection _AddCacheCore(IServiceCollection services, bool isDefault)
    {
        services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            services.AddKeyedSingleton<ICache, InMemoryCachingFoundatioAdapter>(CacheConstants.MemoryCacheProvider);

            return services;
        }

        services.AddSingleton<ICache, InMemoryCachingFoundatioAdapter>();
        services.AddKeyedSingleton(CacheConstants.MemoryCacheProvider, x => x.GetRequiredService<ICache>());
        services.AddKeyedSingleton(CacheConstants.DistributedCacheProvider, x => x.GetRequiredService<ICache>());

        return services;
    }
}
