// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>DI registration extensions for the Headless <see cref="IOutputCacheStore"/> adapter.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupOutputCache
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Adds a named Headless cache configured for raw <see cref="byte"/> array values and registers it as the
        /// ASP.NET Core <see cref="IOutputCacheStore"/>, making <c>AddOutputCache()</c> distributed and tag-aware.
        /// <see cref="byte"/> arrays are the cache's native wire format (stored verbatim, never through a
        /// serializer), so the <paramref name="configureCache"/> callback only selects the backing provider (for
        /// example <c>UseRedis</c>).
        /// </summary>
        /// <param name="setupAction">Configuration for the store options.</param>
        /// <param name="configureCache">Configuration for the named Headless cache provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseOutputCache(
            Action<HeadlessOutputCacheStoreOptions> setupAction,
            Action<HeadlessCacheInstanceBuilder> configureCache
        )
        {
            Argument.IsNotNull(setupAction);
            Argument.IsNotNull(configureCache);

            var configuredOptions = new HeadlessOutputCacheStoreOptions();
            setupAction(configuredOptions);

            var cacheName = Argument.IsNotNullOrWhiteSpace(configuredOptions.CacheName);
            Argument.IsPositive(configuredOptions.DefaultExpiration);

            // AddNamed validates the reserved-key/uniqueness rules and the single-provider invariant. byte[] is the
            // cache's native wire format, so the callback only selects the backing provider.
            setup.AddNamed(cacheName, configureCache);

            setup.RegisterCrossCuttingExtension(services => services._AddOutputCacheCore(configuredOptions));

            return setup;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddOutputCacheCore(HeadlessOutputCacheStoreOptions configuredOptions)
        {
            // The backing cache provider (InMemory/Redis named core) resolves TimeProvider from DI but does not
            // register it; supply the system clock so the named instance composes. The store itself has no clock
            // dependency — output-cache duration mapping is a straight pass-through.
            services.TryAddSingleton(TimeProvider.System);

            services.Configure<HeadlessOutputCacheStoreOptions, HeadlessOutputCacheStoreOptionsValidator>(options =>
            {
                options.CacheName = configuredOptions.CacheName;
                options.DefaultExpiration = configuredOptions.DefaultExpiration;
            });

            // AddOutputCache() registers MemoryOutputCacheStore via TryAddSingleton<IOutputCacheStore>, so Replace
            // (not TryAdd) is required for the Headless store to win regardless of registration order. The
            // formatter upcasts IOutputCacheStore to IOutputCacheBufferStore via pattern-match, so a single
            // registration of the concrete store covers both interfaces.
            services.Replace(
                ServiceDescriptor.Singleton<IOutputCacheStore>(provider =>
                {
                    var options = provider.GetRequiredService<IOptions<HeadlessOutputCacheStoreOptions>>();

                    return new HeadlessOutputCacheStore(
                        provider.GetRequiredKeyedService<ICache>(options.Value.CacheName),
                        options
                    );
                })
            );

            return services;
        }
    }
}
