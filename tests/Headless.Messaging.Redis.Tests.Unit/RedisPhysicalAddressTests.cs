// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;

namespace Tests;

public sealed class RedisPhysicalAddressTests
{
    [Fact]
    public void should_name_bus_consumer_group_after_consumer_identity()
    {
        RedisPhysicalAddress
            .ConsumerGroup(MessageLane.Bus, "billing.invoice-projection")
            .Should()
            .Be("billing.invoice-projection");
    }

    [Fact]
    public void should_normalize_bus_consumer_identity_with_whitespace_to_stable_group()
    {
        var group = RedisPhysicalAddress.ConsumerGroup(MessageLane.Bus, "billing ops.invoice");

        group.Should().MatchRegex("^billing-ops\\.invoice-[0-9a-f]{12}$");
        RedisPhysicalAddress.ConsumerGroup(MessageLane.Bus, "billing ops.invoice").Should().Be(group);
    }

    [Fact]
    public void should_keep_queue_consumer_group_unchanged()
    {
        RedisPhysicalAddress.ConsumerGroup(MessageLane.Queue, "orders created").Should().Be("orders created");
        RedisPhysicalAddress.QueueStream("orders.created").Should().Be("headless:messaging:queue:orders.created");
    }
}
