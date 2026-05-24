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
        ///   <item>Register messaging before this method when push-based lock release wake-ups are needed</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedLockCore(s =>
                s.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        public IServiceCollection AddRedisDistributedLock(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddRedisDistributedLockCore(s =>
                s.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        public IServiceCollection AddRedisDistributedLock(IConfiguration config)
        {
            return services._AddRedisDistributedLockCore(s =>
                s.AddDistributedLock<RedisDistributedLockStorage>(config)
            );
        }

        #endregion

        #region Redis Distributed Reader-Writer Lock

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedReaderWriterLockCore(s =>
                s.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddRedisDistributedReaderWriterLockCore(s =>
                s.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(IConfiguration config)
        {
            return services._AddRedisDistributedReaderWriterLockCore(s =>
                s.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(config)
            );
        }

        #endregion
    }

    private static IServiceCollection _AddRedisDistributedLockCore(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> registerStorage
    )
    {
        services.TryAddSingleton<HeadlessRedisScriptsLoader>();

        return registerStorage(services);
    }

    private static IServiceCollection _AddRedisDistributedReaderWriterLockCore(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> registerStorage
    )
    {
        services.TryAddSingleton<HeadlessRedisScriptsLoader>();

        return registerStorage(services);
    }
}
