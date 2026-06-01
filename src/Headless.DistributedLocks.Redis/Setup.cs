// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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
/// Messaging is optional; when an <see cref="IOutboxBus"/> registration exists before lock setup,
/// release notifications use push wake-ups. Otherwise, waiters fall back to polling backoff.
/// </remarks>
[PublicAPI]
public static class SetupRedisDistributedLock
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
        ///   <item>Register messaging before this method when push-based lock release wake-ups are needed</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        /// <remarks>
        /// <paramref name="optionSetupAction"/> is optional; when omitted, <see cref="DistributedLockOptions"/>
        /// keeps its defaults.
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(Action<DistributedLockOptions>? optionSetupAction = null)
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction ?? (static _ => { }))
            );
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        public IServiceCollection AddRedisDistributedLock(IConfiguration config)
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedLock<RedisDistributedLockStorage>(config)
            );
        }

        #endregion

        #region Redis Distributed Semaphore

        /// <summary>Adds Redis-backed distributed semaphore provider.</summary>
        public IServiceCollection AddRedisDistributedSemaphore(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedSemaphore<RedisDistributedSemaphoreStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed distributed semaphore provider.</summary>
        public IServiceCollection AddRedisDistributedSemaphore(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedSemaphore<RedisDistributedSemaphoreStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed distributed semaphore provider.</summary>
        public IServiceCollection AddRedisDistributedSemaphore(IConfiguration config)
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedSemaphore<RedisDistributedSemaphoreStorage>(config)
            );
        }

        #endregion

        #region Redis Distributed Reader-Writer Lock

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(IConfiguration config)
        {
            return services._AddRedisDistributedCore(s =>
                s.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(config)
            );
        }

        #endregion
    }

    private static IServiceCollection _AddRedisDistributedCore(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> registerStorage
    )
    {
        services.TryAddSingleton<HeadlessRedisScriptsLoader>();

        return registerStorage(services);
    }
}
