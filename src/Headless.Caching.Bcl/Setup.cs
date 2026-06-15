// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Serializer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Caching;

/// <summary>DI registration extensions for the BCL <see cref="IDistributedCache"/> adapter.</summary>
[PublicAPI]
public static class SetupHeadlessDistributedCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Adds a named Headless cache configured for raw <see cref="byte"/> array values and exposes it as
        /// <see cref="IDistributedCache"/> for ASP.NET Core integrations such as session state.
        /// </summary>
        /// <param name="setupAction">Configuration for the adapter options.</param>
        /// <param name="configureCache">Configuration for the named Headless cache provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder AddHeadlessDistributedCache(
            Action<HeadlessDistributedCacheAdapterOptions> setupAction,
            Action<HeadlessCacheInstanceBuilder> configureCache
        )
        {
            Argument.IsNotNull(setupAction);
            Argument.IsNotNull(configureCache);

            var configuredOptions = new HeadlessDistributedCacheAdapterOptions();
            setupAction(configuredOptions);

            var cacheName = Argument.IsNotNullOrWhiteSpace(configuredOptions.CacheName);
            Argument.IsPositive(configuredOptions.DefaultAbsoluteExpiration);

            if (CacheConstants.IsReservedProviderKey(cacheName))
            {
                throw new ArgumentException(
                    $"The cache name '{cacheName}' is reserved for Headless caching provider registrations.",
                    nameof(setupAction)
                );
            }

            setup.AddNamed(
                cacheName,
                instance =>
                {
                    configureCache(instance);
                    instance.WithSerializer(new RawBytesSerializer());
                }
            );

            setup.RegisterCrossCuttingExtension(services =>
                services._AddHeadlessDistributedCacheCore(configuredOptions)
            );

            return setup;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddHeadlessDistributedCacheCore(
            HeadlessDistributedCacheAdapterOptions configuredOptions
        )
        {
            services.TryAddSingleton(TimeProvider.System);
            services.Configure<HeadlessDistributedCacheAdapterOptions, HeadlessDistributedCacheAdapterOptionsValidator>(
                options =>
                {
                    options.CacheName = configuredOptions.CacheName;
                    options.DefaultAbsoluteExpiration = configuredOptions.DefaultAbsoluteExpiration;
                }
            );

            services.TryAddSingleton<HeadlessDistributedCacheAdapter>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<HeadlessDistributedCacheAdapterOptions>>().Value;

                return new HeadlessDistributedCacheAdapter(
                    provider.GetRequiredKeyedService<ICache>(options.CacheName),
                    provider.GetRequiredService<IOptions<HeadlessDistributedCacheAdapterOptions>>(),
                    provider.GetRequiredService<TimeProvider>()
                );
            });
            services.TryAddSingleton<IDistributedCache>(provider =>
                provider.GetRequiredService<HeadlessDistributedCacheAdapter>()
            );

            return services;
        }
    }
}
