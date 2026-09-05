// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Immutable identity and business lineage captured when a fact is raised; independent of tracing and delivery.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="CorrelationId">Identity of the root business operation.</param>
/// <param name="CausationId">Identity of the immediate cause, or null for a root.</param>
/// <param name="TenantId">Tenant captured at emission, or null for system scope.</param>
[PublicAPI]
public sealed record EventOccurrenceContext(string EventId, string CorrelationId, string? CausationId, string? TenantId)
{
    /// <summary>Validated occurrence identity.</summary>
    public string EventId { get; } = Argument.IsNotNullOrWhiteSpace(EventId);

    /// <summary>Validated root correlation.</summary>
    public string CorrelationId { get; } = Argument.IsNotNullOrWhiteSpace(CorrelationId);

    /// <summary>Immediate cause, when known.</summary>
    public string? CausationId { get; } = CausationId is null ? null : Argument.IsNotNullOrWhiteSpace(CausationId);

    /// <summary>Tenant, or null for system scope.</summary>
    public string? TenantId { get; } = TenantId is null ? null : Argument.IsNotNullOrWhiteSpace(TenantId);
}

/// <summary>A business payload paired with its immutable emission snapshot. Treat the payload as immutable after raising it.</summary>
/// <typeparam name="TPayload">Business payload type.</typeparam>
/// <param name="Payload">The business fact.</param>
/// <param name="Context">Captured occurrence identity and lineage; preserve this when forwarding an existing occurrence.</param>
[PublicAPI]
public sealed record EventOccurrence<TPayload>(TPayload Payload, EventOccurrenceContext Context)
    where TPayload : class
{
    /// <summary>The non-null business payload; never mutate it after capture.</summary>
    public TPayload Payload { get; } = Argument.IsNotNull(Payload);

    /// <summary>Immutable occurrence identity and lineage.</summary>
    public EventOccurrenceContext Context { get; } = Argument.IsNotNull(Context);
}

/// <summary>Captures business payloads with new occurrence identity and the active emission lineage.</summary>
[PublicAPI]
public static class EventOccurrence
{
    /// <summary>Captures a new occurrence, inheriting the active emission scope or becoming a correlation root.</summary>
    public static EventOccurrence<TPayload> Capture<TPayload>(TPayload payload)
        where TPayload : class
    {
        var eventId = Guid.CreateVersion7().ToString();
        var parent = EventEmissionScope.Current;
        return new(payload, new(eventId, parent?.CorrelationId ?? eventId, parent?.CausationId, parent?.TenantId));
    }
}
