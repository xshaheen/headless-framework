// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
    /// <param name="payload">The event payload to capture and add.</param>
    public void Add(object payload) => Add(EventContext.Capture(payload));

    /// <summary>Appends an existing occurrence without changing its identity, lineage, tenant, or payload.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="context">The event context to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
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
    /// <returns>A read-only snapshot of buffered event occurrences.</returns>
    public IReadOnlyList<EventContext<object>> Snapshot() => _events?.ToArray() ?? [];

    /// <summary>Discards all pending occurrences without dispatching them.</summary>
    public void Clear() => _events?.Clear();

    /// <summary>Removes occurrences with the saved batch's event IDs, retaining occurrences raised after collection.</summary>
    /// <param name="occurrences">The list of event occurrences to remove from the buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="occurrences"/> is <see langword="null"/>.</exception>
    public void Clear(IReadOnlyList<EventContext<object>> occurrences)
    {
        Argument.IsNotNull(occurrences);
        var ids = occurrences.Select(occurrence => occurrence.EventId).ToHashSet(StringComparer.Ordinal);
        _events?.RemoveAll(occurrence => ids.Contains(occurrence.EventId));
    }
}
