// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.AzureServiceBus;

internal static class AzureServiceBusRoutingAffinity
{
    internal static readonly MessagingRoutingAffinityMapping Mapping = new(
        AzureServiceBusMessagingHeaders.SessionId,
        maximumKeyLength: 128,
        matchingHeaders: [AzureServiceBusMessagingHeaders.PartitionKey]
    );
}
