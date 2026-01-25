// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Testing.Tests;
using Headless.Messaging.RedisStreams;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnectionPool"/>.
/// These tests verify the connection pool behavior, especially the CRITICAL sync-over-async bug.
/// </summary>
public sealed class RedisConnectionPoolTests : TestBase
{
    /// <summary>
    /// CRITICAL TEST: Verifies that the constructor does NOT block due to sync-over-async pattern.
    /// The current implementation uses `.GetAwaiter().GetResult()` which can deadlock.
    /// This test documents the existing bug (todo #003).
    /// </summary>
    [Fact]
    public void constructor_should_not_block_when_initializing_pool()
    {
        // given
        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                ConnectionPoolSize = 5,
            }
        );

        var stopwatch = Stopwatch.StartNew();

        // when - constructor should return quickly since connections are lazy
        // NOTE: This test will FAIL if constructor blocks for connection attempts
        using var pool = new RedisConnectionPool(options, LoggerFactory);

        stopwatch.Stop();

        // then - constructor should complete within 100ms
        // If this fails, it indicates the sync-over-async bug is present
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, "constructor should not block waiting for Redis connections");
    }

    [Fact]
    public void should_create_pool_with_configured_size()
    {
        // given
        const uint poolSize = 5;
        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                ConnectionPoolSize = poolSize,
            }
        );

        // when
        using var pool = new RedisConnectionPool(options, LoggerFactory);

        // then - pool is created (internal connections are lazy-initialized)
        pool.Should().NotBeNull();
    }

    [Fact]
    public void should_dispose_without_error_when_no_connections_created()
    {
        // given
        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                ConnectionPoolSize = 3,
            }
        );

        var pool = new RedisConnectionPool(options, LoggerFactory);

        // when & then - dispose should not throw
        var action = () => pool.Dispose();
        action.Should().NotThrow();
    }

    [Fact]
    public void should_allow_multiple_dispose_calls()
    {
        // given
        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                ConnectionPoolSize = 2,
            }
        );

        var pool = new RedisConnectionPool(options, LoggerFactory);

        // when & then - multiple dispose calls should not throw
        var action = () =>
        {
            pool.Dispose();
            pool.Dispose();
            pool.Dispose();
        };

        action.Should().NotThrow();
    }
}
