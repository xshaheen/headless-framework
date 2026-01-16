// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Framework.ResourceLocks;

/// <summary>
/// Extension methods for registering Redis-backed resource locks.
/// Suitable for distributed multi-instance deployments.
/// </summary>
/// <remarks>
/// Requires <see cref="IConnectionMultiplexer"/> to be registered in the service collection.
/// Requires a message bus (e.g., Redis pub/sub via Foundatio) to be registered for lock release notifications.
/// </remarks>
[PublicAPI]
public static class RedisResourceLockSetup
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
        ///   <item>IMessageBus must be registered (e.g., via Redis pub/sub)</item>
        /// </list>
        /// This registers:
        /// <list type="bullet">
        ///   <item><see cref="HeadlessRedisScriptsLoader"/> for atomic Redis operations</item>
        ///   <item><see cref="Redis.RedisResourceLockStorage"/> for distributed lock storage</item>
        ///   <item>Resource lock provider with configurable options</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisResourceLock(
            Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddResourceLock<Redis.RedisResourceLockStorage>(optionSetupAction);
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
        public IServiceCollection AddRedisThrottlingResourceLock(ThrottlingResourceLockOptions options)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddThrottlingResourceLock(
                options,
                sp => new Redis.RedisThrottlingResourceLockStorage(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<HeadlessRedisScriptsLoader>()
                )
            );
        }

        /// <summary>
        /// Adds keyed Redis-backed throttling resource lock provider.
        /// Suitable for distributed rate limiting with multiple configurations.
        /// </summary>
        public IServiceCollection AddKeyedRedisThrottlingResourceLock(string key, ThrottlingResourceLockOptions options)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddKeyedThrottlingResourceLock(
                key,
                options,
                sp => new Redis.RedisThrottlingResourceLockStorage(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<HeadlessRedisScriptsLoader>()
                )
            );
        }
    }
}
