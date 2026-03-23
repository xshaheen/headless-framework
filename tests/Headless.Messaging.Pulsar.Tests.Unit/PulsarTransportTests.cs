// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class PulsarTransportTests : TestBase
{
    private readonly ILogger<PulsarTransport> _logger;
    private readonly IConnectionFactory _connectionFactory;

    public PulsarTransportTests()
    {
        _logger = NullLogger<PulsarTransport>.Instance;
        _connectionFactory = Substitute.For<IConnectionFactory>();
        _connectionFactory.ServersAddress.Returns("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // then
        transport.BrokerAddress.Name.Should().Be("pulsar");
        transport.BrokerAddress.Endpoint.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_request_producer_for_message_topic()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        _connectionFactory.CreateProducerAsync("TestTopic").ThrowsAsync(new InvalidOperationException("Expected"));

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        await _connectionFactory.Received(1).CreateProducerAsync("TestTopic");
    }

    [Fact]
    public async Task should_wrap_exception_from_create_producer()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        _connectionFactory
            .CreateProducerAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        result.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // when
        var act = async () => await transport.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_use_message_name_as_topic()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "orders.created");

        _connectionFactory
            .CreateProducerAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("Expected"));

        // when
        await transport.SendAsync(message);

        // then
        await _connectionFactory.Received(1).CreateProducerAsync("orders.created");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await transport.SendAsync(_CreateTransportMessage("msg-123", "TestTopic"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _connectionFactory
            .ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(IConnectionFactory.CreateProducerAsync));
    }

    private static TransportMessage _CreateTransportMessage(string messageId, string messageName)
    {
        return new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, messageId },
                { MessagingHeaders.MessageName, messageName },
            },
            body: "test-body"u8.ToArray()
        );
    }
}
