// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Caching;

/// <summary>DI registration for the distributed factory-lock adapter.</summary>
[PublicAPI]
public static class SetupCachingDistributedLocks
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ICacheFactoryLockProvider"/> backed by the application's
        /// <c>IDistributedLock</c> registration, enabling
        /// <see cref="CacheEntryOptions.UseDistributedFactoryLock"/> on factory-backed cache reads.
        /// Requires a distributed lock provider to be registered (for example via the
        /// <c>Headless.DistributedLocks.*</c> setup builders).
        /// </summary>
        /// <param name="setupAction">Optional configuration for <see cref="CacheFactoryLockOptions"/>.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddCachingDistributedFactoryLock(Action<CacheFactoryLockOptions>? setupAction = null)
        {
            if (setupAction is not null)
            {
                services.Configure(setupAction);
            }

            services.AddOptions();
            services.AddSingletonOptionValue<CacheFactoryLockOptions>();
            services.TryAddSingleton<ICacheFactoryLockProvider, DistributedLockCacheFactoryLockProvider>();

            return services;
        }
    }
}
