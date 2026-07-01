// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        await _consumerClient.SubscribeAsync(messageNames);

        // then - subscribe should complete without exception
        // The subscription is verified by being able to send messages
        _consumerClient.Should().NotBeNull();
    }

    [Fact]
    public async Task should_deliver_message_to_subscribed_consumer()
    {
        // given
        var messageNames = new[] { "test-messageName" };
        await _consumerClient.SubscribeAsync(messageNames);

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

        // when
        await Task.Delay(50, AbortToken); // Let listener start
        _queue.SendBus(message);

        // Wait for message to be delivered
        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
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
        await _consumerClient.SubscribeAsync(messageNames);

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

        // when - send to both messageNames
        _queue.SendBus(_CreateTestMessage("msg-a", "messageName-a"));
        _queue.SendBus(_CreateTestMessage("msg-b", "messageName-b"));

        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
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
        await _consumerClient.SubscribeAsync(messageNames);

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
            .Select(i =>
                Task.Run(() => _queue.SendBus(_CreateTestMessage($"msg-{i}", "concurrent-messageName")), AbortToken)
            );

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

        await client1.SubscribeAsync(["shared-messageName"]);
        await client2.SubscribeAsync(["shared-messageName"]);

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

        await Task.Delay(50, AbortToken);

        // when
        queue.SendBus(_CreateTestMessage("msg-1", "shared-messageName"));

        await Task.WhenAll(
            Task.WhenAny(tcs1.Task, Task.Delay(5000, AbortToken)),
            Task.WhenAny(tcs2.Task, Task.Delay(5000, AbortToken))
        );
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
        await _consumerClient.SubscribeAsync(["test-messageName"]);

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
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - unsubscribe the consumer
        _queue.Unsubscribe(IntentType.Bus, "test-group", _consumerClient);

        // then - the messageName binding is removed with the final client; send is a silent no-op
        var act = () => _queue.SendBus(_CreateTestMessage("msg-1", "test-messageName"));
        act.Should().NotThrow();
        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // Consumer should not receive the message since it was unsubscribed
        receivedMessage.Should().BeNull();
    }

    [Fact]
    public async Task should_not_add_duplicate_group_to_topic()
    {
        // given
        await _consumerClient.SubscribeAsync(["messageName-1"]);

        // when - subscribe same group to same messageName again
        await _consumerClient.SubscribeAsync(["messageName-1"]);

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
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // Send one message
        _queue.SendBus(_CreateTestMessage("msg-1", "messageName-1"));

        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
        await cts.CancelAsync();

        // then - should only receive one message, not duplicates
        receivedMessages.Should().ContainSingle();
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

        await client1.SubscribeAsync(["drain-messageName"]);
        await client2.SubscribeAsync(["drain-messageName"]);

        // Enqueue messages (goes to both groups via Send)
        queue.SendBus(_CreateTestMessage("d1", "drain-messageName"));
        queue.SendBus(_CreateTestMessage("d2", "drain-messageName"));

        // when
        queue.DrainAllPendingMessages();

        // then — listeners should receive nothing
        var received1 = new List<TransportMessage>();
        var received2 = new List<TransportMessage>();

        client1.OnMessageCallback = (msg, _) =>
        {
            received1.Add(msg);
            return Task.CompletedTask;
        };
        client2.OnMessageCallback = (msg, _) =>
        {
            received2.Add(msg);
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

        await Task.Delay(300, AbortToken);
        await cts.CancelAsync();

        received1.Should().BeEmpty();
        received2.Should().BeEmpty();

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
