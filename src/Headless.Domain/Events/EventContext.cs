// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>A business payload with immutable identity and lineage captured at emission. Treat the payload as immutable after capture.</summary>
/// <typeparam name="TPayload">Business payload type.</typeparam>
/// <param name="Payload">The business fact.</param>
/// <param name="EventId">Identity of this occurrence; preserve it when forwarding or retrying.</param>
/// <param name="CorrelationId">Identity of the root business operation, independent of tracing.</param>
/// <param name="CausationId">Identity of the immediate cause, or null for a root.</param>
/// <param name="TenantId">Tenant captured at emission, or null for system scope.</param>
[PublicAPI]
public sealed record EventContext<TPayload>(
    TPayload Payload,
    string EventId,
    string CorrelationId,
    string? CausationId = null,
    string? TenantId = null
)
    where TPayload : class
{
    /// <summary>The non-null business payload; never mutate it after capture.</summary>
    public TPayload Payload { get; } = Argument.IsNotNull(Payload);

    /// <summary>Validated occurrence identity.</summary>
    public string EventId { get; } = Argument.IsNotNullOrWhiteSpace(EventId);

    /// <summary>Validated root correlation.</summary>
    public string CorrelationId { get; } = Argument.IsNotNullOrWhiteSpace(CorrelationId);

    /// <summary>Immediate cause, when known.</summary>
    public string? CausationId { get; } = CausationId is null ? null : Argument.IsNotNullOrWhiteSpace(CausationId);

    /// <summary>Tenant, or null for system scope.</summary>
    public string? TenantId { get; } = TenantId is null ? null : Argument.IsNotNullOrWhiteSpace(TenantId);
}

/// <summary>Captures payloads with new event identity and the active emission lineage.</summary>
[PublicAPI]
public static class EventContext
{
    /// <summary>Captures a new event, inheriting the active emission scope or becoming a correlation root.</summary>
    public static EventContext<TPayload> Capture<TPayload>(TPayload payload)
        where TPayload : class
    {
        var eventId = Guid.CreateVersion7().ToString();
        var parent = EventEmissionScope.Current;
        return new(payload, eventId, parent?.CorrelationId ?? eventId, parent?.CausationId, parent?.TenantId);
    }
}
