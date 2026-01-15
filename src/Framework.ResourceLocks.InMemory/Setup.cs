// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Messaging;
using Framework.ResourceLocks.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.ResourceLocks;

/// <summary>
/// Extension methods for registering in-memory resource locks.
/// Use for single-instance deployments, development, or testing.
/// NOT suitable for multi-instance/distributed scenarios.
/// </summary>
[PublicAPI]
public static class InMemoryResourceLockSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds in-memory resource lock provider using in-memory cache and message bus.
        /// Suitable for single-instance deployments, development, or testing only.
        /// </summary>
        /// <remarks>
        /// This registers:
        /// - In-memory cache for lock storage
        /// - In-memory message bus for lock release notifications
        /// - Resource lock provider with default options
        /// </remarks>
        public IServiceCollection AddInMemoryResourceLock(
            Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null
        )
        {
            services.AddInMemoryCache();
            services.AddFoundatioInMemoryMessageBus();

            return services.AddResourceLock<CacheResourceLockStorage>(optionSetupAction);
        }

        /// <summary>
        /// Adds in-memory throttling resource lock provider.
        /// Suitable for single-instance deployments, development, or testing only.
        /// </summary>
        public IServiceCollection AddInMemoryThrottlingResourceLock(ThrottlingResourceLockOptions options)
        {
            services.AddInMemoryCache();

            return services.AddThrottlingResourceLock(
                options,
                sp => new CacheThrottlingResourceLockStorage(sp.GetRequiredService<ICache>())
            );
        }

        /// <summary>
        /// Adds keyed in-memory throttling resource lock provider.
        /// Suitable for single-instance deployments, development, or testing only.
        /// </summary>
        public IServiceCollection AddKeyedInMemoryThrottlingResourceLock(
            string key,
            ThrottlingResourceLockOptions options
        )
        {
            services.AddInMemoryCache();

            return services.AddKeyedThrottlingResourceLock(
                key,
                options,
                sp => new CacheThrottlingResourceLockStorage(sp.GetRequiredService<ICache>())
            );
        }
    }
}
