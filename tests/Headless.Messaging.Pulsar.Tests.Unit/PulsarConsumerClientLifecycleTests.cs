// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
