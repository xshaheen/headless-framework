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
                Arg.Any<StreamAddOptions>(),
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
                Arg.Any<StreamAddOptions>(),
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
    public async Task should_trim_entries_older_than_the_max_age_when_publishing()
    {
        // given
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var sut = _CreateSut(new FakeTimeProvider(now), options => options.StreamMaxAge = TimeSpan.FromHours(1));
        StreamAddOptions? sent = null;
        _mockDatabase
            .StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Do<StreamAddOptions>(options => sent = options),
                Arg.Any<CommandFlags>()
            )
            .Returns(new RedisValue("1-0"));

        // when
        await sut.PublishAsync("orders-stream", [new("headers", "{}")], AbortToken);

        // then
        var expectedMinId = now.AddHours(-1).ToUnixTimeMilliseconds();
        sent.Should().NotBeNull();
        sent!.Value.MinId.Should().Be((RedisValue)$"{expectedMinId}-0");
        sent.Value.Approximate.Should().BeTrue();
        sent.Value.MaxLength.Should().BeNull();
    }

    [Fact]
    public async Task should_not_trim_when_the_max_age_is_zero()
    {
        // given
        var sut = _CreateSut(new FakeTimeProvider(), options => options.StreamMaxAge = TimeSpan.Zero);
        StreamAddOptions? sent = null;
        _mockDatabase
            .StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Do<StreamAddOptions>(options => sent = options),
                Arg.Any<CommandFlags>()
            )
            .Returns(new RedisValue("1-0"));

        // when
        await sut.PublishAsync("orders-stream", [new("headers", "{}")], AbortToken);

        // then
        sent.Should().NotBeNull();
        sent!.Value.MinId.IsNull.Should().BeTrue();
        sent.Value.MaxLength.Should().BeNull();
    }

    [Fact]
    public async Task should_sweep_idle_consumers_on_the_first_claim_pass_and_then_once_per_claim_interval()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var sut = _CreateSut(timeProvider, options => options.IdleConsumerDeleteAfter = TimeSpan.FromMinutes(30));
        var claimMinIdleTime = TimeSpan.FromSeconds(60);
        var pollDelay = TimeSpan.FromSeconds(1);
        _mockDatabase
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(RedisResult.Create(1));
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

        await using var enumerator = sut.PollStreamsStalePendingMessagesAsync(
                ["orders-stream"],
                "group",
                "consumer",
                claimMinIdleTime,
                pollDelay,
                AbortToken
            )
            .GetAsyncEnumerator(AbortToken);

        // when
        await enumerator.MoveNextAsync();
        var secondPass = enumerator.MoveNextAsync().AsTask();
        timeProvider.Advance(pollDelay);
        await secondPass.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var sweepsBeforeInterval = _SweepCalls();
        var thirdPass = enumerator.MoveNextAsync().AsTask();
        timeProvider.Advance(claimMinIdleTime);
        await thirdPass.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        sweepsBeforeInterval.Should().Be(1);
        _SweepCalls().Should().Be(2);
        await _mockDatabase
            .Received()
            .ScriptEvaluateAsync(
                Arg.Is<string>(script => script.Contains("DELCONSUMER", StringComparison.Ordinal)),
                Arg.Is<RedisKey[]?>(keys => keys!.Single() == "orders-stream"),
                Arg.Is<RedisValue[]?>(values =>
                    values!.Length == 2
                    && values[0] == "group"
                    && values[1] == (long)TimeSpan.FromMinutes(30).TotalMilliseconds
                ),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_not_sweep_idle_consumers_when_disabled()
    {
        // given
        var sut = _CreateSut(new FakeTimeProvider(), options => options.IdleConsumerDeleteAfter = TimeSpan.Zero);
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

        await using var enumerator = sut.PollStreamsStalePendingMessagesAsync(
                ["orders-stream"],
                "group",
                "consumer",
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(1),
                AbortToken
            )
            .GetAsyncEnumerator(AbortToken);

        // when
        await enumerator.MoveNextAsync();

        // then
        _SweepCalls().Should().Be(0);
    }

    private int _SweepCalls()
    {
        return _mockDatabase
            .ReceivedCalls()
            .Count(call =>
                string.Equals(
                    call.GetMethodInfo().Name,
                    nameof(IDatabaseAsync.ScriptEvaluateAsync),
                    StringComparison.Ordinal
                )
            );
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

    private RedisStreamManager _CreateSut(TimeProvider timeProvider, Action<RedisMessagingOptions>? configure = null)
    {
        var redisOptions = new RedisMessagingOptions
        {
            Configuration = ConfigurationOptions.Parse("localhost:6379"),
            StreamEntriesCount = 10,
        };
        configure?.Invoke(redisOptions);
        var options = Options.Create(redisOptions);

        var logger = LoggerFactory.CreateLogger<RedisStreamManager>();
        return new RedisStreamManager(_mockConnectionPool, options, logger, timeProvider);
    }
}
