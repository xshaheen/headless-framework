// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Domain;

/// <summary>Represents a business payload with immutable identity and lineage captured at emission. Treat the payload as immutable after capture.</summary>
/// <typeparam name="TPayload">The business payload type.</typeparam>
/// <param name="Payload">The business payload.</param>
/// <param name="EventId">The unique identifier of this occurrence.</param>
/// <param name="CorrelationId">The identifier of the root business operation, independent of distributed tracing.</param>
/// <param name="CausationId">The identifier of the immediate cause, or <see langword="null"/> for a root event.</param>
/// <param name="TenantId">The tenant captured at emission, or <see langword="null"/> for system scope.</param>
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
    /// <summary>Gets the business payload.</summary>
    public TPayload Payload { get; } = Argument.IsNotNull(Payload);

    /// <summary>Gets the occurrence identifier.</summary>
    public string EventId { get; } = Argument.IsNotNullOrWhiteSpace(EventId);

    /// <summary>Gets the root correlation identifier.</summary>
    public string CorrelationId { get; } = Argument.IsNotNullOrWhiteSpace(CorrelationId);

    /// <summary>Gets the immediate causal identifier, when known.</summary>
    public string? CausationId { get; } = CausationId is null ? null : Argument.IsNotNullOrWhiteSpace(CausationId);

    /// <summary>Gets the tenant identifier, or <see langword="null"/> for system scope.</summary>
    public string? TenantId { get; } = TenantId is null ? null : Argument.IsNotNullOrWhiteSpace(TenantId);
}

/// <summary>Provides factory methods for capturing event payloads with emission lineage.</summary>
[PublicAPI]
public static class EventContext
{
    /// <summary>Captures a new event context, inheriting the active emission scope or initializing a correlation root.</summary>
    /// <typeparam name="TPayload">The business payload type.</typeparam>
    /// <param name="payload">The event payload to capture.</param>
    /// <returns>The captured <see cref="EventContext{TPayload}"/> instance.</returns>
    public static EventContext<TPayload> Capture<TPayload>(TPayload payload)
        where TPayload : class
    {
        var eventId = Guid.CreateVersion7().ToString();
        var parent = EventEmissionScope.Current;
        return new(payload, eventId, parent?.CorrelationId ?? eventId, parent?.CausationId, parent?.TenantId);
    }
}
