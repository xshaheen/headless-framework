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
    public async Task should_accept_real_delivery_value_for_commit_callback()
    {
        // given
        var streamName = $"consume-commit-{Guid.NewGuid():N}"[..30];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(NatsStreamProvisioning.Disabled);
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

        var options = _CreateOptions(NatsStreamProvisioning.Disabled);
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
    public async Task should_fail_a_second_consumer_group_whose_subject_the_shared_stream_does_not_carry()
    {
        // given — group A creates the shared stream carrying only its own subject
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

        // when — group B verifies against a stream that cannot deliver to it
        await using (
            var groupB = new NatsConsumerClient(
                "group-b",
                0,
                _CreateOptions(NatsStreamProvisioning.Verify),
                _serviceProvider
            )
        )
        {
            await groupB.ConnectAsync(AbortToken);
            var act = async () => await groupB.FetchMessageNamesAsync([subjectB], AbortToken);

            // then — the silent-loss case the old unconditional upsert hid
            var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
            thrown.WithMessage("*no messages*");
        }

        // and — reconciling admits the second group and the stream then carries both subjects
        await using var groupBReconciling = new NatsConsumerClient(
            "group-b",
            0,
            _CreateOptions(NatsStreamProvisioning.Reconcile),
            _serviceProvider
        );
        await groupBReconciling.ConnectAsync(AbortToken);
        await groupBReconciling.FetchMessageNamesAsync([subjectB], AbortToken);

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream
            .Info.Config.Subjects.Should()
            .Contain(NatsPhysicalAddress.Subject(MessageLane.Bus, subjectA))
            .And.Contain(NatsPhysicalAddress.Subject(MessageLane.Bus, subjectB));
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
