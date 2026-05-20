// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.RateLimiting;
using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Headless.RateLimiting.Redis;

/// <summary>
/// Extension methods for registering Redis-backed distributed rate limiters.
/// </summary>
/// <remarks>Requires <see cref="IConnectionMultiplexer"/> to be registered in the service collection.</remarks>
[PublicAPI]
public static class SetupRedisRateLimiter
{
    extension(IServiceCollection services)
    {
        #region Redis Rate Limiter

        /// <summary>Adds a Redis-backed distributed rate limiter.</summary>
        public IServiceCollection AddRedisRateLimiter(
            Action<SlidingWindowRateLimiterOptions, IServiceProvider> optionSetupAction
        )
        {
            return _WithRedisScriptsLoader(services).AddRateLimiter(_CreateRateLimiterStorage, optionSetupAction);
        }

        /// <summary>Adds a Redis-backed distributed rate limiter.</summary>
        public IServiceCollection AddRedisRateLimiter(Action<SlidingWindowRateLimiterOptions> optionSetupAction)
        {
            return _WithRedisScriptsLoader(services).AddRateLimiter(_CreateRateLimiterStorage, optionSetupAction);
        }

        /// <summary>Adds a Redis-backed distributed rate limiter.</summary>
        public IServiceCollection AddRedisRateLimiter(IConfiguration config)
        {
            return _WithRedisScriptsLoader(services).AddRateLimiter(_CreateRateLimiterStorage, config);
        }

        #endregion

        #region Keyed Redis Rate Limiter

        /// <summary>Adds a keyed Redis-backed distributed rate limiter.</summary>
        public IServiceCollection AddKeyedRedisRateLimiter(
            string key,
            Action<SlidingWindowRateLimiterOptions, IServiceProvider> optionSetupAction
        )
        {
            return _WithRedisScriptsLoader(services)
                .AddKeyedRateLimiter(key, _CreateRateLimiterStorage, optionSetupAction);
        }

        /// <summary>Adds a keyed Redis-backed distributed rate limiter.</summary>
        public IServiceCollection AddKeyedRedisRateLimiter(
            string key,
            Action<SlidingWindowRateLimiterOptions> optionSetupAction
        )
        {
            return _WithRedisScriptsLoader(services)
                .AddKeyedRateLimiter(key, _CreateRateLimiterStorage, optionSetupAction);
        }

        /// <summary>Adds a keyed Redis-backed distributed rate limiter.</summary>
        public IServiceCollection AddKeyedRedisRateLimiter(string key, IConfiguration config)
        {
            return _WithRedisScriptsLoader(services).AddKeyedRateLimiter(key, _CreateRateLimiterStorage, config);
        }

        #endregion
    }

    private static RedisDistributedRateLimiterStorage _CreateRateLimiterStorage(IServiceProvider provider)
    {
        return new RedisDistributedRateLimiterStorage(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<HeadlessRedisScriptsLoader>()
        );
    }

    private static IServiceCollection _WithRedisScriptsLoader(IServiceCollection services)
    {
        services.TryAddSingleton<HeadlessRedisScriptsLoader>();

        return services;
    }
}
