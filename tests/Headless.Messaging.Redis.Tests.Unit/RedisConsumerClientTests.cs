// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
    public async Task should_create_no_consumer_group_when_subscribing_to_the_deployed_topology()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("my-group", 1, _mockStreamManager, _options, logger);

        // when
        await client.SubscribeAsync(["messageName-1", "messageName-2"], AbortToken);

        // then - resources provision creates the groups; the host only reads them
        await _mockStreamManager
            .DidNotReceiveWithAnyArgs()
            .CreateStreamWithConsumerGroupAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_create_its_consumer_groups_when_subscribing_with_a_topology_of_its_own()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = _CreateProvisioningClient("my-group", logger);

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
    public async Task should_be_ready_once_the_first_group_read_reaches_the_server()
    {
        // given a pending read that fails once, then reaches the server
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _SetupGroupReads();
        _mockStreamManager
            .PollStreamsPendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => _ReadsAfterOneFailure(callInfo.ArgAt<Action<Exception?>?>(4), firstRead, release));
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // when
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listen = client.ListeningAsync(TimeSpan.FromMilliseconds(10), listening.Token).AsTask();
        await firstRead.Task.Bounded();
        var readyBeforeSuccess = client.WaitUntilReadyAsync(AbortToken).AsTask();

        // then - a failed read proves nothing about the group; the first one that reaches it does
        readyBeforeSuccess.IsCompleted.Should().BeFalse();
        release.TrySetResult();
        await readyBeforeSuccess.Bounded();

        await listening.CancelAsync();
        await listen;
    }

    [Fact]
    public async Task should_fail_readiness_naming_resources_provision_when_the_consumer_group_is_missing()
    {
        // given a deployment that never provisioned the group
        var missing = new ResourceProvisioningException("Run 'resources provision'");
        _SetupGroupReads();
        _mockStreamManager
            .PollStreamsPendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_FaultingReads(missing));
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // when
        var listen = () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).Bounded();
        var ready = () => client.WaitUntilReadyAsync(AbortToken).AsTask().Bounded();

        // then
        await listen.Should().ThrowExactlyAsync<ResourceProvisioningException>();
        (await ready.Should().ThrowExactlyAsync<ResourceProvisioningException>()).Which.Should().BeSameAs(missing);
    }

    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _ReadsAfterOneFailure(
        Action<Exception?>? onRead,
        TaskCompletionSource firstRead,
        TaskCompletionSource release
    )
    {
        onRead?.Invoke(new RedisException("No connection is available"));
        firstRead.TrySetResult();
        yield return [];

        await release.Task;
        onRead?.Invoke(null);
    }

    private RedisConsumerClient _CreateProvisioningClient(
        string subscriptionName,
        ILogger<RedisConsumerClient> logger,
        MessageLane lane = MessageLane.Queue
    ) => new(subscriptionName, 1, _mockStreamManager, _options, logger, lane, provisionsTopology: true);

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
            .PollStreamsFromAsync(
                tails,
                TimeSpan.FromMilliseconds(10),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            );
        await _mockStreamManager
            .DidNotReceiveWithAnyArgs()
            .CreateStreamWithConsumerGroupAsync(default!, default!, AbortToken);
        _mockStreamManager
            .DidNotReceiveWithAnyArgs()
            .PollStreamsLatestMessagesAsync(default!, default!, default!, default, default, AbortToken);
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
            .PollStreamsFromAsync(
                Arg.Any<StreamPosition[]>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
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
                new NameValueEntry("body", "payload"u8.ToArray()),
            ]
        );
        _mockStreamManager
            .GetStreamTailPositionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns([new StreamPosition(stream, StreamPosition.Beginning)]);
        _mockStreamManager
            .PollStreamsFromAsync(
                Arg.Any<StreamPosition[]>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
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

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public async Task should_raise_the_reestablished_signal_before_dispatch_when_an_every_instance_read_gap_reaches_half_the_stream_age(
        int gapMinutes,
        bool expectSignal
    )
    {
        // given a read, then a failed read, then a successful one after the gap (EveryInstanceStreamMaxAge is 5 minutes)
        const string stream = "headless:messaging:bus:orders.created";
        var timeProvider = new FakeTimeProvider();
        _mockStreamManager
            .GetStreamTailPositionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns([new StreamPosition(stream, StreamPosition.Beginning)]);
        _mockStreamManager
            .PollStreamsFromAsync(
                Arg.Any<StreamPosition[]>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                _ReadsAfterGap(
                    callInfo.ArgAt<Action<Exception?>?>(2),
                    timeProvider,
                    TimeSpan.FromMinutes(gapMinutes),
                    new RedisStreamMessages(stream, [_ValidEntry("2-0")])
                )
            );
        await using var client = new RedisConsumerClient(
            "billing.cache",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>(),
            MessageLane.Bus,
            timeProvider,
            ConsumerSubscriptionKind.EveryInstance
        );
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            (_, _) =>
            {
                events.Enqueue("delivered");
                delivered.TrySetResult();
                return Task.CompletedTask;
            },
            onLog: null
        );
        client.AttachReestablishedCallback(_ =>
        {
            events.Enqueue("reestablished");
            return Task.CompletedTask;
        });
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening;

        // then
        events.Should().Equal(expectSignal ? ["reestablished", "delivered"] : ["delivered"]);
    }

    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _ReadsAfterGap(
        Action<Exception?>? onRead,
        FakeTimeProvider timeProvider,
        TimeSpan gap,
        RedisStreamMessages afterGap
    )
    {
        onRead?.Invoke(null);
        yield return [];

        onRead?.Invoke(new RedisException("No connection is available"));
        yield return [];

        timeProvider.Advance(gap);
        onRead?.Invoke(null);
        yield return [afterGap];

        await Task.CompletedTask;
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
                new NameValueEntry("body", "payload"u8.ToArray()),
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
        await using var client = _CreateProvisioningClient("my-group", logger);
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
        await using var client = _CreateProvisioningClient("billing", logger, MessageLane.Bus);

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
    public async Task should_leave_rejected_entry_pending_for_the_claim_pass()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);
        var sender = new RedisConsumerDelivery("test-stream", "test-group", "1234567-0");

        // when
        await client.RejectAsync(sender, AbortToken);

        // then
        _mockStreamManager.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_release_a_delayed_reject_at_once_because_the_claim_pass_already_waits()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);
        var sender = new RedisConsumerDelivery("test-stream", "test-group", "1234567-0");

        // when
        await client
            .RejectAsync(sender, TimeSpan.FromMinutes(5), AbortToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: the entry stays pending for PendingClaimMinIdleTime
        _mockStreamManager.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_under_the_leased_name_and_claim_after_the_configured_min_idle_time()
    {
        // given
        var options = Options.Create(
            new RedisMessagingOptions
            {
                Configuration = ConfigurationOptions.Parse("localhost:6379"),
                PendingClaimMinIdleTime = TimeSpan.FromSeconds(42),
            }
        );
        var names = new RedisConsumerNames("host-a");
        var lease = names.Acquire("orders");
        _SetupGroupReads();
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            options,
            LoggerFactory.CreateLogger<RedisConsumerClient>(),
            consumerName: lease
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        await cts.CancelAsync();
        await listening;

        // then
        _mockStreamManager
            .Received(1)
            .PollStreamsPendingMessagesAsync(
                Arg.Any<string[]>(),
                "orders",
                "orders:host-a:0",
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            );
        _mockStreamManager
            .Received(1)
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                "orders",
                "orders:host-a:0",
                TimeSpan.FromSeconds(42),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            );
        _mockStreamManager
            .Received(1)
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                "orders",
                "orders:host-a:0",
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_release_the_consumer_name_once_shut_down()
    {
        // given
        var names = new RedisConsumerNames("host-a");
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>(),
            consumerName: names.Acquire("orders")
        );

        // when
        var whileLive = names.Acquire("orders");
        await client.ShutdownAsync(TimeSpan.FromSeconds(1), AbortToken);
        var afterShutdown = names.Acquire("orders");

        // then
        whileLive.Name.Should().Be("orders:host-a:1");
        afterShutdown.Name.Should().Be("orders:host-a:0");
    }

    [Fact]
    public async Task should_drain_an_in_flight_handler_before_shutdown_completes()
    {
        // given
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerFinished = false;
        _SetupGroupReads(new RedisStreamMessages("headless:messaging:queue:orders.created", [_ValidEntry("1-0")]));
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        client.AttachCallbacks(
            async (_, _) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.Bounded();
                handlerFinished = true;
            },
            onLog: null
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening;

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(5), AbortToken).AsTask();
        var completedBeforeRelease = shutdown.IsCompleted;
        releaseHandler.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        completedBeforeRelease.Should().BeFalse();
        handlerFinished.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_the_read_loop_before_disposing_when_shutdown_begins_without_cancellation()
    {
        // given
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = false;
        _SetupGroupReads();
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                _DelayedMessages(
                    readEntered,
                    completeRead.Task,
                    new RedisStreamMessages("headless:messaging:queue:orders.created", [_ValidEntry("1-0")])
                )
            );
        var logger = new CapturingLogger<RedisConsumerClient>();
        var timeProvider = new DrainWatchingTimeProvider();
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            logger,
            timeProvider: timeProvider
        );
        client.AttachCallbacks(
            (_, _) =>
            {
                handled = true;
                return Task.CompletedTask;
            },
            onLog: null
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).AsTask();
        await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(30), AbortToken).AsTask();

        // A shutdown that waits for the read loop arms the drain's timeout timer and then stays pending; one that does
        // not finishes without ever arming it. Exactly one of the two happens, so no wait is needed to tell them apart.
        var completedBeforeRead = await Task.WhenAny(shutdown, timeProvider.DrainWaiting) == shutdown;
        completeRead.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        completedBeforeRead.Should().BeFalse("shutdown waits for the read loop before it disposes the semaphore");
        handled.Should().BeFalse("an entry read after shutdown began stays pending for the claim");
        logger.Entries.Should().NotContain(log => log.EventId == 3007, "the loop never touches a disposed semaphore");
    }

    [Fact]
    public async Task should_bound_shutdown_when_a_handler_never_finishes()
    {
        // given
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _SetupGroupReads(new RedisStreamMessages("headless:messaging:queue:orders.created", [_ValidEntry("1-0")]));
        var logger = new CapturingLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("orders", 1, _mockStreamManager, _options, logger);
        client.AttachCallbacks(
            async (_, _) =>
            {
                handlerStarted.TrySetResult();
                await neverReleased.Task.Bounded();
            },
            onLog: null
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening;

        try
        {
            // when
            await client
                .ShutdownAsync(TimeSpan.FromMilliseconds(100), AbortToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then
            logger.Entries.Should().Contain(log => log.EventId == 3008);
        }
        finally
        {
            neverReleased.SetResult();
        }
    }

    [Fact]
    public async Task should_end_listening_with_the_fault_when_the_new_message_read_faults_while_the_stale_read_polls()
    {
        // given: a group subscription whose stale-pending read keeps polling while its new-message read faults
        var staleStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _SetupGroupReads();
        _mockStreamManager
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ReadsUntilCancelled(staleStopped, AbortToken));
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_FaultingReads(new InvalidOperationException("new-message read failed")));
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // when
        var act = () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).Bounded();

        // then: listening ends with the read's fault, so the consumer register marks the subscription failed, and the
        // stale-pending read stops instead of claiming entries for a subscription about to be rebuilt
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("new-message read failed");
        await staleStopped.Task.Bounded();
    }

    [Fact]
    public async Task should_end_listening_with_the_fault_when_the_stale_pending_read_faults()
    {
        // given
        var newStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _SetupGroupReads();
        _mockStreamManager
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_FaultingReads(new InvalidOperationException("stale-pending read failed")));
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ReadsUntilCancelled(newStopped, AbortToken));
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // when
        var act = () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).Bounded();

        // then
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("stale-pending read failed");
        await newStopped.Task.Bounded();
    }

    [Fact]
    public async Task should_end_listening_with_the_fault_when_the_every_instance_read_faults()
    {
        // given
        _mockStreamManager
            .GetStreamTailPositionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns([new StreamPosition("headless:messaging:bus:orders.created", StreamPosition.Beginning)]);
        _mockStreamManager
            .PollStreamsFromAsync(
                Arg.Any<StreamPosition[]>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_FaultingReads(new InvalidOperationException("every-instance read failed")));
        await using var client = _CreateEveryInstanceClient(LoggerFactory.CreateLogger<RedisConsumerClient>());
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // when
        var act = () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).Bounded();

        // then
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("every-instance read failed");
    }

    [Fact]
    public async Task should_stop_listening_when_the_reads_end_without_being_cancelled()
    {
        // given: reads that end on their own, as a read that meets an entry it cannot handle does
        _SetupGroupReads();
        await using var client = new RedisConsumerClient(
            "orders",
            1,
            _mockStreamManager,
            _options,
            LoggerFactory.CreateLogger<RedisConsumerClient>()
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // when
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).AsTask();

        // then: listening returns while its token is not cancelled, which the consumer register reads as a failure
        await listening.Bounded();
        AbortToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_stop_both_reads_and_report_no_fault_when_listening_is_cancelled()
    {
        // given
        var staleStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _SetupGroupReads();
        _mockStreamManager
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ReadsUntilCancelled(staleStopped, AbortToken));
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ReadsUntilCancelled(newStopped, AbortToken));
        var logger = new CapturingLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("orders", 1, _mockStreamManager, _options, logger);
        client.AttachCallbacks((_, _) => Task.CompletedTask, onLog: null);
        await client.SubscribeAsync(["orders.created"], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        await cts.CancelAsync();
        await listening.Bounded();

        // then
        await staleStopped.Task.Bounded();
        await newStopped.Task.Bounded();
        logger.Entries.Should().NotContain(log => log.Exception != null);
    }

    private void _SetupGroupReads(RedisStreamMessages? pending = null)
    {
        _mockStreamManager
            .PollStreamsPendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages(pending));
        _mockStreamManager
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());
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
                Arg.Any<Action<Exception?>?>(),
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
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
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

    // A read that keeps polling, with nothing to hand out, until listening stops it.
    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _ReadsUntilCancelled(
        TaskCompletionSource stopped,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        yield return [];

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (cancellationToken.Register(() => cancelled.TrySetResult()))
        {
            await cancelled.Task;
        }

        stopped.TrySetResult();
        cancellationToken.ThrowIfCancellationRequested();
    }

    // A read that ends with a fault the stream manager did not retry, after one empty batch.
    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _FaultingReads(Exception fault)
    {
        yield return [];

        await Task.CompletedTask;

        throw fault;
    }

    // A read that returns its entries as shutdown cancels it: Redis had already answered the XREADGROUP.
    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _DelayedMessages(
        TaskCompletionSource readEntered,
        Task completeRead,
        RedisStreamMessages stream
    )
    {
        readEntered.TrySetResult();
        await completeRead;

        yield return [stream];
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

    [Fact]
    public async Task should_report_failed_reads_as_broker_unreachable_and_the_recovery_once_as_reachable()
    {
        // given a read loop that cannot reach Redis twice, then reads it on every later poll
        _SetupGroupReads();
        var allReadsConsumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockStreamManager
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                _ReadsWithOutcomes(
                    callInfo.ArgAt<Action<Exception?>?>(4),
                    allReadsConsumed,
                    new RedisException("No connection is available"),
                    new RedisException("No connection is available"),
                    null,
                    null
                )
            );
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);
        var reports = new System.Collections.Concurrent.ConcurrentQueue<MqLogType>();
        client.AttachCallbacks((_, _) => Task.CompletedTask, log => reports.Enqueue(log.LogType));
        await client.SubscribeAsync(["orders"], AbortToken);

        // when
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listen = client.ListeningAsync(TimeSpan.FromSeconds(1), listening.Token).AsTask();

        // The reads run in order, so every outcome, the repeated success included, has been reported or not by the
        // time the loop asks for the batch after the last one.
        await allReadsConsumed.Task.Bounded();

        await listening.CancelAsync();
        await listen;

        // then the later successful read is not reported again
        reports.Should().Equal(MqLogType.BrokerUnreachable, MqLogType.BrokerUnreachable, MqLogType.BrokerReachable);
    }

    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _ReadsWithOutcomes(
        Action<Exception?>? onRead,
        TaskCompletionSource allConsumed,
        params Exception?[] outcomes
    )
    {
        foreach (var outcome in outcomes)
        {
            onRead?.Invoke(outcome);
            yield return [];
        }

        allConsumed.TrySetResult();

        await Task.CompletedTask;
    }

    // Reports the timer a drain arms for its timeout, which only happens while it waits on a handler or read loop.
    private sealed class DrainWatchingTimeProvider : FakeTimeProvider
    {
        private readonly TaskCompletionSource _drainWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DrainWaiting => _drainWaiting.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);

            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                _drainWaiting.TrySetResult();
            }

            return timer;
        }
    }

    private sealed record LogEntry(int EventId, string Message, Exception? Exception);
}
