// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.RedisStreams;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnection"/>.
/// </summary>
public sealed class RedisConnectionTests : TestBase
{
    [Fact]
    public void should_throw_when_connection_is_null()
    {
        // when & then
        var action = () => new RedisConnection(null!);
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void should_expose_underlying_connection()
    {
        // given
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        mockConnection.GetCounters().Returns(default(ServerCounters));

        // when
        using var redisConnection = new RedisConnection(mockConnection);

        // then
        redisConnection.Connection.Should().BeSameAs(mockConnection);
    }

    [Fact]
    public void should_dispose_underlying_connection()
    {
        // given
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        mockConnection.GetCounters().Returns(default(ServerCounters));

        var redisConnection = new RedisConnection(mockConnection);

        // when
        redisConnection.Dispose();

        // then
        mockConnection.Received(1).Dispose();
    }

    [Fact]
    public void should_not_dispose_multiple_times()
    {
        // given
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        mockConnection.GetCounters().Returns(default(ServerCounters));

        var redisConnection = new RedisConnection(mockConnection);

        // when
        redisConnection.Dispose();
        redisConnection.Dispose();
        redisConnection.Dispose();

        // then - underlying connection disposed only once
        mockConnection.Received(1).Dispose();
    }
}
