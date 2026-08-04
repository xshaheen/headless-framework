// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;

namespace Tests;

public sealed class RabbitMqPhysicalAddressTests
{
    [Theory]
    [InlineData(MessageLane.Bus, "messaging.bus", "topic", "bus.orders", "bus.workers")]
    [InlineData(MessageLane.Queue, "messaging.queue", "direct", "queue.orders", "queue.orders")]
    public void should_derive_every_physical_address_from_lane(
        MessageLane lane,
        string exchange,
        string exchangeType,
        string routingKey,
        string queue
    )
    {
        RabbitMqPhysicalAddress.Exchange("messaging", lane).Should().Be(exchange);
        RabbitMqPhysicalAddress.ExchangeType(lane).Should().Be(exchangeType);
        RabbitMqPhysicalAddress.RoutingKey(lane, "orders").Should().Be(routingKey);
        RabbitMqPhysicalAddress.Queue(lane, "workers", "orders").Should().Be(queue);
    }
}
