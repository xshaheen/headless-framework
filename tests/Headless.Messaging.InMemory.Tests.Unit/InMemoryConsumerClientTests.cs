// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

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
        address.Name.Should().Be("in_memory");
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
        receivedSender.Should().NotBeNull("the sender is the settlement token a reject requeues");
    }

    [Fact]
    public async Task should_not_block_the_calling_thread_while_waiting_for_a_message()
    {
        // given
        await _client.SubscribeAsync(["test-messageName"], AbortToken);
        using var cts = new CancellationTokenSource();

        // A blocking take would never return on its own, so a deadline keeps a regression a failure, not a hang.
        cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

        // when - the call runs on this thread until its first real await, so a blocking take would hold it here
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var listening = _client.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token).AsTask();
        stopwatch.Stop();

        // then
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        listening.IsCompleted.Should().BeFalse();

        var act = async () => await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        var clock = new FakeTimeProvider();
        await using var client = new InMemoryConsumerClient(_queue, "reject-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["reject-messageName"], AbortToken);

        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveries = 0;
        client.OnMessageCallback = async (_, sender) =>
        {
            if (Interlocked.Increment(ref deliveries) == 1)
            {
                await client.RejectAsync(sender, AbortToken);
                rejected.SetResult();
                return;
            }

            await client.CommitAsync(sender, AbortToken);
            committed.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);

        // when
        _queue.SendBus(_CreateTestMessage("msg-1", "reject-messageName"));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        clock.Advance(_MaxFirstRedeliveryDelay);

        // then: the rejected delivery comes back and settles; the slot it held was released, or it could not run
        await committed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        deliveries.Should().Be(2);
    }

    [Fact]
    public async Task should_redeliver_a_rejected_message_to_the_same_client_after_the_redelivery_delay()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var client = new InMemoryConsumerClient(_queue, "redeliver-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["redeliver-messageName"], AbortToken);

        var deliveredIds = new ConcurrentQueue<string>();
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = async (message, sender) =>
        {
            deliveredIds.Enqueue(message.Id);
            if (deliveredIds.Count == 1)
            {
                await client.RejectAsync(sender, AbortToken);
                rejected.SetResult();
                return;
            }

            await client.CommitAsync(sender, AbortToken);
            redelivered.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);
        _queue.SendBus(_CreateTestMessage("msg-1", "redeliver-messageName"));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        clock.Advance(_MaxFirstRedeliveryDelay);

        // then
        await redelivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        deliveredIds.Should().Equal("msg-1", "msg-1");
    }

    [Fact]
    public async Task should_not_redeliver_a_rejected_message_before_the_first_redelivery_delay_elapses()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var client = new InMemoryConsumerClient(_queue, "early-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["early-messageName"], AbortToken);

        var deliveries = 0;
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = async (_, sender) =>
        {
            if (Interlocked.Increment(ref deliveries) == 1)
            {
                await client.RejectAsync(sender, AbortToken);
                rejected.SetResult();
            }
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);
        _queue.SendBus(_CreateTestMessage("msg-1", "early-messageName"));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when: just short of the shortest first delay (a second jittered down by at most a quarter)
        clock.Advance(_MinFirstRedeliveryDelay - TimeSpan.FromMilliseconds(1));
        await Task.Delay(200, AbortToken);

        // then: an immediate requeue would spin on a delivery the core keeps rejecting
        await cts.CancelAsync();
        deliveries.Should().Be(1);
    }

    [Fact]
    public async Task should_not_grow_concurrency_when_a_message_is_rejected_repeatedly()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var client = new InMemoryConsumerClient(_queue, "slots-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["slots-messageName"], AbortToken);

        const int rejections = 3;
        var rejectCount = 0;
        using var rejectedOnce = new SemaphoreSlim(0);
        var rejectedDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maxRunning = 0;
        var blockingStarted = 0;
        client.OnMessageCallback = async (message, sender) =>
        {
            if (string.Equals(message.Id, "rejected", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref rejectCount) <= rejections)
                {
                    await client.RejectAsync(sender, AbortToken);
                    rejectedOnce.Release();
                    return;
                }

                await client.CommitAsync(sender, AbortToken);
                rejectedDone.TrySetResult();
                return;
            }

            var now = Interlocked.Increment(ref running);
            Interlocked.Increment(ref blockingStarted);
            int seen;
            while (now > (seen = Volatile.Read(ref maxRunning)))
            {
                Interlocked.CompareExchange(ref maxRunning, now, seen);
            }

            await blockingGate.Task.WaitAsync(AbortToken);
            Interlocked.Decrement(ref running);
            await client.CommitAsync(sender, AbortToken);
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);
        _queue.SendBus(_CreateTestMessage("rejected", "slots-messageName"));
        for (var i = 0; i < rejections; i++)
        {
            (await rejectedOnce.WaitAsync(TimeSpan.FromSeconds(5), AbortToken)).Should().BeTrue();
            clock.Advance(ReconnectBackoff.Ceiling);
        }

        await rejectedDone.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        _queue.SendBus(_CreateTestMessage("blocking-1", "slots-messageName"));
        _queue.SendBus(_CreateTestMessage("blocking-2", "slots-messageName"));
        await Task.Delay(200, AbortToken);

        // then: one handler at a time, as configured, however many times a delivery was rejected
        Volatile.Read(ref blockingStarted).Should().Be(1);
        blockingGate.SetResult();
        await cts.CancelAsync();
        Volatile.Read(ref maxRunning).Should().Be(1);
    }

    [Fact]
    public async Task should_drop_a_rejected_message_when_the_client_stops_before_the_redelivery_delay_ends()
    {
        // given
        var clock = new FakeTimeProvider();
        var client = new InMemoryConsumerClient(_queue, "stopped-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["stopped-messageName"], AbortToken);

        var deliveries = 0;
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = async (_, sender) =>
        {
            Interlocked.Increment(ref deliveries);
            await client.RejectAsync(sender, AbortToken);
            rejected.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);
        _queue.SendBus(_CreateTestMessage("msg-1", "stopped-messageName"));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await client.DisposeAsync();
        clock.Advance(ReconnectBackoff.Ceiling);
        await Task.Delay(100, AbortToken);

        // then
        await cts.CancelAsync();
        deliveries.Should().Be(1);
    }

    [Fact]
    public async Task should_drop_a_rejected_message_when_pending_messages_are_drained_before_the_redelivery_delay_ends()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var client = new InMemoryConsumerClient(_queue, "drained-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["drained-messageName"], AbortToken);

        var deliveries = 0;
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = async (_, sender) =>
        {
            Interlocked.Increment(ref deliveries);
            await client.RejectAsync(sender, AbortToken);
            rejected.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);
        _queue.SendBus(_CreateTestMessage("msg-1", "drained-messageName"));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        client.DrainPendingMessages();
        clock.Advance(ReconnectBackoff.Ceiling);
        await Task.Delay(100, AbortToken);

        // then
        await cts.CancelAsync();
        deliveries.Should().Be(1, "a drain discards pending work, including a redelivery still waiting on its delay");
    }

    [Fact]
    public async Task should_not_throw_or_redeliver_when_rejecting_after_the_client_stopped()
    {
        // given
        var clock = new FakeTimeProvider();
        var client = new InMemoryConsumerClient(_queue, "late-group", 1, redeliveryClock: clock);
        await client.SubscribeAsync(["late-messageName"], AbortToken);

        var deliveries = 0;
        var delivered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (_, sender) =>
        {
            Interlocked.Increment(ref deliveries);
            delivered.TrySetResult(sender);
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        _StartListening(client, cts.Token);
        _queue.SendBus(_CreateTestMessage("msg-1", "late-messageName"));
        var sender = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await client.DisposeAsync();

        // when
        var act = async () => await client.RejectAsync(sender, AbortToken);

        // then
        await act.Should().NotThrowAsync();
        clock.Advance(ReconnectBackoff.Ceiling);
        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();
        deliveries.Should().Be(1);
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
    public async Task should_not_stamp_consumer_identity_on_delivered_message()
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
        // The messaging core stamps the identity once the delivery is routed to a consumer.
        receivedMessage.GetConsumerIdentity().Should().BeNull();
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

    // The first redelivery waits about a second, jittered down by at most a quarter.
    private static readonly TimeSpan _MinFirstRedeliveryDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan _MaxFirstRedeliveryDelay = TimeSpan.FromSeconds(1);

    private void _StartListening(InMemoryConsumerClient client, CancellationToken cancellationToken)
    {
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await client.ListeningAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );
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
