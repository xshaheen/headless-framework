// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Demo.Contracts.DomainEvents;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class ServiceBusTransportTests : TestBase
{
    private readonly IOptions<AzureServiceBusMessagingOptions> _options;

    public ServiceBusTransportTests()
    {
        var config = new AzureServiceBusMessagingOptions
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
        };

        config.ConfigureCustomProducer<EntityCreated>(cfg => cfg.UseTopic("entity-created").WithSubscription());

        _options = Options.Create(config);
    }

    private static AzureServiceBusTransport _CreateTransport(IOptions<AzureServiceBusMessagingOptions> options)
    {
        return new AzureServiceBusTransport(
            NullLogger<AzureServiceBusTransport>.Instance,
            options,
            Substitute.For<IAzureServiceBusClientPool>()
        );
    }

    private static AzureServiceBusTransport _CreateTransport(
        IOptions<AzureServiceBusMessagingOptions> options,
        IAzureServiceBusClientPool pool
    )
    {
        return new AzureServiceBusTransport(NullLogger<AzureServiceBusTransport>.Instance, options, pool);
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var transport = _CreateTransport(_options);

        // then
        transport.BrokerAddress.Name.Should().Be("servicebus");
        transport.BrokerAddress.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_have_correct_broker_address_when_using_namespace()
    {
        // given
        var options = Options.Create(
            new AzureServiceBusMessagingOptions { Namespace = "sb://custom.servicebus.windows.net/" }
        );

        // when
        await using var transport = _CreateTransport(options);

        // then
        transport.BrokerAddress.Endpoint.Should().Be("sb://custom.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_use_custom_topic_for_configured_producer()
    {
        // given
        await using var transport = _CreateTransport(_options);

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
        await using var transport = _CreateTransport(_options);

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
        await using var transport = _CreateTransport(_options);

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
        await using var transport = _CreateTransport(_options);

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
            new AzureServiceBusMessagingOptions
            {
                ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
                TopicPath = "custom-topic",
            }
        );

        await using var transport = _CreateTransport(options);

        var transportMessage = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { Headers.MessageName, "SomeMessage" } },
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
        await using var transport = _CreateTransport(_options);

        // when
        var act = async () => await transport.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_send_message_to_pooled_topic_sender()
    {
        // given
        var sender = Substitute.For<ServiceBusSender>();
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var pool = Substitute.For<IAzureServiceBusClientPool>();
        pool.GetSender("entity-created").Returns(sender);

        await using var transport = _CreateTransport(_options, pool);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityCreated) },
                { Headers.MessageId, "message-1" },
            },
            body: """{"id":42}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        pool.Received(1).GetSender("entity-created");
        await sender
            .Received(1)
            .SendMessageAsync(
                Arg.Is<ServiceBusMessage>(m =>
                    m.Subject == nameof(EntityCreated)
                    && m.ApplicationProperties[Headers.MessageId].ToString() == "message-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_failed_when_send_fails()
    {
        // given
        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("Network error", ServiceBusFailureReason.ServiceBusy));

        var pool = Substitute.For<IAzureServiceBusClientPool>();
        pool.GetSender("entity-created").Returns(sender);

        await using var transport = _CreateTransport(_options, pool);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityCreated) },
                { Headers.MessageId, "message-1" },
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Contain("Network error");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        // given
        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var pool = Substitute.For<IAzureServiceBusClientPool>();
        pool.GetSender("entity-created").Returns(sender);

        await using var transport = _CreateTransport(_options, pool);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageName, nameof(EntityCreated) },
                { Headers.MessageId, "message-1" },
            },
            body: "test"u8.ToArray()
        );

        // when
        var act = () => transport.SendAsync(message);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
