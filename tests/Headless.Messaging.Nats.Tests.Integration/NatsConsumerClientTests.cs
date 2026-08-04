// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Nats")]
public sealed class NatsConsumerClientTests(NatsFixture fixture) : TransportConsumerConformanceTestsBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    protected override string ProviderName => "NATS";

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseNats(fixture.ConnectionString);
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers()
    {
        return base.should_round_trip_queue_message_body_and_headers();
    }

    [Fact]
    public override Task should_match_production_runtime_capabilities()
    {
        return base.should_match_production_runtime_capabilities();
    }

    [Fact]
    public async Task should_fan_out_bus_message_to_distinct_real_subscriptions()
    {
        RequireSupport(TransportConformanceScenario.BusRoundTrip);
        var streamName = $"bus-{Guid.NewGuid():N}"[..29];
        var destination = $"{streamName}.probe";
        await using var first = await fixture.CreateBusSessionAsync(
            streamName,
            destination,
            $"group-{Guid.NewGuid():N}"[..30],
            AbortToken
        );
        await using var second = await fixture.CreateBusSessionAsync(
            streamName,
            destination,
            $"group-{Guid.NewGuid():N}"[..30],
            AbortToken
        );

        await TransportBusConformance.AssertFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public Task should_fan_out_one_bus_copy_per_group_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(
            new NatsProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(
            new NatsProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_isolate_same_logical_name_across_bus_and_queue()
    {
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(
            new NatsProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_terminally_acknowledge_malformed_envelope_across_consumer_restart()
    {
        var streamName = $"malformed-{Guid.NewGuid():N}"[..30];
        var destination = $"{streamName}.probe";
        var group = $"group-{Guid.NewGuid():N}"[..30];
        var terminalLogs = 0;
        await using var session = await fixture.CreateMalformedSessionAsync(streamName, destination, group, AbortToken);
        await session.StartAsync(
            onLog: log =>
            {
                if (log.Reason?.Contains("terminally acknowledged", StringComparison.Ordinal) == true)
                {
                    Interlocked.Increment(ref terminalLogs);
                }
            },
            cancellationToken: AbortToken
        );

        var result = await session.PublishAsync(_Message(destination, MessageLane.Queue), AbortToken);
        result.Succeeded.Should().BeTrue();

        using (var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken))
        {
            while (Volatile.Read(ref terminalLogs) == 0)
            {
                await Task.Delay(20, timeout.Token);
            }
        }

        await session.StopAsync(TimeSpan.FromSeconds(2));
        await using var replacement = await session.CreateReplacementAsync(AbortToken);
        await replacement.StartAsync(cancellationToken: AbortToken);

        (await replacement.RemainsEmptyAsync(TimeSpan.FromSeconds(3), AbortToken)).Should().BeTrue();
        Volatile.Read(ref terminalLogs).Should().Be(1);
    }

    [Fact]
    public async Task should_drain_legacy_stream_before_lane_cutover_and_reconcile_forward()
    {
        var logicalName = $"legacy-{Guid.NewGuid():N}"[..29];
        var legacyStream = logicalName;
        var legacyGroup = $"legacy-group-{Guid.NewGuid():N}"[..30];
        var legacyMessageId = $"legacy-{Guid.NewGuid():N}";
        await fixture.EnsureStreamAsync(legacyStream, logicalName);

        await using (
            var abortedConsumer = await PreviousMessagingPackageProbe.StartConsumerAsync(
                "nats",
                "queue",
                fixture.ConnectionString,
                logicalName,
                legacyGroup,
                legacyMessageId,
                AbortToken
            )
        )
        {
            await PreviousMessagingPackageProbe.ProduceAsync(
                "nats",
                "queue",
                fixture.ConnectionString,
                logicalName,
                legacyGroup,
                legacyMessageId,
                AbortToken
            );
            await abortedConsumer.WaitUntilReceivedAsync(AbortToken);

            var connection = await fixture.GetConnectionAsync();
            var js = new NatsJSContext(connection);
            var durable = $"queue-{logicalName}";
            var pending = await js.GetConsumerAsync(legacyStream, durable, AbortToken);
            pending.Info.NumAckPending.Should().Be(1, "the drain fence must observe unsettled previous-version work");

            await abortedConsumer.AbortAsync(AbortToken);
            abortedConsumer.HasExited.Should().BeTrue("the version fence stops the old process before cutover");
        }

        await using (
            var drainConsumer = await PreviousMessagingPackageProbe.StartConsumerAsync(
                "nats",
                "queue",
                fixture.ConnectionString,
                logicalName,
                legacyGroup,
                legacyMessageId,
                AbortToken
            )
        )
        {
            await drainConsumer.WaitUntilReceivedAsync(AbortToken);
            await drainConsumer.CommitAsync(AbortToken);
            drainConsumer.HasExited.Should().BeTrue("the old consumer must exit before new topology is provisioned");
        }

        var nats = new NatsJSContext(await fixture.GetConnectionAsync());
        var drained = await nats.GetConsumerAsync(legacyStream, $"queue-{logicalName}", AbortToken);
        drained.Info.NumAckPending.Should().Be(0);
        drained.Info.NumPending.Should().Be(0, "zero pending and zero ack-pending is the cutover drain signal");

        await using var bus = await fixture.CreateLaneSessionAsync(
            MessageLane.Bus,
            $"cutover-{Guid.NewGuid():N}"[..29],
            logicalName,
            "orders-subscribers",
            AbortToken
        );
        await using var queue = await fixture.CreateLaneSessionAsync(
            MessageLane.Queue,
            $"cutover-{Guid.NewGuid():N}"[..29],
            logicalName,
            logicalName,
            AbortToken
        );
        await bus.StartAsync(cancellationToken: AbortToken);
        await queue.StartAsync(cancellationToken: AbortToken);

        (await bus.PublishAsync(_Message(logicalName, MessageLane.Bus), AbortToken)).Succeeded.Should().BeTrue();
        var busDelivery = await bus.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await bus.Consumer.CommitAsync(busDelivery.SettlementValue, AbortToken);
        (await queue.RemainsEmptyAsync(TimeSpan.FromSeconds(1), AbortToken)).Should().BeTrue();

        (await queue.PublishAsync(_Message(logicalName, MessageLane.Queue), AbortToken)).Succeeded.Should().BeTrue();
        var queueDelivery = await queue.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await queue.Consumer.CommitAsync(queueDelivery.SettlementValue, AbortToken);
        (await bus.RemainsEmptyAsync(TimeSpan.FromSeconds(1), AbortToken)).Should().BeTrue();

        await bus.StopAsync(TimeSpan.FromSeconds(2));
        await using var restartedBus = await bus.CreateReplacementAsync(AbortToken);
        await restartedBus.StartAsync(cancellationToken: AbortToken);
        (await bus.PublishAsync(_Message(logicalName, MessageLane.Bus), AbortToken)).Succeeded.Should().BeTrue();
        var restartedDelivery = await restartedBus.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await restartedBus.Consumer.CommitAsync(restartedDelivery.SettlementValue, AbortToken);

        var reconciled = await nats.GetConsumerAsync(legacyStream, $"queue-{logicalName}", AbortToken);
        reconciled.Info.NumAckPending.Should().Be(0);
        reconciled
            .Info.NumPending.Should()
            .Be(
                0,
                "after first lane-qualified publish recovery is roll-forward and must not repopulate legacy topology"
            );
    }

    private static TransportMessage _Message(string logicalName, MessageLane lane) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
                [MessagingHeaders.MessageName] = logicalName,
                [MessagingHeaders.Intent] = lane.ToString(),
            },
            "cutover"u8.ToArray()
        );

    [Fact]
    public override Task should_dispatch_empty_message_body()
    {
        return base.should_dispatch_empty_message_body();
    }

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery()
    {
        return base.should_commit_real_delivery_and_prevent_redelivery();
    }

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery()
    {
        return base.should_reject_real_delivery_and_observe_redelivery();
    }

    [Fact]
    public override Task should_isolate_unique_destinations()
    {
        return base.should_isolate_unique_destinations();
    }

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound()
    {
        return base.should_shutdown_idle_consumer_within_bound();
    }

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active()
    {
        return base.should_bound_shutdown_while_handler_is_active();
    }

    [Fact]
    public async Task should_accept_real_delivery_value_for_commit_callback()
    {
        // given
        var streamName = $"consume-commit-{Guid.NewGuid():N}"[..30];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        var topics = await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync(topics, AbortToken);

        var received = new TaskCompletionSource<(TransportMessage msg, object? sender)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.OnMessageCallback = (msg, sender) =>
        {
            received.TrySetResult((msg, sender));
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // when — start listening, then publish
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken);

            var body = "hello-commit"u8.ToArray();
            await _PublishAsync(subject, body);

            var (transportMsg, natsMsg) = await received.Task.WaitAsync(cts.Token);

            // then — message received with correct body
            transportMsg.Body.ToArray().Should().BeEquivalentTo(body);
            transportMsg.Headers[MessagingHeaders.Group].Should().Be("test-group");

            // commit should not throw
            await client.CommitAsync(natsMsg, AbortToken);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_accept_real_delivery_value_for_reject_callback()
    {
        // given
        var streamName = $"consume-reject-{Guid.NewGuid():N}"[..30];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var received = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (_, sender) =>
        {
            received.TrySetResult(sender);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // when — start listening, then publish
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken);
            await _PublishAsync(subject, "hello-reject"u8.ToArray());

            var natsMsg = await received.Task.WaitAsync(cts.Token);

            // then — reject (nak) should not throw
            await client.RejectAsync(natsMsg, AbortToken);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_create_stream_when_fetch_message_names_async_enabled()
    {
        // given
        var streamName = $"autocreate-{Guid.NewGuid():N}"[..25];
        var subject = $"{streamName}.orders";

        var options = _CreateOptions(enableStreamCreation: true);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when — FetchMessageNamesAsync with EnableSubscriberClientStreamAndSubjectCreation=true
        var result = await client.FetchMessageNamesAsync([subject], AbortToken);

        // then — stream should exist on the NATS server
        result.Should().Contain(subject);

        var conn = await fixture.GetConnectionAsync();
        var js = new NatsJSContext(conn);
        var stream = await js.GetStreamAsync(
            NatsPhysicalAddress.Stream(MessageLane.Bus, streamName),
            cancellationToken: AbortToken
        );
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task should_apply_stream_options_callback_when_fetch_message_names_async()
    {
        // given
        var streamName = $"stropts-{Guid.NewGuid():N}"[..22];
        var subject = $"{streamName}.events";

        var opts = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = true,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
            }
        );

        await using var client = new NatsConsumerClient("test-group", 0, opts, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        await client.FetchMessageNamesAsync([subject], AbortToken);

        // then — stream should use Memory storage (from callback)
        var conn = await fixture.GetConnectionAsync();
        var js = new NatsJSContext(conn);
        var stream = await js.GetStreamAsync(
            NatsPhysicalAddress.Stream(MessageLane.Bus, streamName),
            cancellationToken: AbortToken
        );
        var info = stream.Info;
        info.Config.Storage.Should().Be(StreamConfigStorage.Memory);
    }

    [Theory]
    [InlineData(MessageLane.Bus, false)]
    [InlineData(MessageLane.Bus, true)]
    [InlineData(MessageLane.Queue, false)]
    [InlineData(MessageLane.Queue, true)]
    public async Task should_reject_consumer_options_lane_override_before_readiness(
        MessageLane lane,
        bool overrideDeliveryPolicy
    )
    {
        var streamName = $"consumer-guard-{Guid.NewGuid():N}"[..29];
        var subject = $"{streamName}.events";
        var options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = true,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
                ConsumerOptions = config =>
                {
                    if (overrideDeliveryPolicy)
                    {
                        config.DeliverPolicy =
                            lane == MessageLane.Bus ? ConsumerConfigDeliverPolicy.All : ConsumerConfigDeliverPolicy.New;
                    }
                    else
                    {
                        config.FilterSubject =
                            lane == MessageLane.Bus ? "headless.queue.redirected" : "headless.bus.redirected";
                    }
                },
            }
        );
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider, lane: lane);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var act = async () => await client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*provider-owned*lane topology*");
    }

    [Fact]
    public async Task should_allow_consumer_options_acknowledgement_tuning()
    {
        var streamName = $"consumer-tuning-{Guid.NewGuid():N}"[..29];
        var subject = $"{streamName}.events";
        var options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = true,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
                ConsumerOptions = config => config.AckWait = TimeSpan.FromSeconds(5),
            }
        );
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token).AsTask();
        await client.WaitUntilReadyAsync(AbortToken);

        listening.IsFaulted.Should().BeFalse();
        await _StopListeningAsync(listening, cts);
    }

    [Fact]
    public async Task should_receive_headers_from_published_message()
    {
        // given
        var streamName = $"headers-{Guid.NewGuid():N}"[..22];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (msg, _) =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // when — start listening, then publish with headers
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken);

            var conn = await fixture.GetConnectionAsync();
            var js = new NatsJSContext(conn);
            var headers = _CreateHeaders();
            headers.Add("X-Custom", "custom-value");
            await js.PublishAsync(
                NatsPhysicalAddress.Subject(MessageLane.Bus, subject),
                "body"u8.ToArray(),
                serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                headers: headers,
                cancellationToken: AbortToken
            );

            var transportMsg = await received.Task.WaitAsync(cts.Token);

            // then
            transportMsg.Headers["X-Custom"].Should().Be("custom-value");
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_throw_broker_connection_exception_for_bad_server_when_factory()
    {
        // given
        var badOptions = Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://localhost:19999", // no server here
                ConfigureConnection = o => o with { ConnectTimeout = TimeSpan.FromSeconds(2) },
            }
        );
        var factory = new NatsConsumerClientFactory(badOptions, _serviceProvider);

        // when
        var act = async () => await factory.CreateAsync("test-group", 1, MessageLane.Queue);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_pause_and_resume_consumer()
    {
        // given
        var streamName = $"pause-resume-{Guid.NewGuid():N}"[..28];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var messageReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageCount = 0;
        client.OnMessageCallback = (_, _) =>
        {
            Interlocked.Increment(ref messageCount);
            messageReceived.TrySetResult();
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // when — start listening, pause, publish, verify no delivery
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await client.WaitUntilReadyAsync(AbortToken);

            await client.PauseAsync(AbortToken);
            await _PublishAsync(subject, "paused-msg"u8.ToArray());
            await Task.Delay(1000, AbortToken);

            var countWhilePaused = Volatile.Read(ref messageCount);

            // resume and wait for delivery via signal
            await client.ResumeAsync(AbortToken);
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

            var countAfterResume = Volatile.Read(ref messageCount);

            // then
            countWhilePaused.Should().Be(0);
            countAfterResume.Should().BePositive();
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    private IOptions<NatsMessagingOptions> _CreateOptions(bool enableStreamCreation)
    {
        return Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = enableStreamCreation,
            }
        );
    }

    private async Task _EnsureStreamAsync(string streamName, string subjectPattern)
    {
        await fixture.EnsureStreamAsync(
            NatsPhysicalAddress.Stream(MessageLane.Bus, streamName),
            NatsPhysicalAddress.Subject(MessageLane.Bus, subjectPattern),
            StreamConfigRetention.Interest
        );
    }

    private async Task _PublishAsync(string subject, byte[] body)
    {
        var conn = await fixture.GetConnectionAsync();
        var js = new NatsJSContext(conn);
        await js.PublishAsync(
            NatsPhysicalAddress.Subject(MessageLane.Bus, subject),
            new ReadOnlyMemory<byte>(body),
            serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
            headers: _CreateHeaders(),
            cancellationToken: AbortToken
        );
    }

    private static NatsHeaders _CreateHeaders()
    {
        return new NatsHeaders
        {
            { MessagingHeaders.MessageId, Guid.NewGuid().ToString("N") },
            { MessagingHeaders.MessageName, "TestEvent" },
        };
    }

    private static async Task _StopListeningAsync(Task listeningTask, CancellationTokenSource cts)
    {
        await cts.CancelAsync();

        try
        {
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
