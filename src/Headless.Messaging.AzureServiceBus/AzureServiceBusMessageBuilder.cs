// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Headless.Messaging.AzureServiceBus;

internal static class AzureServiceBusMessageBuilder
{
    public static ServiceBusMessage Build(TransportMessage transportMessage, bool enableSessions)
    {
        if (!enableSessions)
        {
            Configuration.MessagingRoutingAffinityMapping.RejectUnsupported(
                transportMessage,
                "Azure Service Bus without sessions"
            );
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

        foreach (var header in transportMessage.Headers)
        {
            message.ApplicationProperties.Add(header.Key, header.Value);
        }

        return message;
    }
}
