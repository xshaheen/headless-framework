// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using DotPulsar;
using DotPulsar.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Builds received Pulsar messages, which DotPulsar only creates on receipt.</summary>
internal static class PulsarMessages
{
    public const ulong PublishTimeMs = 1_791_547_200_000;

    public static IMessage<ReadOnlySequence<byte>> Create(
        string topic,
        IReadOnlyDictionary<string, string>? properties = null,
        ulong entryId = 22,
        int partition = -1,
        int batchIndex = -1,
        ulong publishTime = PublishTimeMs,
        uint redeliveryCount = 0,
        string? key = null,
        byte[]? body = null
    )
    {
        var message = Substitute.For<IMessage<ReadOnlySequence<byte>>>();
        message.MessageId.Returns(new MessageId(11, entryId, partition, batchIndex, topic));
        message.Data.Returns(new ReadOnlySequence<byte>(body ?? "{}"u8.ToArray()));
        message.Properties.Returns(properties ?? new Dictionary<string, string>(StringComparer.Ordinal));
        message.PublishTime.Returns(publishTime);
        message.RedeliveryCount.Returns(redeliveryCount);
        message.HasKey.Returns(key is not null);
        message.Key.Returns(key);

        return message;
    }
}

/// <summary>A receiver whose broker the test plays.</summary>
internal sealed class FakePulsarReceiver(string topic, bool settles = true) : IPulsarReceiver
{
    private readonly Channel<PulsarLinkState> _links = Channel.CreateUnbounded<PulsarLinkState>();
    private PulsarLinkState _link = PulsarLinkState.Connected;

    public string Topic { get; } = topic;

    public bool Settles { get; } = settles;

    public bool HasEnded { get; set; }

    /// <summary>Gets or sets what each receive returns; by default it waits until canceled.</summary>
    public Func<CancellationToken, Task<IMessage<ReadOnlySequence<byte>>>> OnReceive { get; set; } =
        static async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };

    /// <summary>Gets or sets what disposing returns.</summary>
    public Func<ValueTask> OnDispose { get; set; } = static () => ValueTask.CompletedTask;

    public ConcurrentQueue<MessageId> Acknowledged { get; } = new();

    public ConcurrentQueue<MessageId> Redelivered { get; } = new();

    /// <summary>Gets the calls in the order they arrived: <c>ack</c>, <c>redeliver</c>, and <c>dispose</c>.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    private int _receiveCount;

    public int ReceiveCount => Volatile.Read(ref _receiveCount);

    /// <summary>Makes the receiver report <paramref name="state"/> to whoever watches its link.</summary>
    public void SetLink(PulsarLinkState state) => _links.Writer.TryWrite(state);

    /// <summary>Creates a receiver that returns <paramref name="messages"/> one per receive, then waits until canceled.</summary>
    public static FakePulsarReceiver Delivering(string topic, params IMessage<ReadOnlySequence<byte>>[] messages)
    {
        var receiver = new FakePulsarReceiver(topic);
        receiver._Deliver(messages);
        return receiver;
    }

    private void _Deliver(IMessage<ReadOnlySequence<byte>>[] messages)
    {
        OnReceive = async token =>
        {
            var index = Interlocked.Increment(ref _receiveCount) - 1;
            if (index < messages.Length)
            {
                return messages[index];
            }

            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };
    }

    public ValueTask<IMessage<ReadOnlySequence<byte>>> ReceiveAsync(CancellationToken cancellationToken) =>
        new(OnReceive(cancellationToken));

    public ValueTask AcknowledgeAsync(MessageId messageId, CancellationToken cancellationToken)
    {
        Calls.Enqueue("ack");
        Acknowledged.Enqueue(messageId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RedeliverAsync(IEnumerable<MessageId> messageIds, CancellationToken cancellationToken)
    {
        Calls.Enqueue("redeliver");
        foreach (var id in messageIds)
        {
            Redelivered.Enqueue(id);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<PulsarLinkState> WaitForLinkChangeAsync(
        PulsarLinkState current,
        CancellationToken cancellationToken
    )
    {
        while (_link == current)
        {
            _link = await _links.Reader.ReadAsync(cancellationToken);
        }

        return _link;
    }

    public ValueTask DisposeAsync()
    {
        Calls.Enqueue("dispose");
        return OnDispose();
    }
}

/// <summary>Creates consumer clients whose receivers the test supplies.</summary>
internal static class PulsarConsumerClients
{
    public const string ServiceUrl = "pulsar://localhost:6650";

    public static PulsarConsumerClient Create(
        ConsumerClientRequest request,
        Func<string, IPulsarReceiver>? receivers = null,
        PulsarMessagingOptions? options = null,
        PulsarConsumerConfig? config = null,
        IServiceProvider? serviceProvider = null,
        TimeProvider? timeProvider = null,
        IConnectionFactory? connection = null,
        Func<IReadOnlyDictionary<string, string?>, byte[], TransportMessage>? transportMessageFactory = null
    )
    {
        return new PulsarConsumerClient(
            Options.Create(options ?? new PulsarMessagingOptions { ServiceUrl = ServiceUrl }),
            connection ?? Substitute.For<IConnectionFactory>(),
            request,
            config,
            timeProvider,
            transportMessageFactory,
            serviceProvider,
            receivers ?? (static topic => new FakePulsarReceiver(topic))
        );
    }

    /// <summary>Creates a client and subscribes it to <paramref name="name"/> through <paramref name="receiver"/>.</summary>
    public static async Task<PulsarConsumerClient> SubscribedAsync(
        ConsumerClientRequest request,
        FakePulsarReceiver receiver,
        CancellationToken cancellationToken,
        string name = "orders.created",
        PulsarMessagingOptions? options = null,
        PulsarConsumerConfig? config = null,
        IServiceProvider? serviceProvider = null,
        TimeProvider? timeProvider = null,
        IConnectionFactory? connection = null
    )
    {
        var client = Create(request, _ => receiver, options, config, serviceProvider, timeProvider, connection);

        await client.SubscribeAsync([name], cancellationToken);

        return client;
    }
}
