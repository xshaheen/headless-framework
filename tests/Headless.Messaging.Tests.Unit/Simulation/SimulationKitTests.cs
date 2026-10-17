// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Simulation;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests.Simulation;

/// <summary>
/// Each fault the simulation kit can inject, proven on its own before a contract suite or a pipeline test relies on it,
/// and the kit's own wiring: the real pipeline on the simulated broker, the decorated store, and the shutdown guard.
/// </summary>
public sealed class SimulationKitTests : TestBase
{
    // A bound on event-driven waits so a broken kit fails instead of hanging; a passing test never waits it out.
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_deliver_a_bus_publish_to_its_consumer_without_moving_the_clock()
    {
        // given
        await using var host = await _StartConsumerHostAsync();
        var startedAt = host.Clock.GetUtcNow();

        // when
        await _PublishAsync(host, new SimulationOrder("order-1"));

        // then: the delivery is driven by the publish itself, not by a timer or a poll
        var received = await host.GetRequiredService<SimulationOrderConsumer>().NextAsync(_Bound, AbortToken);
        received.Id.Should().Be("order-1");
        host.Clock.GetUtcNow().Should().Be(startedAt);
    }

    [Fact]
    public async Task should_report_swallowed_receive_failures_to_reachability_until_the_failing_client_recovers()
    {
        // given: a host with an order client that will fail its receives and an invoice client that stays healthy. Every
        // broker change wakes the invoice client, so it keeps receiving successfully all through the order client's run.
        await using var host = await _StartConsumerHostAsync();
        var broker = host.Broker;
        var connectivity = host.GetRequiredService<TransportConnectivity>();
        var orders = broker.SubscriptionsFor(MessageLane.Bus, SimulationOrder.Name).Should().ContainSingle().Subject;
        var invoices = broker
            .SubscriptionsFor(MessageLane.Bus, SimulationInvoice.Name)
            .Should()
            .ContainSingle()
            .Subject;

        // when: three receives in a row fail, each retried after the broker's retry interval
        broker.Faults.FailReceives(orders, times: 3);
        for (var failure = 1; failure <= 3; failure++)
        {
            await broker
                .WaitForEventsAsync(e => _IsFailedReceive(e, orders), failure, AbortToken)
                .WaitAsync(_Bound, AbortToken);

            if (failure < 3)
            {
                host.Advance(broker.RetryInterval);
            }
        }

        // then: the swallowed errors mark the broker unreachable, and the invoice client's healthy receives, which
        // report nothing because that client never failed, did not end the order client's run
        connectivity.IsReachable.Should().BeFalse("three failed receives in a row mark the broker unreachable");
        broker
            .Events.Should()
            .NotContain(e => e.Subscription == invoices && e.Kind == BrokerEventKind.ReceiveRecovered);

        // and: the order client's next receive reaches the broker and reports its own recovery
        host.Advance(broker.RetryInterval);
        await broker
            .WaitForEventAsync(
                e =>
                    e.Kind is BrokerEventKind.ReceiveRecovered
                    && string.Equals(e.Subscription, orders, StringComparison.Ordinal),
                AbortToken
            )
            .WaitAsync(_Bound, AbortToken);
        connectivity.IsReachable.Should().BeTrue();
    }

