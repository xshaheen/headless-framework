// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;

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
public readonly struct MessagingEnrichmentContext : IEquatable<MessagingEnrichmentContext>
{
    /// <summary>Which span type is being enriched.</summary>
    public MessagingEventKind Kind { get; init; }

    /// <summary>The message ID, or <see langword="null"/> when not available for this span kind.</summary>
    public string? MessageId { get; init; }

    /// <summary>The topic / operation name.</summary>
    public string? MessageName { get; init; }

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
    /// The dictionary is read-only; enrichers must not mutate it.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Headers { get; init; }

    /// <inheritdoc />
    public bool Equals(MessagingEnrichmentContext other) =>
        Kind == other.Kind
        && MessageId == other.MessageId
        && MessageName == other.MessageName
        && TenantId == other.TenantId
        && CorrelationId == other.CorrelationId
        && RetryCount == other.RetryCount
        && ReferenceEquals(Headers, other.Headers);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MessagingEnrichmentContext other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Kind, MessageId, MessageName, TenantId, CorrelationId, RetryCount, RuntimeHelpers.GetHashCode(Headers));

    public static bool operator ==(MessagingEnrichmentContext left, MessagingEnrichmentContext right) =>
        left.Equals(right);

    public static bool operator !=(MessagingEnrichmentContext left, MessagingEnrichmentContext right) =>
        !left.Equals(right);
}
