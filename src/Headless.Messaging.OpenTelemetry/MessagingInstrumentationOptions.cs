// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.OpenTelemetry;

/// <summary>Options for <c>AddMessagingInstrumentation</c>.</summary>
[PublicAPI]
public sealed class MessagingInstrumentationOptions
{
    /// <summary>
    /// When <see langword="true"/>, OpenTelemetry metrics (message sizes, latencies, etc.) are
    /// collected in addition to traces. Default: <see langword="false"/>.
    /// </summary>
    public bool EnableMetrics { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the built-in <c>headless.messaging.tenant_id</c> tag enricher
    /// is not registered. Use this in shared-backend scenarios where tagging tenant IDs on spans
    /// would expose cross-tenant data in the same trace store. Default: <see langword="false"/>.
    /// </summary>
    public bool SuppressTenantIdTag { get; set; }

    /// <summary>
    /// Custom enrichers appended after the built-in enrichers. Enrichers are called in insertion
    /// order for every span type.
    /// </summary>
    public IList<IActivityTagEnricher> Enrichers { get; } = [];
}
