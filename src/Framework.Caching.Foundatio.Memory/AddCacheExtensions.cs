// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        services._AddCacheCore(isDefault);

        return services;
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

        services._AddCacheCore(isDefault);

        return services;
    }

    private static void _AddCacheCore(this IServiceCollection services, bool isDefault)
    {
        services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

        if (!isDefault)
        {
            services.AddKeyedSingleton<ICache, InMemoryCachingFoundatioAdapter>(CacheConstants.MemoryCacheProvider);

            return;
        }

        services.AddSingleton<ICache, InMemoryCachingFoundatioAdapter>();
        services.AddKeyedSingleton(
            CacheConstants.MemoryCacheProvider,
            provider => provider.GetRequiredService<ICache>()
        );
    }
}
