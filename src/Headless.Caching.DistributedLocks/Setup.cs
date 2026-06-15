// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

/// <summary>DI registration for the distributed factory-lock adapter.</summary>
[PublicAPI]
public static class SetupCachingDistributedLocks
{
    extension(HeadlessCachingSetupBuilder setup)
    {
        /// <summary>
        /// Registers <see cref="ICacheFactoryLockProvider"/> backed by the application's
        /// <c>IDistributedLock</c> registration, enabling
        /// <see cref="CacheEntryOptions.UseDistributedFactoryLock"/> on factory-backed cache reads.
        /// Requires a distributed lock provider to be registered (for example via the
        /// <c>Headless.DistributedLocks.*</c> setup builders).
        /// </summary>
        /// <param name="setupAction">Optional configuration for <see cref="CacheFactoryLockOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseDistributedFactoryLock(
            Action<CacheFactoryLockOptions>? setupAction = null
        )
        {
            setup.RegisterCrossCuttingExtension(services =>
            {
                if (setupAction is null)
                {
                    services.AddOptions<CacheFactoryLockOptions, CacheFactoryLockOptionsValidator>();
                }
                else
                {
                    services.Configure<CacheFactoryLockOptions, CacheFactoryLockOptionsValidator>(setupAction);
                }

                services.AddSingletonOptionValue<CacheFactoryLockOptions>();
                services.TryAddSingleton<ICacheFactoryLockProvider, DistributedLockCacheFactoryLockProvider>();
            });

            return setup;
        }

        /// <summary>
        /// Registers <see cref="ICacheFactoryLockProvider"/> with service provider-aware configuration.
        /// See <see cref="UseDistributedFactoryLock(HeadlessCachingSetupBuilder, Action{CacheFactoryLockOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseDistributedFactoryLock(
            Action<CacheFactoryLockOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterCrossCuttingExtension(services =>
            {
                services.Configure<CacheFactoryLockOptions, CacheFactoryLockOptionsValidator>(setupAction);
                services.AddSingletonOptionValue<CacheFactoryLockOptions>();
                services.TryAddSingleton<ICacheFactoryLockProvider, DistributedLockCacheFactoryLockProvider>();
            });

            return setup;
        }

        /// <summary>
        /// Registers <see cref="ICacheFactoryLockProvider"/>, binding <see cref="CacheFactoryLockOptions"/>
        /// from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCachingSetupBuilder UseDistributedFactoryLock(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterCrossCuttingExtension(services =>
            {
                services.Configure<CacheFactoryLockOptions, CacheFactoryLockOptionsValidator>(configuration);
                services.AddSingletonOptionValue<CacheFactoryLockOptions>();
                services.TryAddSingleton<ICacheFactoryLockProvider, DistributedLockCacheFactoryLockProvider>();
            });

            return setup;
        }
    }
}
