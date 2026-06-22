// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>DI registration extensions for the BCL <see cref="IDistributedCache"/> adapter.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupBclCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Adds a named Headless cache configured for raw <see cref="byte"/> array values and exposes it as
        /// <see cref="IDistributedCache"/> for ASP.NET Core integrations such as session state. <see cref="byte"/>
        /// arrays are the cache's native wire format (stored verbatim, never through a serializer), so the
        /// <paramref name="configureCache"/> callback only selects the backing provider (for example
        /// <c>UseRedis</c>).
        /// </summary>
        /// <param name="setupAction">Configuration for the adapter options.</param>
        /// <param name="configureCache">Configuration for the named Headless cache provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseBclCache(
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

            // AddNamed validates the reserved-key/uniqueness rules and the single-provider invariant. byte[] is the
            // cache's native wire format, so the callback only selects the backing provider; reject a serializer
            // configured on the instance (it would be silently ignored on the byte[] path) to fail fast on the
            // meaningless configuration.
            setup.AddNamed(
                cacheName,
                instance =>
                {
                    configureCache(instance);

                    if (instance.SerializerFactory is not null)
                    {
                        throw new InvalidOperationException(
                            "The BCL distributed-cache adapter stores byte[] verbatim (the cache's native wire "
                                + "format); do not configure a serializer on the named cache instance via WithSerializer."
                        );
                    }
                }
            );

            setup.RegisterCrossCuttingExtension(services => services._AddBclCacheCore(configuredOptions));

            return setup;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddBclCacheCore(HeadlessDistributedCacheAdapterOptions configuredOptions)
        {
            services.TryAddSingleton(TimeProvider.System);

            services.Configure<HeadlessDistributedCacheAdapterOptions, HeadlessDistributedCacheAdapterOptionsValidator>(
                options =>
                {
                    options.CacheName = configuredOptions.CacheName;
                    options.DefaultAbsoluteExpiration = configuredOptions.DefaultAbsoluteExpiration;
                }
            );

            // The adapter owns the IDistributedCache slot. TryAdd defers to a consumer-registered
            // IDistributedCache if one already exists; consumers wanting the Headless adapter must not also
            // register a competing IDistributedCache (e.g. AddStackExchangeRedisCache).
            services.TryAddSingleton<IDistributedCache>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<HeadlessDistributedCacheAdapterOptions>>();

                return new HeadlessDistributedCacheAdapter(
                    provider.GetRequiredKeyedService<ICache>(options.Value.CacheName),
                    options,
                    provider.GetRequiredService<TimeProvider>()
                );
            });

            return services;
        }
    }
}
