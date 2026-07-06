// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Azure.Messaging.ServiceBus;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AzureServiceBusQueueTransportTests
{
    private static readonly IOptions<AzureServiceBusMessagingOptions> _Options = Options.Create(
        new AzureServiceBusMessagingOptions
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
        }
    );

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        await using var transport = new AzureServiceBusQueueTransport(
            NullLogger<AzureServiceBusQueueTransport>.Instance,
            _Options
        );

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("servicebus");
        brokerAddress.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_send_message_to_cached_queue_sender()
    {
        // given
        await using var transport = new AzureServiceBusQueueTransport(
            NullLogger<AzureServiceBusQueueTransport>.Instance,
            _Options
        );

        var sender = Substitute.For<ServiceBusSender>();
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _SetSender(transport, "OrderCreated", sender);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
            },
            body: """{"id":42}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, TestContext.Current.CancellationToken);

        // then
        result.Succeeded.Should().BeTrue();
        await sender
            .Received(1)
            .SendMessageAsync(
                Arg.Is<ServiceBusMessage>(m =>
                    m.Subject == "OrderCreated" && m.ApplicationProperties[Headers.MessageId].ToString() == "message-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_failed_when_send_fails()
    {
        // given
        await using var transport = new AzureServiceBusQueueTransport(
            NullLogger<AzureServiceBusQueueTransport>.Instance,
            _Options
        );

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("Network error", ServiceBusFailureReason.ServiceBusy));
        _SetSender(transport, "OrderCreated", sender);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, TestContext.Current.CancellationToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Contain("Network error");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        // given
        await using var transport = new AzureServiceBusQueueTransport(
            NullLogger<AzureServiceBusQueueTransport>.Instance,
            _Options
        );

        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        _SetSender(transport, "OrderCreated", sender);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
            },
            body: "test"u8.ToArray()
        );

        // when
        var act = () => transport.SendAsync(message, TestContext.Current.CancellationToken);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static void _SetSender(AzureServiceBusQueueTransport transport, string queueName, ServiceBusSender sender)
    {
        var field = typeof(AzureServiceBusQueueTransport).GetField(
            "_senders",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var senders = (ConcurrentDictionary<string, Lazy<ServiceBusSender>>)field.GetValue(transport)!;
        senders[queueName] = new Lazy<ServiceBusSender>(() => sender);
    }
}
