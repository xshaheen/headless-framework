// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.RabbitMq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

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

    protected override async ValueTask DisposeAsyncCore()
    {
        await _channel.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, When
        await using var transport = new RabbitMqTransport(_logger, _pool);

        // then
        transport.BrokerAddress.Name.Should().Be("RabbitMQ");
        transport.BrokerAddress.Endpoint.Should().Be("localhost:5672");
    }

    [Fact]
    public async Task should_publish_message_successfully()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await _channel
            .Received(1)
            .BasicPublishAsync(
                "test.exchange",
                "TestMessage",
                false,
                Arg.Is<BasicProperties>(p => p.MessageId == "msg-123" && p.DeliveryMode == DeliveryModes.Persistent),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            );
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_return_channel_to_pool_after_publish()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        // when
        await transport.SendAsync(message);

        // then
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_return_failed_result_on_exception()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        _channel
            .When(x =>
                _ = x.BasicPublishAsync(
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<bool>(),
                        Arg.Any<BasicProperties>(),
                        Arg.Any<ReadOnlyMemory<byte>>(),
                        Arg.Any<CancellationToken>()
                    )
                    .AsTask()
            )
            .Do(_ => throw new InvalidOperationException("Publish failed"));

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_dispose_channel_when_already_closed_exception()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        _channel.IsOpen.Returns(true);
        _channel
            .When(x =>
                _ = x.BasicPublishAsync(
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<bool>(),
                        Arg.Any<BasicProperties>(),
                        Arg.Any<ReadOnlyMemory<byte>>(),
                        Arg.Any<CancellationToken>()
                    )
                    .AsTask()
            )
            .Do(_ =>
                throw new AlreadyClosedException(
                    new ShutdownEventArgs(ShutdownInitiator.Library, 0, "Connection closed")
                )
            );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        await _channel.Received(1).DisposeAsync();
        _pool.Received(1).Return(_channel);
    }

    [Fact]
    public async Task should_include_headers_in_published_message()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
                { "CustomHeader", "CustomValue" },
            },
            body: "test-body"u8.ToArray()
        );

        // when
        await transport.SendAsync(message);

        // then
        await _channel
            .Received(1)
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Is<BasicProperties>(p =>
                    p.Headers != null
                    && p.Headers.ContainsKey("CustomHeader")
                    && p.Headers["CustomHeader"]!.ToString() == "CustomValue"
                ),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_reject_invalid_topic_name()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "invalid topic name" },
            },
            body: "test-body"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
    }

    [Fact]
    public async Task should_reject_topic_name_exceeding_max_length()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var tooLongName = new string('a', 256);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, tooLongName },
            },
            body: "test-body"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
    }
}
