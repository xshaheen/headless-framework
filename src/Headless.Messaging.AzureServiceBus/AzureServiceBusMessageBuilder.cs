// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Headless.Messaging.AzureServiceBus;

internal static class AzureServiceBusMessageBuilder
{
    public static ServiceBusMessage Build(TransportMessage transportMessage, bool enableSessions)
    {
        var message = new ServiceBusMessage(transportMessage.Body.ToArray())
        {
            MessageId = transportMessage.GetId(),
            Subject = transportMessage.GetName(),
            CorrelationId = transportMessage.GetCorrelationId(),
        };

        if (enableSessions)
        {
            transportMessage.Headers.TryGetValue(AzureServiceBusHeaders.SessionId, out var sessionId);
            if (string.IsNullOrEmpty(sessionId))
            {
                transportMessage.Headers.TryGetValue(AzureServiceBusHeaders.PartitionKey, out var fallbackPartitionKey);
                message.SessionId = string.IsNullOrWhiteSpace(fallbackPartitionKey)
                    ? transportMessage.GetId()
                    : fallbackPartitionKey;
            }
            else
            {
                message.SessionId = sessionId;
            }
        }

        if (
            transportMessage.Headers.TryGetValue(AzureServiceBusHeaders.PartitionKey, out var partitionKey)
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
                AzureServiceBusHeaders.ScheduledEnqueueTimeUtc,
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
