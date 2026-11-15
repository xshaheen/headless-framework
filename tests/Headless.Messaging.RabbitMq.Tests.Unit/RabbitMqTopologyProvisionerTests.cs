// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tests;

public sealed class RabbitMqTopologyProvisionerTests : TestBase
{
    [Fact]
    public void should_own_only_the_queues_this_host_consumes()
    {
        // given — the host consumes "orders" and only publishes "invoices"
        var topology = new MessagingTopology(
            [new TopologySubscription("orders", MessageLane.Queue, ConsumerSubscriptionKind.Competing, ["orders"])],
            [new TopologyMessage("orders", MessageLane.Queue), new TopologyMessage("invoices", MessageLane.Queue)]
        );

        // when
        var queues = RabbitMqTopologyProvisioner.Queues(topology);

        // then
        queues.Should().HaveCount(2);
        queues.Should().ContainSingle(q => q.Name == "queue.orders").Which.Owned.Should().BeTrue();
        queues.Should().ContainSingle(q => q.Name == "queue.invoices").Which.Owned.Should().BeFalse();
    }

    [Fact]
    public void should_own_a_bus_identity_queue_and_skip_every_instance_subscriptions()
    {
        // given
        var topology = new MessagingTopology(
            [
                new TopologySubscription("billing", MessageLane.Bus, ConsumerSubscriptionKind.Competing, ["paid"]),
                new TopologySubscription("cache", MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance, ["paid"]),
            ],
            [new TopologyMessage("paid", MessageLane.Bus)]
        );

        // when
        var queues = RabbitMqTopologyProvisioner.Queues(topology);

        // then
        queues.Should().ContainSingle().Which.Owned.Should().BeTrue();
    }

    [Fact]
    public void should_mark_only_a_sequential_competing_queue_for_a_single_active_consumer()
    {
        // given
        var topology = new MessagingTopology(
            [
                new TopologySubscription("orders", MessageLane.Queue, ConsumerSubscriptionKind.Competing, ["orders"])
                {
                    SingleActiveConsumer = true,
                },
                new TopologySubscription("billing", MessageLane.Bus, ConsumerSubscriptionKind.Competing, ["paid"]),
            ],
            // The host also publishes the message it consumes sequentially: the two entries name one queue.
            [new TopologyMessage("orders", MessageLane.Queue), new TopologyMessage("paid", MessageLane.Bus)]
        );

        // when
        var queues = RabbitMqTopologyProvisioner.Queues(topology);

        // then
        queues.Should().ContainSingle(q => q.Name == "queue.orders").Which.SingleActiveConsumer.Should().BeTrue();
        queues.Should().ContainSingle(q => q.Name == "bus.billing").Which.SingleActiveConsumer.Should().BeFalse();
    }

    [Fact]
    public async Task should_provision_a_sequential_queue_with_the_single_active_consumer_argument()
    {
        // given
        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);
        var pool = Substitute.For<IConnectionChannelPool>();
        pool.Exchange.Returns("test.exchange");
        pool.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        var provisioner = new RabbitMqTopologyProvisioner(
            pool,
            Options.Create(
                new RabbitMqMessagingOptions
                {
                    HostName = "localhost",
                    UserName = "test_user",
                    Password = "test_pass",
                    ExchangeName = "test.exchange",
                }
            )
        );
        var topology = new MessagingTopology(
            [
                new TopologySubscription("orders", MessageLane.Queue, ConsumerSubscriptionKind.Competing, ["orders"])
                {
                    SingleActiveConsumer = true,
                },
                new TopologySubscription(
                    "invoices",
                    MessageLane.Queue,
                    ConsumerSubscriptionKind.Competing,
                    ["invoices"]
                ),
            ],
            []
        );

        // when
        await provisioner.ProvisionAsync(topology, AbortToken);

        // then
        await channel
            .Received(1)
            .QueueDeclareAsync(
                "queue.orders",
                true,
                false,
                false,
                Arg.Is<IDictionary<string, object?>>(a =>
                    a.ContainsKey("x-single-active-consumer") && Equals(a["x-single-active-consumer"], true)
                ),
                false,
                false,
                AbortToken
            );
        await channel
            .Received(1)
            .QueueDeclareAsync(
                "queue.invoices",
                true,
                false,
                false,
                Arg.Is<IDictionary<string, object?>>(a => !a.ContainsKey("x-single-active-consumer")),
                false,
                false,
                AbortToken
            );
    }
}
