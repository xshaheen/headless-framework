// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging.Internal;

namespace Headless.Messaging.AzureServiceBus;

internal static class AzureServiceBusMessageBuilder
{
    /// <summary>Maps a framework transport message onto a Service Bus message.</summary>
    /// <param name="transportMessage">The message to map.</param>
    /// <param name="enableSessions">Whether the destination entity requires a session id.</param>
    /// <param name="now">
    /// The sender's current time on the app clock, used to turn a delivery deadline into the remaining
    /// <see cref="ServiceBusMessage.TimeToLive"/>. Defaults to the system clock.
    /// </param>
    public static ServiceBusMessage Build(
        TransportMessage transportMessage,
        bool enableSessions,
        DateTimeOffset? now = null
    )
    {
        if (!enableSessions)
        {
            MessagingRoutingAffinityMapping.RejectUnsupported(transportMessage, "Azure Service Bus without sessions");
        }

        var affinityKey = AzureServiceBusRoutingAffinity.Mapping.ResolveKey(transportMessage);
        // BinaryData.FromBytes wraps the ReadOnlyMemory without copying; ServiceBusMessage(byte[]) would force a
        // full payload copy via Body.ToArray() on every publish.
        var message = new ServiceBusMessage(BinaryData.FromBytes(transportMessage.Body))
        {
            MessageId = transportMessage.Id,
            Subject = transportMessage.Name,
            CorrelationId = transportMessage.GetCorrelationId(),
        };

        if (enableSessions)
        {
            var sessionId = affinityKey;
            if (string.IsNullOrEmpty(sessionId))
            {
                transportMessage.Headers.TryGetValue(
                    AzureServiceBusMessagingHeaders.PartitionKey,
                    out var fallbackPartitionKey
                );
                message.SessionId = string.IsNullOrWhiteSpace(fallbackPartitionKey)
                    ? transportMessage.Id
                    : fallbackPartitionKey;
            }
            else
            {
                message.SessionId = sessionId;
            }
        }

        if (
            transportMessage.Headers.TryGetValue(AzureServiceBusMessagingHeaders.PartitionKey, out var partitionKey)
            && !string.IsNullOrWhiteSpace(partitionKey)
        )
        {
            if (
                enableSessions
                && !string.IsNullOrWhiteSpace(message.SessionId)
                && !string.Equals(message.SessionId, partitionKey, StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Azure Service Bus requires PartitionKey to match SessionId when sessions are enabled."
                );
            }

            message.PartitionKey = partitionKey;
        }

        if (
            transportMessage.Headers.TryGetValue(
                AzureServiceBusMessagingHeaders.ScheduledEnqueueTimeUtc,
                out var scheduledEnqueueTimeUtcString
            )
            && DateTimeOffset.TryParse(
                scheduledEnqueueTimeUtcString,
                CultureInfo.InvariantCulture,
                out var scheduledEnqueueTimeUtc
            )
        )
        {
            message.ScheduledEnqueueTime = scheduledEnqueueTimeUtc;
        }

        // A delivery deadline the broker can enforce itself: with a per-message TTL an undelivered message expires
        // inside the entity instead of lingering past the instant nobody may consume it at. The relay drops an
        // already-expired message before it ever reaches this send, so the remaining time is positive.
        if (DeliverByEnvelope.GetDeadline(transportMessage.Headers) is { } deliverBy)
        {
            var remaining = deliverBy - (now ?? TimeProvider.System.GetUtcNow());
            if (remaining > TimeSpan.Zero)
            {
                message.TimeToLive = remaining;
            }
        }

        foreach (var header in transportMessage.Headers)
        {
            message.ApplicationProperties.Add(header.Key, header.Value);
        }

        return message;
    }
}
