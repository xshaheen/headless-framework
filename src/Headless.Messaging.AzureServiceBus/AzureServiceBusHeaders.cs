// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus;

/// <summary>Framework-defined header names used with the Azure Service Bus transport.</summary>
public static class AzureServiceBusHeaders
{
    /// <summary>
    /// Header carrying the Service Bus session identifier. Required on every message when
    /// <see cref="AzureServiceBusMessagingOptions.EnableSessions"/> is <see langword="true"/>; ignored
    /// otherwise. Messages that omit this header when sessions are enabled are rejected by the broker.
    /// </summary>
    public const string SessionId = "headless-session-id";

    /// <summary>
    /// Header carrying the Service Bus <c>PartitionKey</c>. The broker uses this value to route
    /// messages to the same partition, guaranteeing ordering within a partition. Values must be
    /// 128 characters or fewer; longer values throw <c>InvalidOperationException</c> at publish time.
    /// </summary>
    public const string PartitionKey = "headless-asb-partition-key";

    /// <summary>
    /// Header carrying a <see cref="DateTimeOffset"/> that specifies when the message becomes
    /// visible in the queue or topic (scheduled delivery). The broker holds the message until
    /// the specified time before making it available to subscribers.
    /// See <see href="https://docs.microsoft.com/en-us/azure/service-bus-messaging/message-sequencing#scheduled-messages"/>.
    /// </summary>
    public const string ScheduledEnqueueTimeUtc = "headless-scheduled-enqueue-time-utc";
}
