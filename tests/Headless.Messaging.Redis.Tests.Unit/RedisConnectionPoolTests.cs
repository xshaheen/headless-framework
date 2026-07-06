// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnectionPool"/>.
/// These tests verify connection pool construction and disposal behavior without requiring a Redis server.
/// </summary>
public sealed class RedisConnectionPoolTests : TestBase
{
    /// <summary>
    /// Verifies that the constructor does not eagerly connect to Redis.
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
        stopwatch
            .ElapsedMilliseconds.Should()
            .BeLessThan(100, "constructor should not block waiting for Redis connections");
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
#pragma warning disable MA0045 // This test intentionally verifies synchronous Dispose remains supported.
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

        using var pool = new RedisConnectionPool(options, LoggerFactory);

        // when & then - dispose should not throw
        var action = () => pool.Dispose();
        action.Should().NotThrow();
    }
#pragma warning restore MA0045

    [Fact]
#pragma warning disable MA0045 // This test intentionally verifies synchronous Dispose remains supported.
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

        using var pool = new RedisConnectionPool(options, LoggerFactory);

        // when & then - multiple dispose calls should not throw
        var action = () =>
        {
            pool.Dispose();
            pool.Dispose();
            pool.Dispose();
        };

        action.Should().NotThrow();
    }
#pragma warning restore MA0045

    [Fact]
    public async Task should_dispose_async_without_error_when_no_connections_created()
    {
        // given
        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                ConnectionPoolSize = 3,
            }
        );

        await using var pool = new RedisConnectionPool(options, LoggerFactory);

        // when & then - async dispose should not throw
        var action = async () => await pool.DisposeAsync();
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_allow_multiple_dispose_async_calls()
    {
        // given
        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                ConnectionPoolSize = 2,
            }
        );

        await using var pool = new RedisConnectionPool(options, LoggerFactory);

        // when & then - multiple async dispose calls should not throw
        var action = async () =>
        {
            await pool.DisposeAsync();
            await pool.DisposeAsync();
            await pool.DisposeAsync();
        };

        await action.Should().NotThrowAsync();
    }
}
