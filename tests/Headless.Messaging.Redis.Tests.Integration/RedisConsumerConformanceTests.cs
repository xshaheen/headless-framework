// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
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
    public Task should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete() =>
        TransportProviderConformance.AssertBusConsumerIdentitiesAsync(
            new RedisProviderConformanceDriver(fixture),
            AbortToken
        );

    [Fact]
    public Task should_deliver_every_bus_message_to_every_every_instance_replica() =>
        TransportProviderConformance.AssertBusEveryInstanceAsync(
            new RedisProviderConformanceDriver(fixture),
            AbortToken
        );

    [Fact]
    public async Task should_fan_out_bus_message_to_every_instance_sessions_of_one_identity()
    {
        var destination = $"every-{Guid.NewGuid():N}";
        var identity = $"group-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateSessionAsync(
            MessageLane.Bus,
            destination,
            identity,
            AbortToken,
            ownsStream: true,
            request: _EveryInstanceRequest(identity)
        );
        await using var second = await fixture.CreateSessionAsync(
            MessageLane.Bus,
            destination,
            identity,
            AbortToken,
            ownsStream: false,
            request: _EveryInstanceRequest(identity)
        );

        await TransportBusConformance.AssertEveryInstanceFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public async Task should_leave_no_consumer_group_after_every_instance_client_disposes()
    {
        // given
        var destination = $"every-{Guid.NewGuid():N}";
        var identity = $"group-{Guid.NewGuid():N}";
        var stream = RedisPhysicalAddress.BusStream(destination);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var database = connection.GetDatabase();

        try
        {
            var session = await fixture.CreateSessionAsync(
                MessageLane.Bus,
                destination,
                identity,
                AbortToken,
                ownsStream: false,
                request: _EveryInstanceRequest(identity)
            );

            try
            {
                await session.StartAsync(cancellationToken: AbortToken);
                var message = new TransportMessage(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [Headers.MessageId] = Guid.NewGuid().ToString("N"),
                        [Headers.MessageName] = destination,
                        [Headers.Intent] = nameof(MessageLane.Bus),
                    },
                    "every"u8.ToArray()
                );
                (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
                (await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken)).Message.Id.Should().Be(message.Id);
            }
            finally
            {
                // when
                await session.DisposeAsync();
            }

            // then: the stream the publish created is shared, but no read left a group or pending entry on it
            (await database.KeyExistsAsync(stream))
                .Should()
                .BeTrue();
            (await database.StreamGroupInfoAsync(stream)).Should().BeEmpty();
        }
        finally
        {
            await database.KeyDeleteAsync(stream);
        }
    }

    private static ConsumerClientRequest _EveryInstanceRequest(string identity) =>
        new(identity, 1, MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance, Guid.NewGuid());

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
