// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Tests;

public sealed class RabbitMqTransportTests : TestBase
{
    private readonly ILogger<RabbitMqTransport> _logger;
    private readonly IConnectionChannelPool _pool;
    private readonly IChannel _channel;

    public RabbitMqTransportTests()
    {
        _logger = NullLogger<RabbitMqTransport>.Instance;
        _pool = Substitute.For<IConnectionChannelPool>();
        _channel = Substitute.For<IChannel>();

        _pool.Exchange.Returns("test.exchange");
        _pool.HostAddress.Returns("localhost:5672");
        _pool.Rent().Returns(_channel);
    }

    [Fact]
    public void should_have_correct_broker_address()
    {
        // Given, When
        var transport = new RabbitMqTransport(_logger, _pool);

        // Then
        transport.BrokerAddress.Name.Should().Be("RabbitMQ");
        transport.BrokerAddress.Endpoint.Should().Be("localhost:5672");
    }

    [Fact]
    public async Task should_publish_message_successfully()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        // When
        var result = await transport.SendAsync(message);

        // Then
        result.Succeeded.Should().BeTrue();
        await _channel
            .Received(1)
            .BasicPublishAsync(
                "test.exchange",
                "TestMessage",
                false,
                Arg.Is<BasicProperties>(p => p.MessageId == "msg-123" && p.DeliveryMode == DeliveryModes.Persistent),
                Arg.Any<ReadOnlyMemory<byte>>()
            );
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_return_channel_to_pool_after_publish()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        // When
        await transport.SendAsync(message);

        // Then
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_return_failed_result_on_exception()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        _channel
            .When(x =>
                x.BasicPublishAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<IReadOnlyBasicProperties>(),
                    Arg.Any<ReadOnlyMemory<byte>>()
                )
            )
            .Do(_ => throw new InvalidOperationException("Publish failed"));

        // When
        var result = await transport.SendAsync(message);

        // Then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        result.Errors.Should().NotBeEmpty();
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_dispose_channel_when_already_closed_exception()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        _channel.IsOpen.Returns(true);
        _channel
            .When(x =>
                x.BasicPublishAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<IReadOnlyBasicProperties>(),
                    Arg.Any<ReadOnlyMemory<byte>>()
                )
            )
            .Do(_ =>
                throw new AlreadyClosedException(
                    new ShutdownEventArgs(ShutdownInitiator.Library, 0, "Connection closed")
                )
            );

        // When
        var result = await transport.SendAsync(message);

        // Then
        result.Succeeded.Should().BeFalse();
        await _channel.Received(1).DisposeAsync();
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_include_headers_in_published_message()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, "TestMessage" },
                { "CustomHeader", "CustomValue" },
            },
            body: "test-body"u8.ToArray()
        );

        // When
        await transport.SendAsync(message);

        // Then
        await _channel
            .Received(1)
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Is<BasicProperties>(p =>
                    p.Headers.ContainsKey("CustomHeader") && p.Headers["CustomHeader"]!.ToString() == "CustomValue"
                ),
                Arg.Any<ReadOnlyMemory<byte>>()
            );
    }

    [Fact]
    public async Task should_reject_invalid_topic_name()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, "invalid topic name" },
            },
            body: "test-body"u8.ToArray()
        );

        // When
        var result = await transport.SendAsync(message);

        // Then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
    }

    [Fact]
    public async Task should_reject_topic_name_exceeding_max_length()
    {
        // Given
        var transport = new RabbitMqTransport(_logger, _pool);
        var tooLongName = new string('a', 256);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { Headers.MessageId, "msg-123" },
                { Headers.MessageName, tooLongName },
            },
            body: "test-body"u8.ToArray()
        );

        // When
        var result = await transport.SendAsync(message);

        // Then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
    }
}
