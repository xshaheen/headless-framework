// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Well-known messaging tag names emitted by the framework on activity spans.
/// </summary>
[PublicAPI]
public static class MessagingTags
{
    /// <summary>Messaging delivery intent: <c>bus</c> for broadcast, <c>queue</c> for point-to-point.</summary>
    public const string Intent = "headless.messaging.intent";

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
}
