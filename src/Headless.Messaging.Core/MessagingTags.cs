// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Well-known messaging tag names emitted by the framework on activity spans.
/// </summary>
[PublicAPI]
public static class MessagingTags
{
    /// <summary>Messaging delivery lane: <c>bus</c> for broadcast, <c>queue</c> for point-to-point.</summary>
    public const string Lane = "headless.messaging.lane";

    /// <summary>Delivery mode requested by the caller: <c>auto</c>, <c>durable</c>, or <c>transport_direct</c>.</summary>
    public const string RequestedDeliveryMode = "headless.messaging.delivery.requested";

    /// <summary>Delivery mode resolved by the framework: <c>durable</c> or <c>transport_direct</c>.</summary>
    public const string ResolvedDeliveryMode = "headless.messaging.delivery.resolved";

    /// <summary>Finite delivery outcome diagnostic; currently <c>ambiguous</c> when transport acceptance is unknown.</summary>
    public const string DeliveryOutcome = "headless.messaging.delivery.outcome";

    /// <summary>Messaging destination kind aligned with OpenTelemetry messaging conventions.</summary>
    public const string DestinationKind = "messaging.destination.kind";

    /// <summary>Number of persisted retry pickups for a subscriber invocation.</summary>
    public const string RetryCount = "headless.messaging.retry_count";

    /// <summary>Tenant identifier extracted from the wire header.</summary>
    public const string TenantId = "headless.messaging.tenant_id";

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
    public const string InboxTier = "headless.messaging.inbox.tier";

    /// <summary>Configured storage provider name.</summary>
    public const string InboxProvider = "headless.messaging.inbox.provider";
}
