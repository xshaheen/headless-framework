// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZiggyCreatures.Caching.Fusion;

namespace Framework.Caching;

/// <summary>
/// Extension methods for setting up FusionCache-based <see cref="IHybridCache"/> in an <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class FusionCacheSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds FusionCache-based <see cref="IHybridCache"/> to the service collection.
        /// </summary>
        /// <param name="setupAction">Action to configure the cache options.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddFusionHybridCache(Action<FusionCacheProviderOptions> setupAction)
        {
            services.Configure<FusionCacheProviderOptions, FusionCacheProviderOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Adds FusionCache-based <see cref="IHybridCache"/> to the service collection with service provider access.
        /// </summary>
        /// <param name="setupAction">Action to configure the cache options with access to the service provider.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddFusionHybridCache(Action<FusionCacheProviderOptions, IServiceProvider> setupAction)
        {
            services.Configure<FusionCacheProviderOptions, FusionCacheProviderOptionsValidator>(setupAction);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            // Register the JSON serializer if not already registered
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
            services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
                sp.GetRequiredService<IJsonOptionsProvider>()
            ));
            services.TryAddSingleton<ISerializer>(sp => sp.GetRequiredService<IJsonSerializer>());

            // Register the options value
            services.AddSingletonOptionValue<FusionCacheProviderOptions>();

            // Register FusionCache's serializer adapter
            services.TryAddSingleton<ZiggyCreatures.Caching.Fusion.Serialization.IFusionCacheSerializer>(sp =>
                new FusionCacheSerializerAdapter(sp.GetRequiredService<ISerializer>())
            );

            // Register FusionCache itself - options are applied via post-configure
            services.AddFusionCache()
                .WithSerializer(sp => sp.GetRequiredService<ZiggyCreatures.Caching.Fusion.Serialization.IFusionCacheSerializer>());

            // Configure FusionCache options from our provider options
            services.AddOptions<ZiggyCreatures.Caching.Fusion.FusionCacheOptions>()
                .Configure<FusionCacheProviderOptions>((fcOpt, providerOpt) =>
                {
                    fcOpt.CacheName = providerOpt.CacheName;
                    fcOpt.CacheKeyPrefix = providerOpt.KeyPrefix;
                });

            services.AddOptions<ZiggyCreatures.Caching.Fusion.FusionCacheEntryOptions>()
                .Configure<FusionCacheProviderOptions>((fcOpt, providerOpt) =>
                {
                    fcOpt.Duration = providerOpt.DefaultDuration;
                    fcOpt.IsFailSafeEnabled = providerOpt.EnableFailSafe;
                    fcOpt.FailSafeMaxDuration = providerOpt.FailSafeMaxDuration;
                    fcOpt.FactorySoftTimeout = providerOpt.FactoryTimeout;
                    fcOpt.DistributedCacheSoftTimeout = providerOpt.DistributedCacheSoftTimeout;
                    fcOpt.DistributedCacheHardTimeout = providerOpt.DistributedCacheHardTimeout;
                    fcOpt.AllowBackgroundDistributedCacheOperations = providerOpt.AllowBackgroundDistributedCacheOperations;
                    fcOpt.JitterMaxDuration = providerOpt.JitterMaxDuration;
                });

            // Register our IHybridCache adapter
            services.TryAddSingleton<IHybridCache, FusionCacheAdapter>();
            services.TryAddSingleton(typeof(IHybridCache<>), typeof(HybridCache<>));

            return services;
        }
    }
}
