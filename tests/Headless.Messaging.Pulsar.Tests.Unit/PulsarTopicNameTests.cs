// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarTopicNameTests : TestBase
{
    [Theory]
    [InlineData("orders", "persistent://public/default/orders")]
    [InlineData("tenant/ns/orders", "persistent://tenant/ns/orders")]
    [InlineData("persistent://tenant/ns/orders-partition-2", "persistent://tenant/ns/orders-partition-2")]
    [InlineData("non-persistent://t/n/x", "non-persistent://t/n/x")]
    public void should_complete_a_topic_name_in_any_form(string topic, string expected)
    {
        PulsarTopicName.Complete(topic).Should().Be(expected);
    }

    [Theory]
    [InlineData("headless-queue-runs.result", "persistent://public/default/headless-queue-runs.result")]
    [InlineData("tenant/ns/headless-queue-x", "persistent://tenant/ns/headless-queue-x")]
    [InlineData("persistent://public/default/x-partition-12", "persistent://public/default/x")]
    [InlineData("x-partition-0", "persistent://public/default/x")]
    [InlineData("non-persistent://t/n/x", "non-persistent://t/n/x")]
    [InlineData("persistent://public/default/x-partition-", "persistent://public/default/x-partition-")]
    [InlineData("persistent://public/default/x-partition-a", "persistent://public/default/x-partition-a")]
    [InlineData("persistent://t/x-partition-1/y", "persistent://t/x-partition-1/y")]
    public void should_normalize_a_topic_to_its_complete_partition_free_name(string topic, string expected)
    {
        PulsarTopicName.Normalize(topic).Should().Be(expected);
    }

    [Fact]
    public void should_name_a_partition_of_a_topic()
    {
        PulsarTopicName.Partition("headless-queue-orders", 3).Should().Be("headless-queue-orders-partition-3");
    }
}
