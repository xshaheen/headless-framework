using Framework.Kernel.BuildingBlocks.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Caching;

[PublicAPI]
public static class AddCacheExtensions
{
    public static IHostApplicationBuilder AddInMemoryCache(
        this IHostApplicationBuilder builder,
        Action<InMemoryCacheOptions> setupAction
    )
    {
        builder.Services.ConfigureSingleton(setupAction);
        builder.Services.AddSingleton(typeof(ICache<>), typeof(Cache<>));
        builder.Services.AddSingleton<ICache, InMemoryCachingFoundatioAdapter>();
        builder.Services.AddKeyedSingleton(
            CacheConstants.MemoryCacheProvider,
            (services, _) => services.GetRequiredKeyedService<ICache>(CacheConstants.DistributedCacheProvider)
        );
        builder.Services.AddKeyedSingleton(
            CacheConstants.DistributedCacheProvider,
            (services, _) => services.GetRequiredKeyedService<ICache>(CacheConstants.DistributedCacheProvider)
        );

        return builder;
    }
}
