// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Extensions;

namespace Headless.Messaging.Pulsar;

/// <summary>Receives the messages of one subscribed topic.</summary>
/// <remarks>
/// A consumer client owns one receiver per topic rather than one multi-topic consumer: DotPulsar gives a message from a
/// producer's batch an id without its topic, and a multi-topic consumer routes acknowledgements by that topic.
/// </remarks>
internal interface IPulsarReceiver : IAsyncDisposable
{
    /// <summary>Gets the topic as it was subscribed.</summary>
    string Topic { get; }

    /// <summary>
    /// Gets whether messages are acknowledged and redelivered. A reader of a non-durable subscription keeps no
    /// acknowledgements: its subscription ends with the reader.
    /// </summary>
    bool Settles { get; }

    /// <summary>Gets whether the receiver ended and never delivers again.</summary>
    bool HasEnded { get; }

    /// <summary>Receives the next message.</summary>
    ValueTask<IMessage<ReadOnlySequence<byte>>> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Acknowledges a received message.</summary>
    ValueTask AcknowledgeAsync(MessageId messageId, CancellationToken cancellationToken);

    /// <summary>Asks the broker to redeliver received messages.</summary>
    ValueTask RedeliverAsync(IEnumerable<MessageId> messageIds, CancellationToken cancellationToken);

    /// <summary>Waits until the link state differs from <paramref name="current"/>, and returns the new state.</summary>
    ValueTask<PulsarLinkState> WaitForLinkChangeAsync(PulsarLinkState current, CancellationToken cancellationToken);
}

/// <summary>Receives through a durable consumer, which acknowledges and redelivers messages.</summary>
internal sealed class PulsarConsumerReceiver(IConsumer<ReadOnlySequence<byte>> consumer, string topic) : IPulsarReceiver
{
    // Read and written only by the one caller that watches the link at a time: subscription, then the monitor.
    private ConsumerState _state = ConsumerState.Disconnected;

    public string Topic { get; } = topic;

    public bool Settles => true;

    public bool HasEnded => consumer.State.IsFinalState();

    public ValueTask<IMessage<ReadOnlySequence<byte>>> ReceiveAsync(CancellationToken cancellationToken)
    {
        return consumer.Receive(cancellationToken);
    }

    public ValueTask AcknowledgeAsync(MessageId messageId, CancellationToken cancellationToken)
    {
        return consumer.Acknowledge(messageId, cancellationToken);
    }

    public ValueTask RedeliverAsync(IEnumerable<MessageId> messageIds, CancellationToken cancellationToken)
    {
        return consumer.RedeliverUnacknowledgedMessages(messageIds, cancellationToken);
    }

    public async ValueTask<PulsarLinkState> WaitForLinkChangeAsync(
        PulsarLinkState current,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var link = Map(_state);
            if (link != current)
            {
                return link;
            }

            _state = await consumer.State.OnStateChangeFrom(_state, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        return consumer.DisposeAsync();
    }

    internal static PulsarLinkState Map(ConsumerState state) =>
        state switch
        {
            // Inactive is a connected standby on a failover subscription, which this transport never opens.
            ConsumerState.Active or ConsumerState.Inactive => PulsarLinkState.Connected,
            ConsumerState.Disconnected or ConsumerState.PartiallyConnected => PulsarLinkState.Disconnected,
            _ => PulsarLinkState.Ended,
        };
}

/// <summary>
/// Receives through a reader, whose subscription is exclusive and non-durable: the broker keeps no cursor for it once
/// the reader disconnects, so nothing is left behind after a crash. DotPulsar opens a consumer only as durable.
/// </summary>
internal sealed class PulsarReaderReceiver(IReader<ReadOnlySequence<byte>> reader, string topic) : IPulsarReceiver
{
    private ReaderState _state = ReaderState.Disconnected;

    public string Topic { get; } = topic;

    public bool Settles => false;

    public bool HasEnded => reader.State.IsFinalState();

    public ValueTask<IMessage<ReadOnlySequence<byte>>> ReceiveAsync(CancellationToken cancellationToken)
    {
        return reader.Receive(cancellationToken);
    }

    public ValueTask AcknowledgeAsync(MessageId messageId, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask RedeliverAsync(IEnumerable<MessageId> messageIds, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask<PulsarLinkState> WaitForLinkChangeAsync(
        PulsarLinkState current,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var link = Map(_state);
            if (link != current)
            {
                return link;
            }

            _state = await reader.State.OnStateChangeFrom(_state, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        return reader.DisposeAsync();
    }

    internal static PulsarLinkState Map(ReaderState state) =>
        state switch
        {
            ReaderState.Connected => PulsarLinkState.Connected,
            ReaderState.Disconnected or ReaderState.PartiallyConnected => PulsarLinkState.Disconnected,
            _ => PulsarLinkState.Ended,
        };
}

/// <summary>A received message and the receiver that settles it.</summary>
internal sealed class PulsarDelivery(IPulsarReceiver receiver, IMessage<ReadOnlySequence<byte>> message)
{
    public IPulsarReceiver Receiver { get; } = receiver;

    public IMessage<ReadOnlySequence<byte>> Message { get; } = message;

    /// <summary>
    /// Gets the id that settles the message. DotPulsar leaves the topic off the id of a message from a producer's batch,
    /// and a partitioned consumer routes an acknowledgement by that topic, so the id is completed with the partition
    /// topic name DotPulsar subscribed.
    /// </summary>
    public MessageId MessageId { get; } = _WithTopic(message.MessageId, receiver.Topic);

    /// <summary>Gets the topic the message was delivered on, partition suffix included.</summary>
    public string DeliveryTopic => MessageId.Topic;

    private static MessageId _WithTopic(MessageId id, string subscribedTopic)
    {
        if (id.Topic.Length > 0)
        {
            return id;
        }

        var topic = id.Partition >= 0 ? PulsarTopicName.Partition(subscribedTopic, id.Partition) : subscribedTopic;

        return new MessageId(id.LedgerId, id.EntryId, id.Partition, id.BatchIndex, topic);
    }
}
