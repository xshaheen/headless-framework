// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

// ReSharper disable DisposeOnUsingVariable
/// <summary>
/// Unit tests for <see cref="RedisConsumerClient"/>.
/// </summary>
public sealed class RedisConsumerClientTests : TestBase
{
    private readonly IRedisStreamManager _mockStreamManager = Substitute.For<IRedisStreamManager>();

    private readonly IOptions<RedisMessagingOptions> _options = Options.Create(
        new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
    );

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when
        var address = client.BrokerAddress;

        // then
        address.Name.Should().Be("redis");
        address.Endpoint.Should().Be("localhost:6379");
    }

    [Fact]
    public async Task should_create_consumer_group_when_subscribing()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("my-group", 1, _mockStreamManager, _options, logger);

        var messageNames = new[] { "messageName-1", "messageName-2" };

        // when
        await client.SubscribeAsync(messageNames, AbortToken);

        // then
        await _mockStreamManager
            .Received(1)
            .CreateStreamWithConsumerGroupAsync(
                "headless:messaging:queue:messageName-1",
                "my-group",
                Arg.Any<CancellationToken>()
            );
        await _mockStreamManager
            .Received(1)
            .CreateStreamWithConsumerGroupAsync(
                "headless:messaging:queue:messageName-2",
                "my-group",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_start_every_instance_reads_at_stream_tails_without_creating_a_consumer_group()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        var tails = new[]
        {
            new StreamPosition("headless:messaging:bus:orders.created", "5-0"),
            new StreamPosition("headless:messaging:bus:orders.cancelled", StreamPosition.Beginning),
        };
        _mockStreamManager
            .GetStreamTailPositionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(tails);
        await using var client = _CreateEveryInstanceClient(logger);

        // when
        await client.SubscribeAsync(["orders.created", "orders.cancelled"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        await cts.CancelAsync();
        await listening;

        // then
        await _mockStreamManager
            .Received(1)
            .GetStreamTailPositionsAsync(
                Arg.Is<string[]>(x =>
                    x.SequenceEqual(
                        new[] { "headless:messaging:bus:orders.created", "headless:messaging:bus:orders.cancelled" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        _mockStreamManager
            .Received(1)
            .PollStreamsFromAsync(tails, TimeSpan.FromMilliseconds(10), Arg.Any<CancellationToken>());
        await _mockStreamManager
            .DidNotReceiveWithAnyArgs()
            .CreateStreamWithConsumerGroupAsync(default!, default!, AbortToken);
        _mockStreamManager
            .DidNotReceiveWithAnyArgs()
            .PollStreamsLatestMessagesAsync(default!, default!, default!, default, AbortToken);
        await client.WaitUntilReadyAsync(AbortToken);
    }

    [Fact]
    public async Task should_hand_every_instance_delivery_to_the_core_and_never_acknowledge_it()
    {
        // given
        const string stream = "headless:messaging:bus:orders.created";
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        _mockStreamManager
            .GetStreamTailPositionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns([new StreamPosition(stream, StreamPosition.Beginning)]);
        _mockStreamManager
            .PollStreamsFromAsync(Arg.Any<StreamPosition[]>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_Batches(new RedisStreamMessages(stream, [_ValidEntry("1-0")])));
        await using var client = _CreateEveryInstanceClient(logger);
        var delivered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            (_, sender) =>
            {
                delivered.TrySetResult(sender);
                return Task.CompletedTask;
            },
            onLog: null
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);

        // when
        var sender = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await client.CommitAsync(sender, AbortToken);
        await client.RejectAsync(sender, AbortToken);
        await cts.CancelAsync();
        await listening;

        // then
        sender.Should().Be(new RedisEveryInstanceDelivery(stream, "1-0"));
        await _mockStreamManager.DidNotReceiveWithAnyArgs().Ack(default!, default!, default!, AbortToken);
        await _mockStreamManager
            .DidNotReceiveWithAnyArgs()
            .RequeueAndAck(default!, default!, default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_stamp_stream_key_as_transport_address_when_wire_header_spoofs_it()
    {
        // given
        const string stream = "headless:messaging:bus:orders.created";
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        var spoofedEntry = new StreamEntry(
            "1-0",
            [
                new NameValueEntry(
                    "headers",
                    $$"""{"{{Headers.MessageId}}":"{{Guid.NewGuid():N}}","{{Headers.MessageName}}":"orders.created","{{Headers.TransportAddress}}":"wire-spoofed"}"""
                ),
                new NameValueEntry("body", "\"cGF5bG9hZA==\""),
            ]
        );
        _mockStreamManager
            .GetStreamTailPositionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns([new StreamPosition(stream, StreamPosition.Beginning)]);
        _mockStreamManager
            .PollStreamsFromAsync(Arg.Any<StreamPosition[]>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_Batches(new RedisStreamMessages(stream, [spoofedEntry])));
        await using var client = _CreateEveryInstanceClient(logger);
        var delivered = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            (message, _) =>
            {
                delivered.TrySetResult(message);
                return Task.CompletedTask;
            },
            onLog: null
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);

        // when
        var message = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening;

        // then
        message.Headers[Headers.TransportAddress].Should().Be(stream);
    }

    private RedisConsumerClient _CreateEveryInstanceClient(ILogger<RedisConsumerClient> logger) =>
        new(
            "billing.cache",
            1,
            _mockStreamManager,
            _options,
            logger,
            MessageLane.Bus,
            kind: ConsumerSubscriptionKind.EveryInstance
        );

    private static StreamEntry _ValidEntry(string id) =>
        new(
            id,
            [
                new NameValueEntry(
                    "headers",
                    $$"""{"{{Headers.MessageId}}":"{{Guid.NewGuid():N}}","{{Headers.MessageName}}":"orders.created"}"""
                ),
                new NameValueEntry("body", "\"cGF5bG9hZA==\""),
            ]
        );

    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _Batches(params RedisStreamMessages[] batch)
    {
        yield return batch;
    }

    [Fact]
    public async Task should_propagate_exact_token_when_subscribing()
    {
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("my-group", 1, _mockStreamManager, _options, logger);
        using var cts = new CancellationTokenSource();

        await client.SubscribeAsync(["orders"], cts.Token);

        await _mockStreamManager
            .Received(1)
            .CreateStreamWithConsumerGroupAsync("headless:messaging:queue:orders", "my-group", cts.Token);
    }

    [Fact]
    public async Task should_lane_qualify_bus_streams()
    {
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient(
            "billing",
            1,
            _mockStreamManager,
            _options,
            logger,
            MessageLane.Bus
        );

        await client.SubscribeAsync(["orders"], AbortToken);

        await _mockStreamManager
            .Received(1)
            .CreateStreamWithConsumerGroupAsync("headless:messaging:bus:orders", "billing", AbortToken);
    }

    [Fact]
    public async Task should_throw_when_subscribing_to_null_topics()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when & then
        var action = async () => await client.SubscribeAsync(null!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_acknowledge_message_on_commit()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        var sender = ("test-stream", "test-group", "1234567-0");

        // when
        await client.CommitAsync(sender, AbortToken);

        // then
        await _mockStreamManager
            .Received(1)
            .Ack("test-stream", "test-group", "1234567-0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_complete_reject_without_error()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when & then - reject should complete without error
        var action = async () => await client.RejectAsync(null);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_requeue_and_ack_message_on_reject()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);
        var entries = new NameValueEntry[] { new("headers", "{}"), new("body", "[]") };
        var sender = new RedisConsumerDelivery("test-stream", "test-group", "1234567-0", entries);

        // when
        await client.RejectAsync(sender, AbortToken);

        // then
        await _mockStreamManager
            .Received(1)
            .RequeueAndAck("test-stream", "test-group", "1234567-0", entries, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when & then
        var action = async () => await client.DisposeAsync();
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_allow_setting_callbacks()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        Func<TransportMessage, object?, Task> messageCallback = (_, _) => Task.CompletedTask;
        Action<LogMessageEventArgs> logCallback = _ => { };

        // when
        client.OnMessageCallback = messageCallback;
        client.OnLogCallback = logCallback;

        // then
        client.OnMessageCallback.Should().BeSameAs(messageCallback);
        client.OnLogCallback.Should().BeSameAs(logCallback);
    }

    [Fact]
    public async Task should_sanitize_malformed_entry_across_diagnostic_surfaces()
    {
        // given
        const string secret = "sentinel-secret-value";
        var entry = new StreamEntry(
            "1234567-0",
            [new NameValueEntry("headers", $"{{\"secret\":\"{secret}"), new NameValueEntry("body", secret)]
        );
        var stream = new RedisStreamMessages("test-stream", [entry]);
        _mockStreamManager
            .PollStreamsPendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages(stream));
        _mockStreamManager
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());

        var consumeError = new TaskCompletionSource<RedisMessagingOptions.ConsumeErrorContext>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var transportLog = new TaskCompletionSource<LogMessageEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var options = Options.Create(
            new RedisMessagingOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                OnConsumeError = context =>
                {
                    consumeError.TrySetResult(context);
                    return Task.CompletedTask;
                },
            }
        );
        var logger = new CapturingLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 0, _mockStreamManager, options, logger);
        client.OnLogCallback = args => transportLog.TrySetResult(args);
        await client.SubscribeAsync(["test-stream"], AbortToken);
        using var listeningCancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var listening = client.ListeningAsync(TimeSpan.Zero, listeningCancellation.Token).AsTask();
        var context = await consumeError.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var logArgs = await transportLog.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await listeningCancellation.CancelAsync();
        await listening;

        // then
        context.Entry.Should().NotBeNull();
        context.Entry.Value.Id.Should().Be(entry.Id);
        context.Entry.Value.Values.Should().BeEmpty();
        context.Exception.ToString().Should().NotContain(secret);
        logArgs.Reason.Should().NotContain(secret);
        logger.Entries.Should().NotBeEmpty();
        logger.Entries.Select(log => log.Message).Should().AllSatisfy(message => message.Should().NotContain(secret));
        var malformedEntryLog = logger.Entries.Should().ContainSingle(log => log.EventId == 3004).Which;
        malformedEntryLog.Exception.Should().NotBeNull();
        malformedEntryLog.Exception!.ToString().Should().NotContain(secret);
        await _mockStreamManager
            .Received(1)
            .Ack("test-stream", "test-group", entry.Id.ToString(), CancellationToken.None);
    }

    // -------------------------------------------------------------------------
    // PauseAsync / ResumeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task pause_async_is_idempotent_when_called_twice()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when
        await client.PauseAsync(AbortToken);
        await client.PauseAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task resume_async_is_noop_when_not_paused()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when
        await client.ResumeAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task pause_async_then_resume_async_completes_full_cycle()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when
        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);

        // then — no exception
    }

    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _Messages(
        RedisStreamMessages? stream = null
    )
    {
        if (stream is { } value)
        {
            yield return [value];
        }

        await Task.CompletedTask;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(eventId.Id, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(int EventId, string Message, Exception? Exception);
}
