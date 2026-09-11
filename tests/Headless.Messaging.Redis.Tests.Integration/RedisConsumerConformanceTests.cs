// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using StackExchange.Redis;
using Tests.Capabilities;

namespace Tests;

[Collection<RedisMessagingFixture>]
public sealed class RedisConsumerConformanceTests(RedisMessagingFixture fixture) : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "Redis";

    protected override void ConfigureTransport(MessagingSetupBuilder setup) => setup.UseRedis(fixture.ConnectionString);

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        var destination = $"conformance-{Guid.NewGuid():N}";
        return fixture.CreateSessionAsync(MessageLane.Queue, destination, destination, cancellationToken);
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers() =>
        base.should_round_trip_queue_message_body_and_headers();

    [Fact]
    public override Task should_match_production_runtime_capabilities() =>
        base.should_match_production_runtime_capabilities();

    [Fact]
    public override Task should_dispatch_empty_message_body() => base.should_dispatch_empty_message_body();

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery() =>
        base.should_commit_real_delivery_and_prevent_redelivery();

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery() =>
        base.should_reject_real_delivery_and_observe_redelivery();

    [Fact]
    public override Task should_isolate_unique_destinations() => base.should_isolate_unique_destinations();

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound() =>
        base.should_shutdown_idle_consumer_within_bound();

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active() =>
        base.should_bound_shutdown_while_handler_is_active();

    [Fact]
    public Task should_deliver_one_bus_copy_per_group_while_replicas_compete() =>
        TransportProviderConformance.AssertBusSubscriberGroupsAsync(
            new RedisProviderConformanceDriver(fixture),
            AbortToken
        );

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas() =>
        TransportProviderConformance.AssertQueueOwnershipAsync(new RedisProviderConformanceDriver(fixture), AbortToken);

    [Fact]
    public Task should_isolate_same_logical_name_between_bus_and_queue() =>
        TransportProviderConformance.AssertSameNameLaneIsolationAsync(
            new RedisProviderConformanceDriver(fixture),
            AbortToken
        );

    [Fact]
    public async Task should_terminally_ack_malformed_entry_across_consumer_restart()
    {
        var destination = $"malformed-{Guid.NewGuid():N}";
        var group = $"group-{Guid.NewGuid():N}";
        var consumeErrors = 0;
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            group,
            AbortToken,
            onConsumeError: _ =>
            {
                Interlocked.Increment(ref consumeErrors);
                return Task.CompletedTask;
            }
        );
        await session.StartAsync(cancellationToken: AbortToken);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var database = connection.GetDatabase();
        var stream = $"headless:messaging:queue:{destination}";
        var malformed = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = destination,
                [Headers.Intent] = nameof(MessageLane.Queue),
            },
            "valid-body"u8.ToArray()
        );
        await database.StreamAddAsync(stream, malformed.AsStreamEntries());

        using (var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken))
        {
            while (Volatile.Read(ref consumeErrors) == 0)
            {
                await Task.Delay(20, timeout.Token);
            }
        }

        (await database.StreamPendingAsync(stream, group)).PendingMessageCount.Should().Be(0);
        await session.StopAsync(TimeSpan.FromSeconds(2));
        await using var replacement = await session.CreateReplacementAsync(AbortToken);
        await replacement.StartAsync(cancellationToken: AbortToken);

        (await replacement.RemainsEmptyAsync(TimeSpan.FromSeconds(2), AbortToken)).Should().BeTrue();
        Volatile.Read(ref consumeErrors).Should().Be(1);
    }
}
