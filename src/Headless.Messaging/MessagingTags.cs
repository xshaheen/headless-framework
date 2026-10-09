// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Well-known messaging tag names emitted by the framework on activity spans.
/// </summary>
[PublicAPI]
public static class MessagingTags
{
    /// <summary>Messaging delivery lane: <c>bus</c> for broadcast, <c>queue</c> for point-to-point.</summary>
    public const string Lane = "headless.messaging.lane";

    /// <summary>Delivery mode requested by the caller: <c>durable</c> or <c>direct</c>.</summary>
    public const string RequestedDeliveryMode = "headless.messaging.delivery.requested";

    /// <summary>Delivery mode resolved by the framework: <c>durable</c> or <c>direct</c>.</summary>
    public const string ResolvedDeliveryMode = "headless.messaging.delivery.resolved";

    /// <summary>Finite delivery outcome diagnostic; currently <c>ambiguous</c> when transport acceptance is unknown.</summary>
    public const string DeliveryOutcome = "headless.messaging.delivery.outcome";

    /// <summary>
    /// Destination kind of the message's lane: <c>topic</c> for the bus lane, <c>queue</c> for the queue lane. The
    /// OpenTelemetry messaging conventions no longer define a destination-kind attribute, so it is a framework one.
    /// </summary>
    public const string DestinationKind = "headless.messaging.destination.kind";

    /// <summary>Number of persisted retry pickups for a subscriber invocation.</summary>
    public const string RetryCount = "headless.messaging.retry_count";

    /// <summary>Elapsed time (ms) for persisting an outbound message to the store.</summary>
    public const string PersistenceDurationMs = "headless.messaging.persistence.duration_ms";

    /// <summary>Elapsed time (ms) for sending a message through the transport.</summary>
    public const string SendDurationMs = "headless.messaging.send.duration_ms";

    /// <summary>Elapsed time (ms) for receiving a message from the transport.</summary>
    public const string ReceiveDurationMs = "headless.messaging.receive.duration_ms";

    /// <summary>Elapsed time (ms) for invoking a subscriber handler.</summary>
    public const string InvokeDurationMs = "headless.messaging.invoke.duration_ms";

    /// <summary>Registered stable consumer identity used by bounded inbox metrics.</summary>
    public const string InboxConsumer = "headless.messaging.inbox.consumer";

    /// <summary>Finite inbox lifecycle outcome.</summary>
    public const string InboxOutcome = "headless.messaging.inbox.outcome";

    /// <summary>Configured inbox guarantee tier.</summary>
    public const string InboxGuarantee = "headless.messaging.inbox.guarantee";

    /// <summary>Configured storage provider name.</summary>
    public const string InboxProvider = "headless.messaging.inbox.provider";
}
