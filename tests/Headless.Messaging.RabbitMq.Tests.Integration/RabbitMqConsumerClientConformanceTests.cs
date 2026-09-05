// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using RabbitMQ.Client;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqConsumerClientConformanceTests(RabbitMqFixture fixture)
    : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "RabbitMQ";

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseRabbitMq(options =>
        {
            options.HostName = fixture.HostName;
            options.Port = fixture.Port;
            options.UserName = fixture.UserName;
            options.Password = fixture.Password;
        });
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
        var exchangeName = $"bus-{Guid.NewGuid():N}";
        var destination = $"message-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateBusSessionAsync(
            exchangeName,
            destination,
            $"group-{Guid.NewGuid():N}",
            AbortToken
        );
        await using var second = await fixture.CreateBusSessionAsync(
            exchangeName,
            destination,
            $"group-{Guid.NewGuid():N}",
            AbortToken
        );

        await TransportBusConformance.AssertFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public Task should_fan_out_one_bus_copy_per_group_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(
            new RabbitMqProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(
            new RabbitMqProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_isolate_same_logical_name_across_bus_and_queue()
    {
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(
            new RabbitMqProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_terminally_reject_malformed_envelope_across_consumer_restart()
    {
        var exchangeName = $"malformed-{Guid.NewGuid():N}";
        var destination = $"malformed-{Guid.NewGuid():N}";
        var group = $"group-{Guid.NewGuid():N}";
        var terminalLogs = 0;
        await using var session = await fixture.CreateMalformedSessionAsync(
            exchangeName,
            destination,
            group,
            AbortToken
        );
        await session.StartAsync(
            onLog: log =>
            {
                if (log.Reason?.Contains("terminally rejected", StringComparison.Ordinal) == true)
                {
                    Interlocked.Increment(ref terminalLogs);
                }
            },
            cancellationToken: AbortToken
        );

        var result = await session.PublishAsync(
            new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
                    [MessagingHeaders.MessageName] = destination,
                },
                "malformed"u8.ToArray()
            ),
            AbortToken
        );
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
}
