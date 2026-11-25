// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureServiceBusHostPrefixTests : TestBase
{
    private const string _ConnectionString =
        "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";

    [Fact]
    public void should_keep_names_unchanged_without_a_prefix()
    {
        AzureServiceBusPhysicalAddress.Topic("", "messaging").Should().Be("messaging");
        AzureServiceBusPhysicalAddress.Queue("", "orders.created").Should().Be("orders.created");
        AzureServiceBusPhysicalAddress.QueueMessageName("", "orders.created").Should().Be("orders.created");
    }

    [Fact]
    public void should_put_the_prefix_in_front_of_topics_and_queues_and_map_queues_back()
    {
        AzureServiceBusPhysicalAddress.Topic("wt1", "messaging").Should().Be("wt1.messaging");
        AzureServiceBusPhysicalAddress.Queue("wt1", "orders.created").Should().Be("wt1.orders.created");
        AzureServiceBusPhysicalAddress.QueueMessageName("wt1", "wt1.orders.created").Should().Be("orders.created");
        AzureServiceBusPhysicalAddress.QueueMessageName("wt1", "wt2.orders.created").Should().BeNull();
    }

    [Fact]
    public void should_fail_when_the_prefix_pushes_an_entity_past_the_limit()
    {
        var act = () => AzureServiceBusPhysicalAddress.Queue("wt1", new string('o', 258));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'wt1'*Headless:Prefix*260*");
        AzureServiceBusPhysicalAddress.Queue("", new string('o', 258)).Should().HaveLength(258);
    }

    [Fact]
    public async Task should_prefix_the_shared_and_custom_producer_topics_once()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString = _ConnectionString;
                options.ConfigureCustomProducer<AzureServiceBusHostPrefixTests>(producer => producer.UseTopic("audit"));
            })
        );
        await using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<AzureServiceBusMessagingOptions>>().Value;
        options.HostPrefix.Should().Be("wt1");
        options.TopicPath.Should().Be("wt1." + AzureServiceBusMessagingOptions.DefaultTopicPath);
        options.CustomProducers.Should().ContainSingle().Which.TopicPath.Should().Be("wt1.audit");
    }

    [Fact]
    public async Task should_leave_topics_unchanged_without_a_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options => options.ConnectionString = _ConnectionString)
        );
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IOptions<AzureServiceBusMessagingOptions>>()
            .Value.TopicPath.Should()
            .Be(AzureServiceBusMessagingOptions.DefaultTopicPath);
    }
}
