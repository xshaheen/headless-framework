// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
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
        var messageNames = new[] { "messageName-1", "messageName-2", "messageName-3" };

        // when
        await _client.SubscribeAsync(messageNames, AbortToken);

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
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

        var received = new TaskCompletionSource<(TransportMessage Message, object? Sender)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _client.OnMessageCallback = (msg, sender) =>
        {
            received.TrySetResult((msg, sender));
            return Task.CompletedTask;
        };

        var message = _CreateTestMessage("msg-1", "test-messageName");

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
        _queue.SendBus(message);

        var (receivedMessage, receivedSender) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();

        // then
        receivedMessage.Id.Should().Be("msg-1");
        receivedSender.Should().BeNull();
    }

    [Fact]
    public async Task should_commit_and_release_semaphore()
    {
        // given
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.OnMessageCallback = async (_, sender) =>
        {
            await _client.CommitAsync(sender);
            committed.SetResult();
        };

        using var cts = new CancellationTokenSource();

        _ = Task.Run(
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
        _queue.SendBus(_CreateTestMessage("msg-1", "test-messageName"));

        await committed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();

        // then
        committed.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_and_release_semaphore()
    {
        // given
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.OnMessageCallback = async (_, sender) =>
        {
            await _client.RejectAsync(sender);
            rejected.SetResult();
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
        _queue.SendBus(_CreateTestMessage("msg-1", "test-messageName"));

        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();

        // then
        rejected.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_listening_on_cancellation()
    {
        // given
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

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

        // when: awaiting the listener task itself (bounded) instead of a fixed post-cancel
        // delay keeps the assertion deterministic under CI scheduling pressure
        await cts.CancelAsync();
        await listenTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

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
        await client.SubscribeAsync(["dispose-messageName"], AbortToken);

        TransportMessage? receivedMessage = null;
        client.OnMessageCallback = (msg, _) =>
        {
            receivedMessage = msg;
            return Task.CompletedTask;
        };

        // when
        await client.DisposeAsync();

        // then - disposed client removes the binding; send is a silent no-op (real-broker semantics)
        var act = () => queue.SendBus(_CreateTestMessage("msg-1", "dispose-messageName"));
        act.Should().NotThrow();
        await Task.Delay(100, AbortToken);
        receivedMessage.Should().BeNull();
    }

    [Fact]
    public async Task should_support_concurrent_message_processing()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);
        const byte concurrency = 4;
        var client = new InMemoryConsumerClient(queue, "concurrent-group", concurrency);
        await client.SubscribeAsync(["concurrent-messageName"], AbortToken);

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
            queue.SendBus(_CreateTestMessage($"msg-{i}", "concurrent-messageName"));
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
        await client.SubscribeAsync(["sequential-messageName"], AbortToken);

        var processOrder = new List<string>();
        var allProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (msg, _) =>
        {
            processOrder.Add(msg.Id);
            if (processOrder.Count == 3)
            {
                allProcessed.TrySetResult();
            }

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
        queue.SendBus(_CreateTestMessage("1", "sequential-messageName"));
        queue.SendBus(_CreateTestMessage("2", "sequential-messageName"));
        queue.SendBus(_CreateTestMessage("3", "sequential-messageName"));

        await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await client.DisposeAsync();

        // then - should maintain order
        processOrder.Should().Equal(["1", "2", "3"]);
    }

    [Fact]
    public void should_set_and_get_log_callback()
    {
        // given
        // ReSharper disable once NotAccessedVariable
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
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.OnMessageCallback = (msg, _) =>
        {
            received.TrySetResult(msg);
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
        _queue.SendBus(_CreateTestMessage("msg-1", "test-messageName"));

        var receivedMessage = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();

        // then
        receivedMessage.GetGroup().Should().Be("test-group");
    }

    // -------------------------------------------------------------------------
    // PauseAsync / ResumeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task pause_async_is_idempotent_when_called_twice()
    {
        // when
        await _client.PauseAsync(AbortToken);
        await _client.PauseAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task resume_async_is_noop_when_not_paused()
    {
        // when
        await _client.ResumeAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task pause_async_then_resume_async_completes_full_cycle()
    {
        // when
        await _client.PauseAsync(AbortToken);
        await _client.ResumeAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task pause_async_blocks_message_delivery()
    {
        // given
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

        var receivedCount = 0;
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.OnMessageCallback = (_, _) =>
        {
            Interlocked.Increment(ref receivedCount);
            received.TrySetResult();
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

        // when — pause then send
        await _client.PauseAsync(AbortToken);
        _queue.SendBus(_CreateTestMessage("paused-msg", "test-messageName"));
        await Task.Delay(200, AbortToken);

        // then — message not delivered while paused
        receivedCount.Should().Be(0);

        // cleanup — resume, then wait for the resumed delivery deterministically
        await _client.ResumeAsync(AbortToken);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();

        // message should now have been delivered after resume
        receivedCount.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // DrainPendingMessages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_drain_pending_messages_without_processing()
    {
        // given
        await _client.SubscribeAsync(["test-messageName"], AbortToken);

        _queue.SendBus(_CreateTestMessage("drain-1", "test-messageName"));
        _queue.SendBus(_CreateTestMessage("drain-2", "test-messageName"));

        // when — drain before any listener picks them up
        _client.DrainPendingMessages();

        // then — start listening; no messages should arrive
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
                    await _client.ListeningAsync(TimeSpan.FromMilliseconds(200), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(300, AbortToken);
        await cts.CancelAsync();

        receivedMessage.Should().BeNull();
    }

    [Fact]
    public async Task should_not_throw_when_draining_disposed_client()
    {
        // given
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        var queue = new MemoryQueue(logger);
        var client = new InMemoryConsumerClient(queue, "disposed-group", 1);
        await client.DisposeAsync();

        // when & then
        var act = client.DrainPendingMessages;
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_when_draining_empty_queue()
    {
        // given — client created but no messages enqueued
        var act = () => _client.DrainPendingMessages();

        // when & then
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
