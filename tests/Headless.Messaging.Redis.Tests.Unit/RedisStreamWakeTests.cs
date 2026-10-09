// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisStreamWakeTests : TestBase
{
    private const string _Stream = "headless:messaging:queue:orders.placed";
    private static readonly RedisChannel _WakeChannel = RedisChannel.Literal(_Stream + ":wake");
    private static readonly TimeSpan _PollDelay = TimeSpan.FromMinutes(1);

    private readonly IRedisConnectionPool _connectionPool = Substitute.For<IRedisConnectionPool>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly ISubscriber _subscriber = Substitute.For<ISubscriber>();
    private readonly FakeTimeProvider _timeProvider = new();
    private Action<RedisChannel, RedisValue>? _onWake;

    public RedisStreamWakeTests()
    {
        _connectionPool.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(_multiplexer));
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _multiplexer.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);
        _subscriber
            .SubscribeAsync(_WakeChannel, Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _onWake = call.Arg<Action<RedisChannel, RedisValue>>());
        _database
            .StreamReadAsync(
                Arg.Any<StreamPosition[]>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(Task.FromResult(Array.Empty<RedisStream>()));
        _database
            .StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<StreamAddOptions>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(new RedisValue("1-0"));
    }

    [Fact]
    public async Task should_announce_a_published_entry_on_the_stream_wake_channel_after_adding_it()
    {
        // given
        var sut = _CreateManager();

        // when
        await sut.PublishAsync(_Stream, [new("body", "[]")], AbortToken);

        // then
        Received.InOrder(() =>
        {
            _ = _database.StreamAddAsync(
                _Stream,
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<StreamAddOptions>(),
                Arg.Any<CommandFlags>()
            );
            _ = _subscriber.PublishAsync(_WakeChannel, RedisValue.EmptyString, CommandFlags.FireAndForget);
        });
    }

    [Fact]
    public async Task should_not_announce_entries_when_wake_ups_are_off()
    {
        // given
        var sut = _CreateManager(options => options.WakeConsumersOnPublish = false);

        // when
        await sut.PublishAsync(_Stream, [new("body", "[]")], AbortToken);

        // then
        await _subscriber.DidNotReceiveWithAnyArgs().PublishAsync(default, default, CommandFlags.FireAndForget);
    }

    [Fact]
    public async Task should_succeed_the_publish_when_the_announcement_fails()
    {
        // given
        _multiplexer.GetSubscriber(Arg.Any<object?>()).Throws(new ObjectDisposedException("multiplexer"));
        var sut = _CreateManager();

        // when
        var act = () => sut.PublishAsync(_Stream, [new("body", "[]")], AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_read_an_idle_stream_again_as_soon_as_a_publish_wakes_it()
    {
        // given
        var sut = _CreateManager();
        await using var enumerator = sut.PollStreamsFromAsync([new(_Stream, "0-0")], _PollDelay, AbortToken)
            .GetAsyncEnumerator(AbortToken);
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        // when: the idle wait is underway, and a publisher announces an entry once the woken-read interval has passed
        var secondRead = enumerator.MoveNextAsync().AsTask();
        _timeProvider.Advance(RedisStreamWake.MinWokenReadInterval);
        _onWake.Should().NotBeNull("the loop subscribes before its first read");
        _onWake!(_WakeChannel, RedisValue.EmptyString);

        // then
        (await secondRead.WaitAsync(TimeSpan.FromSeconds(5), AbortToken))
            .Should()
            .BeTrue();
        await _database
            .Received(2)
            .StreamReadAsync(
                Arg.Any<StreamPosition[]>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_read_an_idle_stream_only_at_the_poll_interval_when_wake_ups_are_off()
    {
        // given
        var sut = _CreateManager(options => options.WakeConsumersOnPublish = false);
        await using var enumerator = sut.PollStreamsFromAsync([new(_Stream, "0-0")], _PollDelay, AbortToken)
            .GetAsyncEnumerator(AbortToken);
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        // when
        var secondRead = enumerator.MoveNextAsync().AsTask();
        _timeProvider.Advance(_PollDelay - TimeSpan.FromSeconds(1));
        await Task.Yield();

        // then
        secondRead.IsCompleted.Should().BeFalse();
        await _subscriber
            .DidNotReceive()
            .SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>()
            );
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await secondRead.WaitAsync(TimeSpan.FromSeconds(5), AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_hold_a_woken_read_until_the_woken_read_interval_has_passed_since_the_last_read()
    {
        // given
        await using var wake = _CreateWake();
        await wake.EnsureSubscribedAsync(AbortToken);
        var readStartedAt = _timeProvider.GetTimestamp();
        _onWake!(_WakeChannel, RedisValue.EmptyString);

        // when
        var wait = wake.WaitAsync(_PollDelay, readStartedAt, AbortToken);
        await Task.Yield();

        // then
        wait.IsCompleted.Should().BeFalse("a wake-up right after a read must not trigger a read at once");
        _timeProvider.Advance(RedisStreamWake.MinWokenReadInterval);
        await wait.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_cap_the_woken_read_interval_at_the_poll_interval()
    {
        // given
        var pollDelay = TimeSpan.FromMilliseconds(10);
        await using var wake = _CreateWake();
        await wake.EnsureSubscribedAsync(AbortToken);
        var readStartedAt = _timeProvider.GetTimestamp();
        _onWake!(_WakeChannel, RedisValue.EmptyString);

        // when
        var wait = wake.WaitAsync(pollDelay, readStartedAt, AbortToken);
        _timeProvider.Advance(pollDelay);

        // then
        await wait.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_keep_a_wake_up_that_arrives_during_a_read_for_the_next_wait()
    {
        // given
        await using var wake = _CreateWake();
        await wake.EnsureSubscribedAsync(AbortToken);
        var readStartedAt = _timeProvider.GetTimestamp();

        // when: the announcement lands while no wait is underway
        _onWake!(_WakeChannel, RedisValue.EmptyString);
        _timeProvider.Advance(RedisStreamWake.MinWokenReadInterval);

        // then
        await wake.WaitAsync(_PollDelay, readStartedAt, AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_poll_and_warn_once_when_the_subscription_keeps_failing()
    {
        // given
        _subscriber
            .SubscribeAsync(_WakeChannel, Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new InvalidOperationException("NOPERM this user has no permissions to access the channel"));
        var logger = new WarningCounter();
        await using var wake = new RedisStreamWake([_Stream], _connectionPool, _timeProvider, logger);

        // when
        await wake.EnsureSubscribedAsync(AbortToken);
        await wake.EnsureSubscribedAsync(AbortToken);
        var wait = wake.WaitAsync(_PollDelay, _timeProvider.GetTimestamp(), AbortToken);
        _timeProvider.Advance(_PollDelay - TimeSpan.FromSeconds(1));
        await Task.Yield();

        // then
        wait.IsCompleted.Should().BeFalse("without a subscription the loop waits the full poll interval");
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await wait.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        logger.Warnings.Should().Be(1, "a failure streak logs one warning");
    }

    [Fact]
    public async Task should_remove_only_its_own_handler_when_disposed()
    {
        // given
        var wake = _CreateWake();
        await wake.EnsureSubscribedAsync(AbortToken);

        // when
        await wake.DisposeAsync();

        // then
        await _subscriber.Received(1).UnsubscribeAsync(_WakeChannel, _onWake, Arg.Any<CommandFlags>());
        await _subscriber.DidNotReceive().UnsubscribeAllAsync(Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_end_the_wait_when_cancelled()
    {
        // given
        await using var wake = _CreateWake();
        await wake.EnsureSubscribedAsync(AbortToken);
        using var cts = new CancellationTokenSource();

        // when
        var act = async () =>
        {
            var wait = wake.WaitAsync(_PollDelay, _timeProvider.GetTimestamp(), cts.Token);
            await cts.CancelAsync();
            await wait;
        };

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class WarningCounter : ILogger
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }

    private RedisStreamWake _CreateWake() =>
        new([_Stream], _connectionPool, _timeProvider, LoggerFactory.CreateLogger<RedisStreamWake>());

    private RedisStreamManager _CreateManager(Action<RedisMessagingOptions>? configure = null)
    {
        var options = new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") };
        configure?.Invoke(options);

        return new RedisStreamManager(
            _connectionPool,
            Options.Create(options),
            LoggerFactory.CreateLogger<RedisStreamManager>(),
            _timeProvider
        );
    }
}
