// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Headers = Headless.Messaging.Headers;

namespace Tests;

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
        var consumeFailed = _CreateSignal();
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
            msgCallback,
            args => _RecordLog(args, consumeFailed),
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
            _CreateProperties(),
            body,
            CancellationToken.None
        );

        await _WaitForSignalAsync(consumeFailed.Task);

        // then
        exceptionThrown.Should().BeTrue();
        consumeCallCount.Should().Be(1);
        _loggedEvents.Should().ContainSingle();
        _loggedEvents[0].LogType.Should().Be(MqLogType.ConsumeError);
        _loggedEvents[0].Reason.Should().Contain("Error consuming message");
        _loggedEvents[0].Reason.Should().Contain("Simulated consumption error");
    }

    [Fact]
    public async Task should_not_transport_nack_when_consume_fails_with_concurrent_processing()
    {
        // given
        const byte concurrent = 2;
        var consumeFailed = _CreateSignal();

        static Task callback(TransportMessage transportMessage, object? o) =>
            throw new InvalidOperationException("Simulated consumption error");

        _channel.IsOpen.Returns(true);

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            callback,
            args => _RecordLog(args, consumeFailed),
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
            _CreateProperties(),
            body,
            CancellationToken.None
        );

        await _WaitForSignalAsync(consumeFailed.Task);

        // then - reject is owned by the framework callback wrapper, not the transport callback shell
        await _channel
            .DidNotReceive()
            .BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        _loggedEvents.Should().ContainSingle();
        _loggedEvents[0].LogType.Should().Be(MqLogType.ConsumeError);
        _loggedEvents[0].Reason.Should().Contain("Simulated consumption error");
    }

    [Fact]
    public async Task should_not_nack_when_channel_closed_after_consume_fails()
    {
        // given
        const byte concurrent = 2;
        var consumeFailed = _CreateSignal();

        static Task callback(TransportMessage transportMessage, object? o) =>
            throw new InvalidOperationException("Simulated consumption error");

        _channel.IsOpen.Returns(false);

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            callback,
            args => _RecordLog(args, consumeFailed),
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
            _CreateProperties(),
            body,
            CancellationToken.None
        );

        await _WaitForSignalAsync(consumeFailed.Task);

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
        var callbackCompleted = _CreateSignal();
        var consumeCallCount = 0;

        Task callback(TransportMessage transportMessage, object? o)
        {
            consumeCallCount++;
            callbackCompleted.TrySetResult();

            return Task.CompletedTask;
        }

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
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
            _CreateProperties(),
            body,
            CancellationToken.None
        );

        await _WaitForSignalAsync(callbackCompleted.Task);

        // then
        consumeCallCount.Should().Be(1);
        _loggedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task should_process_synchronously_when_concurrent_is_zero()
    {
        // given
        const byte concurrent = 0;
        var consumeCallCount = 0;

        Task callback(TransportMessage transportMessage, object? o)
        {
            consumeCallCount++;

            return Task.CompletedTask;
        }

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
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
            _CreateProperties(),
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

        static Task callback(TransportMessage transportMessage, object? o) =>
            throw new InvalidOperationException("Simulated consumption error");

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
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
                _CreateProperties(),
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

        var properties = _CreateProperties(
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
        receivedMessage.Value.Headers["TestHeader"].Should().Be("TestValue");
        // The consumer identity is stamped by the messaging core once the delivery is routed, never by the transport.
        receivedMessage.Value.Headers.Should().NotContainKey(Headers.ConsumerIdentity);
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
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            null,
            _serviceProvider
        );

        var properties = _CreateProperties(
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
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            null,
            _serviceProvider
        );

        var properties = _CreateProperties(
            new Dictionary<string, object?>(StringComparer.Ordinal) { { "IntHeader", 123 } }
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
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            customBuilder,
            _serviceProvider
        );

        var properties = _CreateProperties();

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
    public async Task should_stamp_routing_key_as_transport_address_when_wire_and_builder_spoof_it()
    {
        // given
        TransportMessage? receivedMessage = null;

        static List<KeyValuePair<string, string>> spoofingBuilder(BasicDeliverEventArgs _, IServiceProvider __) =>
            [new(Headers.TransportAddress, "builder-spoofed")];

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            spoofingBuilder,
            _serviceProvider
        );

        var properties = _CreateProperties(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Headers.TransportAddress] = "wire-spoofed"u8.ToArray(),
            }
        );

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            1UL,
            false,
            "test-exchange",
            "orders.created",
            properties,
            "test-body"u8.ToArray(),
            AbortToken
        );

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.Headers[Headers.TransportAddress].Should().Be("orders.created");
    }

    [Fact]
    public async Task should_requeue_without_terminal_reject_when_custom_headers_builder_throws()
    {
        // given
        _channel.IsOpen.Returns(true);
        static List<KeyValuePair<string, string>> throwingBuilder(BasicDeliverEventArgs _, IServiceProvider __) =>
            throw new InvalidOperationException("bad header builder");

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            (_, _) => Task.CompletedTask,
            args => _loggedEvents.Add(args),
            throwingBuilder,
            _serviceProvider
        );

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            42ul,
            false,
            "test-exchange",
            "test-routing-key",
            _CreateProperties(),
            "test-body"u8.ToArray(),
            AbortToken
        );

        // then
        await _channel.Received(1).BasicRejectAsync(42ul, true, Arg.Any<CancellationToken>());
        await _channel.DidNotReceive().BasicRejectAsync(42ul, false, Arg.Any<CancellationToken>());
        _loggedEvents
            .Should()
            .ContainSingle(e => e.Reason != null && e.Reason.Contains("rejected for retry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_ack_message_when_channel_open()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicAck(42UL, AbortToken);

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
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicAck(42UL, AbortToken);

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
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicReject(42UL, AbortToken);

        // then
        await _channel.Received(1).BasicRejectAsync(42UL, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_without_requeue_when_dead_lettering()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicDeadLetter(42UL, AbortToken);

        // then — the queue's dead-letter exchange receives it, or the broker discards it when there is none
        await _channel.Received(1).BasicRejectAsync(42UL, false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceive().BasicRejectAsync(42UL, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_dead_letter_when_channel_closed()
    {
        // given
        _channel.IsOpen.Returns(false);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicDeadLetter(42UL, AbortToken);

        // then
        await _channel
            .DidNotReceive()
            .BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_reject_when_channel_closed()
    {
        // given
        _channel.IsOpen.Returns(false);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        await consumer.BasicReject(42UL, AbortToken);

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
    public async Task should_terminally_reject_null_headers_in_properties()
    {
        // given
        _channel.IsOpen.Returns(true);
        TransportMessage? receivedMessage = null;

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
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
        receivedMessage.Should().BeNull();
        await _channel.Received(1).BasicRejectAsync(1UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_dispose_semaphore_on_dispose()
    {
        // given
        var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when
        consumer.Dispose();

        // then - should not throw on second dispose
        var act = consumer.Dispose;
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
            (msg, _) =>
            {
                receivedMessage = msg;
                return Task.CompletedTask;
            },
            _ => { },
            null,
            _serviceProvider
        );

        var properties = _CreateProperties(
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
    public async Task should_terminally_reject_when_required_header_is_missing_with_concurrent_processing()
    {
        // given
        _channel.IsOpen.Returns(true);
        var consumeFailed = _CreateSignal();
        var callbackInvoked = false;

        static List<KeyValuePair<string, string>> malformedBuilder(BasicDeliverEventArgs _, IServiceProvider __) =>
            [new(Headers.MessageId, null!)];

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            2,
            (_, _) =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            args => _RecordLog(args, consumeFailed),
            malformedBuilder,
            _serviceProvider
        );

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            999ul,
            false,
            "exchange",
            "routingKey",
            _CreateProperties(),
            "test"u8.ToArray(),
            CancellationToken.None
        );

        await _WaitForSignalAsync(consumeFailed.Task);

        // then — malformed transport input is terminal and does not enter a requeue storm
        callbackInvoked.Should().BeFalse();
        await _channel.Received(1).BasicRejectAsync(999ul, false, Arg.Any<CancellationToken>());
        _loggedEvents.Should().ContainSingle(e => e.LogType == MqLogType.ConsumeError);
        _loggedEvents[0].Reason.Should().Contain("terminally rejected").And.NotContain("Messaging header");
    }

    private TaskCompletionSource _CreateSignal()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static IReadOnlyBasicProperties _CreateProperties(IDictionary<string, object?>? headers = null)
    {
        headers ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        headers[Headers.MessageId] = "msg-1"u8.ToArray();
        headers[Headers.MessageName] = "TestEvent"u8.ToArray();

        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.Headers.Returns(headers);
        return properties;
    }

    private void _RecordLog(LogMessageEventArgs args, TaskCompletionSource signal)
    {
        _loggedEvents.Add(args);
        signal.TrySetResult();
    }

    private async Task _WaitForSignalAsync(Task signal)
    {
        await signal.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_terminally_reject_when_required_header_is_missing_without_concurrent_processing()
    {
        // given
        _channel.IsOpen.Returns(true);
        var callbackInvoked = false;

        static List<KeyValuePair<string, string>> malformedBuilder(BasicDeliverEventArgs _, IServiceProvider __) =>
            [new(Headers.MessageId, null!)];

        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            0,
            (_, _) =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            args => _loggedEvents.Add(args),
            malformedBuilder,
            _serviceProvider
        );

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            888ul,
            false,
            "exchange",
            "routingKey",
            _CreateProperties(),
            "test"u8.ToArray(),
            CancellationToken.None
        );

        // then — malformed transport input is terminal and does not enter a requeue storm
        callbackInvoked.Should().BeFalse();
        await _channel.Received(1).BasicRejectAsync(888ul, false, Arg.Any<CancellationToken>());
        _loggedEvents.Should().ContainSingle(e => e.LogType == MqLogType.ConsumeError);
    }

    [Fact]
    public async Task should_release_semaphore_on_successful_ack()
    {
        // given
        _channel.IsOpen.Returns(true);
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            2,
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when - multiple acks should not block if semaphore is released properly
        await consumer.BasicAck(1UL, AbortToken);
        await consumer.BasicAck(2UL, AbortToken);
        await consumer.BasicAck(3UL, AbortToken);

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
            (_, _) => Task.CompletedTask,
            _ => { },
            null,
            _serviceProvider
        );

        // when - multiple rejects should not block if semaphore is released properly
        await consumer.BasicReject(1UL, AbortToken);
        await consumer.BasicReject(2UL, AbortToken);
        await consumer.BasicReject(3UL, AbortToken);

        // then - should complete without deadlock
        await _channel.Received(3).BasicRejectAsync(Arg.Any<ulong>(), true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task should_wait_for_running_handler_when_draining(byte concurrent)
    {
        // given
        var handlerStarted = _CreateSignal();
        var releaseHandler = _CreateSignal();
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            async (_, _) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task;
            },
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        // The sequential path runs the handler on the delivering call, so that call stays pending until release.
        var delivery = consumer.HandleBasicDeliverAsync(
            "consumerTag",
            1ul,
            false,
            "exchange",
            "routingKey",
            _CreateProperties(),
            "body"u8.ToArray(),
            CancellationToken.None
        );
        await _WaitForSignalAsync(handlerStarted.Task);

        // when
        var drain = consumer.DrainAsync(TimeSpan.FromSeconds(5), TimeProvider.System);

        // then
        drain.IsCompleted.Should().BeFalse();
        consumer.InFlightCount.Should().Be(1);

        releaseHandler.TrySetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await delivery.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        consumer.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task should_time_out_draining_when_handler_outlives_the_budget()
    {
        // given
        var handlerStarted = _CreateSignal();
        var releaseHandler = _CreateSignal();
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            1,
            async (_, _) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task;
            },
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );

        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            1ul,
            false,
            "exchange",
            "routingKey",
            _CreateProperties(),
            "body"u8.ToArray(),
            CancellationToken.None
        );
        await _WaitForSignalAsync(handlerStarted.Task);

        // when
        var drain = async () => await consumer.DrainAsync(TimeSpan.FromMilliseconds(50), TimeProvider.System);

        // then
        await drain.Should().ThrowAsync<TimeoutException>();
        releaseHandler.TrySetResult();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task should_not_dispatch_deliveries_after_dispatching_stops(byte concurrent)
    {
        // given
        var callbackInvoked = false;
        using var consumer = new RabbitMqBasicConsumer(
            _channel,
            concurrent,
            (_, _) =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            args => _loggedEvents.Add(args),
            null,
            _serviceProvider
        );
        await consumer.DrainAsync(TimeSpan.FromSeconds(5), TimeProvider.System);

        // when
        await consumer.HandleBasicDeliverAsync(
            "consumerTag",
            7ul,
            false,
            "exchange",
            "routingKey",
            _CreateProperties(),
            "body"u8.ToArray(),
            CancellationToken.None
        );

        // then — left unacknowledged, so the broker requeues it when the channel closes
        callbackInvoked.Should().BeFalse();
        consumer.InFlightCount.Should().Be(0);
        _channel
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Should()
            .NotContain([nameof(IChannel.BasicAckAsync), nameof(IChannel.BasicRejectAsync)]);
    }
}
