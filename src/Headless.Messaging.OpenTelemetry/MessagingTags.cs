// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.OpenTelemetry;

/// <summary>
/// Well-known messaging tag names emitted by the framework on activity spans.
/// </summary>
[PublicAPI]
public static class MessagingTags
{
    /// <summary>Number of persisted retry pickups for a subscriber invocation.</summary>
    public const string RetryCount = "headless.messaging.retry_count";

    /// <summary>Tenant identifier extracted from the wire header.</summary>
    public const string TenantId = "headless.messaging.tenant_id";
}