    [Fact]
    public async Task should_ignore_a_settlement_value_from_before_a_reconnect_without_settling_its_redelivery()
    {
        // given: a client holds a delivery when the connection drops
        var broker = new SimulatedBroker(new SimulationClock());
        await using var probe = await ClientProbe.StartAsync(broker, "orders", MessageLane.Queue, AbortToken);
        await _SendAsync(broker, MessageLane.Queue, "orders", "message-1");
        var beforeReconnect = await probe.NextAsync(AbortToken);

        broker.Disconnect();
        await broker.WaitForEventAsync(e => _IsFailedReceive(e, "orders"), AbortToken).WaitAsync(_Bound, AbortToken);
        broker.Restore();
        broker.Clock.Advance(broker.RetryInterval);
        var afterReconnect = await probe.NextAsync(AbortToken);

        // when: the value from before the reconnect is used
        await probe.Client.CommitAsync(beforeReconnect, AbortToken);

        // then: it settles nothing, and the redelivery is still live
        afterReconnect.Message.Id.Should().Be("message-1");
        afterReconnect.DeliveryCount.Should().Be(2);
        broker.Events.Should().Contain(e => e.Kind == BrokerEventKind.SettlementRefused && e.MessageId == "message-1");
        broker.Events.Should().NotContain(e => e.Kind == BrokerEventKind.Committed);
        probe.Client.InFlightCount.Should().Be(1);

        // and: the redelivery's own value settles it
        await probe.Client.CommitAsync(afterReconnect, AbortToken);
        broker.Events.Should().ContainSingle(e => e.Kind == BrokerEventKind.Committed && e.DeliveryCount == 2);
        probe.Client.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_publishes_and_new_clients_while_the_connection_is_down()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var transport = broker.CreateTransport(MessageLane.Queue);
        broker.Disconnect();

        // when
        var duringOutage = await transport.SendAsync(_Message("orders", "message-1"), AbortToken);
        var connect = () => broker.CreateConsumerClient(new ConsumerClientRequest("orders", 0, MessageLane.Queue));

        // then
        duringOutage.Succeeded.Should().BeFalse();
        duringOutage.IsBrokerUnreachable.Should().BeTrue();
        connect.Should().Throw<BrokerConnectionException>();

        // and: the connection coming back lets the next publish through
        broker.Restore();
        (await transport.SendAsync(_Message("orders", "message-2"), AbortToken)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_a_delivery_unsettled_when_its_settlement_fails()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var probe = await ClientProbe.StartAsync(broker, "orders", MessageLane.Queue, AbortToken);
        await _SendAsync(broker, MessageLane.Queue, "orders", "message-1");
        var delivery = await probe.NextAsync(AbortToken);
        broker.Faults.FailSettlements(1);

        // when
        var commit = async () => await probe.Client.CommitAsync(delivery, AbortToken);

        // then: the failed ack leaves the delivery live, so the next ack settles it
        await commit.Should().ThrowAsync<SimulatedBrokerException>();
        probe.Client.InFlightCount.Should().Be(1);
        await probe.Client.CommitAsync(delivery, AbortToken);
        probe.Client.InFlightCount.Should().Be(0);
        broker.Events.Should().ContainSingle(e => e.Kind == BrokerEventKind.Committed);
    }

    [Fact]
    public async Task should_return_unsettled_deliveries_and_report_shutdown_when_a_channel_closes()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var first = await ClientProbe.StartAsync(broker, "orders", MessageLane.Queue, AbortToken);
        await _SendAsync(broker, MessageLane.Queue, "orders", "message-1");
        var lost = await first.NextAsync(AbortToken);

        // when
        broker.CloseChannels("orders");

        // then: the client stops and says why, and its delivery goes to the next client with a stale value left behind
        await first.Listening.WaitAsync(_Bound, AbortToken);
        first.Logs.Should().Contain(log => log.LogType == MqLogType.ConsumerShutdown);

        await using var replacement = await ClientProbe.StartAsync(broker, "orders", MessageLane.Queue, AbortToken);
        var redelivery = await replacement.NextAsync(AbortToken);
        redelivery.Message.Id.Should().Be("message-1");
        redelivery.DeliveryCount.Should().Be(2);

        await first.Client.CommitAsync(lost, AbortToken);
        replacement.Client.InFlightCount.Should().Be(1);
    }

    [Fact]
    public async Task should_redeliver_a_delayed_reject_only_once_the_clock_passes_the_delay()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var probe = await ClientProbe.StartAsync(broker, "orders", MessageLane.Queue, AbortToken);
        await _SendAsync(broker, MessageLane.Queue, "orders", "message-1");
        var delivery = await probe.NextAsync(AbortToken);

        // when
        await probe.Client.RejectAsync(delivery, TimeSpan.FromSeconds(5), AbortToken);
        broker.Clock.Advance(TimeSpan.FromSeconds(4));

        // then: nothing is back before the delay ends
        broker.Events.Should().NotContain(e => e.Kind == BrokerEventKind.Requeued);

