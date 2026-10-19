// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Pulsar.Client.Common;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarConsumerClientHarnessTests(PulsarFixture fixture) : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "Pulsar";

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UsePulsar(fixture.ConnectionString);
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceSessionOptions options,
        CancellationToken cancellationToken
    )
    {
        // Key_Shared sends every message of one key to one consumer in publish order, and a dead-letter policy is how
        // the client stops redelivering a message after its budget; Pulsar counts the redeliveries, not the first one.
        var consumerConfig =
            options.RoutingAffinity || options.DeliveryLimit is not null
                ? new PulsarConsumerConfig(options.RoutingAffinity, options.DeliveryLimit - 1)
                : null;

        return PulsarFixture.CreateSessionAsync(
            fixture.ConnectionString,
            MessageLane.Queue,
            cancellationToken,
            request: new ConsumerClientRequest($"group-{Guid.NewGuid():N}", options.Concurrency, MessageLane.Queue),
            configureOptions: pulsar =>
            {
                if (options.HeaderBuilder is { } headerBuilder)
                {
                    pulsar.CustomHeadersBuilder = (message, _) =>
                    {
                        headerBuilder(message.Properties.GetValueOrDefault(MessagingHeaders.MessageId));
                        return [];
                    };
                }
            },
            consumerConfig: consumerConfig
        );
    }

    /// <summary>
    /// The broker's default <c>maxMessageSize</c> of 5 MiB, which counts the message metadata too: the conformance
    /// envelope's properties take 218 bytes of it, and the client refuses a larger message before sending it.
    /// </summary>
    protected override int MaximumBodySize => (5 * 1024 * 1024) - 218;

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
    public Task should_fan_out_one_bus_copy_per_consumer_identity_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusConsumerIdentitiesAsync(
            new PulsarProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(
            new PulsarProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_isolate_same_logical_name_across_bus_and_queue()
    {
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(
            new PulsarProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_terminally_acknowledge_malformed_envelope_across_consumer_restart()
    {
        var destination = $"persistent://public/default/malformed-{Guid.NewGuid():N}";
        var group = $"group-{Guid.NewGuid():N}";
        var terminalLogs = 0;
        await using var session = await fixture.CreateMalformedSessionAsync(destination, group, AbortToken);
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
    public override Task should_not_settle_another_delivery_when_a_settled_value_is_used_again()
    {
        return base.should_not_settle_another_delivery_when_a_settled_value_is_used_again();
    }

    [Fact]
    public override Task should_keep_the_delivery_and_the_listener_when_the_callback_throws()
    {
        return base.should_keep_the_delivery_and_the_listener_when_the_callback_throws();
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
    public override Task should_dispatch_empty_message_body()
    {
        return base.should_dispatch_empty_message_body();
    }
}
