// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Stores pending event occurrences for entities that use composition or inherit <see cref="AggregateRoot"/>.</summary>
/// <remarks>
/// Use a separate instance for each emitter and event kind. This in-memory buffer is not thread-safe
/// and does not dispatch or persist events. Payloads are retained by reference and must remain immutable after emission.
/// </remarks>
[PublicAPI]
public sealed class EventBuffer
{
    private List<EventContext<object>>? _events;

    /// <summary>Captures a new occurrence using the current emission scope, even when the payload was raised before.</summary>
    public void Add(object payload) => Add(EventContext.Capture(payload));

    /// <summary>Appends an existing occurrence without changing its identity, lineage, tenant, or payload.</summary>
    public void Add<TPayload>(EventContext<TPayload> context)
        where TPayload : class
    {
        Argument.IsNotNull(context);
        (_events ??= []).Add(
            context as EventContext<object>
                ?? new(context.Payload, context.EventId, context.CorrelationId, context.CausationId, context.TenantId)
        );
    }

    /// <summary>Returns an insertion-ordered snapshot whose membership is independent of subsequent buffer changes.</summary>
    public IReadOnlyList<EventContext<object>> Snapshot() => _events?.ToArray() ?? [];

    /// <summary>Discards all pending occurrences without dispatching them.</summary>
    public void Clear() => _events?.Clear();

    /// <summary>Removes occurrences with the saved batch's event IDs, retaining occurrences raised after collection.</summary>
    public void Clear(IReadOnlyList<EventContext<object>> occurrences)
    {
        Argument.IsNotNull(occurrences);
        var ids = occurrences.Select(occurrence => occurrence.EventId).ToHashSet(StringComparer.Ordinal);
        _events?.RemoveAll(occurrence => ids.Contains(occurrence.EventId));
    }
}
