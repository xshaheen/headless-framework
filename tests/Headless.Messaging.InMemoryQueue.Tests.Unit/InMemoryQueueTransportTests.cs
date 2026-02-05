// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class InMemoryQueueTransportTests : TestBase
{
    private readonly MemoryQueue _queue;
    private readonly InMemoryQueueTransport _transport;
    private readonly InMemoryConsumerClient _consumerClient;

    public InMemoryQueueTransportTests()
    {
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryQueueTransport>>();

        _queue = new MemoryQueue(queueLogger);
        _transport = new InMemoryQueueTransport(_queue, transportLogger);
        _consumerClient = new InMemoryConsumerClient(_queue, "test-group", 1);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _consumerClient.DisposeAsync();
        await _transport.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public void should_return_in_memory_broker_address()
    {
        // when
        var address = _transport.BrokerAddress;

        // then
        address.Name.Should().Be("InMemory");
        address.Endpoint.Should().Be("localhost");
    }

    [Fact]
    public async Task should_send_message_successfully()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-topic"]);

        TransportMessage? receivedMessage = null;
        _consumerClient.OnMessageCallback = (msg, _) =>
        {
            receivedMessage = msg;
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        var message = _CreateTestMessage("msg-1", "test-topic");

        // when
        var result = await _transport.SendAsync(message);

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        result.Succeeded.Should().BeTrue();
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.GetId().Should().Be("msg-1");
    }

    [Fact]
    public async Task should_return_success_result_on_send()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-topic"]);
        var message = _CreateTestMessage("msg-1", "test-topic");

        // when
        var result = await _transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task should_return_failed_result_when_no_subscriber()
    {
        // given
        var message = _CreateTestMessage("msg-1", "unsubscribed-topic");

        // when
        var result = await _transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
    }

    [Fact]
    public async Task should_support_message_headers()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-topic"]);

        TransportMessage? receivedMessage = null;
        _consumerClient.OnMessageCallback = (msg, _) =>
        {
            receivedMessage = msg;
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "test-topic",
            [Headers.CorrelationId] = "corr-123",
            ["custom-header"] = "custom-value",
        };

        var message = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);

        // when
        await _transport.SendAsync(message);

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.GetCorrelationId().Should().Be("corr-123");
        receivedMessage.Value.Headers["custom-header"].Should().Be("custom-value");
    }

    [Fact]
    public async Task should_send_message_body()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-topic"]);

        TransportMessage? receivedMessage = null;
        _consumerClient.OnMessageCallback = (msg, _) =>
        {
            receivedMessage = msg;
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        var bodyContent = "{\"key\":\"value\"}"u8.ToArray();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "test-topic",
        };
        var message = new TransportMessage(headers, bodyContent);

        // when
        await _transport.SendAsync(message);

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.Body.ToArray().Should().Equal(bodyContent);
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryQueueTransport>>();
        var queue = new MemoryQueue(queueLogger);
        await using var transport = new InMemoryQueueTransport(queue, transportLogger);

        // when
        var action = async () => await transport.DisposeAsync();

        // then
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_be_thread_safe_for_concurrent_sends()
    {
        // given
        await _consumerClient.SubscribeAsync(["concurrent-topic"]);

        var receivedMessages = new List<TransportMessage>();
        var lockObj = new Lock();
        const int messageCount = 50;
        var tcs = new TaskCompletionSource();
        var count = 0;

        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(msg);
                count++;
                if (count >= messageCount)
                {
                    tcs.TrySetResult();
                }
            }

            await _consumerClient.CommitAsync(sender);
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(30), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - send messages concurrently via transport
        var sendTasks = Enumerable
            .Range(0, messageCount)
            .Select(i => _transport.SendAsync(_CreateTestMessage($"msg-{i}", "concurrent-topic")));

        var results = await Task.WhenAll(sendTasks);

        _ = await Task.WhenAny(tcs.Task, Task.Delay(30000, AbortToken));
        await cts.CancelAsync();

        // then
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
        receivedMessages.Should().HaveCount(messageCount);
    }

    private static TransportMessage _CreateTestMessage(string id, string topic)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = id,
            [Headers.MessageName] = topic,
        };

        return new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);
    }
}
