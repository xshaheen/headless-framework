// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.DistributedLocks.Redis.Scripts;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.DistributedLocks;

/// <summary>
/// Extension methods for registering Redis-backed resource locks.
/// Suitable for distributed multi-instance deployments.
/// </summary>
/// <remarks>
/// Requires <see cref="IConnectionMultiplexer"/> to be registered in the service collection.
/// Messaging is optional; when an <see cref="IBus"/> registration exists before lock setup,
/// release notifications use push wake-ups. Otherwise, waiters fall back to polling backoff.
/// <c>UseRedis</c> also contributes the <c>distributed-locks-redis</c> readiness health check, which pings Redis
/// through the registered multiplexer.
/// </remarks>
[PublicAPI]
public static class SetupDistributedLocksRedis
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>
        /// Adds Redis-backed distributed-lock primitives.
        /// Suitable for distributed multi-instance deployments.
        /// </summary>
        /// <remarks>
        /// Prerequisites:
        /// <list type="bullet">
        ///   <item><see cref="IConnectionMultiplexer"/> must be registered</item>
        ///   <item>Call <c>AddHeadlessDistributedLocks(...)</c> before <c>AddHeadlessMessaging(...)</c>
        ///   when push-based lock release wake-ups are needed</item>
        /// </list>
        /// </remarks>
        public HeadlessDistributedLocksSetupBuilder UseRedis()
        {
            setup.RegisterExtension(new RedisDistributedLocksOptionsExtension());

            return setup;
        }
    }

    private sealed class RedisDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            // One loader serves every lock family. Its scripts load in a warm-up after the host starts, so neither
            // the start nor the first acquire waits for them.
            services.TryAddKeyedSingleton(
                RedisDistributedLockServiceKeys.ScriptsLoader,
                (sp, _) =>
                    new HeadlessRedisScriptsLoader(
                        sp.GetRequiredService<IConnectionMultiplexer>(),
                        sp.GetService<TimeProvider>(),
                        sp.GetService<ILogger<HeadlessRedisScriptsLoader>>()
                    )
            );
            services.AddWarmup<RedisDistributedLockScriptsWarmup>();
            services.AddDistributedLockCore<RedisDistributedLockStorage>();
            services.AddDistributedReadWriteLockCore<RedisDistributedReadWriteLockStorage>();
            services.AddDistributedSemaphoreCore<RedisDistributedSemaphoreStorage>();
            services.AddHeadlessHealthCheck(
                "distributed-locks-redis",
                HeadlessHealthCheckResource.Shared(static provider =>
                    provider.GetRequiredService<IConnectionMultiplexer>().GetEndpointsKey()
                ),
                static async (provider, cancellationToken) =>
                    await provider
                        .GetRequiredService<IConnectionMultiplexer>()
                        .ProbeEndpointsAsync(cancellationToken)
                        .ConfigureAwait(false),
                HeadlessHealthCheckTags.Redis
            );
        }
    }
}
