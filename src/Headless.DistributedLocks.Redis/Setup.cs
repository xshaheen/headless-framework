// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
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
/// Requires a message bus (e.g., Redis pub/sub via Foundatio) to be registered for lock release notifications.
/// </remarks>
[PublicAPI]
public static class RedisDistributedLockSetup
{
    extension(IServiceCollection services)
    {
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
        /// This registers:
        /// <list type="bullet">
        ///   <item><see cref="HeadlessRedisScriptsLoader"/> for atomic Redis operations</item>
        ///   <item><see cref="RedisDistributedLockStorage"/> for distributed lock storage</item>
        ///   <item>Resource lock provider with configurable options</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(
            Action<DistributedLockOptions, IServiceProvider>? optionSetupAction = null
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction);
        }

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
        public IServiceCollection AddRedisThrottlingDistributedLock(ThrottlingDistributedLockOptions options)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddThrottlingDistributedLock(
                options,
                sp => new RedisThrottlingDistributedLockStorage(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<HeadlessRedisScriptsLoader>()
                )
            );
        }

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

            return services.AddKeyedThrottlingDistributedLock(
                key,
                options,
                sp => new RedisThrottlingDistributedLockStorage(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<HeadlessRedisScriptsLoader>()
                )
            );
        }
    }
}
