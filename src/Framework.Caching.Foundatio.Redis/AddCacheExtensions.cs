// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Caching;

[PublicAPI]
public static class AddCacheExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRedisCache(
            Action<RedisCacheOptions, IServiceProvider> setupAction,
            bool isDefault = true
        )
        {
            services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);

            return services._AddCacheCore(isDefault);
        }

        public IServiceCollection AddRedisCache(
            Action<RedisCacheOptions> setupAction,
            bool isDefault = true
        )
        {
            services.Configure<RedisCacheOptions, RedisCacheOptionsValidator>(setupAction);

            return services._AddCacheCore(isDefault);
        }

        private IServiceCollection _AddCacheCore(bool isDefault)
        {
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
            services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
                sp.GetRequiredService<IJsonOptionsProvider>()
            ));

            services.AddSingletonOptionValue<RedisCacheOptions>();
            services.TryAddSingleton<IDistributedCache, RedisCachingFoundatioAdapter>();
            services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));
            services.TryAddSingleton(typeof(IDistributedCache<>), typeof(DistributedCache<>));

            if (!isDefault)
            {
                services.AddKeyedSingleton<ICache>(CacheConstants.DistributedCacheProvider, provider => provider.GetRequiredService<IDistributedCache>());
            }
            else
            {
                services.AddSingleton<ICache>(provider => provider.GetRequiredService<IDistributedCache>());
                services.AddKeyedSingleton(CacheConstants.DistributedCacheProvider, x => x.GetRequiredService<ICache>());
            }

            return services;
        }
    }
}
