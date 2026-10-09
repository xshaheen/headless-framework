// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    public Task should_fan_out_one_bus_copy_per_consumer_identity_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusConsumerIdentitiesAsync(
            new NatsProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_deliver_every_bus_message_to_every_every_instance_replica()
    {
        return TransportProviderConformance.AssertBusEveryInstanceAsync(
            new NatsProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_fan_out_bus_message_to_every_instance_sessions_of_one_identity()
    {
        var streamName = $"every-{Guid.NewGuid():N}"[..29];
        var destination = $"{streamName}.probe";
        var identity = $"group-{Guid.NewGuid():N}"[..30];
        await using var first = await fixture.CreateEndpointSessionAsync(
            _EveryInstanceEndpoint(destination, identity, "replica-1"),
            streamName,
            AbortToken
        );
        await using var second = await fixture.CreateEndpointSessionAsync(
            _EveryInstanceEndpoint(destination, identity, "replica-2"),
            streamName,
            AbortToken
        );

        await TransportBusConformance.AssertEveryInstanceFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public async Task should_leave_no_subscription_or_jetstream_consumer_after_every_instance_client_disposes()
    {
        // given
        var streamName = $"every-{Guid.NewGuid():N}"[..29];
        var destination = $"{streamName}.probe";
        var subject = $"headless.bus.{destination}";
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var session = await fixture.CreateEndpointSessionAsync(
            _EveryInstanceEndpoint(destination, $"group-{Guid.NewGuid():N}"[..30], "replica-1"),
            streamName,
            AbortToken
        );

        try
        {
            await session.StartAsync(cancellationToken: AbortToken);

            // Positive control: the listening client holds exactly one plain subscription and no JetStream consumer.
            (await fixture.CountSubscriptionsAsync(subject, AbortToken))
                .Should()
                .Be(1);
            (await _CountConsumersAsync(js, NatsPhysicalAddress.Stream(MessageLane.Bus, streamName))).Should().Be(0);
        }
        finally
        {
            // when
            await session.DisposeAsync();
        }

        // then
        (await fixture.CountSubscriptionsAsync(subject, AbortToken))
            .Should()
            .Be(0);
        (await _CountConsumersAsync(js, NatsPhysicalAddress.Stream(MessageLane.Bus, streamName))).Should().Be(0);
    }

    [Fact]
    public async Task should_report_one_gap_and_count_every_drop_when_the_every_instance_channel_overflows()
    {
        // given - a one-slot subscription channel and a consumer stuck on its first message
        var subject = $"overflow-{Guid.NewGuid():N}"[..30] + ".probe";
        var identity = $"group-{Guid.NewGuid():N}"[..30];
        var dropped = 0;
        using var listener = _ListenToDroppedEveryInstanceDeliveries(
            identity,
            () => Interlocked.Increment(ref dropped)
        );
        var options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                StreamProvisioning = NatsStreamProvisioning.Disabled,
                ConfigureConnection = opts => opts with { SubPendingChannelCapacity = 1 },
            }
        );
        await using var client = new NatsConsumerClient(
            identity,
            0,
            options,
            _serviceProvider,
            kind: ConsumerSubscriptionKind.EveryInstance
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gaps = 0;
        client.AttachCallbacks(onMessage: (_, _) => release.Task, onLog: _ => { });
        client.AttachReestablishedCallback(_ =>
        {
            Interlocked.Increment(ref gaps);
            return Task.CompletedTask;
        });
        await client.ConnectAsync(AbortToken);
        await client.SubscribeAsync([subject], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        try
        {
            await client.WaitUntilReadyAsync(AbortToken);

            // when - a burst far larger than the channel while the consumer is blocked
            var connection = await fixture.GetConnectionAsync();
            for (var i = 0; i < 50; i++)
            {
                await connection.PublishAsync(
                    NatsPhysicalAddress.Subject(MessageLane.Bus, subject),
                    new ReadOnlyMemory<byte>([1]),
                    headers: _CreateHeaders(),
                    serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                    cancellationToken: AbortToken
                );
            }

            await connection.PingAsync(AbortToken);
            await Task.Delay(NatsEveryInstanceListener.DropSignalCoalesceWindow * 3, AbortToken);

            // then - every drop is counted and the burst is reported to the consumer once
            Volatile.Read(ref dropped).Should().BePositive();
            Volatile.Read(ref gaps).Should().Be(1, "one burst of drops is one gap");
        }
        finally
        {
            release.TrySetResult();
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_end_every_instance_listening_quietly_when_the_host_cancels_it()
    {
        // Cancelling the listening token also ends the core subscriptions it created, so the read can observe a closed
        // channel instead of a cancelled read; either way this is a shutdown, never a broker fault. Repeated because
        // which of the two the read observes is a race.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            // given
            var subject = $"stop-{Guid.NewGuid():N}"[..30] + ".probe";
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new NatsConsumerClient(
                $"group-{Guid.NewGuid():N}"[..30],
                0,
                _CreateOptions(NatsStreamProvisioning.Disabled),
                _serviceProvider,
                kind: ConsumerSubscriptionKind.EveryInstance
            );
            var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

            try
            {
                client.AttachCallbacks(
                    onMessage: (_, _) =>
                    {
                        received.TrySetResult();
                        return release.Task;
                    },
                    onLog: _ => { }
                );
                await client.ConnectAsync(AbortToken);
                await client.SubscribeAsync([subject], AbortToken);
                var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
                await client.WaitUntilReadyAsync(AbortToken);

                // A consumer busy on one message with more buffered behind it, as at any shutdown under load.
                var connection = await fixture.GetConnectionAsync();
                for (var i = 0; i < 3; i++)
                {
                    await connection.PublishAsync(
                        NatsPhysicalAddress.Subject(MessageLane.Bus, subject),
                        new ReadOnlyMemory<byte>([1]),
                        headers: _CreateHeaders(),
                        serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                        cancellationToken: AbortToken
                    );
                }

                await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

                // when - the consumer finishes its message as the host stops
                release.TrySetResult();
                await cts.CancelAsync();

                // then
                var act = () => listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
                await act.Should().NotThrowAsync("cancelling the listener is a shutdown (attempt {0})", attempt);
            }
            finally
            {
                release.TrySetResult();
                await client.DisposeAsync();
                cts.Dispose();
            }
        }
    }

    [Fact]
    public async Task should_fail_listening_for_a_rebuild_when_an_every_instance_subscription_ends()
    {
        // given
        var subject = $"ended-{Guid.NewGuid():N}"[..30] + ".probe";
        NatsConnection? connection = null;
        var client = new NatsConsumerClient(
            $"group-{Guid.NewGuid():N}"[..30],
            0,
            _CreateOptions(NatsStreamProvisioning.Disabled),
            _serviceProvider,
            connect: c =>
            {
                connection = c;
                return c.ConnectAsync().AsTask();
            },
            kind: ConsumerSubscriptionKind.EveryInstance
        );

        try
        {
            client.AttachCallbacks(onMessage: (_, _) => Task.CompletedTask, onLog: _ => { });
            await client.ConnectAsync(AbortToken);
            await client.SubscribeAsync([subject], AbortToken);
            var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
            await client.WaitUntilReadyAsync(AbortToken);

            // when - the connection underneath the client ends, completing its subscription channels
            await connection!.DisposeAsync();

            // then
            var act = () => listening.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
            await act.Should().ThrowAsync<BrokerConnectionException>();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static MeterListener _ListenToDroppedEveryInstanceDeliveries(string identity, Action onDropped)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                    && string.Equals(
                        instrument.Name,
                        "headless.messaging.every_instance.deliveries",
                        StringComparison.Ordinal
                    )
                )
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                var matchesIdentity = false;
                var outcome = default(string);
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, "messaging.consumer.group.name", StringComparison.Ordinal))
                    {
                        matchesIdentity = string.Equals(tag.Value as string, identity, StringComparison.Ordinal);
                    }
                    else if (
                        string.Equals(tag.Key, "headless.messaging.every_instance.outcome", StringComparison.Ordinal)
                    )
                    {
                        outcome = tag.Value as string;
                    }
                }

                if (matchesIdentity && string.Equals(outcome, "dropped", StringComparison.Ordinal))
                {
                    onDropped();
                }
            }
        );
        listener.Start();
        return listener;
    }

    private async Task<int> _CountConsumersAsync(NatsJSContext js, string streamName)
    {
        var count = 0;

        await foreach (var _ in js.ListConsumerNamesAsync(streamName, AbortToken))
        {
            count++;
        }

        return count;
    }

    private static TransportConformanceEndpoint _EveryInstanceEndpoint(
        string destination,
        string identity,
        string replica
    ) =>
        new(MessageLane.Bus, destination, identity, replica)
        {
            Kind = ConsumerSubscriptionKind.EveryInstance,
            InstanceId = Guid.NewGuid(),
        };

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

    private static TransportMessage _Message(string logicalName, MessageLane lane) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
                [MessagingHeaders.MessageName] = logicalName,
                [MessagingHeaders.Intent] = lane.ToString(),
            },
            "conformance"u8.ToArray()
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
    public async Task should_create_stream_when_fetch_message_names_async_enabled()
    {
        // given
        var streamName = $"autocreate-{Guid.NewGuid():N}"[..25];
        var subject = $"{streamName}.orders";

        var options = _CreateOptions(NatsStreamProvisioning.Reconcile);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when — FetchMessageNamesAsync under NatsStreamProvisioning.Reconcile
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
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
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
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
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
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
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

        var options = _CreateOptions(NatsStreamProvisioning.Disabled);
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
            await client.WaitUntilReadyAsync(AbortToken);

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
        var act = async () => await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue));

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

        var options = _CreateOptions(NatsStreamProvisioning.Disabled);
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // when — start listening, pause, publish, verify no delivery
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await client.WaitUntilReadyAsync(AbortToken);

            await client.PauseAsync(AbortToken);

            // Pausing cancels the in-flight pull on the client, but the server keeps that pull open until the cancel
            // reaches it or the pull's 1-second expiry passes. A publish on another connection can arrive first, and
            // the server then hands the message to the dead pull, which redelivers it only after AckWait (30 seconds).
            // Wait out the expiry so no pull is open when the paused message arrives.
            await Task.Delay(TimeSpan.FromSeconds(1.5), AbortToken);
            await _PublishAsync(subject, "paused-msg"u8.ToArray());
            await Task.Delay(500, AbortToken);

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

    // Stream provisioning modes

    [Fact]
    public async Task should_leave_an_operator_provisioned_stream_unmodified_and_report_it_when_verifying()
    {
        // given — a stream provisioned out of band on Memory storage, while the provider wants File
        var logicalName = $"opprov-{Guid.NewGuid():N}"[..22];
        var subject = $"{logicalName}.orders";
        var streamName = NatsPhysicalAddress.Stream(MessageLane.Bus, logicalName);
        await fixture.EnsureStreamAsync(streamName, NatsPhysicalAddress.Subject(MessageLane.Bus, $"{logicalName}.>"));

        var options = _CreateOptions(NatsStreamProvisioning.Verify);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        var act = async () => await client.FetchMessageNamesAsync([subject], AbortToken);

        // then — startup stops, and the operator's storage choice is untouched
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage($"*{streamName}*");
        thrown.WithMessage("*Recreate or migrate*");

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Info.Config.Storage.Should().Be(StreamConfigStorage.Memory);
    }

    [Fact]
    public async Task should_report_no_divergence_when_verifying_a_stream_this_provider_created()
    {
        // given — the provider creates the stream itself
        var logicalName = $"selfcreate-{Guid.NewGuid():N}"[..24];
        var subject = $"{logicalName}.orders";

        await using (
            var creator = new NatsConsumerClient(
                "test-group",
                0,
                _CreateOptions(NatsStreamProvisioning.Verify),
                _serviceProvider
            )
        )
        {
            await creator.ConnectAsync(AbortToken);
            await creator.FetchMessageNamesAsync([subject], AbortToken);
        }

        // when — a second startup verifies the same stream
        await using var verifier = new NatsConsumerClient(
            "test-group",
            0,
            _CreateOptions(NatsStreamProvisioning.Verify),
            _serviceProvider
        );
        await verifier.ConnectAsync(AbortToken);
        var act = async () => await verifier.FetchMessageNamesAsync([subject], AbortToken);

        // then — server-applied defaults must not read as drift
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_write_a_mutable_divergence_when_reconciling()
    {
        // given — same storage as the provider wants, but a different retention limit
        var logicalName = $"mutable-{Guid.NewGuid():N}"[..22];
        var subject = $"{logicalName}.orders";
        var streamName = NatsPhysicalAddress.Stream(MessageLane.Bus, logicalName);

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        await js.CreateStreamAsync(
            new StreamConfig
            {
                Name = streamName,
                Subjects = [NatsPhysicalAddress.Subject(MessageLane.Bus, $"{logicalName}.>")],
                Storage = StreamConfigStorage.Memory,
                Retention = NatsPhysicalAddress.Retention(MessageLane.Bus),
                MaxMsgs = 100,
            },
            AbortToken
        );

        var options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
                StreamOptions = config =>
                {
                    config.Storage = StreamConfigStorage.Memory;
                    config.MaxMsgs = 500;
                },
            }
        );

        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        await client.FetchMessageNamesAsync([subject], AbortToken);

        // then
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Info.Config.MaxMsgs.Should().Be(500);
    }

    [Fact]
    public async Task should_report_an_immutable_divergence_when_reconciling_instead_of_attempting_the_update()
    {
        // given — storage differs, which JetStream refuses to change on a live stream
        var logicalName = $"immut-{Guid.NewGuid():N}"[..22];
        var subject = $"{logicalName}.orders";
        var streamName = NatsPhysicalAddress.Stream(MessageLane.Bus, logicalName);
        await fixture.EnsureStreamAsync(streamName, NatsPhysicalAddress.Subject(MessageLane.Bus, $"{logicalName}.>"));

        var options = _CreateOptions(NatsStreamProvisioning.Reconcile);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        var act = async () => await client.FetchMessageNamesAsync([subject], AbortToken);

        // then — a migration remedy, not a rejected update and not a Reconcile suggestion
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("*Recreate or migrate*");

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Info.Config.Storage.Should().Be(StreamConfigStorage.Memory);
    }

    [Fact]
    public async Task should_not_create_a_missing_stream_when_provisioning_is_disabled()
    {
        // given
        var logicalName = $"nocreate-{Guid.NewGuid():N}"[..22];
        var subject = $"{logicalName}.orders";
        var streamName = NatsPhysicalAddress.Stream(MessageLane.Bus, logicalName);

        var options = _CreateOptions(NatsStreamProvisioning.Disabled);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        var result = await client.FetchMessageNamesAsync([subject], AbortToken);

        // then
        result.Should().Contain(subject);

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var act = async () => await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        await act.Should().ThrowAsync<NatsJSApiException>();
    }

    [Fact]
    public async Task should_admit_a_second_consumer_group_under_verify_because_the_stream_carries_the_key()
    {
        // given — group A creates the shared stream knowing only its own message
        var logicalName = $"twogroup-{Guid.NewGuid():N}"[..22];
        var subjectA = $"{logicalName}.created";
        var subjectB = $"{logicalName}.shipped";
        var streamName = NatsPhysicalAddress.Stream(MessageLane.Bus, logicalName);

        await using (
            var groupA = new NatsConsumerClient(
                "group-a",
                0,
                _CreateOptions(NatsStreamProvisioning.Verify),
                _serviceProvider
            )
        )
        {
            await groupA.ConnectAsync(AbortToken);
            await groupA.FetchMessageNamesAsync([subjectA], AbortToken);
        }

        // when — group B, which consumes another message on the same key, verifies the stream
        await using var groupB = new NatsConsumerClient(
            "group-b",
            0,
            _CreateOptions(NatsStreamProvisioning.Verify),
            _serviceProvider
        );
        await groupB.ConnectAsync(AbortToken);
        var act = async () => await groupB.FetchMessageNamesAsync([subjectB], AbortToken);

        // then — the stream group A created already covers group B, so start order cannot fail a host
        await act.Should().NotThrowAsync();
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Info.Config.Subjects.Should().Equal(NatsPhysicalAddress.Subject(MessageLane.Bus, $"{logicalName}.>"));
    }

    [Fact]
    public async Task should_publish_from_a_publish_only_host_and_deliver_to_a_later_consumer_on_the_queue_lane()
    {
        // given — no consumer host has ever started, so no stream exists yet
        var logicalName = $"pubonly-{Guid.NewGuid():N}"[..22];
        var subject = $"{logicalName}.placed";
        var options = _CreateOptions(NatsStreamProvisioning.Verify);

        // when — the publisher sends before any consumer exists, and the work-queue stream holds the message
        var messageId = await _PublishThroughTransportAsync(options, MessageLane.Queue, subject);

        // then — the first consumer receives it
        var message = await _ConsumeOneAsync(options, MessageLane.Queue, subject, "pubonly-consumer");
        message.Id.Should().Be(messageId);
        message
            .Headers[MessagingHeaders.TransportAddress]
            .Should()
            .Be(NatsPhysicalAddress.Subject(MessageLane.Queue, subject));
    }

    [Fact]
    public async Task should_create_a_declared_owned_stream_and_deliver_through_it()
    {
        // given — an application that names its stream and lets Headless own it
        var logicalName = $"owned-{Guid.NewGuid():N}"[..20];
        var subject = $"{logicalName}.placed";
        var streamName = $"OWNED_{logicalName[6..]}";
        var natsOptions = new NatsMessagingOptions { Servers = fixture.ConnectionString };
        natsOptions.Streams.Own(
            streamName,
            stream => stream.Subjects(NatsPhysicalAddress.Subject(MessageLane.Queue, $"{logicalName}.>"))
        );
        var options = Options.Create(natsOptions);
        var messageId = await _PublishThroughTransportAsync(options, MessageLane.Queue, subject);

        // when
        var message = await _ConsumeOneAsync(options, MessageLane.Queue, subject, "owned-consumer");

        // then — the message lived on the declared stream, created with limits retention and the default age limit
        message.Id.Should().Be(messageId);
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Info.Config.Retention.Should().Be(StreamConfigRetention.Limits);
        stream.Info.Config.MaxAge.Should().Be(TimeSpan.FromDays(7));
        stream.Info.State.Messages.Should().Be(1);
    }

    [Fact]
    public async Task should_deliver_through_a_bound_stream_without_changing_it()
    {
        // given — a stream an operator created, with no age limit, that the application binds to
        var logicalName = $"bound-{Guid.NewGuid():N}"[..20];
        var subject = $"{logicalName}.placed";
        var streamName = $"BOUND_{logicalName[6..]}";
        var streamSubject = NatsPhysicalAddress.Subject(MessageLane.Queue, $"{logicalName}.>");
        await fixture.EnsureStreamAsync(streamName, streamSubject, StreamConfigRetention.Workqueue);
        var natsOptions = new NatsMessagingOptions
        {
            Servers = fixture.ConnectionString,
            StreamProvisioning = NatsStreamProvisioning.Reconcile,
        };
        natsOptions.Streams.Bind(streamName, streamSubject);
        var options = Options.Create(natsOptions);
        var messageId = await _PublishThroughTransportAsync(options, MessageLane.Queue, subject);

        // when
        var message = await _ConsumeOneAsync(options, MessageLane.Queue, subject, "bound-consumer");

        // then — even Reconcile left the bound stream as the operator made it
        message.Id.Should().Be(messageId);
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Info.Config.Subjects.Should().Equal(streamSubject);
        stream.Info.Config.MaxAge.Should().Be(TimeSpan.Zero);
        stream.Info.Config.Storage.Should().Be(StreamConfigStorage.Memory);
        (await _StreamExistsAsync(js, NatsPhysicalAddress.Stream(MessageLane.Queue, logicalName))).Should().BeFalse();
    }

    [Fact]
    public async Task should_publish_over_the_application_connection_and_leave_it_open_when_use_connection()
    {
        // given — an app that owns its NATS connection and hands it to Headless
        var logicalName = $"appconn-{Guid.NewGuid():N}"[..22];
        var subject = $"{logicalName}.placed";
        await using var appConnection = new NatsConnection(new NatsOpts { Url = fixture.ConnectionString });
        await appConnection.ConnectAsync();
        var options = Options.Create(new NatsMessagingOptions().UseConnection(_ => appConnection));
        var pool = new Headless.Messaging.Nats.NatsConnectionPool(
            NullLogger<Headless.Messaging.Nats.NatsConnectionPool>.Instance,
            options,
            _serviceProvider
        );
        var transport = new NatsTransport(
            NullLogger<NatsTransport>.Instance,
            pool,
            new NatsStreamProvisioner(options),
            MessageLane.Queue
        );
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = subject,
        };

        // when
        var result = await transport.SendAsync(new TransportMessage(headers, "{}"u8.ToArray()), AbortToken);
        await transport.DisposeAsync();
        await pool.DisposeAsync();

        // then — Headless published on the app's connection and did not close it
        result.Succeeded.Should().BeTrue(result.Exception?.Message);
        pool.ServersAddress.Should().Be(BrokerAddressDisplay.FormatMany(fixture.ConnectionString));
        appConnection.ConnectionState.Should().Be(NatsConnectionState.Open);
        await appConnection.PingAsync(AbortToken);
    }

    private static async Task<string> _PublishThroughTransportAsync(
        IOptions<NatsMessagingOptions> options,
        MessageLane lane,
        string subject
    )
    {
        await using var pool = new Headless.Messaging.Nats.NatsConnectionPool(
            NullLogger<Headless.Messaging.Nats.NatsConnectionPool>.Instance,
            options
        );
        await using var transport = new NatsTransport(
            NullLogger<NatsTransport>.Instance,
            pool,
            new NatsStreamProvisioner(options),
            lane
        );
        var messageId = Guid.NewGuid().ToString("N");
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = subject,
        };

        var result = await transport.SendAsync(new TransportMessage(headers, "{}"u8.ToArray()), AbortToken);

        result.Succeeded.Should().BeTrue(result.Exception?.Message);
        return messageId;
    }

    [Fact]
    public async Task should_keep_a_delivery_whose_handler_outlasts_ack_wait_from_being_redelivered()
    {
        // given — a 1 s AckWait and a concurrent handler that runs four of them
        var streamName = $"progress-{Guid.NewGuid():N}"[..29];
        var subject = $"{streamName}.events";
        var options = _CreateMemoryOptions(config => config.AckWait = TimeSpan.FromSeconds(1));
        await using var client = new NatsConsumerClient("progress-group", 2, options, _serviceProvider);
        var deliveries = 0;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = async (_, sender) =>
        {
            if (Interlocked.Increment(ref deliveries) == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(4), AbortToken);
                await client.CommitAsync(sender, AbortToken);
                settled.TrySetResult();
            }
        };
        client.OnLogCallback = _ => { };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _PrepareListeningAsync(client, subject);
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await client.WaitUntilReadyAsync(AbortToken);

        try
        {
            // when
            await _PublishAsync(subject, "slow"u8.ToArray());
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(15), AbortToken);
            await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

            // then — in-progress signals held the delivery, so JetStream never redelivered it
            Volatile.Read(ref deliveries).Should().Be(1);
            var js = new NatsJSContext(await fixture.GetConnectionAsync());
            var consumer = await js.GetConsumerAsync(
                NatsPhysicalAddress.Stream(MessageLane.Bus, streamName),
                NatsConsumerClient.BuildDurableName("progress-group", subject, MessageLane.Bus),
                AbortToken
            );
            consumer.Info.NumRedelivered.Should().Be(0);
            consumer.Info.NumAckPending.Should().Be(0);
        }
        finally
        {
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_delay_the_redelivery_of_a_rejected_delivery()
    {
        // given
        var streamName = $"nakdelay-{Guid.NewGuid():N}"[..29];
        var subject = $"{streamName}.events";
        var options = _CreateMemoryOptions(configureConsumer: null);
        await using var client = new NatsConsumerClient("nak-group", 0, options, _serviceProvider);
        var rejectedAt = System.Diagnostics.Stopwatch.StartNew();
        var redelivered = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveries = 0;
        client.OnMessageCallback = async (_, sender) =>
        {
            if (Interlocked.Increment(ref deliveries) == 1)
            {
                await client.RejectAsync(sender, AbortToken);
                rejectedAt.Restart();
                return;
            }

            redelivered.TrySetResult(rejectedAt.Elapsed);
            await client.CommitAsync(sender, AbortToken);
        };
        client.OnLogCallback = _ => { };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _PrepareListeningAsync(client, subject);
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await client.WaitUntilReadyAsync(AbortToken);

        try
        {
            // when
            await _PublishAsync(subject, "rejected"u8.ToArray());
            var delay = await redelivered.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

            // then — the first rejection waits about a second (jittered down by at most a quarter), not one pull
            delay.Should().BeGreaterThan(TimeSpan.FromMilliseconds(700));
        }
        finally
        {
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_terminate_a_malformed_envelope_with_a_reason_the_advisory_carries()
    {
        // given
        var streamName = $"terminate-{Guid.NewGuid():N}"[..29];
        var subject = $"{streamName}.events";
        var options = _CreateMemoryOptions(configureConsumer: null);
        options.Value.CustomHeadersBuilder = static (_, _, _) =>
            [new KeyValuePair<string, string>(MessagingHeaders.MessageId, string.Empty)];
        await using var client = new NatsConsumerClient("terminate-group", 0, options, _serviceProvider);
        client.OnMessageCallback = (_, _) => Task.CompletedTask;
        client.OnLogCallback = _ => { };
        var connection = await fixture.GetConnectionAsync();
        await using var advisories = await connection.SubscribeCoreAsync<string>(
            $"$JS.EVENT.ADVISORY.CONSUMER.MSG_TERMINATED.{NatsPhysicalAddress.Stream(MessageLane.Bus, streamName)}.>",
            cancellationToken: AbortToken
        );
        await connection.PingAsync(AbortToken);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _PrepareListeningAsync(client, subject);
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await client.WaitUntilReadyAsync(AbortToken);

        try
        {
            // when
            await _PublishAsync(subject, "malformed"u8.ToArray());
            using var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken);
            var advisory = await advisories.Msgs.ReadAsync(timeout.Token);

            // then — operators see the poison message and why in the advisory and the consumer's stats
            advisory.Data.Should().Contain("malformed headless envelope: InvalidDataException");
        }
        finally
        {
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_terminate_a_dead_lettered_message_with_the_core_reason_the_advisory_carries()
    {
        // given — a well-formed message the core dead-letters on arrival
        var streamName = $"deadletter-{Guid.NewGuid():N}"[..29];
        var subject = $"{streamName}.events";
        var options = _CreateMemoryOptions(configureConsumer: null);
        await using var client = new NatsConsumerClient("deadletter-group", 0, options, _serviceProvider);
        client.OnMessageCallback = async (_, sender) =>
            await client.DeadLetterAsync(sender, "SubscriberNotFound", "no consumer", CancellationToken.None);
        client.OnLogCallback = _ => { };
        var connection = await fixture.GetConnectionAsync();
        await using var advisories = await connection.SubscribeCoreAsync<string>(
            $"$JS.EVENT.ADVISORY.CONSUMER.MSG_TERMINATED.{NatsPhysicalAddress.Stream(MessageLane.Bus, streamName)}.>",
            cancellationToken: AbortToken
        );
        await connection.PingAsync(AbortToken);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _PrepareListeningAsync(client, subject);
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await client.WaitUntilReadyAsync(AbortToken);

        try
        {
            // when
            await _PublishAsync(subject, "{}"u8.ToArray());
            using var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken);
            var advisory = await advisories.Msgs.ReadAsync(timeout.Token);

            // then — JetStream has no dead-letter queue, so the advisory is where the message and its reason surface
            advisory.Data.Should().Contain("SubscriberNotFound: no consumer");
        }
        finally
        {
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_create_streams_with_their_duplicate_window_and_drop_a_repeated_message_id_within_it()
    {
        // given — a declared stream with its own window, and a derived one on the default
        var logicalName = $"dedup-{Guid.NewGuid():N}"[..20];
        var declaredStream = $"DEDUP_{logicalName[6..]}";
        var natsOptions = new NatsMessagingOptions
        {
            Servers = fixture.ConnectionString,
            StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
        };
        natsOptions.Streams.Own(
            declaredStream,
            stream =>
                stream
                    .Subjects(NatsPhysicalAddress.Subject(MessageLane.Queue, $"{logicalName}.>"))
                    .DuplicateWindow(TimeSpan.FromMinutes(10))
        );
        var options = Options.Create(natsOptions);
        var derivedName = $"derived-{Guid.NewGuid():N}"[..24];

        // when — the same message is published twice, as an outbox retry would
        var message = _Message($"{logicalName}.placed", MessageLane.Queue);
        await _PublishTransportMessageAsync(options, MessageLane.Queue, message);
        await _PublishTransportMessageAsync(options, MessageLane.Queue, message);
        await _PublishTransportMessageAsync(
            options,
            MessageLane.Queue,
            _Message($"{derivedName}.placed", MessageLane.Queue)
        );

        // then
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var declared = await js.GetStreamAsync(declaredStream, cancellationToken: AbortToken);
        declared.Info.Config.DuplicateWindow.Should().Be(TimeSpan.FromMinutes(10));
        declared.Info.State.Messages.Should().Be(1, "the stream dropped the repeated Nats-Msg-Id inside its window");
        var derived = await js.GetStreamAsync(
            NatsPhysicalAddress.Stream(MessageLane.Queue, derivedName),
            cancellationToken: AbortToken
        );
        derived.Info.Config.DuplicateWindow.Should().Be(TimeSpan.FromMinutes(2));
    }

    private static async Task _PublishTransportMessageAsync(
        IOptions<NatsMessagingOptions> options,
        MessageLane lane,
        TransportMessage message
    )
    {
        await using var pool = new Headless.Messaging.Nats.NatsConnectionPool(
            NullLogger<Headless.Messaging.Nats.NatsConnectionPool>.Instance,
            options
        );
        await using var transport = new NatsTransport(
            NullLogger<NatsTransport>.Instance,
            pool,
            new NatsStreamProvisioner(options),
            lane
        );
        var result = await transport.SendAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue(result.Exception?.ToString());
    }

    // Creates the subject's derived stream and subscribes the client; the caller then listens and waits until the durable
    // is bound, so a Bus publish after that has a consumer to land on.
    private static async Task _PrepareListeningAsync(NatsConsumerClient client, string subject)
    {
        await client.ConnectAsync(AbortToken);
        var names = await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync(names, AbortToken);
    }

    private IOptions<NatsMessagingOptions> _CreateMemoryOptions(Action<ConsumerConfig>? configureConsumer)
    {
        return Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
                ConsumerOptions = configureConsumer,
            }
        );
    }

    // Starts a consumer for one message name and returns the first message it receives.
    private async Task<TransportMessage> _ConsumeOneAsync(
        IOptions<NatsMessagingOptions> options,
        MessageLane lane,
        string subject,
        string consumerName
    )
    {
        await using var consumer = new NatsConsumerClient(consumerName, 0, options, _serviceProvider, lane: lane);
        var delivered = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.OnMessageCallback = async (message, sender) =>
        {
            delivered.TrySetResult(message);
            await consumer.CommitAsync(sender);
        };
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        consumer.OnLogCallback = args => logs.Enqueue($"{args.LogType}: {args.Reason}");
        await consumer.ConnectAsync(AbortToken);
        var names = await consumer.FetchMessageNamesAsync([subject], AbortToken);
        await consumer.SubscribeAsync(names, AbortToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = consumer.ListeningAsync(TimeSpan.FromSeconds(2), cts.Token).AsTask();
        try
        {
            var completed = await Task.WhenAny(delivered.Task, Task.Delay(TimeSpan.FromSeconds(10), AbortToken));
            completed.Should().BeSameAs(delivered.Task, string.Join(" | ", logs));
            return await delivered.Task;
        }
        finally
        {
            await cts.CancelAsync();
#pragma warning disable ERP022 // The listening loop ends with the cancellation this test requested.
            try
            {
                await listening;
            }
            catch
            {
                // Shutdown only.
            }
#pragma warning restore ERP022
        }
    }

    private static async Task<bool> _StreamExistsAsync(NatsJSContext js, string streamName)
    {
        try
        {
            await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
            return true;
        }
        catch (NatsJSApiException e) when (e.Error.Code == 404 || e.Error.ErrCode == 10059)
        {
            return false;
        }
    }

    private IOptions<NatsMessagingOptions> _CreateOptions(NatsStreamProvisioning streamProvisioning)
    {
        return Options.Create(
            new NatsMessagingOptions { Servers = fixture.ConnectionString, StreamProvisioning = streamProvisioning }
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
