// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaHostPrefixTests : TestBase
{
    // The analyzer requires a bound on every match; these patterns are short and anchored.
    private static readonly TimeSpan _MatchTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public void should_keep_topics_and_groups_unchanged_without_a_prefix()
    {
        KafkaPhysicalAddress.Topic("", "orders.created").Should().Be("orders.created");
        KafkaPhysicalAddress.GroupId("", "orders.created").Should().Be("orders.created");
        KafkaPhysicalAddress.TopicPattern("", "^orders\\.(?>[0-9a-zA-Z]+)$").Should().Be("^orders\\.(?>[0-9a-zA-Z]+)$");
    }

    [Fact]
    public void should_put_the_prefix_in_front_of_topics_groups_and_patterns()
    {
        KafkaPhysicalAddress.Topic("wt1", "orders.created").Should().Be("wt1.orders.created");
        KafkaPhysicalAddress.GroupId("wt1", "orders.created").Should().Be("wt1.orders.created");

        var pattern = KafkaPhysicalAddress.TopicPattern("wt1", TransportNaming.WildcardToRegex("orders.*"));

        pattern.Should().StartWith("^");
        Regex.IsMatch("wt1.orders.created", pattern, RegexOptions.None, _MatchTimeout).Should().BeTrue();
        Regex.IsMatch("orders.created", pattern, RegexOptions.None, _MatchTimeout).Should().BeFalse();
        Regex.IsMatch("wt2.orders.created", pattern, RegexOptions.None, _MatchTimeout).Should().BeFalse();
    }

    [Fact]
    public void should_hash_a_prefixed_topic_past_the_kafka_limit_the_same_way_on_every_host()
    {
        var name = new string('o', 250);

        var topic = KafkaPhysicalAddress.Topic("wt1", name);

        topic.Should().HaveLength(249);
        topic.Should().StartWith("wt1.ooo");
        KafkaPhysicalAddress.Topic("wt1", name).Should().Be(topic);
        KafkaPhysicalAddress.Topic("", name).Should().Be(name);
    }

    [Fact]
    public async Task should_apply_the_host_prefix_to_the_transport_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessMessaging(setup => setup.UseKafka("localhost:9092"));
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value.HostPrefix.Should().Be("wt1");
        provider.GetRequiredService<IKafkaProducerProvider>().HostPrefix.Should().Be("wt1");
    }
}
