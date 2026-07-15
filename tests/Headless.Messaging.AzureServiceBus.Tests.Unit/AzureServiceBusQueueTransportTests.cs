// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AzureServiceBusQueueTransportTests : TestBase
{
    private static readonly IOptions<AzureServiceBusMessagingOptions> _Options = Options.Create(
        new AzureServiceBusMessagingOptions
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
        }
    );

    private static AzureServiceBusQueueTransport _CreateTransport(IAzureServiceBusClientPool pool)
    {
        return new AzureServiceBusQueueTransport(NullLogger<AzureServiceBusQueueTransport>.Instance, _Options, pool);
    }

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        await using var transport = _CreateTransport(Substitute.For<IAzureServiceBusClientPool>());

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("servicebus");
        brokerAddress.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public async Task should_send_message_to_pooled_queue_sender()
    {
        // given
        var sender = Substitute.For<ServiceBusSender>();
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var pool = Substitute.For<IAzureServiceBusClientPool>();
        pool.GetSender("OrderCreated").Returns(sender);

        await using var transport = _CreateTransport(pool);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
            },
            body: """{"id":42}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        pool.Received(1).GetSender("OrderCreated");
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
        var sender = Substitute.For<ServiceBusSender>();
        sender
            .SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("Network error", ServiceBusFailureReason.ServiceBusy));

        var pool = Substitute.For<IAzureServiceBusClientPool>();
        pool.GetSender("OrderCreated").Returns(sender);

        await using var transport = _CreateTransport(pool);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
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
        pool.GetSender("OrderCreated").Returns(sender);

        await using var transport = _CreateTransport(pool);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
            },
            body: "test"u8.ToArray()
        );

        // when
        var act = () => transport.SendAsync(message, AbortToken);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
