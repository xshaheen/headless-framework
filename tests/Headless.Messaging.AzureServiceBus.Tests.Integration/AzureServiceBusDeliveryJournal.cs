// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace Tests;

/// <summary>
/// What Service Bus reported about each delivery a conformance session's headers builder saw and each dead-letter copy
/// the drain took, per queue, so a dead-letter count that is off says which copies the broker held.
/// </summary>
/// <remarks>
/// Two dead-letter copies with different sequence numbers mean two messages were enqueued, which a retried send that had
/// already landed would do. One sequence number taken twice means the broker handed a completed copy back. Each queue a
/// session reads is that session's alone for the run, so the queue name keys its entries.
/// </remarks>
internal static class AzureServiceBusDeliveryJournal
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _Entries = new(
        StringComparer.Ordinal
    );

    public static void RecordDelivery(string queueName, ServiceBusReceivedMessage message)
    {
        _Record(queueName, "delivered", message);
    }

    public static void RecordDeadLetterTaken(string queueName, ServiceBusReceivedMessage message)
    {
        _Record(queueName, "dead-letter taken", message);
    }

    public static string Describe(string queueName)
    {
        return _Entries.TryGetValue(queueName, out var entries) && !entries.IsEmpty
            ? $"Service Bus reported, in order, for queue '{queueName}':{Environment.NewLine}{string.Join(Environment.NewLine, entries)}"
            : $"Service Bus reported nothing for queue '{queueName}'";
    }

    private static void _Record(string queueName, string what, ServiceBusReceivedMessage message)
    {
        // wall-clock: a diagnostic timestamp read beside the broker's own EnqueuedTime and LockedUntil; nothing waits on it.
        var seenAt = DateTimeOffset.UtcNow;
        _Entries
            .GetOrAdd(queueName, static _ => new ConcurrentQueue<string>())
            .Enqueue(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {seenAt:HH:mm:ss.fff} {what}: MessageId={message.MessageId} SequenceNumber={message.SequenceNumber} EnqueuedTime={message.EnqueuedTime:HH:mm:ss.fff} DeliveryCount={message.DeliveryCount} LockedUntil={message.LockedUntil:HH:mm:ss.fff} DeadLetterReason={message.DeadLetterReason}"
                )
            );
    }
}
