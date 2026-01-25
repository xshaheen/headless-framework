// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.RedisStreams;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="AsyncLazyRedisConnection"/>.
/// </summary>
public sealed class AsyncLazyRedisConnectionTests : TestBase
{
    [Fact]
    public void should_not_create_connection_immediately()
    {
        // given
        var options = new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") };
        var logger = LoggerFactory.CreateLogger<AsyncLazyRedisConnection>();

        // when
        var lazy = new AsyncLazyRedisConnection(options, logger);

        // then - connection should not be created yet
        lazy.IsValueCreated.Should().BeFalse();
    }

    [Fact]
    public void created_connection_should_be_null_when_not_created()
    {
        // given
        var options = new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") };
        var logger = LoggerFactory.CreateLogger<AsyncLazyRedisConnection>();
        var lazy = new AsyncLazyRedisConnection(options, logger);

        // when
        var connection = lazy.CreatedConnection;

        // then
        connection.Should().BeNull();
    }

    [Fact]
    public void should_have_awaiter_for_async_access()
    {
        // given
        var options = new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") };
        var logger = LoggerFactory.CreateLogger<AsyncLazyRedisConnection>();
        var lazy = new AsyncLazyRedisConnection(options, logger);

        // when
        var awaiter = lazy.GetAwaiter();

        // then - should have a valid awaiter
        awaiter.Should().NotBeNull();
    }
}
