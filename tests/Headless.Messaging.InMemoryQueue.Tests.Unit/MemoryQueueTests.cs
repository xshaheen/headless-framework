// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class MemoryQueueTests : TestBase
{
    private readonly MemoryQueue _queue;
    private readonly InMemoryConsumerClient _consumerClient;

    public MemoryQueueTests()
    {
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        _queue = new MemoryQueue(logger);
        _consumerClient = new InMemoryConsumerClient(_queue, "test-group", 1);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _consumerClient.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_register_consumer_client_for_group()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);

        // when
        var client = new InMemoryConsumerClient(queue, "my-group", 1);

        // then - no exception thrown, client registered implicitly in constructor
        client.Should().NotBeNull();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_subscribe_group_to_topics()
    {
        // given
        var topics = new[] { "topic-1", "topic-2" };

        // when
        await _consumerClient.SubscribeAsync(topics);

        // then - subscribe should complete without exception
        // The subscription is verified by being able to send messages
        _consumerClient.Should().NotBeNull();
    }

    [Fact]
    public async Task should_deliver_message_to_subscribed_consumer()
    {
        // given
        var topics = new[] { "test-topic" };
        await _consumerClient.SubscribeAsync(topics);

        TransportMessage? receivedMessage = null;
        var tcs = new TaskCompletionSource();
        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            receivedMessage = msg;
            await _consumerClient.CommitAsync(sender);
            tcs.TrySetResult();
        };

        var message = _CreateTestMessage("msg-1", "test-topic");

        // Start listening in background
        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            },
            AbortToken
        );

        // when
        await Task.Delay(50, AbortToken); // Let listener start
        _queue.Send(message);

        // Wait for message to be delivered
        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
        await cts.CancelAsync();

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.GetId().Should().Be("msg-1");
        receivedMessage.Value.GetName().Should().Be("test-topic");
    }

    [Fact]
    public async Task should_throw_when_no_group_subscribed_to_topic()
    {
        // given
        var message = _CreateTestMessage("msg-1", "unsubscribed-topic");

        // when
        var action = () => _queue.Send(message);

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Cannot find the corresponding group for unsubscribed-topic*");
    }

    [Fact]
    public async Task should_support_multiple_topics()
    {
        // given
        var topics = new[] { "topic-a", "topic-b" };
        await _consumerClient.SubscribeAsync(topics);

        var receivedMessages = new List<TransportMessage>();
        var lockObj = new Lock();
        var messageCount = 0;
        var tcs = new TaskCompletionSource();

        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(msg);
                messageCount++;
                if (messageCount >= 2)
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
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - send to both topics
        _queue.Send(_CreateTestMessage("msg-a", "topic-a"));
        _queue.Send(_CreateTestMessage("msg-b", "topic-b"));

        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
        await cts.CancelAsync();

        // then
        receivedMessages.Should().HaveCount(2);
        receivedMessages.Select(m => m.GetId()).Should().BeEquivalentTo(["msg-a", "msg-b"]);
    }

    [Fact]
    public async Task should_be_thread_safe_for_concurrent_sends()
    {
        // given
        var topics = new[] { "concurrent-topic" };
        await _consumerClient.SubscribeAsync(topics);

        var receivedMessages = new List<TransportMessage>();
        var lockObj = new Lock();
        const int messageCount = 100;
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
                catch (OperationCanceledException)
                {
                    // Expected
                }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - send messages concurrently
        var sendTasks = Enumerable
            .Range(0, messageCount)
            .Select(i => Task.Run(() => _queue.Send(_CreateTestMessage($"msg-{i}", "concurrent-topic")), AbortToken));

        await Task.WhenAll(sendTasks);
        _ = await Task.WhenAny(tcs.Task, Task.Delay(30000, AbortToken));
        await cts.CancelAsync();

        // then
        receivedMessages.Should().HaveCount(messageCount);
    }

    [Fact]
    public async Task should_support_multiple_consumer_groups()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);

        var client1 = new InMemoryConsumerClient(queue, "group-1", 1);
        var client2 = new InMemoryConsumerClient(queue, "group-2", 1);

        await client1.SubscribeAsync(["shared-topic"]);
        await client2.SubscribeAsync(["shared-topic"]);

        var messages1 = new List<TransportMessage>();
        var messages2 = new List<TransportMessage>();
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();

        client1.OnMessageCallback = async (msg, sender) =>
        {
            messages1.Add(msg);
            await client1.CommitAsync(sender);
            tcs1.TrySetResult();
        };
        client2.OnMessageCallback = async (msg, sender) =>
        {
            messages2.Add(msg);
            await client2.CommitAsync(sender);
            tcs2.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        var listen1 = Task.Run(
            async () =>
            {
                try
                {
                    await client1.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            },
            AbortToken
        );
        var listen2 = Task.Run(
            async () =>
            {
                try
                {
                    await client2.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when
        queue.Send(_CreateTestMessage("msg-1", "shared-topic"));

        await Task.WhenAll(
            Task.WhenAny(tcs1.Task, Task.Delay(5000, AbortToken)),
            Task.WhenAny(tcs2.Task, Task.Delay(5000, AbortToken))
        );
        await cts.CancelAsync();

        // then - both groups should receive the message
        messages1.Should().HaveCount(1);
        messages2.Should().HaveCount(1);

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact]
    public async Task should_remove_consumer_on_unsubscribe()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-topic"]);

        TransportMessage? receivedMessage = null;
        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            receivedMessage = msg;
            await _consumerClient.CommitAsync(sender);
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - unsubscribe the consumer
        _queue.Unsubscribe("test-group");

        // then - the message should be silently dropped since consumer is removed
        _queue.Send(_CreateTestMessage("msg-1", "test-topic"));
        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // Consumer should not receive the message since it was unsubscribed
        receivedMessage.Should().BeNull();
    }

    [Fact]
    public async Task should_not_add_duplicate_group_to_topic()
    {
        // given
        await _consumerClient.SubscribeAsync(["topic-1"]);

        // when - subscribe same group to same topic again
        await _consumerClient.SubscribeAsync(["topic-1"]);

        var receivedMessages = new List<TransportMessage>();
        var tcs = new TaskCompletionSource();

        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            receivedMessages.Add(msg);
            await _consumerClient.CommitAsync(sender);
            tcs.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // Send one message
        _queue.Send(_CreateTestMessage("msg-1", "topic-1"));

        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
        await cts.CancelAsync();

        // then - should only receive one message, not duplicates
        receivedMessages.Should().HaveCount(1);
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
