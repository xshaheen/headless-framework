// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarConsumerClientTests : TestBase
{
    [Fact]
    public void should_use_consumer_identity_for_bus_intent_when_get_subscription_name()
    {
        PulsarConsumerClient.GetSubscriptionName("payments", MessageLane.Bus).Should().Be("headless-bus-payments");
    }

    [Fact]
    public void should_keep_dotted_consumer_identity_readable_when_get_bus_subscription_name()
    {
        PulsarConsumerClient
            .GetSubscriptionName("billing.invoice-projection", MessageLane.Bus)
            .Should()
            .Be("headless-bus-billing.invoice-projection");
    }

    [Fact]
    public void should_normalize_consumer_identity_outside_strict_subscription_charset_when_get_bus_subscription_name()
    {
        var name = PulsarConsumerClient.GetSubscriptionName("billing/invoice projection", MessageLane.Bus);

        name.Should().MatchRegex("^headless-bus-billing-invoice-projection-[0-9a-f]{12}$");
        PulsarConsumerClient.GetSubscriptionName("billing/invoice projection", MessageLane.Bus).Should().Be(name);
    }

    [Fact]
    public void should_use_shared_subscription_for_queue_intent_when_get_subscription_name()
    {
        PulsarConsumerClient.GetSubscriptionName("payments", MessageLane.Queue).Should().Be("headless-queue");
    }

    [Fact]
    public void should_open_a_subscription_per_process_for_an_every_instance_bus_request()
    {
        // given
        var mine = _EveryInstance("pricing.price-cache", Guid.NewGuid());
        var theirs = _EveryInstance("pricing.price-cache", Guid.NewGuid());

        // when
        var name = PulsarPhysicalAddress.Subscription(mine);

        // then
        name.Should().StartWith("headless-bus-pricing.price-cache.");
        name.Should().Be(PulsarPhysicalAddress.Subscription(mine with { }), "one process keeps one name");
        name.Should().NotBe(PulsarPhysicalAddress.Subscription(theirs), "every process holds its own subscription");
        name.Should()
            .NotBe(
                PulsarConsumerClient.GetSubscriptionName("pricing.price-cache", MessageLane.Bus),
                "an every-instance subscription never joins the competing one"
            );
    }

    [Fact]
    public void should_keep_an_every_instance_name_unique_per_process_when_the_identity_is_shortened()
    {
        // given
        var identity = "pricing." + new string('a', 300);

        // when
        var first = PulsarPhysicalAddress.Subscription(_EveryInstance(identity, Guid.NewGuid()));
        var second = PulsarPhysicalAddress.Subscription(_EveryInstance(identity, Guid.NewGuid()));

        // then
        first.Length.Should().BeLessThanOrEqualTo(255);
        first.Should().NotBe(second);
    }

    private static ConsumerClientRequest _EveryInstance(string identity, Guid instanceId) =>
        new(identity, 1, MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance, instanceId);

    [Fact]
    public async Task should_report_the_service_url_as_broker_address()
    {
        // given
        await using var client = PulsarConsumerClients.Create(
            new ConsumerClientRequest("address-test", 1, MessageLane.Bus),
            options: new PulsarMessagingOptions { ServiceUrl = "pulsar://custom-host:6650" }
        );

        // when, then
        client.BrokerAddress.Name.Should().Be("pulsar");
        client.BrokerAddress.Endpoint.Should().Be("pulsar://custom-host:6650");
    }

    [Theory]
    [InlineData(false, DotPulsar.SubscriptionType.Shared)]
    [InlineData(true, DotPulsar.SubscriptionType.KeyShared)]
    public void should_open_a_shared_or_key_shared_competing_subscription(
        bool keyShared,
        DotPulsar.SubscriptionType expected
    )
    {
        PulsarConsumerClient.SubscriptionTypeFor(new PulsarConsumerConfig(keyShared)).Should().Be(expected);
    }

    [Fact]
    public void should_open_a_shared_competing_subscription_when_no_consumer_options_are_set()
    {
        PulsarConsumerClient.SubscriptionTypeFor(config: null).Should().Be(DotPulsar.SubscriptionType.Shared);
    }
}
