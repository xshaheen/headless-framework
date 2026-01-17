// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Demo.Contracts.DomainEvents;
using Framework.Messages;
using Framework.Messages.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public class ServiceBusTransportTests
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
    public void Transport_ShouldHaveCorrectBrokerAddress()
    {
        // Given, When
        var transport = new AzureServiceBusTransport(NullLogger<AzureServiceBusTransport>.Instance, _options);

        // Then
        transport.BrokerAddress.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void CustomProducer_ShouldHaveCustomTopic()
    {
        // Given
        var transport = new AzureServiceBusTransport(NullLogger<AzureServiceBusTransport>.Instance, _options);

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityCreated) },
            },
            body: null
        );

        // When
        var producer = transport.CreateProducerForMessage(transportMessage);

        // Then
        producer.MessageTypeName.Should().Be(nameof(EntityCreated));
        producer.TopicPath.Should().Be("entity-created");
    }

    [Fact]
    public void DefaultProducer_ShouldHaveDefaultTopic()
    {
        // Given
        var transport = new AzureServiceBusTransport(NullLogger<AzureServiceBusTransport>.Instance, _options);

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityDeleted) },
            },
            body: null
        );

        // When
        var producer = transport.CreateProducerForMessage(transportMessage);

        // Then
        producer.MessageTypeName.Should().Be(nameof(EntityDeleted));
        producer.TopicPath.Should().Be(_options.Value.TopicPath);
    }
}
