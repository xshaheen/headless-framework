// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using DotPulsar.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarConsumerClientLifecycleTests : TestBase
{
    private const string _Topic = "persistent://public/default/headless-bus-orders.created";

    [Fact]
    public async Task should_keep_replacement_receive_token_active_when_pause_and_resume_overlap()
    {
        // given
        await using var client = PulsarConsumerClients.Create(
            new ConsumerClientRequest("lifecycle-test", concurrency: 0, MessageLane.Bus)
        );
        var receiveLock = _GetField<Lock>(client, "_receiveLock");
        var pauseGate = _GetField<ConsumerPauseGate>(client, "_pauseGate");

        Task pauseTask;
        Task resumeTask;
        receiveLock.Enter();
        try
        {
            // Pause owns the transition lock and reaches the receive lock. Resume must not
            // replace the receive source until pause has cancelled the previous generation.
            pauseTask = Task.Run(async () => await client.PauseAsync(AbortToken), AbortToken);
            SpinWait.SpinUntil(() => pauseGate.IsPaused, TimeSpan.FromSeconds(5)).Should().BeTrue();

            // when
            resumeTask = client.ResumeAsync(AbortToken).AsTask();

            // then
            resumeTask.IsCompleted.Should().BeFalse("pause and resume transitions must be serialized");
        }
        finally
        {
            receiveLock.Exit();
        }

        await Task.WhenAll(pauseTask, resumeTask).WaitAsync(AbortToken);
        var receiveCts = _GetField<CancellationTokenSource>(client, "_receiveCts");
        pauseGate.IsPaused.Should().BeFalse();
        receiveCts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_release_a_delayed_reject_at_once_because_the_consumer_delays_the_redelivery()
    {
        // given
        await using var client = PulsarConsumerClients.Create(
            new ConsumerClientRequest("delayed-reject-test", concurrency: 0, MessageLane.Bus)
        );

        // when: a delivery with no message to reject, so only the hold is observable
        var reject = client.RejectAsync(sender: null, TimeSpan.FromMinutes(5), AbortToken).AsTask();

        // then: NegativeAckRedeliveryDelay holds the message back, so the core's delay is not waited out here
        await reject.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_stamp_topic_as_transport_address_when_wire_property_spoofs_it()
    {
        // given
        await using var receiver = FakePulsarReceiver.Delivering(
            _Topic,
            PulsarMessages.Create(
                _Topic,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "msg-1",
                    [Headers.MessageName] = "TestEvent",
                    [Headers.TransportAddress] = "wire-spoofed",
                }
            )
        );
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("address-test", concurrency: 0, MessageLane.Bus),
            receiver,
            AbortToken
        );

        var delivered = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            (message, _) =>
            {
                delivered.TrySetResult(message);
                return Task.CompletedTask;
            },
            _ => { }
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        var received = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listeningTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        received.Headers[Headers.TransportAddress].Should().Be(_Topic);
    }

    [Fact]
    public async Task should_complete_a_batched_message_id_with_its_partition_topic()
    {
        // given: DotPulsar leaves the topic off the id of a message from a producer's batch
        const string partitioned = "headless-queue-orders";
        await using var receiver = FakePulsarReceiver.Delivering(
            partitioned,
            PulsarMessages.Create(
                topic: "",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "msg-1",
                    [Headers.MessageName] = "orders",
                },
                partition: 3,
                batchIndex: 1
            )
        );
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("batch-test", concurrency: 0, MessageLane.Queue),
            receiver,
            AbortToken,
            name: "orders"
        );

        var delivered = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            async (message, sender) =>
            {
                await client.CommitAsync(sender);
                delivered.TrySetResult(message);
            },
            _ => { }
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        var received = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: a partitioned consumer routes the acknowledgement by the id's topic
        receiver.Acknowledged.Should().ContainSingle().Which.Topic.Should().Be($"{partitioned}-partition-3");
        received
            .Headers[Headers.TransportAddress]
            .Should()
            .Be($"persistent://public/default/{partitioned}-partition-3");
    }

    [Fact]
    public async Task should_receive_from_every_subscribed_topic()
    {
        // given
        var receivers = new ConcurrentDictionary<string, FakePulsarReceiver>(StringComparer.Ordinal);
        await using var client = PulsarConsumerClients.Create(
            new ConsumerClientRequest("multi-topic-test", concurrency: 0, MessageLane.Bus),
            topic =>
                receivers.GetOrAdd(
                    topic,
                    static name =>
                        FakePulsarReceiver.Delivering(
                            name,
                            PulsarMessages.Create(
                                name,
                                new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    [Headers.MessageId] = name,
                                    [Headers.MessageName] = name,
                                }
                            )
                        )
                )
        );
        await client.SubscribeAsync(["orders.created", "orders.shipped"], AbortToken);

        var delivered = new ConcurrentQueue<string>();
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            (message, _) =>
            {
                delivered.Enqueue(message.Headers[Headers.MessageId]!);
                if (delivered.Count == 2)
                {
                    both.TrySetResult();
                }

                return Task.CompletedTask;
            },
            _ => { }
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        await both.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        receivers.Keys.Should().BeEquivalentTo("headless-bus-orders.created", "headless-bus-orders.shipped");
        delivered.Should().BeEquivalentTo(receivers.Keys);
    }

    [Fact]
    public async Task should_keep_a_message_received_while_a_pause_cancels_the_receive()
    {
        // given: the receive hands its message over as the pause cancels it
        var message = PulsarMessages.Create(
            _Topic,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "TestEvent",
            }
        );
        await using var otherReceiver = new FakePulsarReceiver("headless-bus-other");
        var pausing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var receiver = new FakePulsarReceiver(_Topic);
        var receives = 0;
        receiver.OnReceive = async token =>
        {
            if (Interlocked.Increment(ref receives) == 1)
            {
                await pausing.Task;
                return message;
            }

            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };
        await using var client = PulsarConsumerClients.Create(
            new ConsumerClientRequest("pause-race-test", concurrency: 0, MessageLane.Bus),
            topic => string.Equals(topic, _Topic, StringComparison.Ordinal) ? receiver : otherReceiver
        );
        await client.SubscribeAsync(["persistent://public/default/orders.created", "other"], AbortToken);

        var delivered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            (_, sender) =>
            {
                delivered.TrySetResult(sender);
                return Task.CompletedTask;
            },
            _ => { }
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        SpinWait.SpinUntil(() => Volatile.Read(ref receives) == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();

        // when: the other receiver's receive is canceled by the pause first, while this one completes
        await client.PauseAsync(AbortToken);
        pausing.SetResult();
        await Task.Delay(100, AbortToken);
        var deliveredWhilePaused = delivered.Task.IsCompleted;
        await client.ResumeAsync(AbortToken);

        // then
        var sender = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        deliveredWhilePaused.Should().BeFalse("a paused consumer dispatches nothing");
        sender.Should().BeOfType<PulsarDelivery>().Which.Message.Should().BeSameAs(message);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_fail_the_listener_when_a_receiver_ended()
    {
        // given: a faulted consumer never delivers again, so only a rebuilt client recovers
        await using var receiver = new FakePulsarReceiver(_Topic)
        {
            OnReceive = static _ =>
                Task.FromException<IMessage<ReadOnlySequence<byte>>>(new InvalidOperationException("faulted")),
        };
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("ended-test", concurrency: 0, MessageLane.Bus),
            receiver,
            AbortToken
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, _ => { });
        receiver.HasEnded = true;

        // when
        var act = () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).AsTask();

        // then
        (await act.Should().ThrowAsync<BrokerConnectionException>())
            .WithInnerException<InvalidOperationException>()
            .WithMessage("faulted");
    }

    [Fact]
    public async Task should_fail_the_subscription_when_a_receiver_ends_before_it_connects()
    {
        // given
        await using var receiver = new FakePulsarReceiver(_Topic);
        var disposed = false;
        receiver.OnDispose = () =>
        {
            disposed = true;
            return ValueTask.CompletedTask;
        };
        await using var client = PulsarConsumerClients.Create(
            new ConsumerClientRequest("subscribe-fault-test", concurrency: 0, MessageLane.Bus),
            _ => new _NeverConnectedReceiver(receiver)
        );

        // when
        var act = () => client.SubscribeAsync(["orders.created"], AbortToken).AsTask();

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
        disposed.Should().BeTrue("a subscription that failed to start leaves nothing open");
    }

    [Fact]
    public async Task should_let_in_flight_handler_acknowledge_before_closing_when_shutdown_async()
    {
        // given
        var (client, receiver, _, releaseHandler, listening, cts) = await _StartWithBlockedHandlerAsync();
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(5), AbortToken).AsTask();
        await Task.Delay(100, AbortToken);
        var completedBeforeHandler = shutdown.IsCompleted;
        releaseHandler.TrySetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        completedBeforeHandler.Should().BeFalse("shutdown waits for the in-flight handler");
        receiver.Calls.Should().Equal("ack", "dispose");
        cts.Dispose();
    }

    [Fact]
    public async Task should_wait_for_a_handler_the_receive_loop_starts_after_shutdown_began()
    {
        // given
        var delivery = PulsarMessages.Create(
            _Topic,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "TestEvent",
            }
        );
        var receiveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeReceive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var receiver = new FakePulsarReceiver(_Topic);
        var receiveCount = 0;
        receiver.OnReceive = async token =>
        {
            if (Interlocked.Increment(ref receiveCount) == 1)
            {
                // A receive that completes with a message as shutdown cancels it: the broker had already sent it.
                receiveEntered.TrySetResult();
                await completeReceive.Task;
                return delivery;
            }

            await Task.Delay(Timeout.Infinite, token);
            return delivery;
        };
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("shutdown-test", concurrency: 1, MessageLane.Bus),
            receiver,
            AbortToken
        );
        client.AttachCallbacks((_, sender) => client.CommitAsync(sender).AsTask(), _ => { });

        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken).AsTask();
        await receiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(30), AbortToken).AsTask();
        var completedBeforeReceive = await Task.WhenAny(shutdown, Task.Delay(200, AbortToken)) == shutdown;
        completeReceive.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        completedBeforeReceive.Should().BeFalse("shutdown waits for the receive loop and the handler it starts");
        receiver.Calls.Should().Equal("ack", "dispose");
    }

    [Fact]
    public async Task should_close_after_the_budget_when_a_handler_outlives_shutdown()
    {
        // given
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var (client, receiver, _, releaseHandler, listening, cts) = await _StartWithBlockedHandlerAsync(logs);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await client.ShutdownAsync(TimeSpan.FromMilliseconds(50), AbortToken);

        // then
        receiver.Calls.Should().Equal("dispose");
        logs.Should().Contain(x => x.Reason!.Contains("draining in-flight Pulsar handlers", StringComparison.Ordinal));
        releaseHandler.TrySetResult();
        cts.Dispose();
    }

    [Fact]
    public async Task should_skip_the_drain_when_the_shared_deadline_already_expired()
    {
        // given
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var (client, receiver, _, releaseHandler, listening, cts) = await _StartWithBlockedHandlerAsync(logs);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await client.ShutdownAsync(TimeSpan.Zero, AbortToken);

        // then
        receiver.Calls.Should().Contain("dispose");
        logs.Should().Contain(x => x.Reason!.Contains("deadline has expired", StringComparison.Ordinal));
        releaseHandler.TrySetResult();
        cts.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task should_reject_and_keep_receiving_when_the_callback_throws(byte concurrency)
    {
        // given
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "TestEvent",
        };
        var failing = PulsarMessages.Create(_Topic, properties, entryId: 1);
        var next = PulsarMessages.Create(_Topic, properties, entryId: 2);
        await using var receiver = FakePulsarReceiver.Delivering(_Topic, failing, next);
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("callback-failure-test", concurrency, MessageLane.Bus),
            receiver,
            AbortToken
        );

        var secondDelivered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var calls = 0;
        client.AttachCallbacks(
            (_, sender) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("callback failure");
                }

                secondDelivered.TrySetResult(sender);
                return Task.CompletedTask;
            },
            logs.Enqueue
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        var nextSender = await secondDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        nextSender
            .Should()
            .BeOfType<PulsarDelivery>()
            .Which.Message.Should()
            .BeSameAs(next, "the listener must survive a callback failure");
        client.Redelivery.Count.Should().Be(1, "only the client can make a delivery whose callback threw available");
        logs.Should().Contain(log => log.Reason!.Contains("callback failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_return_within_the_budget_when_closing_the_consumer_hangs()
    {
        // given
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var neverClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var receiver = new FakePulsarReceiver(_Topic) { OnDispose = () => new ValueTask(neverClosed.Task) };
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("close-hang-test", concurrency: 1, MessageLane.Bus),
            receiver,
            AbortToken
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);

        // when: a close the broker never answers, as after a broker restart
        var shutdown = client.ShutdownAsync(TimeSpan.FromMilliseconds(200), AbortToken).AsTask();

        // then
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        receiver.Calls.Should().Equal("dispose");
        logs.Should().Contain(log => log.Reason!.Contains("close", StringComparison.Ordinal));
        neverClosed.TrySetResult();
    }

    [Fact]
    public async Task should_stop_listening_when_canceled_while_the_receive_ignores_cancellation()
    {
        // given: neither the receive nor the state watch honors cancellation
        var receiveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var receiver = new FakePulsarReceiver(_Topic)
        {
            OnReceive = _ =>
            {
                receiveStarted.TrySetResult();
                return new TaskCompletionSource<IMessage<ReadOnlySequence<byte>>>().Task;
            },
        };
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("stalled-receive-test", concurrency: 1, MessageLane.Bus),
            receiver,
            AbortToken
        );
        client.AttachCallbacks((_, _) => Task.CompletedTask, _ => { });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await cts.CancelAsync();

        // then
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_report_the_broker_unreachable_while_a_receiver_reconnects()
    {
        // given
        await using var receiver = new FakePulsarReceiver(_Topic);
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("reachability-test", concurrency: 0, MessageLane.Bus),
            receiver,
            AbortToken
        );
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        receiver.SetLink(PulsarLinkState.Disconnected);
        SpinWait
            .SpinUntil(() => logs.Any(log => log.LogType == MqLogType.BrokerUnreachable), TimeSpan.FromSeconds(5))
            .Should()
            .BeTrue();
        receiver.SetLink(PulsarLinkState.Connected);

        // then
        SpinWait
            .SpinUntil(() => logs.Any(log => log.LogType == MqLogType.BrokerReachable), TimeSpan.FromSeconds(5))
            .Should()
            .BeTrue();
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Theory]
    [InlineData(ConsumerSubscriptionKind.EveryInstance, true)]
    [InlineData(ConsumerSubscriptionKind.Competing, false)]
    public async Task should_report_a_recovered_subscription_only_for_an_every_instance_consumer(
        ConsumerSubscriptionKind kind,
        bool expected
    )
    {
        // given
        await using var receiver = new FakePulsarReceiver(_Topic, settles: kind is ConsumerSubscriptionKind.Competing);
        await using var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("reestablished-test", 0, MessageLane.Bus, kind, Guid.NewGuid()),
            receiver,
            AbortToken
        );
        var reestablished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachReestablishedCallback(_ =>
        {
            reestablished.TrySetResult();
            return Task.CompletedTask;
        });
        client.AttachCallbacks((_, _) => Task.CompletedTask, _ => { });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        // when
        receiver.SetLink(PulsarLinkState.Disconnected);
        receiver.SetLink(PulsarLinkState.Connected);
        var raised = await Task.WhenAny(reestablished.Task, Task.Delay(500, AbortToken)) == reestablished.Task;

        // then
        raised.Should().Be(expected);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    private async Task<(
        PulsarConsumerClient Client,
        FakePulsarReceiver Receiver,
        TaskCompletionSource HandlerStarted,
        TaskCompletionSource ReleaseHandler,
        Task Listening,
        CancellationTokenSource Cts
    )> _StartWithBlockedHandlerAsync(ConcurrentQueue<LogMessageEventArgs>? logs = null)
    {
        var receiver = FakePulsarReceiver.Delivering(
            _Topic,
            PulsarMessages.Create(
                _Topic,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "msg-1",
                    [Headers.MessageName] = "TestEvent",
                }
            )
        );
        var client = await PulsarConsumerClients.SubscribedAsync(
            new ConsumerClientRequest("shutdown-test", concurrency: 2, MessageLane.Bus),
            receiver,
            AbortToken
        );

        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            async (_, sender) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
                await client.CommitAsync(sender).ConfigureAwait(false);
            },
            log => logs?.Enqueue(log)
        );

        var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        return (client, receiver, handlerStarted, releaseHandler, listening, cts);
    }

    private static T _GetField<T>(object instance, string fieldName)
    {
        return (T)
            instance
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                .GetValue(instance)!;
    }

    // Starts disconnected, so the subscription watches the link until the wrapped receiver reports its state.
    private sealed class _NeverConnectedReceiver(FakePulsarReceiver inner) : IPulsarReceiver
    {
        public string Topic => inner.Topic;

        public bool Settles => inner.Settles;

        public bool HasEnded => true;

        public ValueTask<IMessage<ReadOnlySequence<byte>>> ReceiveAsync(CancellationToken cancellationToken) =>
            inner.ReceiveAsync(cancellationToken);

        public ValueTask AcknowledgeAsync(DotPulsar.MessageId messageId, CancellationToken cancellationToken) =>
            inner.AcknowledgeAsync(messageId, cancellationToken);

        public ValueTask RedeliverAsync(
            IEnumerable<DotPulsar.MessageId> messageIds,
            CancellationToken cancellationToken
        ) => inner.RedeliverAsync(messageIds, cancellationToken);

        public ValueTask<PulsarLinkState> WaitForLinkChangeAsync(
            PulsarLinkState current,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(PulsarLinkState.Ended);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
