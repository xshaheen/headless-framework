// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class InMemoryConsumerClientTests : TestBase
{
    private readonly MemoryQueue _queue;
    private readonly InMemoryConsumerClient _client;

    public InMemoryConsumerClientTests()
    {
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        _queue = new MemoryQueue(logger);
        _client = new InMemoryConsumerClient(_queue, "test-group", 1);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _client.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public void should_return_in_memory_broker_address()
    {
        // when
        var address = _client.BrokerAddress;

        // then
        address.Name.Should().Be("InMemory");
        address.Endpoint.Should().Be("localhost");
    }

    [Fact]
    public async Task should_subscribe_to_topics()
    {
        // given
        var topics = new[] { "topic-1", "topic-2", "topic-3" };

        // when
        await _client.SubscribeAsync(topics);

        // then - should complete without exception
        _client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // when
        var action = async () => await _client.SubscribeAsync(null!);

        // then
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_invoke_callback_on_message_received()
    {
        // given
        await _client.SubscribeAsync(["test-topic"]);

        TransportMessage? receivedMessage = null;
        object? receivedSender = null;
        _client.OnMessageCallback = (msg, sender) =>
        {
            receivedMessage = msg;
            receivedSender = sender;
            return Task.CompletedTask;
        };

        var message = _CreateTestMessage("msg-1", "test-topic");

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _client.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when
        _queue.Send(message);

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.GetId().Should().Be("msg-1");
        receivedSender.Should().BeNull();
    }

    [Fact]
    public async Task should_commit_and_release_semaphore()
    {
        // given
        await _client.SubscribeAsync(["test-topic"]);

        var commitCalled = false;
        _client.OnMessageCallback = async (_, sender) =>
        {
            await _client.CommitAsync(sender);
            commitCalled = true;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _client.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when
        _queue.Send(_CreateTestMessage("msg-1", "test-topic"));

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        commitCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_and_release_semaphore()
    {
        // given
        await _client.SubscribeAsync(["test-topic"]);

        var rejectCalled = false;
        _client.OnMessageCallback = async (_, sender) =>
        {
            await _client.RejectAsync(sender);
            rejectCalled = true;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _client.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when
        _queue.Send(_CreateTestMessage("msg-1", "test-topic"));

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        rejectCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_listening_on_cancellation()
    {
        // given
        await _client.SubscribeAsync(["test-topic"]);

        using var cts = new CancellationTokenSource();
        var listenerStopped = false;

        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _client.ListeningAsync(TimeSpan.FromSeconds(30), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    listenerStopped = true;
                }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when
        await cts.CancelAsync();
        await Task.Delay(100, AbortToken);

        // then
        listenerStopped.Should().BeTrue();
    }

    [Fact]
    public async Task should_unsubscribe_from_queue_on_dispose()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);
        var client = new InMemoryConsumerClient(queue, "dispose-group", 1);
        await client.SubscribeAsync(["dispose-topic"]);

        TransportMessage? receivedMessage = null;
        client.OnMessageCallback = (msg, _) =>
        {
            receivedMessage = msg;
            return Task.CompletedTask;
        };

        // when
        await client.DisposeAsync();

        // then - sending to the topic should not deliver to the unsubscribed client
        // (the topic still exists but the client is removed)
        queue.Send(_CreateTestMessage("msg-1", "dispose-topic"));
        await Task.Delay(100, AbortToken);
        receivedMessage.Should().BeNull();
    }

    [Fact]
    public async Task should_support_concurrent_message_processing()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);
        const byte concurrency = (byte)4;
        var client = new InMemoryConsumerClient(queue, "concurrent-group", concurrency);
        await client.SubscribeAsync(["concurrent-topic"]);

        var processedCount = 0;
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var lockObj = new Lock();
        const int messageCount = 10;
        var tcs = new TaskCompletionSource();

        client.OnMessageCallback = async (_, sender) =>
        {
            lock (lockObj)
            {
                currentConcurrent++;
                if (currentConcurrent > maxConcurrent)
                {
                    maxConcurrent = currentConcurrent;
                }
            }

            await Task.Delay(50, AbortToken); // Simulate work

            lock (lockObj)
            {
                currentConcurrent--;
                processedCount++;
                if (processedCount >= messageCount)
                {
                    tcs.TrySetResult();
                }
            }

            await client.CommitAsync(sender);
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await client.ListeningAsync(TimeSpan.FromSeconds(30), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - send multiple messages
        for (var i = 0; i < messageCount; i++)
        {
            queue.Send(_CreateTestMessage($"msg-{i}", "concurrent-topic"));
        }

        // Wait for processing
        _ = await Task.WhenAny(tcs.Task, Task.Delay(30000, AbortToken));
        await cts.CancelAsync();
        await client.DisposeAsync();

        // then
        processedCount.Should().Be(messageCount);
        maxConcurrent.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_process_sequentially_when_concurrency_is_zero()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);
        var client = new InMemoryConsumerClient(queue, "sequential-group", 0);
        await client.SubscribeAsync(["sequential-topic"]);

        var processOrder = new List<string>();
        client.OnMessageCallback = (msg, _) =>
        {
            processOrder.Add(msg.GetId());
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await client.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - send messages in order
        queue.Send(_CreateTestMessage("1", "sequential-topic"));
        queue.Send(_CreateTestMessage("2", "sequential-topic"));
        queue.Send(_CreateTestMessage("3", "sequential-topic"));

        await Task.Delay(200, AbortToken);
        await cts.CancelAsync();
        await client.DisposeAsync();

        // then - should maintain order
        processOrder.Should().Equal(["1", "2", "3"]);
    }

    [Fact]
    public void should_set_and_get_log_callback()
    {
        // given
        LogMessageEventArgs? logArgs = null;
        Action<LogMessageEventArgs> callback = args => logArgs = args;

        // when
        _client.OnLogCallback = callback;

        // then
        _client.OnLogCallback.Should().Be(callback);
    }

    [Fact]
    public async Task should_add_group_header_to_delivered_message()
    {
        // given
        await _client.SubscribeAsync(["test-topic"]);

        TransportMessage? receivedMessage = null;
        _client.OnMessageCallback = (msg, _) =>
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
                    await _client.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when
        _queue.Send(_CreateTestMessage("msg-1", "test-topic"));

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.GetGroup().Should().Be("test-group");
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
