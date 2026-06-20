// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.OpenTelemetry;

/// <summary>
/// Contextual data passed to each <see cref="IActivityTagEnricher"/> when a messaging span starts.
/// All fields reflect values already set on the <see cref="Activity"/> so enrichers can read or
/// supplement them without re-parsing headers themselves.
/// </summary>
/// <remarks>
/// Headers are the raw wire headers. On the consume side they are untrusted external data;
/// enrichers that write header values to sensitive sinks must sanitize at their own call site.
/// </remarks>
[PublicAPI]
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "Transient parameter passed `in` to enrichers; never compared or stored."
)]
public readonly struct MessagingEnrichmentContext
{
#pragma warning disable IDE0032 // Use auto property — backing field is intentionally nullable so `default(T).Headers` returns a non-null empty dictionary.
    private readonly IReadOnlyDictionary<string, string?>? _headers;
#pragma warning restore IDE0032

    /// <summary>Which span type is being enriched.</summary>
    public MessagingEventKind Kind { get; init; }

    /// <summary>The message ID, or <see langword="null"/> when not available for this span kind.</summary>
    public string? MessageId { get; init; }

    /// <summary>The message name / operation name.</summary>
    public string? MessageName { get; init; }

    /// <summary>
    /// Delivery intent associated with the span. <see cref="IntentType.Bus"/> means broadcast
    /// pub/sub; <see cref="IntentType.Queue"/> means point-to-point work-queue delivery.
    /// </summary>
    public IntentType IntentType { get; init; }

    /// <summary>
    /// Tenant ID extracted from the <c>headless-tenant-id</c> wire header, or
    /// <see langword="null"/> when the header is absent.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Correlation ID from the wire header, or <see langword="null"/> when absent.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Number of persisted retry pickups. Zero for <see cref="MessagingEventKind.Persist"/>,
    /// <see cref="MessagingEventKind.Publish"/>, and <see cref="MessagingEventKind.Consume"/> spans,
    /// and for the first delivery of a <see cref="MessagingEventKind.SubscriberInvoke"/> span.
    /// Inline retries do not advance this counter.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Raw wire headers. On the consume side these are untrusted external data.
    /// The dictionary is read-only; enrichers must not mutate it. Defaults to an empty
    /// dictionary when the struct is created via <c>default(MessagingEnrichmentContext)</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Headers
    {
        get => _headers ?? FrozenDictionary<string, string?>.Empty;
        init => _headers = value;
    }
}
