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
        Action<InMemoryCacheOptions, IServiceProvider> setupAction,
        bool isDefault = true
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        _AddCore(builder, isDefault);

        return builder;
    }

    public static IHostApplicationBuilder AddInMemoryCache(
        this IHostApplicationBuilder builder,
        Action<InMemoryCacheOptions> setupAction,
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
            builder.Services.AddKeyedSingleton<ICache, InMemoryCachingFoundatioAdapter>(
                CacheConstants.MemoryCacheProvider
            );

            return;
        }

        builder.Services.AddSingleton<ICache, InMemoryCachingFoundatioAdapter>();
        builder.Services.AddKeyedSingleton(
            CacheConstants.MemoryCacheProvider,
            services => services.GetRequiredService<ICache>()
        );
    }
}
