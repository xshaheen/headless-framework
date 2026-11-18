// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests.Simulation;

/// <summary>
/// The clock a contract suite's own waits run on against the simulated broker: it may move simulated time only once
/// the broker has done everything already due, or a window would close before a delivery that was on its way.
/// </summary>
public sealed class SimulationWaitClockTests : TestBase
{
    [Fact]
    public async Task should_see_a_delivery_already_due_before_a_no_delivery_window_closes()
    {
        // given
        await using var simulation = await _StartAsync();
        var session = simulation.Session;

        // when: the window opens while the publish is still on its way to the consumer
        (await session.PublishAsync(_Message(session.Destination), AbortToken))
            .Succeeded.Should()
            .BeTrue();
        var stayedEmpty = await session.RemainsEmptyAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        stayedEmpty.Should().BeFalse("the delivery was due at once, so the window must not close before it");
        simulation.Clock.GetUtcNow().Should().Be(simulation.StartedAt);
    }

    [Fact]
    public async Task should_move_time_to_a_delayed_redelivery_before_a_receive_timeout()
    {
        // given
        await using var simulation = await _StartAsync();
        var session = simulation.Session;
        var message = _Message(session.Destination);
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        await session.Consumer.RejectAsync(first.SettlementValue, TimeSpan.FromSeconds(5), AbortToken);
        var redelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        redelivery.Message.Id.Should().Be(message.Id);
        simulation.Clock.GetUtcNow().Should().Be(simulation.StartedAt + TimeSpan.FromSeconds(5));
        await session.Consumer.CommitAsync(redelivery.SettlementValue, AbortToken);
    }

    [Fact]
    public async Task should_not_move_time_when_a_receive_meets_a_delivery_already_on_its_way()
    {
        // given
        await using var simulation = await _StartAsync();
        var session = simulation.Session;

        // when: each receive starts while its delivery may be landing, the race in which a wait could arm a timeout
        // for a receive that had already ended and let the clock step to it
        for (var i = 0; i < 300; i++)
        {
            (await session.PublishAsync(_Message(session.Destination), AbortToken)).Succeeded.Should().BeTrue();
            var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
            await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        }

        // then: every delivery was due at once, so no timeout was ever the nearest pending wait
        simulation.Clock.GetUtcNow().Should().Be(simulation.StartedAt);
    }

    [Fact]
    public async Task should_move_time_only_to_a_cut_off_clients_retry_before_a_receive_timeout()
    {
        // given
        await using var simulation = await _StartAsync();
        var session = simulation.Session;
        var broker = simulation.Broker;
        var subscription = ((SimulatedConsumerClient)session.Consumer).Subscription;
        const int rounds = 100;

        // when: each round cuts the connection until the client has seen a receive fail, restores it at once, and
        // receives a publish the client can take only on its retry. A client that counted as parked before its retry
        // was pending on the clock would let the wait clock step straight to the receive timeout.
        for (var round = 1; round <= rounds; round++)
        {
            broker.Disconnect();
            await broker.WaitForEventsAsync(
                brokerEvent =>
                    brokerEvent.Kind is BrokerEventKind.ReceiveFailed
                    && string.Equals(brokerEvent.Subscription, subscription, StringComparison.Ordinal),
                round,
                AbortToken
            );
            broker.Restore();

            var message = _Message(session.Destination);
            (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
            var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
            delivery.Message.Id.Should().Be(message.Id);
            await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        }

        // then: every round waited out exactly the client's retry, never a receive timeout
        simulation.Clock.GetUtcNow().Should().Be(simulation.StartedAt + (broker.RetryInterval * rounds));
    }

    private static async Task<Simulation> _StartAsync()
    {
        var broker = new SimulatedBroker(new SimulationClock());
#pragma warning disable CA2000 // False positive: ownership transfers to the returned simulation, which disposes both.
        var clock = new SimulationWaitClock(broker);
        var destination = $"wait-clock-{Guid.NewGuid():N}";
        var consumer = broker.CreateConsumerClient(new ConsumerClientRequest(destination, 1, MessageLane.Queue));
        await consumer.SubscribeAsync([destination], AbortToken);

        var session = new TransportConsumerConformanceSession(
            destination,
            broker.CreateTransport(MessageLane.Queue),
            consumer,
            TimeSpan.FromSeconds(1),
            timeProvider: clock
        );
#pragma warning restore CA2000
        await session.StartAsync(cancellationToken: AbortToken);

        return new Simulation(session, broker, clock, broker.Clock.GetUtcNow());
    }

    private static TransportMessage _Message(string destination)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = destination,
        };

        return new TransportMessage(headers, "wait-clock"u8.ToArray());
    }

    private sealed record Simulation(
        TransportConsumerConformanceSession Session,
        SimulatedBroker Broker,
        SimulationWaitClock Clock,
        DateTimeOffset StartedAt
    ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            await Clock.DisposeAsync();
        }
    }
}
