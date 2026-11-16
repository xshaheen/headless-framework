// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
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
        var messageNames = new[] { "messageName-1", "messageName-2" };

        // when
        await _consumerClient.SubscribeAsync(messageNames, AbortToken);

        // then - subscribe should complete without exception
        // The subscription is verified by being able to send messages
        _consumerClient.Should().NotBeNull();
    }

    [Fact]
    public async Task should_deliver_message_to_subscribed_consumer()
    {
        // given
        var messageNames = new[] { "test-messageName" };
        await _consumerClient.SubscribeAsync(messageNames, AbortToken);

        TransportMessage? receivedMessage = null;
        var tcs = new TaskCompletionSource();
        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            receivedMessage = msg;
            await _consumerClient.CommitAsync(sender);
            tcs.TrySetResult();
        };

        var message = _CreateTestMessage("msg-1", "test-messageName");

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

        // when - the client's channel holds a message sent before the listener reads, so there is nothing to wait for
        _queue.SendBus(message);

        await tcs.Task.Bounded();
        await cts.CancelAsync();

        // then
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.Id.Should().Be("msg-1");
        receivedMessage.Value.Name.Should().Be("test-messageName");
    }

    [Fact]
    public async Task should_silently_drop_message_when_no_group_subscribed_to_topic()
    {
        // given — no subscriber registered; SendBus matches real-broker no-op semantics (no throw)
        var message = _CreateTestMessage("msg-1", "unsubscribed-messageName");

        // when & then — must not throw
        var action = () => _queue.SendBus(message);
        action.Should().NotThrow();
    }

    [Fact]
    public async Task should_support_multiple_topics()
    {
        // given
        var messageNames = new[] { "messageName-a", "messageName-b" };
        await _consumerClient.SubscribeAsync(messageNames, AbortToken);

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

        // when - send to both messageNames
        _queue.SendBus(_CreateTestMessage("msg-a", "messageName-a"));
        _queue.SendBus(_CreateTestMessage("msg-b", "messageName-b"));

        await tcs.Task.Bounded();
        await cts.CancelAsync();

        // then
        receivedMessages.Should().HaveCount(2);
        receivedMessages.Select(m => m.Id).Should().BeEquivalentTo(["msg-a", "msg-b"]);
    }

    [Fact]
    public async Task should_be_thread_safe_for_concurrent_sends()
    {
        // given
        var messageNames = new[] { "concurrent-messageName" };
        await _consumerClient.SubscribeAsync(messageNames, AbortToken);

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

        // when - send messages concurrently
        var sendTasks = Enumerable
            .Range(0, messageCount)
            .Select(i =>
                Task.Run(() => _queue.SendBus(_CreateTestMessage($"msg-{i}", "concurrent-messageName")), AbortToken)
            );

        await Task.WhenAll(sendTasks);
        await tcs.Task.Bounded();
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

        await client1.SubscribeAsync(["shared-messageName"], AbortToken);
        await client2.SubscribeAsync(["shared-messageName"], AbortToken);

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
                catch (OperationCanceledException) { }
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
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        // when
        queue.SendBus(_CreateTestMessage("msg-1", "shared-messageName"));

        await Task.WhenAll(tcs1.Task.Bounded(), tcs2.Task.Bounded());
        await cts.CancelAsync();

        // then - both groups should receive the message
        messages1.Should().ContainSingle();
        messages2.Should().ContainSingle();

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact]
    public async Task should_remove_consumer_on_unsubscribe()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-messageName"], AbortToken);

        var receivedIds = new ConcurrentQueue<string>();
        var sentinelReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            receivedIds.Enqueue(msg.Id);
            await _consumerClient.CommitAsync(sender);

            if (string.Equals(msg.Id, "sentinel", StringComparison.Ordinal))
            {
                sentinelReceived.TrySetResult();
            }
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

        // when - unsubscribe the consumer
        _queue.Unsubscribe(MessageLane.Bus, "test-group", _consumerClient);

        // then - the messageName binding is removed with the final client; send is a silent no-op
        var act = () => _queue.SendBus(_CreateTestMessage("msg-1", "test-messageName"));
        act.Should().NotThrow();

        // SendBus delivers synchronously and the client's channel is FIFO, so a copy of msg-1 would sit ahead of a
        // sentinel sent after the client rejoins; waiting for the sentinel gives a leaked copy the time to show up.
        _queue.RegisterConsumerClient(MessageLane.Bus, "test-group", _consumerClient);
        _queue.Subscribe(MessageLane.Bus, "test-group", ["test-messageName"]);
        _queue.SendBus(_CreateTestMessage("sentinel", "test-messageName"));

        await sentinelReceived.Task.Bounded();
        await cts.CancelAsync();

        // Consumer should not receive the message sent while it was unsubscribed
        receivedIds.Should().Equal("sentinel");
    }

    [Fact]
    public async Task should_not_add_duplicate_group_to_topic()
    {
        // given
        await _consumerClient.SubscribeAsync(["messageName-1"], AbortToken);

        // when - subscribe same group to same messageName again
        await _consumerClient.SubscribeAsync(["messageName-1"], AbortToken);

        var receivedIds = new ConcurrentQueue<string>();
        var sentinelReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            receivedIds.Enqueue(msg.Id);
            await _consumerClient.CommitAsync(sender);

            if (string.Equals(msg.Id, "sentinel", StringComparison.Ordinal))
            {
                sentinelReceived.TrySetResult();
            }
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

        // A duplicate subscription would deliver msg-1 twice, and the client channel is FIFO, so any copy arrives
        // before the sentinel sent after it; waiting for the sentinel gives a duplicate the time to show up.
        _queue.SendBus(_CreateTestMessage("msg-1", "messageName-1"));
        _queue.SendBus(_CreateTestMessage("sentinel", "messageName-1"));

        await sentinelReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();

        // then - one copy of msg-1, then the sentinel
        receivedIds.Should().Equal("msg-1", "sentinel");
    }

    // -------------------------------------------------------------------------
    // DrainAllPendingMessages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_drain_all_pending_messages_from_multiple_clients()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);

        var client1 = new InMemoryConsumerClient(queue, "drain-group-1", 1);
        var client2 = new InMemoryConsumerClient(queue, "drain-group-2", 1);

        await client1.SubscribeAsync(["drain-messageName"], AbortToken);
        await client2.SubscribeAsync(["drain-messageName"], AbortToken);

        // Enqueue messages (goes to both groups via Send)
        queue.SendBus(_CreateTestMessage("d1", "drain-messageName"));
        queue.SendBus(_CreateTestMessage("d2", "drain-messageName"));

        // when
        queue.DrainAllPendingMessages();

        // then — listeners should receive nothing but a sentinel sent after the drain. Each client's channel is FIFO,
        // so a message the drain left behind would reach its listener ahead of the sentinel.
        var received1 = new ConcurrentQueue<string>();
        var received2 = new ConcurrentQueue<string>();
        var sentinel1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentinel2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client1.OnMessageCallback = (msg, _) =>
        {
            received1.Enqueue(msg.Id);
            if (string.Equals(msg.Id, "sentinel", StringComparison.Ordinal))
            {
                sentinel1.TrySetResult();
            }

            return Task.CompletedTask;
        };
        client2.OnMessageCallback = (msg, _) =>
        {
            received2.Enqueue(msg.Id);
            if (string.Equals(msg.Id, "sentinel", StringComparison.Ordinal))
            {
                sentinel2.TrySetResult();
            }

            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listen1 = Task.Run(
            async () =>
            {
                try
                {
                    await client1.ListeningAsync(TimeSpan.FromMilliseconds(200), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );
        var listen2 = Task.Run(
            async () =>
            {
                try
                {
                    await client2.ListeningAsync(TimeSpan.FromMilliseconds(200), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        queue.SendBus(_CreateTestMessage("sentinel", "drain-messageName"));

        await Task.WhenAll(sentinel1.Task.Bounded(), sentinel2.Task.Bounded());
        await cts.CancelAsync();

        received1.Should().Equal("sentinel");
        received2.Should().Equal("sentinel");

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact]
    public void should_not_throw_when_draining_with_no_clients()
    {
        // given — queue with no registered clients
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);

        // when & then
        var act = () => queue.DrainAllPendingMessages();
        act.Should().NotThrow();
    }

    private static TransportMessage _CreateTestMessage(string id, string messageName)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = id,
            [Headers.MessageName] = messageName,
        };

        return new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);
    }
}
