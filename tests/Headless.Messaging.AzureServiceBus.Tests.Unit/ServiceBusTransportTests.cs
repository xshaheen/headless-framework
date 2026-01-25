// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo.Contracts.DomainEvents;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ServiceBusTransportTests
{
    private readonly IOptions<AzureServiceBusOptions> _options;

    public ServiceBusTransportTests()
    {
        var config = new AzureServiceBusOptions
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
        };

        config.ConfigureCustomProducer<EntityCreated>(cfg => cfg.UseTopic("entity-created").WithSubscription());

        _options = Options.Create(config);
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            _options
        );

        // then
        transport.BrokerAddress.Name.Should().Be("servicebus");
        transport.BrokerAddress.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_have_correct_broker_address_when_using_namespace()
    {
        // given
        var options = Options.Create(
            new AzureServiceBusOptions { Namespace = "sb://custom.servicebus.windows.net/" }
        );

        // when
        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            options
        );

        // then
        transport.BrokerAddress.Endpoint.Should().Be("sb://custom.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_use_custom_topic_for_configured_producer()
    {
        // given
        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            _options
        );

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityCreated) },
            },
            body: null
        );

        // when
        var producer = transport.CreateProducerForMessage(transportMessage);

        // then
        producer.MessageTypeName.Should().Be(nameof(EntityCreated));
        producer.TopicPath.Should().Be("entity-created");
    }

    [Fact]
    public async Task should_use_default_topic_for_unconfigured_producer()
    {
        // given
        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            _options
        );

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityDeleted) },
            },
            body: null
        );

        // when
        var producer = transport.CreateProducerForMessage(transportMessage);

        // then
        producer.MessageTypeName.Should().Be(nameof(EntityDeleted));
        producer.TopicPath.Should().Be(_options.Value.TopicPath);
    }

    [Fact]
    public async Task should_preserve_create_subscription_setting_from_custom_producer()
    {
        // given
        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            _options
        );

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityCreated) },
            },
            body: null
        );

        // when
        var producer = transport.CreateProducerForMessage(transportMessage);

        // then
        producer.CreateSubscription.Should().BeTrue();
    }

    [Fact]
    public async Task should_use_default_subscription_setting_for_unconfigured_producer()
    {
        // given
        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            _options
        );

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, "UnknownMessage" },
            },
            body: null
        );

        // when
        var producer = transport.CreateProducerForMessage(transportMessage);

        // then
        producer.CreateSubscription.Should().BeTrue(); // Default is true
    }

    [Fact]
    public async Task should_use_custom_topic_path_from_options()
    {
        // given
        var options = Options.Create(
            new AzureServiceBusOptions
            {
                ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
                TopicPath = "custom-topic",
            }
        );

        await using var transport = new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            options
        );

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, "SomeMessage" },
            },
            body: null
        );

        // when
        var producer = transport.CreateProducerForMessage(transportMessage);

        // then
        producer.TopicPath.Should().Be("custom-topic");
    }

    [Fact]
    public async Task should_dispose_without_error_when_no_messages_sent()
    {
        // given
        var transport = new AzureServiceBusTransport(NullLogger<AzureServiceBusTransport>.Instance, _options);

        // when
        var act = async () => await transport.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }
}
