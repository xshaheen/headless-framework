// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

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
        transport.BrokerAddress.Name.Should().Be("Pulsar");
        transport.BrokerAddress.Endpoint.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_request_producer_for_message_topic()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        // CreateProducerAsync will throw since we can't mock IProducer<byte[]>
        // BUG: Exception from CreateProducerAsync is NOT caught by the try-catch in SendAsync
        _connectionFactory.CreateProducerAsync("TestTopic").ThrowsAsync(new InvalidOperationException("Expected"));

        // when
        var act = async () => await transport.SendAsync(message);

        // then - the exception propagates because CreateProducerAsync is outside the try-catch block
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _connectionFactory.Received(1).CreateProducerAsync("TestTopic");
    }

    /// <summary>
    /// DESIGN ISSUE TEST: CreateProducerAsync exceptions are not caught by SendAsync.
    /// The try-catch block in SendAsync only wraps producer.NewMessage and producer.SendAsync,
    /// but CreateProducerAsync is called before the try block, so its exceptions propagate unwrapped.
    /// </summary>
    [Fact]
    public async Task should_propagate_exception_from_create_producer_unwrapped()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        _connectionFactory
            .CreateProducerAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        // when
        var act = async () => await transport.SendAsync(message);

        // then - BUG: Exception is NOT wrapped in PublisherSentFailedException
        // because CreateProducerAsync is called before the try-catch block
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Connection failed");
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
        try
        {
            await transport.SendAsync(message);
        }
        catch (InvalidOperationException)
        {
            // Expected since CreateProducerAsync throws outside try-catch
        }

        // then
        await _connectionFactory.Received(1).CreateProducerAsync("orders.created");
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
