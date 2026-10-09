// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using MessagingHeaders = Headless.Messaging.Headers;

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
        _pool.Rent(Arg.Any<CancellationToken>()).Returns(_channel);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _channel.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_return_failed_result_without_sending_when_sending_after_dispose()
    {
        var transport = new RabbitMqTransport(_logger, _pool);
        await transport.DisposeAsync();

        var result = await transport.SendAsync(
            new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [MessagingHeaders.MessageId] = "msg-123",
                    [MessagingHeaders.MessageName] = "orders",
                },
                "payload"u8.ToArray()
            ),
            AbortToken
        );

        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<ObjectDisposedException>();
        await _pool.DidNotReceive().Rent(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, When
        await using var transport = new RabbitMqTransport(_logger, _pool);

        // then
        transport.BrokerAddress.Name.Should().Be("rabbitmq");
        transport.BrokerAddress.Endpoint.Should().Be("localhost:5672");
    }

    [Fact]
    public async Task should_propagate_cancellation_before_renting_channel()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: "test-body"u8.ToArray()
        );

        // when
        var act = async () => await transport.SendAsync(message, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _pool
            .ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(IConnectionChannelPool.Rent));
        _pool.DidNotReceive().Return(Arg.Any<IChannel>());
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
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await _channel
            .Received(1)
            .BasicPublishAsync(
                "test.exchange.bus",
                "bus.TestMessage",
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
        await transport.SendAsync(message, AbortToken);

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
        var result = await transport.SendAsync(message, AbortToken);

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
        var result = await transport.SendAsync(message, AbortToken);

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
        await transport.SendAsync(message, AbortToken);

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
                { MessagingHeaders.MessageName, "invalid message name" },
            },
            body: "test-body"u8.ToArray()
        );

        // when & then - validation fails before try-catch, throws exception
        var act = async () => await transport.SendAsync(message);
        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*Message name must contain only alphanumeric characters*");
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

        // when & then - validation fails before try-catch, throws exception
        var act = async () => await transport.SendAsync(message);
        await act.Should()
            .ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Message name must not exceed 255 characters*");
    }

    [Fact]
    public async Task should_dispose_async_without_exception()
    {
        // given
        var transport = new RabbitMqTransport(_logger, _pool);

        // when & then - dispose should complete without exception
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_on_validation_failure_before_channel_rent()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "" }, // empty message name - validation fails before rent
            },
            body: "test-body"u8.ToArray()
        );

        // when
        var act = () => transport.SendAsync(message);

        // then - validation fails before channel rent, so exception is thrown
        await act.Should().ThrowAsync<ArgumentException>();
        await _pool.DidNotReceive().Rent(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_use_correct_exchange_from_pool()
    {
        // given
        _pool.Exchange.Returns("custom.exchange");
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
        await transport.SendAsync(message, AbortToken);

        // then
        await _channel
            .Received(1)
            .BasicPublishAsync(
                "custom.exchange.bus",
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_include_message_body_in_publish()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool);
        var expectedBody = "test-message-body"u8.ToArray();
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: expectedBody
        );

        // when
        await transport.SendAsync(message, AbortToken);

        // then
        await _channel
            .Received(1)
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(expectedBody)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_exception_details_in_failed_result()
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

        var expectedException = new InvalidOperationException("Publish failed");
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
            .Do(_ => throw expectedException);

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.InnerException.Should().BeSameAs(expectedException);
    }

    [Fact]
    public async Task should_ensure_queue_before_publishing_mandatory_queue_lane_message()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool, MessageLane.Queue);

        // when
        var result = await transport.SendAsync(_CreateMessage("orders"), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        Received.InOrder(() =>
        {
            _ = _pool.EnsureQueueForPublishAsync("orders", AbortToken);
            _ = _pool.Rent(AbortToken);
            _ = _channel.BasicPublishAsync(
                "test.exchange.queue",
                "queue.orders",
                true,
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                AbortToken
            );
        });
    }

    [Fact]
    public async Task should_not_ensure_any_queue_for_bus_lane_message()
    {
        // given
        await using var transport = new RabbitMqTransport(_logger, _pool, MessageLane.Bus);

        // when
        await transport.SendAsync(_CreateMessage("orders"), AbortToken);

        // then
        await _pool.DidNotReceive().EnsureQueueForPublishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_without_publishing_when_queue_cannot_be_ensured()
    {
        // given
        _pool
            .EnsureQueueForPublishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("NOT_FOUND - no queue 'queue.orders'")));
        await using var transport = new RabbitMqTransport(_logger, _pool, MessageLane.Queue);

        // when
        var result = await transport.SendAsync(_CreateMessage("orders"), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        await _pool.DidNotReceive().Rent(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_and_forget_ensured_queue_when_broker_returns_message()
    {
        // given
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
                throw new PublishReturnException(
                    1,
                    "unroutable",
                    "test.exchange.queue",
                    "queue.orders",
                    312,
                    "NO_ROUTE"
                )
            );
        await using var transport = new RabbitMqTransport(_logger, _pool, MessageLane.Queue);

        // when
        var result = await transport.SendAsync(_CreateMessage("orders"), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception!.InnerException.Should().BeOfType<PublishReturnException>();
        _pool.Received(1).ForgetQueueForPublish("orders");
        _pool.Received(1).Return(_channel);
    }

    private static TransportMessage _CreateMessage(string name)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "msg-123",
                [MessagingHeaders.MessageName] = name,
            },
            "payload"u8.ToArray()
        );
    }
}