        broker.Clock.Advance(TimeSpan.FromSeconds(1));
        var redelivery = await probe.NextAsync(AbortToken);
        redelivery.DeliveryCount.Should().Be(2);
    }

    [Fact]
    public async Task should_fan_a_bus_message_out_to_every_group_and_give_a_queue_message_one_owner()
    {
        // given: two groups on each lane
        var broker = new SimulatedBroker(new SimulationClock());
        await using var billing = await ClientProbe.StartAsync(
            broker,
            "billing",
            MessageLane.Bus,
            AbortToken,
            "order-placed"
        );
        await using var shipping = await ClientProbe.StartAsync(
            broker,
            "shipping",
            MessageLane.Bus,
            AbortToken,
            "order-placed"
        );
        await using var workerA = await ClientProbe.StartAsync(
            broker,
            "worker-a",
            MessageLane.Queue,
            AbortToken,
            "ship-order"
        );
        await using var workerB = await ClientProbe.StartAsync(
            broker,
            "worker-b",
            MessageLane.Queue,
            AbortToken,
            "ship-order"
        );

        // when
        await _SendAsync(broker, MessageLane.Bus, "order-placed", "event-1");
        await _SendAsync(broker, MessageLane.Queue, "ship-order", "command-1");

        // then
        (await billing.NextAsync(AbortToken))
            .Message.Id.Should()
            .Be("event-1");
        (await shipping.NextAsync(AbortToken)).Message.Id.Should().Be("event-1");
        await broker
            .WaitForEventAsync(
                e =>
                    e.Kind is BrokerEventKind.Delivered
                    && string.Equals(e.MessageId, "command-1", StringComparison.Ordinal),
                AbortToken
            )
            .WaitAsync(_Bound, AbortToken);
        broker
            .Events.Count(e =>
                e.Kind is BrokerEventKind.Delivered && string.Equals(e.MessageId, "command-1", StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_hold_a_hung_close_until_it_is_released_however_far_the_clock_moves()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        var probe = await ClientProbe.StartAsync(broker, "orders", MessageLane.Queue, AbortToken);
        broker.Faults.HangCloses();

        // when
        var close = probe.Client.ShutdownAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        broker.Clock.Advance(TimeSpan.FromHours(1));

        // then
        close.IsCompleted.Should().BeFalse();
        broker.Faults.ReleaseHungCloses();
        await close.WaitAsync(_Bound, AbortToken);
        await probe.DisposeAsync();
    }

    [Fact]
    public async Task should_surface_a_store_write_failing_on_its_second_call_to_the_publisher()
    {
        // given
        await using var host = await _StartConsumerHostAsync();
        host.Faults.FailStore(StorageOperation.StoreMessage, onCall: 2);
        await _PublishAsync(host, new SimulationOrder("order-1"));

        // when
        var second = () => _PublishAsync(host, new SimulationOrder("order-2"));

        // then
        await second.Should().ThrowAsync<SimulatedStoreException>();
        host.Storage.AppliedFaults.Should().ContainSingle().Which.Fault.Kind.Should().Be(StoreFaultKind.Fail);
    }

    [Fact]
    public async Task should_keep_the_row_committed_when_the_publisher_sees_a_lost_commit_reply()
    {
        // given
        await using var host = await _StartConsumerHostAsync();
        host.Faults.LoseStoreReply(StorageOperation.StoreMessage);

        // when
        var publish = () => _PublishAsync(host, new SimulationOrder("order-1"));

        // then: the caller sees a failure, and the store holds the row it committed
        await publish.Should().ThrowAsync<SimulatedStoreException>();
        var withheld = host.Storage.AppliedFaults.Should().ContainSingle().Subject.WithheldResult;
        var committed = withheld.Should().BeOfType<MediumMessage>().Subject;
        var row = await host.Storage.GetMonitoringApi().GetPublishedMessageAsync(committed.StorageId, AbortToken);
        row.Should().NotBeNull();
    }

    [Fact]
    public async Task should_commit_a_delayed_store_write_only_after_the_clock_advances()
    {
        // given
        await using var store = _CreateStore();
        store.Faults.DelayStoreCommit(StorageOperation.StoreMessage, TimeSpan.FromSeconds(5));

        // when
        var write = store
            .Storage.StoreMessageAsync("orders", _StoredMessage("message-1"), cancellationToken: AbortToken)
            .AsTask();

        // then
        write.IsCompleted.Should().BeFalse();
        store.Clock.Advance(TimeSpan.FromSeconds(5));
        var stored = await write.WaitAsync(_Bound, AbortToken);
        (await store.Storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken))
            .Should()
            .NotBeNull();
    }

    [Fact]
    public async Task should_report_a_lease_lost_once_the_clock_passes_its_scripted_expiry()
    {
        // given: a five-minute lease the store will lose thirty seconds after granting it
        await using var store = _CreateStore();
        store.Faults.ExpireLeasesAfter(TimeSpan.FromSeconds(30));
        var message = await store.Storage.StoreReceivedMessageAsync(
            "orders",
            "orders-group",
            _StoredMessage("message-1"),
            AbortToken
        );
        (
            await store.Storage.ChangeReceiveStateAsync(
                message,
                StatusName.Failed,
                retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
        var lease = TimeSpan.FromMinutes(5);
        (await store.Storage.LeaseReceiveAsync(message, lease, AbortToken)).Should().BeTrue();

        // when: a renewal before the expiry, then one after it
        store.Clock.Advance(TimeSpan.FromSeconds(10));
        var beforeExpiry = await store.Storage.RenewReceiveLeaseAsync(message, lease, AbortToken);
        store.Clock.Advance(TimeSpan.FromSeconds(25));
        var afterExpiry = await store.Storage.RenewReceiveLeaseAsync(message, lease, AbortToken);

        // then: renewing did not save it
        beforeExpiry.Should().BeTrue();
        afterExpiry.Should().BeFalse();
        store.Storage.LostLeases.Should().Equal(message.StorageId);
    }

    [Fact]
    public async Task should_fail_fast_with_a_message_when_shutdown_is_awaited_without_advancing_the_clock()
    {
        // given: a consumer client whose close takes ten seconds of simulated time
        await using var host = await _StartConsumerHostAsync();
        host.Broker.Faults.SlowCloses(TimeSpan.FromSeconds(10));
        host.ShutdownStallWindow = TimeSpan.FromMilliseconds(200);

        // when
        var stop = () => host.StopAsync(AbortToken);

        // then
        (await stop.Should().ThrowAsync<SimulationStallException>()).WithMessage("*waiting on the simulation clock*");

        // and: advancing the clock while awaiting lets the same shutdown finish
        var finishing = host.StopAsync(AbortToken);
        host.Advance(TimeSpan.FromSeconds(10));
        await finishing.WaitAsync(_Bound, AbortToken);
    }

    [Fact]
    public async Task should_present_the_capabilities_of_the_in_memory_store_and_resolve_as_the_coordination_resolver()
    {
        // given
        await using var store = _CreateStore();
        Type[] capabilities =
        [
            typeof(IDelayedMessageClaimStorage),
            typeof(IGracefulLeaseReleaseStorage),
            typeof(ICircuitRetryDeferralStorage),
            typeof(IDeliveryCoordinationResolver),
            typeof(ICoordinatedMessageStore),
            typeof(IMessageRevocationStorage),
        ];

        // when
        var innerCapabilities = capabilities.Where(type => type.IsInstanceOfType(store.Storage.Inner));
        var decoratorCapabilities = capabilities.Where(type => type.IsInstanceOfType(store.Storage));

        // then
        store.Storage.Inner.GetType().Name.Should().Be("InMemoryDataStorage");
        decoratorCapabilities.Should().BeEquivalentTo(innerCapabilities);
        store.Services.GetRequiredService<IDataStorage>().Should().BeSameAs(store.Storage);
        store.Services.GetRequiredService<IDeliveryCoordinationResolver>().Should().BeSameAs(store.Storage);
    }

    private static async Task<SimulationHost> _StartConsumerHostAsync()
    {
        return await SimulationHost.StartAsync(
            static setup =>
            {
                setup.AddConsumer<SimulationOrderConsumer>();
                setup.AddConsumer<SimulationInvoiceConsumer>();
            },
            static services =>
            {
                _DeclareMessages(services);
                services.AddSingleton<SimulationOrderConsumer>();
                services.AddSingleton<SimulationInvoiceConsumer>();
            },
            cancellationToken: AbortToken
        );
    }

    private static void _DeclareMessages(IServiceCollection services)
    {
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<SimulationOrder>(SimulationOrder.Name);
            messaging.Message<SimulationInvoice>(SimulationInvoice.Name);
        });
    }

    private static async Task _PublishAsync<T>(SimulationHost host, T message)
        where T : class
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBus>().PublishAsync(message, cancellationToken: AbortToken);
    }

    /// <summary>A store on the simulation clock with no host running, so no background processor touches its rows.</summary>
    private static SimulatedStore _CreateStore()
    {
        var clock = new SimulationClock();
        var faults = new FaultScript();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSimulatedBroker(new SimulatedBroker(clock, faults));
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.UseInMemoryStorage();
        });
        FaultInjectingDataStorage.Install(services, faults, clock);

        return new SimulatedStore(services.BuildServiceProvider(), clock, faults);
    }

    private static Message _StoredMessage(string messageId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = "orders",
        };

        return new Message(headers, new { Data = "simulated" });
    }

    private static Task _SendAsync(SimulatedBroker broker, MessageLane lane, string messageName, string messageId)
    {
        return broker.CreateTransport(lane).SendAsync(_Message(messageName, messageId), AbortToken);
    }

    private static TransportMessage _Message(string messageName, string messageId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = messageName,
        };

        return new TransportMessage(headers, "simulated"u8.ToArray());
    }

    private static bool _IsFailedReceive(BrokerEvent brokerEvent, string subscription) =>
        brokerEvent.Kind is BrokerEventKind.ReceiveFailed
        && string.Equals(brokerEvent.Subscription, subscription, StringComparison.Ordinal);

    private sealed class SimulatedStore(ServiceProvider services, SimulationClock clock, FaultScript faults)
        : IAsyncDisposable
    {
        public IServiceProvider Services => services;

        public SimulationClock Clock => clock;

        public FaultScript Faults => faults;

        public FaultInjectingDataStorage Storage { get; } = services.GetRequiredService<FaultInjectingDataStorage>();

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    /// <summary>One consumer client driven directly, collecting what it delivers and logs.</summary>
    private sealed class ClientProbe : IAsyncDisposable
    {
        private readonly Channel<SimulatedDelivery> _deliveries = Channel.CreateUnbounded<SimulatedDelivery>();
        private readonly CancellationTokenSource _stop = new();

        private ClientProbe(SimulatedConsumerClient client)
        {
            Client = client;
        }

        public SimulatedConsumerClient Client { get; }

        public ConcurrentQueue<LogMessageEventArgs> Logs { get; } = new();

        public Task Listening { get; private set; } = Task.CompletedTask;

        public static async Task<ClientProbe> StartAsync(
            SimulatedBroker broker,
            string subscription,
            MessageLane lane,
            CancellationToken cancellationToken,
            string? messageName = null
        )
        {
            var client = broker.CreateConsumerClient(new ConsumerClientRequest(subscription, 0, lane));
            var probe = new ClientProbe(client);
            client.AttachCallbacks(
                (_, settlement) =>
                {
                    probe._deliveries.Writer.TryWrite((SimulatedDelivery)settlement!);
                    return Task.CompletedTask;
                },
                probe.Logs.Enqueue
            );

            await client.SubscribeAsync([messageName ?? subscription], cancellationToken);
            probe.Listening = Task.Run(
                async () => await client.ListeningAsync(TimeSpan.FromSeconds(1), probe._stop.Token),
                CancellationToken.None
            );
            await client.WaitUntilReadyAsync(cancellationToken);

            return probe;
        }

        public Task<SimulatedDelivery> NextAsync(CancellationToken cancellationToken)
        {
            return _deliveries.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_Bound, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await Listening;
            }
            catch (OperationCanceledException)
            {
                // The probe stopped its own listener.
            }

            await Client.DisposeAsync();
            _stop.Dispose();
        }
    }
}

public sealed record SimulationOrder(string Id)
{
    public const string Name = "simulation.order";
}

public sealed record SimulationInvoice(string Id)
{
    public const string Name = "simulation.invoice";
}

/// <summary>Records what it consumes, and lets a test await the next one without polling.</summary>
public abstract class SimulationRecordingConsumer<TMessage> : IConsume<TMessage>
    where TMessage : class
{
    private readonly Channel<TMessage> _received = Channel.CreateUnbounded<TMessage>();

    public ValueTask ConsumeAsync(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
    {
        _received.Writer.TryWrite(context.Message);
        return ValueTask.CompletedTask;
    }

    public Task<TMessage> NextAsync(TimeSpan bound, CancellationToken cancellationToken)
    {
        return _received.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(bound, cancellationToken);
    }
}

[BusConsumer("tests.simulation.orders")]
public sealed class SimulationOrderConsumer : SimulationRecordingConsumer<SimulationOrder>;

[BusConsumer("tests.simulation.invoices")]
public sealed class SimulationInvoiceConsumer : SimulationRecordingConsumer<SimulationInvoice>;
