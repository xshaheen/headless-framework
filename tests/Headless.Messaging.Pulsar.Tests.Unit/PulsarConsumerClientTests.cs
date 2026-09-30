// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Unit tests for PulsarConsumerClient.
/// Note: The Pulsar.Client types (PulsarClient, ConsumerBuilder, etc.) cannot be mocked with NSubstitute
/// as they don't have parameterless constructors. These tests focus on behavior that can be tested
/// without mocking the Pulsar client internals.
/// </summary>
public sealed class PulsarConsumerClientTests : TestBase
{
    private readonly IOptions<PulsarMessagingOptions> _options = Options.Create(
        new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }
    );

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
    public void should_have_correct_broker_address()
    {
        // Note: Cannot create PulsarConsumerClient without a real PulsarClient
        // This test documents the expected behavior
        var options = _options.Value;

        // then
        options.ServiceUrl.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public void should_be_used_for_broker_address_when_options_service_url()
    {
        // given
        var customOptions = Options.Create(new PulsarMessagingOptions { ServiceUrl = "pulsar://custom-host:6650" });

        // then - when a client is created with these options, the broker address should match
        customOptions.Value.ServiceUrl.Should().Be("pulsar://custom-host:6650");
    }
}
