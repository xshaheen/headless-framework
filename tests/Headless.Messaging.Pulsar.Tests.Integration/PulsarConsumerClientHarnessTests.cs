// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
using Tests.Capabilities;
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
    public Task should_fan_out_one_bus_copy_per_group_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(
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

    [Fact]
    public async Task should_drain_legacy_topic_before_lane_cutover_and_reconcile_forward()
    {
        var logicalName = $"persistent://public/default/orders-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UsePulsar(fixture.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        var client = await provider.GetRequiredService<IConnectionFactory>().RentClientAsync(AbortToken);
        await using var legacyProducer = await client.NewProducer().Topic(logicalName).CreateAsync();
        await using var legacyConsumer = await client
            .NewConsumer()
            .Topic(logicalName)
            .SubscriptionName($"legacy-{Guid.NewGuid():N}")
            .SubscriptionInitialPosition(SubscriptionInitialPosition.Earliest)
            .SubscribeAsync();

        await legacyProducer.SendAsync(legacyProducer.NewMessage("legacy"u8.ToArray()));
        var legacyDelivery = await legacyConsumer.ReceiveAsync(AbortToken);
        legacyDelivery.Should().NotBeNull("the deployment fence must observe and drain legacy backlog");
        await legacyConsumer.AcknowledgeAsync(legacyDelivery.MessageId);

        await using var bus = await fixture.CreateLaneSessionAsync(
            MessageLane.Bus,
            logicalName,
            "orders-subscribers",
            AbortToken
        );
        await using var queue = await fixture.CreateLaneSessionAsync(
            MessageLane.Queue,
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

        using var noLegacyDelivery = TimeSpan.FromSeconds(1).ToCancellationTokenSource(AbortToken);
        var legacyReceive = async () => await legacyConsumer.ReceiveAsync(noLegacyDelivery.Token);
        await legacyReceive
            .Should()
            .ThrowAsync<OperationCanceledException>(
                "lane-qualified publication is roll-forward-only and must not return to the legacy topic"
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
}
