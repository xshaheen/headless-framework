// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AwsHostPrefixTests : TestBase
{
    [Fact]
    public void should_keep_names_unchanged_without_a_prefix()
    {
        AwsPhysicalAddress.BusTopic("", "orders.created").Should().StartWith("bus-orders-created-");
        AwsPhysicalAddress.BusTopic("", "orders").Should().Be("bus-orders");
        AwsPhysicalAddress.QueueDestination("", "orders").Should().Be("queue-orders");
        AwsPhysicalAddress.BusSubscriptionQueue("", "billing").Should().Be("bus-billing");
        AwsPhysicalAddress.BusSubscriptionQueue("", "billing.fifo").Should().Be("bus-billing.fifo");
    }

    [Fact]
    public void should_put_the_prefix_in_front_of_every_topic_and_queue()
    {
        AwsPhysicalAddress.BusTopic("wt1", "orders").Should().Be("wt1_bus-orders");
        AwsPhysicalAddress.QueueDestination("wt1", "orders").Should().Be("wt1_queue-orders");
        AwsPhysicalAddress.QueueDestination("wt1", "orders.fifo").Should().Be("wt1_queue-orders.fifo");
        AwsPhysicalAddress.BusSubscriptionQueue("wt1", "billing").Should().Be("wt1_bus-billing");
        AwsPhysicalAddress.BusSubscriptionQueue("wt1", "billing.fifo").Should().Be("wt1_bus-billing.fifo");
    }

    [Fact]
    public void should_hash_a_name_the_prefix_pushes_past_the_sqs_limit()
    {
        var prefix = new string('p', HeadlessPrefixOptions.MaxLength);
        var name = new string('o', 70);

        var queue = AwsPhysicalAddress.QueueDestination(prefix, name);
        var fifo = AwsPhysicalAddress.QueueDestination(prefix, name + ".fifo");
        var subscription = AwsPhysicalAddress.BusSubscriptionQueue(prefix, new string('b', 90) + ".fifo");

        queue.Should().HaveLength(80).And.StartWith(prefix + "_queue-ooo");
        fifo.Should().HaveLength(80).And.EndWith(".fifo");
        subscription.Should().HaveLength(80).And.StartWith(prefix + "_bus-bbb").And.EndWith(".fifo");
        AwsPhysicalAddress.QueueDestination(prefix, name).Should().Be(queue);
    }

    [Fact]
    public async Task should_apply_the_host_prefix_to_the_transport_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessMessaging(setup => setup.UseAws(options => options.Region = Amazon.RegionEndpoint.USEast1));
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<AmazonSqsMessagingOptions>>().Value.HostPrefix.Should().Be("wt1");
    }
}
