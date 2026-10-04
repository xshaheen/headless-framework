// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Exposes the in-process domain event queue of a domain object, allowing infrastructure layers to collect
/// and dispatch pending domain events within the active unit of work.
/// </summary>
[PublicAPI]
public interface IDomainEventEmitter
{
    /// <summary>Appends a domain event to be dispatched within the current unit of work.</summary>
    /// <remarks>
    /// Intended for infrastructure enqueue operations across assemblies when deriving from an aggregate is not feasible.
    /// </remarks>
    /// <param name="domainEvent">The domain event to enqueue.</param>
    void AddDomainEvent(object domainEvent);

    /// <summary>Discards all pending domain events without dispatching them.</summary>
    void ClearDomainEvents();

    /// <summary>Returns the current list of pending domain events.</summary>
    /// <returns>A read-only snapshot of enqueued domain events, or an empty list when none are present.</returns>
    IReadOnlyList<EventContext<object>> GetDomainEvents();

    /// <summary>Preserves an occurrence already captured by infrastructure.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="context">The event context to append.</param>
    void AddDomainEvent<TPayload>(EventContext<TPayload> context)
        where TPayload : class;

    /// <summary>Removes only occurrences included in the captured batch, leaving later occurrences pending.</summary>
    /// <param name="occurrences">The list of event occurrences to remove.</param>
    void ClearDomainEvents(IReadOnlyList<EventContext<object>> occurrences);
}
