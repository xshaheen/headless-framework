// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

/// <summary>DI registration extension methods for hybrid cache.</summary>
[PublicAPI]
public static class HybridCacheSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds hybrid cache services with the specified configuration.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="HybridCacheOptions"/>.</param>
        /// <param name="isDefault">
        /// When true, registers <see cref="HybridCache"/> as the default <see cref="ICache"/> implementation.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        /// <para><b>Prerequisites:</b></para>
        /// <list type="bullet">
        /// <item>In-memory cache: Call <c>AddInMemoryCache()</c> before this</item>
        /// <item>Distributed cache: Call <c>AddRedisCache()</c> or similar before this</item>
        /// <item>Messaging: Configure messaging with <c>AddMessaging()</c></item>
        /// </list>
        /// <para><b>Example:</b></para>
        /// <code>
        /// services.AddInMemoryCache(isDefault: false);
        /// services.AddRedisCache(options => options.ConnectionString = "localhost:6379");
        /// services.AddMessaging(...);
        /// services.AddHybridCache(options =>
        /// {
        ///     options.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
        /// });
        /// </code>
        /// </remarks>
        public IServiceCollection AddHybridCache(Action<HybridCacheOptions>? setupAction = null, bool isDefault = true)
        {
            if (setupAction is null)
            {
                services.AddOptions<HybridCacheOptions, HybridCacheOptionsValidator>();
            }
            else
            {
                services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction);
            }

            services.AddSingletonOptionValue<HybridCacheOptions>();
            services.TryAddSingleton<HybridCache>();
            services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

            if (!isDefault)
            {
                services.AddKeyedSingleton<ICache>(
                    CacheConstants.HybridCacheProvider,
                    (provider, _) => provider.GetRequiredService<HybridCache>()
                );
            }
            else
            {
                services.TryAddSingleton<ICache>(provider => provider.GetRequiredService<HybridCache>());
                services.AddKeyedSingleton(
                    CacheConstants.HybridCacheProvider,
                    (x, _) => x.GetRequiredService<ICache>()
                );
            }

            return services;
        }

        /// <summary>
        /// Adds hybrid cache services with service provider-aware configuration.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <param name="isDefault">
        /// When true, registers <see cref="HybridCache"/> as the default <see cref="ICache"/> implementation.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHybridCache(
            Action<HybridCacheOptions, IServiceProvider> setupAction,
            bool isDefault = true
        )
        {
            services.Configure<HybridCacheOptions, HybridCacheOptionsValidator>(setupAction);
            services.AddSingletonOptionValue<HybridCacheOptions>();
            services.TryAddSingleton<HybridCache>();
            services.TryAddSingleton(typeof(ICache<>), typeof(Cache<>));

            if (!isDefault)
            {
                services.AddKeyedSingleton<ICache>(
                    CacheConstants.HybridCacheProvider,
                    (provider, _) => provider.GetRequiredService<HybridCache>()
                );
            }
            else
            {
                services.TryAddSingleton<ICache>(provider => provider.GetRequiredService<HybridCache>());
                services.AddKeyedSingleton(
                    CacheConstants.HybridCacheProvider,
                    (x, _) => x.GetRequiredService<ICache>()
                );
            }

            return services;
        }
    }
}
