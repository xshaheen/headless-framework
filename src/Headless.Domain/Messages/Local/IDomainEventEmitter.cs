// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Exposes the in-process domain-event queue of a domain object, allowing infrastructure layers to collect
/// and dispatch pending domain events within the active unit of work.
/// </summary>
[PublicAPI]
public interface IDomainEventEmitter
{
    /// <summary>Appends a domain event to be dispatched within the current unit of work.</summary>
    /// <param name="domainEvent">The domain event to enqueue.</param>
    void AddDomainEvent(IDomainEvent domainEvent);

    /// <summary>Discards all pending domain events without dispatching them.</summary>
    void ClearDomainEvents();

    /// <summary>Returns the current list of pending domain events.</summary>
    /// <returns>A read-only snapshot of enqueued domain events; empty when none have been added.</returns>
    IReadOnlyList<IDomainEvent> GetDomainEvents();
}
