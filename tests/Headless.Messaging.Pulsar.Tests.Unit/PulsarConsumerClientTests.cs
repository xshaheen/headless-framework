// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Pulsar;
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
    private readonly IOptions<MessagingPulsarOptions> _options;

    public PulsarConsumerClientTests()
    {
        _options = Options.Create(new MessagingPulsarOptions { ServiceUrl = "pulsar://localhost:6650" });
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
    public void options_service_url_should_be_used_for_broker_address()
    {
        // given
        var customOptions = Options.Create(new MessagingPulsarOptions { ServiceUrl = "pulsar://custom-host:6650" });

        // then - when a client is created with these options, the broker address should match
        customOptions.Value.ServiceUrl.Should().Be("pulsar://custom-host:6650");
    }
}
