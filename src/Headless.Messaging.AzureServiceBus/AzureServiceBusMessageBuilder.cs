// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging.Messages;

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
            message.SessionId = string.IsNullOrEmpty(sessionId) ? transportMessage.GetId() : sessionId;
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
