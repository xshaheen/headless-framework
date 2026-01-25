// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Messages;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Headers = Headless.Messaging.Messages.Headers;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class RabbitMqBasicConsumerTests : TestBase
{
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();
    private readonly List<LogMessageEventArgs> _loggedEvents = [];

    protected override async ValueTask DisposeAsyncCore()
    {
        await _channel.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_log_exception_when_consume_fails_with_concurrent_processing()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";
        var exceptionThrown = false;
        var consumeCallCount = 0;

        Task msgCallback(TransportMessage transportMessage, object? o)
        {
            consumeCallCount++;
            exceptionThrown = true;

            throw new InvalidOperationException("Simulated consumption error");
        }

        _channel.IsOpen.Returns(true);

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            msgCallback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        const ulong deliveryTag = 123ul;
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
        await Task.Delay(100, AbortToken);

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

        static Task callback(TransportMessage transportMessage, object? o) =>
            throw new InvalidOperationException("Simulated consumption error");

        _channel.IsOpen.Returns(true);

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            callback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        const ulong deliveryTag = 456ul;
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
        await Task.Delay(100, AbortToken);

        // then
        await _channel.Received(1).BasicNackAsync(deliveryTag, false, requeue: true, CancellationToken.None);
    }

    [Fact]
    public async Task should_not_nack_when_channel_closed_after_consume_fails()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";

        static Task callback(TransportMessage transportMessage, object? o) =>
            throw new InvalidOperationException("Simulated consumption error");

        _channel.IsOpen.Returns(false);

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            callback,
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        const ulong deliveryTag = 789ul;
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
        await Task.Delay(100, AbortToken);

        // then
        await _channel
            .DidNotReceive()
            .BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        _loggedEvents.Should().ContainSingle();
        _loggedEvents[0].LogType.Should().Be(MqLogType.ConsumeError);
    }

    [Fact]
    public async Task should_not_throw_when_consume_succeeds_with_concurrent_processing()
    {
        // given
        const byte concurrent = 2;
        const string groupName = "test-group";
        var consumeCallCount = 0;

        Task callback(TransportMessage transportMessage, object? o)
        {
            consumeCallCount++;

            return Task.CompletedTask;
        }

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            callback,
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
        await Task.Delay(100, AbortToken);

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

        Task callback(TransportMessage transportMessage, object? o)
        {
            consumeCallCount++;

            return Task.CompletedTask;
        }

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            callback,
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

        static Task callback(TransportMessage transportMessage, object? o) =>
            throw new InvalidOperationException("Simulated consumption error");

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            groupName,
            callback,
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
        // given
        TransportMessage? receivedMessage = null;
        object? receivedSender = null;

        using var consumer = new RabbitMqBasicConsumer(
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

        // when
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

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage.Value.Headers.Should().ContainKey(Headers.Group);
        receivedMessage.Value.Headers[Headers.Group].Should().Be("test-group");
        receivedSender.Should().Be(42UL);
    }

    [Fact]
    public async Task should_convert_byte_array_headers_to_strings()
    {
        // given
        TransportMessage? receivedMessage = null;

        using var consumer = new RabbitMqBasicConsumer(
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

        // when
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

        // then
        receivedMessage!.Value.Headers["ByteHeader"].Should().Be("TestValue");
    }

    [Fact]
    public async Task should_handle_non_byte_array_headers()
    {
        // given
        TransportMessage? receivedMessage = null;

        using var consumer = new RabbitMqBasicConsumer(
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

        // when
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

        // then
        receivedMessage!.Value.Headers["IntHeader"].Should().Be("123");
    }

    [Fact]
    public async Task should_apply_custom_headers_builder()
    {
        // given
        TransportMessage? receivedMessage = null;

        static List<KeyValuePair<string, string>> customBuilder(
            BasicDeliverEventArgs basicDeliverEventArgs,
            IServiceProvider serviceProvider
        ) => [new("CustomHeader", "CustomValue")];

        using var consumer = new RabbitMqBasicConsumer(
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

        // when
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

        // then
        receivedMessage!.Value.Headers["CustomHeader"].Should().Be("CustomValue");
    }

    [Fact]
    public async Task should_ack_message_when_channel_open()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicAck(42UL);

        // then
        await _channel.Received(1).BasicAckAsync(42UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_ack_when_channel_closed()
    {
        // given
        _channel.IsOpen.Returns(false);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicAck(42UL);

        // then
        await _channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_message_when_channel_open()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicReject(42UL);

        // then
        await _channel.Received(1).BasicRejectAsync(42UL, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_reject_when_channel_closed()
    {
        // given
        _channel.IsOpen.Returns(false);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicReject(42UL);

        // then
        await _channel
            .DidNotReceive()
            .BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_log_consumer_registered_event()
    {
        // given
        LogMessageEventArgs? loggedEvent = null;
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (_, _) => Task.CompletedTask,
            args => loggedEvent = args,
            null,
            _serviceProvider
        );

        // when
        await consumer.HandleBasicConsumeOkAsync("consumer-tag-123", CancellationToken.None);

        // then
        loggedEvent.Should().NotBeNull();
        loggedEvent!.LogType.Should().Be(MqLogType.ConsumerRegistered);
        loggedEvent.Reason.Should().Be("consumer-tag-123");
    }

    [Fact]
    public async Task should_log_consumer_unregistered_event()
    {
        // given
        LogMessageEventArgs? loggedEvent = null;
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (_, _) => Task.CompletedTask,
            args => loggedEvent = args,
            null,
            _serviceProvider
        );

        // when
        await consumer.HandleBasicCancelOkAsync("consumer-tag-456", CancellationToken.None);

        // then
        loggedEvent.Should().NotBeNull();
        loggedEvent!.LogType.Should().Be(MqLogType.ConsumerUnregistered);
        loggedEvent.Reason.Should().Be("consumer-tag-456");
    }

    [Fact]
    public async Task should_log_channel_shutdown_event()
    {
        // given
        LogMessageEventArgs? loggedEvent = null;
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            "test-group",
            (_, _) => Task.CompletedTask,
            args => loggedEvent = args,
            null,
            _serviceProvider
        );

        // when
        await consumer.HandleChannelShutdownAsync(
            _channel,
            new ShutdownEventArgs(ShutdownInitiator.Library, 320, "Connection closed")
        );

        // then
        loggedEvent.Should().NotBeNull();
        loggedEvent!.LogType.Should().Be(MqLogType.ConsumerShutdown);
        loggedEvent.Reason.Should().Be("Connection closed");
    }

    [Fact]
    public async Task should_handle_null_headers_in_properties()
    {
        // given
        TransportMessage? receivedMessage = null;

        using var consumer = new RabbitMqBasicConsumer(
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
        properties.Headers.Returns((IDictionary<string, object?>?)null);

        // when
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

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.Headers.Should().ContainKey(Headers.Group);
        receivedMessage.Value.Headers[Headers.Group].Should().Be("test-group");
    }

    [Fact]
    public void should_dispose_semaphore_on_dispose()
    {
        // given
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        consumer.Dispose();

        // then - should not throw on second dispose
        var act = () => consumer.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_handle_null_header_value()
    {
        // given
        TransportMessage? receivedMessage = null;

        using var consumer = new RabbitMqBasicConsumer(
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
            new Dictionary<string, object?>(StringComparer.Ordinal) { { "NullHeader", null } }
        );

        // when
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

        // then
        receivedMessage!.Value.Headers["NullHeader"].Should().BeNull();
    }

    [Fact]
    public async Task should_release_semaphore_on_successful_ack()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            2,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when - multiple acks should not block if semaphore is released properly
        await consumer.BasicAck(1UL);
        await consumer.BasicAck(2UL);
        await consumer.BasicAck(3UL);

        // then - should complete without deadlock
        await _channel.Received(3).BasicAckAsync(Arg.Any<ulong>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_release_semaphore_on_successful_reject()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            2,
            "test-group",
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when - multiple rejects should not block if semaphore is released properly
        await consumer.BasicReject(1UL);
        await consumer.BasicReject(2UL);
        await consumer.BasicReject(3UL);

        // then - should complete without deadlock
        await _channel.Received(3).BasicRejectAsync(Arg.Any<ulong>(), true, Arg.Any<CancellationToken>());
    }
}
