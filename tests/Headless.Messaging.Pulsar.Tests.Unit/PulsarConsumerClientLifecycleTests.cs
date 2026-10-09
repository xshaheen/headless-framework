// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;

namespace Tests;

public sealed class PulsarConsumerClientLifecycleTests : TestBase
{
    [Fact]
    public async Task should_keep_replacement_receive_token_active_when_pause_and_resume_overlap()
    {
        // given
        await using var client = new PulsarConsumerClient(
            Options.Create(new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }),
            client: null!,
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
    public async Task should_stamp_topic_as_transport_address_when_wire_property_spoofs_it()
    {
        // given
        await using var client = new PulsarConsumerClient(
            Options.Create(new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }),
            client: null!,
            new ConsumerClientRequest("address-test", concurrency: 0, MessageLane.Bus)
        );

        const string topic = "persistent://public/default/orders.created";
        var delivery = _CreateMessage(
            topic,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "TestEvent",
                [Headers.TransportAddress] = "wire-spoofed",
            }
        );

        var consumer = Substitute.For<IConsumer<byte[]>>();
        var receiveCount = 0;
        consumer
            .ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref receiveCount) == 1)
                {
                    return delivery;
                }

                // Park the loop until the test cancels listening.
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return delivery;
            });
        _SetField(client, "_consumerClient", consumer);

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
        received.Headers[Headers.TransportAddress].Should().Be(topic);
    }

    [Fact]
    public async Task should_let_in_flight_handler_acknowledge_before_closing_when_shutdown_async()
    {
        // given
        var (client, consumer, handlerStarted, releaseHandler, listening, cts) = await _StartWithBlockedHandlerAsync();
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
        Received.InOrder(() =>
        {
            _ = consumer.AcknowledgeAsync(Arg.Any<MessageId>());
            _ = consumer.DisposeAsync();
        });
        cts.Dispose();
    }

    [Fact]
    public async Task should_close_after_the_budget_when_a_handler_outlives_shutdown()
    {
        // given
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var (client, consumer, _, releaseHandler, listening, cts) = await _StartWithBlockedHandlerAsync(logs);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await client.ShutdownAsync(TimeSpan.FromMilliseconds(50), AbortToken);

        // then
        await consumer.Received(1).DisposeAsync();
        await consumer.DidNotReceive().AcknowledgeAsync(Arg.Any<MessageId>());
        logs.Should().Contain(x => x.Reason!.Contains("draining in-flight Pulsar handlers", StringComparison.Ordinal));
        releaseHandler.TrySetResult();
        cts.Dispose();
    }

    [Fact]
    public async Task should_skip_the_drain_when_the_shared_deadline_already_expired()
    {
        // given
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var (client, consumer, _, releaseHandler, listening, cts) = await _StartWithBlockedHandlerAsync(logs);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await client.ShutdownAsync(TimeSpan.Zero, AbortToken);

        // then
        await consumer.Received(1).DisposeAsync();
        logs.Should().Contain(x => x.Reason!.Contains("deadline has expired", StringComparison.Ordinal));
        releaseHandler.TrySetResult();
        cts.Dispose();
    }

    private async Task<(
        PulsarConsumerClient Client,
        IConsumer<byte[]> Consumer,
        TaskCompletionSource HandlerStarted,
        TaskCompletionSource ReleaseHandler,
        Task Listening,
        CancellationTokenSource Cts
    )> _StartWithBlockedHandlerAsync(ConcurrentQueue<LogMessageEventArgs>? logs = null)
    {
        var client = new PulsarConsumerClient(
            Options.Create(new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }),
            client: null!,
            new ConsumerClientRequest("shutdown-test", concurrency: 2, MessageLane.Bus)
        );
        var delivery = _CreateMessage(
            "persistent://public/default/orders.created",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "TestEvent",
            }
        );
        var consumer = Substitute.For<IConsumer<byte[]>>();
        var receiveCount = 0;
        consumer
            .ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref receiveCount) == 1)
                {
                    return delivery;
                }

                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return delivery;
            });
        _SetField(client, "_consumerClient", consumer);

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

        return (client, consumer, handlerStarted, releaseHandler, listening, cts);
    }

    // Pulsar.Client builds Message<T> only on receipt and exposes no public constructor or factory for it.
    private static Message<byte[]> _CreateMessage(string topic, IReadOnlyDictionary<string, string> properties)
    {
        var messageId = new MessageId(1, 1, MessageIdType.Single, -1, topic, chunkMessageIds: null);
        var constructor = typeof(Message<byte[]>)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 17);

        return (Message<byte[]>)
            constructor.Invoke([
                messageId,
                "body"u8.ToArray(),
                null,
                false,
                properties,
                null,
                null,
                0L,
                null,
                0L,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
            ]);
    }

    private static void _SetField(object instance, string fieldName, object value)
    {
        instance
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .SetValue(instance, value);
    }

    private static T _GetField<T>(object instance, string fieldName)
    {
        return (T)
            instance
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                .GetValue(instance)!;
    }
}
