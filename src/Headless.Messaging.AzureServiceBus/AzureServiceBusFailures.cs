// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging.Transport;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>Tells a Service Bus call that could not reach the namespace from one the service refused.</summary>
internal static class AzureServiceBusFailures
{
    /// <summary>
    /// Whether the call failed to communicate with the namespace or timed out. A missing or disabled entity, a message
    /// over the size limit, an exceeded quota, or throttling is the service answering, so it returns
    /// <see langword="false" />.
    /// </summary>
    public static bool IsUnreachable(Exception exception) =>
        exception
            is ServiceBusException
            {
                Reason: ServiceBusFailureReason.ServiceCommunicationProblem or ServiceBusFailureReason.ServiceTimeout,
            }
        || BrokerFailures.IsConnectivity(exception);
}
