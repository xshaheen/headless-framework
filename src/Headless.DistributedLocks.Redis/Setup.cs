// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Extension methods for registering Redis-backed resource locks.
/// Suitable for distributed multi-instance deployments.
/// </summary>
/// <remarks>
/// Requires <see cref="IConnectionMultiplexer"/> to be registered in the service collection.
/// Requires a message bus to be registered for lock release notifications.
/// </remarks>
[PublicAPI]
public static class RedisDistributedLockSetup
{
    extension(IServiceCollection services)
    {
        #region Redis Distributed Lock

        /// <summary>
        /// Adds Redis-backed resource lock provider.
        /// Suitable for distributed multi-instance deployments.
        /// </summary>
        /// <remarks>
        /// Prerequisites:
        /// <list type="bullet">
        ///   <item><see cref="IConnectionMultiplexer"/> must be registered</item>
        ///   <item>Messaging must be configured via <c>AddMessages()</c> for lock release notifications</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction);
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        public IServiceCollection AddRedisDistributedLock(Action<DistributedLockOptions> optionSetupAction)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction);
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        public IServiceCollection AddRedisDistributedLock(IConfiguration config)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedLock<RedisDistributedLockStorage>(config);
        }

        /// <summary>Adds Redis-backed resource lock provider with default options.</summary>
        public IServiceCollection AddRedisDistributedLock()
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedLock<RedisDistributedLockStorage>();
        }

        #endregion

        #region Redis Throttling Distributed Lock

        /// <summary>
        /// Adds Redis-backed throttling resource lock provider.
        /// Suitable for distributed rate limiting across multiple instances.
        /// </summary>
        /// <remarks>
        /// Prerequisites:
        /// <list type="bullet">
        ///   <item><see cref="IConnectionMultiplexer"/> must be registered</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisThrottlingDistributedLock(
            Action<ThrottlingDistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddThrottlingDistributedLock(_CreateThrottlingStorage, optionSetupAction);
        }

        /// <summary>Adds Redis-backed throttling resource lock provider.</summary>
        public IServiceCollection AddRedisThrottlingDistributedLock(
            Action<ThrottlingDistributedLockOptions> optionSetupAction
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddThrottlingDistributedLock(_CreateThrottlingStorage, optionSetupAction);
        }

        /// <summary>Adds Redis-backed throttling resource lock provider.</summary>
        public IServiceCollection AddRedisThrottlingDistributedLock(IConfiguration config)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddThrottlingDistributedLock(_CreateThrottlingStorage, config);
        }

        #endregion

        #region Keyed Redis Throttling Distributed Lock

        /// <summary>
        /// Adds keyed Redis-backed throttling resource lock provider.
        /// Suitable for distributed rate limiting with multiple configurations.
        /// </summary>
        public IServiceCollection AddKeyedRedisThrottlingDistributedLock(
            string key,
            ThrottlingDistributedLockOptions options
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddKeyedThrottlingDistributedLock(key, options, _CreateThrottlingStorage);
        }

        #endregion
    }

    private static RedisThrottlingDistributedLockStorage _CreateThrottlingStorage(this IServiceProvider provider)
    {
        return new RedisThrottlingDistributedLockStorage(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<HeadlessRedisScriptsLoader>()
        );
    }
}
