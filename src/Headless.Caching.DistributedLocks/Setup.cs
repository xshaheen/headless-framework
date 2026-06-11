// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
            setup.RegisterCrossCuttingExtension(
                new CachingDistributedLocksOptionsExtension(services =>
                {
                    if (setupAction is not null)
                    {
                        services.Configure(setupAction);
                    }

                    services.AddOptions();
                    services.AddSingletonOptionValue<CacheFactoryLockOptions>();
                    services.TryAddSingleton<ICacheFactoryLockProvider, DistributedLockCacheFactoryLockProvider>();
                })
            );

            return setup;
        }
    }

    private sealed class CachingDistributedLocksOptionsExtension(Action<IServiceCollection> apply)
        : ICacheProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) => apply(services);
    }
}
