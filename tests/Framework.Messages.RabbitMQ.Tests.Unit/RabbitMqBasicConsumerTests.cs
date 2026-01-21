// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MessagingHeaders = Framework.Messages.Messages.Headers;

namespace Tests;

public sealed class RabbitMqBasicConsumerTests
{
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();
    private readonly List<LogMessageEventArgs> _loggedEvents = [];

    [Fact]
    public async Task should_log_exception_when_consume_fails_with_concurrent_processing()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";
        var exceptionThrown = false;
        var consumeCallCount = 0;

        Func<TransportMessage, object?, Task> msgCallback = (_, _) =>
        {
            consumeCallCount++;
            exceptionThrown = true;
            throw new InvalidOperationException("Simulated consumption error");
        };

        _channel.IsOpen.Returns(true);

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        var deliveryTag = 123ul;
        var body = "test message"u8.ToArray();

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            deliveryTag,
            false,
            "exchange",
            "routingKey",
            Substitute.For<IReadOnlyBasicProperties>(),
            body,
            CancellationToken.None
        );

        // Allow async task to complete
        await Task.Delay(100);

        // then
        exceptionThrown.Should().BeTrue();
        consumeCallCount.Should().Be(1);
        _loggedEvents.Should().HaveCount(1);
        _loggedEvents[0].LogType.Should().Be(MqLogType.ConsumeError);
        _loggedEvents[0].Reason.Should().Contain("Error consuming message");
        _loggedEvents[0].Reason.Should().Contain("Simulated consumption error");
    }

    [Fact]
    public async Task should_nack_message_when_consume_fails_with_concurrent_processing()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";

        Func<TransportMessage, object?, Task> msgCallback = (_, _) =>
            throw new InvalidOperationException("Simulated consumption error");

        _channel.IsOpen.Returns(true);

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        var deliveryTag = 456ul;
        var body = "test message"u8.ToArray();

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            deliveryTag,
            false,
            "exchange",
            "routingKey",
            Substitute.For<IReadOnlyBasicProperties>(),
            body,
            CancellationToken.None
        );

        // Allow async task to complete
        await Task.Delay(100);

        // then
        await _channel.Received(1).BasicNackAsync(deliveryTag, false, requeue: true);
    }

    [Fact]
    public async Task should_not_nack_when_channel_closed_after_consume_fails()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";

        Func<TransportMessage, object?, Task> msgCallback = (_, _) =>
            throw new InvalidOperationException("Simulated consumption error");

        _channel.IsOpen.Returns(false);

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        var deliveryTag = 789ul;
        var body = "test message"u8.ToArray();

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            deliveryTag,
            false,
            "exchange",
            "routingKey",
            Substitute.For<IReadOnlyBasicProperties>(),
            body,
            CancellationToken.None
        );

        // Allow async task to complete
        await Task.Delay(100);

        // then
        await _channel.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>());
        _loggedEvents.Should().HaveCount(1);
        _loggedEvents[0].LogType.Should().Be(MqLogType.ConsumeError);
    }

    [Fact]
    public async Task should_not_throw_when_consume_succeeds_with_concurrent_processing()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";
        var consumeCallCount = 0;

        Func<TransportMessage, object?, Task> msgCallback = (_, _) =>
        {
            consumeCallCount++;
            return Task.CompletedTask;
        };

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        var body = "test message"u8.ToArray();

        // when
        var act = async () =>
            await consumer.HandleBasicDeliverAsync(
                "consumerTag",
                123ul,
                false,
                "exchange",
                "routingKey",
                Substitute.For<IReadOnlyBasicProperties>(),
                body,
                CancellationToken.None
            );

        // Allow async task to complete
        await Task.Delay(100);

        // then
        await act.Should().NotThrowAsync();
        consumeCallCount.Should().Be(1);
        _loggedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task should_process_synchronously_when_concurrent_is_zero()
    {
        // given
        const byte concurrent = 0;
        const string groupName = "test-group";
        var consumeCallCount = 0;

        Func<TransportMessage, object?, Task> msgCallback = (_, _) =>
        {
            consumeCallCount++;
            return Task.CompletedTask;
        };

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        var body = "test message"u8.ToArray();

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            123ul,
            false,
            "exchange",
            "routingKey",
            Substitute.For<IReadOnlyBasicProperties>(),
            body,
            CancellationToken.None
        );

        // then
        consumeCallCount.Should().Be(1);
        _loggedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task should_propagate_exception_when_consume_fails_without_concurrent_processing()
    {
        // given
        const byte concurrent = 0;
        const string groupName = "test-group";

        Func<TransportMessage, object?, Task> msgCallback = (_, _) =>
            throw new InvalidOperationException("Simulated consumption error");

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        var body = "test message"u8.ToArray();

        // when
        var act = async () =>
            await consumer.HandleBasicDeliverAsync(
                "consumerTag",
                123ul,
                false,
                "exchange",
                "routingKey",
                Substitute.For<IReadOnlyBasicProperties>(),
                body,
                CancellationToken.None
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated consumption error");
    }

    [Fact]
    public async Task should_invoke_callback_on_message_delivery()
    {
        // Given
        TransportMessage? receivedMessage = null;
        object? receivedSender = null;

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (msg, sender) =>
            {
                receivedMessage = msg;
                receivedSender = sender;
                return Task.CompletedTask;
            },
            _ => { },
            null,
            _serviceProvider
        );

        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.Headers.Returns(
            new Dictionary<string, object?>(StringComparer.Ordinal) { { "TestHeader", "TestValue"u8.ToArray() } }
        );

        // When
        await consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            42UL,
            false,
            "test-exchange",
            "test-routing-key",
            properties,
            "test-body"u8.ToArray(),
            CancellationToken.None
        );

        // Then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Headers.Should().ContainKey(Headers.Group);
        receivedMessage.Headers[Headers.Group].Should().Be("test-group");
        receivedSender.Should().Be(42UL);
    }

    [Fact]
    public async Task should_convert_byte_array_headers_to_strings()
    {
        // Given
        TransportMessage? receivedMessage = null;

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            null,
            _serviceProvider
        );

        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.Headers.Returns(
            new Dictionary<string, object?>(StringComparer.Ordinal) { { "ByteHeader", "TestValue"u8.ToArray() } }
        );

        // When
        await consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            1UL,
            false,
            "test-exchange",
            "test-routing-key",
            properties,
            "test-body"u8.ToArray(),
            CancellationToken.None
        );

        // Then
        receivedMessage!.Headers["ByteHeader"].Should().Be("TestValue");
    }

    [Fact]
    public async Task should_handle_non_byte_array_headers()
    {
        // Given
        TransportMessage? receivedMessage = null;

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            null,
            _serviceProvider
        );

        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.Headers.Returns(new Dictionary<string, object?>(StringComparer.Ordinal) { { "IntHeader", 123 } });

        // When
        await consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            1UL,
            false,
            "test-exchange",
            "test-routing-key",
            properties,
            "test-body"u8.ToArray(),
            CancellationToken.None
        );

        // Then
        receivedMessage!.Headers["IntHeader"].Should().Be("123");
    }

    [Fact]
    public async Task should_apply_custom_headers_builder()
    {
        // Given
        TransportMessage? receivedMessage = null;

        Func<BasicDeliverEventArgs, IServiceProvider, List<KeyValuePair<string, string>>> customBuilder = (_, _) =>
            [new KeyValuePair<string, string>("CustomHeader", "CustomValue")];

        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            customBuilder,
            _serviceProvider
        );

        var properties = Substitute.For<IReadOnlyBasicProperties>();

        // When
        await consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            1UL,
            false,
            "test-exchange",
            "test-routing-key",
            properties,
            "test-body"u8.ToArray(),
            CancellationToken.None
        );

        // Then
        receivedMessage!.Headers["CustomHeader"].Should().Be("CustomValue");
    }

    [Fact]
    public async Task should_ack_message_when_channel_open()
    {
        // Given
        _channel.IsOpen.Returns(true);
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // When
        await consumer.BasicAck(42UL);

        // Then
        await _channel.Received(1).BasicAckAsync(42UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_ack_when_channel_closed()
    {
        // Given
        _channel.IsOpen.Returns(false);
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // When
        await consumer.BasicAck(42UL);

        // Then
        await _channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_message_when_channel_open()
    {
        // Given
        _channel.IsOpen.Returns(true);
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // When
        await consumer.BasicReject(42UL);

        // Then
        await _channel.Received(1).BasicRejectAsync(42UL, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_reject_when_channel_closed()
    {
        // Given
        _channel.IsOpen.Returns(false);
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // When
        await consumer.BasicReject(42UL);

        // Then
        await _channel
            .DidNotReceive()
            .BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_log_consumer_registered_event()
    {
        // Given
        LogMessageEventArgs? loggedEvent = null;
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (_, _) => Task.CompletedTask,
            args => loggedEvent = args,
            null,
            _serviceProvider
        );

        // When
        await consumer.HandleBasicConsumeOkAsync("consumer-tag-123", CancellationToken.None);

        // Then
        loggedEvent.Should().NotBeNull();
        loggedEvent!.LogType.Should().Be(MqLogType.ConsumerRegistered);
        loggedEvent.Reason.Should().Be("consumer-tag-123");
    }

    [Fact]
    public async Task should_log_consumer_unregistered_event()
    {
        // Given
        LogMessageEventArgs? loggedEvent = null;
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (_, _) => Task.CompletedTask,
            args => loggedEvent = args,
            null,
            _serviceProvider
        );

        // When
        await consumer.HandleBasicCancelOkAsync("consumer-tag-456", CancellationToken.None);

        // Then
        loggedEvent.Should().NotBeNull();
        loggedEvent!.LogType.Should().Be(MqLogType.ConsumerUnregistered);
        loggedEvent.Reason.Should().Be("consumer-tag-456");
    }

    [Fact]
    public async Task should_log_channel_shutdown_event()
    {
        // Given
        LogMessageEventArgs? loggedEvent = null;
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (_, _) => Task.CompletedTask,
            args => loggedEvent = args,
            null,
            _serviceProvider
        );

        // When
        await consumer.HandleChannelShutdownAsync(
            _channel,
            new ShutdownEventArgs(ShutdownInitiator.Library, 320, "Connection closed")
        );

        // Then
        loggedEvent.Should().NotBeNull();
        loggedEvent!.LogType.Should().Be(MqLogType.ConsumerShutdown);
        loggedEvent.Reason.Should().Be("Connection closed");
    }
}
