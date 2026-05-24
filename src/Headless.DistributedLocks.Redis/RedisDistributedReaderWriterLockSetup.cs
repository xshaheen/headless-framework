// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>Extension methods for registering Redis-backed reader-writer locks.</summary>
/// <remarks>Requires <see cref="IConnectionMultiplexer"/> to be registered in the service collection.</remarks>
[PublicAPI]
public static class RedisDistributedReaderWriterLockSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(optionSetupAction);
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(Action<DistributedLockOptions> optionSetupAction)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(optionSetupAction);
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReaderWriterLock(IConfiguration config)
        {
            services.TryAddSingleton<HeadlessRedisScriptsLoader>();

            return services.AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>(config);
        }
    }
}
