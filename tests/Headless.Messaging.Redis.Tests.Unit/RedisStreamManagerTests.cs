// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisStreamManager"/>.
/// </summary>
public sealed class RedisStreamManagerTests : TestBase
{
    private readonly IRedisConnectionPool _mockConnectionPool;
    private readonly IConnectionMultiplexer _mockMultiplexer;
    private readonly IDatabase _mockDatabase;
    private readonly RedisStreamManager _sut;

    public RedisStreamManagerTests()
    {
        _mockConnectionPool = Substitute.For<IRedisConnectionPool>();
        _mockMultiplexer = Substitute.For<IConnectionMultiplexer>();
        _mockDatabase = Substitute.For<IDatabase>();

        _mockConnectionPool.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(_mockMultiplexer));
        _mockMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_mockDatabase);

        var options = Options.Create(
            new RedisMessagingOptions
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
        await _sut.PublishAsync("test-stream", entries, AbortToken);

        // then
        await _mockConnectionPool.Received(1).ConnectAsync(AbortToken);
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
        await _sut.PublishAsync("orders-stream", entries, AbortToken);

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
        await _sut.Ack("test-stream", "my-group", "1234567-0", AbortToken);

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
        await _sut.Ack("stream", "group", "id", AbortToken);

        // then
        await _mockConnectionPool.Received(1).ConnectAsync(AbortToken);
    }

    [Fact]
    public async Task should_requeue_before_acknowledging_rejected_message()
    {
        // given
        var entries = new NameValueEntry[] { new("headers", "{}"), new("body", "[]") };
        List<string> calls = [];

        _mockDatabase
            .StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<long?>(),
                Arg.Any<StreamTrimMode>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(_ =>
            {
                calls.Add("add");
                return new RedisValue("7654321-0");
            });

        _mockDatabase
            .StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(_ =>
            {
                calls.Add("ack");
                return 1L;
            });

        // when
        await _sut.RequeueAndAck("test-stream", "my-group", "1234567-0", entries, AbortToken);

        // then
        calls.Should().Equal("add", "ack");
    }

    [Fact]
    public async Task should_wait_asynchronously_between_latest_poll_iterations()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var sut = _CreateSut(timeProvider);
        var pollDelay = TimeSpan.FromMinutes(1);

        await using var enumerator = sut.PollStreamsLatestMessagesAsync([], "group", "consumer", pollDelay, AbortToken)
            .GetAsyncEnumerator(AbortToken);

        // when
        var firstMove = await enumerator.MoveNextAsync();
        var secondMoveTask = enumerator.MoveNextAsync().AsTask();

        // then
        firstMove.Should().BeTrue();
        secondMoveTask.IsCompleted.Should().BeFalse();

        timeProvider.Advance(pollDelay);

        (await secondMoveTask).Should().BeTrue();
    }

    [Fact]
    public async Task should_auto_claim_stale_pending_messages_with_min_idle_time()
    {
        // given
        var claimMinIdleTime = TimeSpan.FromMinutes(5);
        var pollDelay = TimeSpan.FromMinutes(1);

        _mockDatabase
            .StreamAutoClaimAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<long>(),
                Arg.Any<RedisValue>(),
                Arg.Any<int?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(StreamAutoClaimResult.Null);

        await using var enumerator = _sut.PollStreamsStalePendingMessagesAsync(
                ["test-stream"],
                "my-group",
                "consumer-1",
                claimMinIdleTime,
                pollDelay,
                AbortToken
            )
            .GetAsyncEnumerator(AbortToken);

        // when
        var moved = await enumerator.MoveNextAsync();

        // then
        moved.Should().BeTrue();
        await _mockDatabase
            .Received(1)
            .StreamAutoClaimAsync(
                "test-stream",
                "my-group",
                "consumer-1",
                (long)claimMinIdleTime.TotalMilliseconds,
                StreamPosition.Beginning,
                10,
                Arg.Any<CommandFlags>()
            );
    }

    private RedisStreamManager _CreateSut(TimeProvider timeProvider)
    {
        var options = Options.Create(
            new RedisMessagingOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                StreamEntriesCount = 10,
            }
        );

        var logger = LoggerFactory.CreateLogger<RedisStreamManager>();
        return new RedisStreamManager(_mockConnectionPool, options, logger, timeProvider);
    }
}
