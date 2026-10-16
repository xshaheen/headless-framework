// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

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
}
