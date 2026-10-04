// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Exposes the integration event outbox of a domain object, allowing infrastructure layers to collect
/// and dispatch pending integration events after a transaction commits.
/// </summary>
[PublicAPI]
public interface IIntegrationEventEmitter
{
    /// <summary>Appends an integration event to the pending outbox.</summary>
    /// <remarks>
    /// Intended for infrastructure enqueue operations across assemblies when deriving from an aggregate is not feasible.
    /// </remarks>
    /// <param name="integrationEvent">The integration event to enqueue.</param>
    void AddIntegrationEvent(object integrationEvent);

    /// <summary>Discards all pending integration events without dispatching them.</summary>
    void ClearIntegrationEvents();

    /// <summary>Returns the current list of pending integration events.</summary>
    /// <returns>A read-only snapshot of enqueued integration events, or an empty list when none are present.</returns>
    IReadOnlyList<EventContext<object>> GetIntegrationEvents();

    /// <summary>Preserves an occurrence already captured by infrastructure.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="context">The event context to append.</param>
    void AddIntegrationEvent<TPayload>(EventContext<TPayload> context)
        where TPayload : class;

    /// <summary>Removes only occurrences included in the captured batch, leaving later occurrences pending.</summary>
    /// <param name="occurrences">The list of event occurrences to remove.</param>
    void ClearIntegrationEvents(IReadOnlyList<EventContext<object>> occurrences);
}
