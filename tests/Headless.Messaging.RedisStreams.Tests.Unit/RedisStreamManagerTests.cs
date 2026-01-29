// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.RedisStreams;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisStreamManager"/>.
/// </summary>
public sealed class RedisStreamManagerTests : TestBase
{
    private readonly IRedisConnectionPool _mockConnectionPool;
#pragma warning disable CA2213
    private readonly IConnectionMultiplexer _mockMultiplexer;
#pragma warning restore CA2213
    private readonly IDatabase _mockDatabase;
    private readonly RedisStreamManager _sut;

    public RedisStreamManagerTests()
    {
        _mockConnectionPool = Substitute.For<IRedisConnectionPool>();
        _mockMultiplexer = Substitute.For<IConnectionMultiplexer>();
        _mockDatabase = Substitute.For<IDatabase>();

        _mockConnectionPool.ConnectAsync().Returns(Task.FromResult(_mockMultiplexer));
        _mockMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_mockDatabase);

        var options = Options.Create(
            new MessagingRedisOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                StreamEntriesCount = 10,
            }
        );

        var logger = LoggerFactory.CreateLogger<RedisStreamManager>();
        _sut = new RedisStreamManager(_mockConnectionPool, options, logger);
    }

    [Fact]
    public async Task should_connect_to_pool_when_publishing()
    {
        // given
        var entries = new NameValueEntry[] { new("key", "value") };

        _mockDatabase
            .StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(new RedisValue("1234567-0"));

        // when
        await _sut.PublishAsync("test-stream", entries);

        // then
        await _mockConnectionPool.Received(1).ConnectAsync();
    }

    [Fact]
    public async Task should_get_database_when_publishing()
    {
        // given
        var entries = new NameValueEntry[] { new("headers", "{}"), new("body", "[]") };

        _mockDatabase
            .StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(new RedisValue("1234567-0"));

        // when
        await _sut.PublishAsync("orders-stream", entries);

        // then - verify database was obtained from multiplexer
        _mockMultiplexer.Received().GetDatabase(Arg.Any<int>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task should_acknowledge_message()
    {
        // given
        _mockDatabase
            .StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(1L);

        // when
        await _sut.Ack("test-stream", "my-group", "1234567-0");

        // then
        await _mockDatabase
            .Received(1)
            .StreamAcknowledgeAsync("test-stream", "my-group", "1234567-0", Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_connect_before_acknowledging()
    {
        // given
        _mockDatabase
            .StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(1L);

        // when
        await _sut.Ack("stream", "group", "id");

        // then
        await _mockConnectionPool.Received(1).ConnectAsync();
    }
}
