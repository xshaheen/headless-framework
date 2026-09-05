// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root. It's primary key may not be "Id" or it may have a composite primary key
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
[PublicAPI]
public interface IAggregateRoot : IEntity;

/// <summary>Base class for aggregate roots that may emit domain (in-process) and integration (distributed) events.</summary>
/// <remarks>
/// The event mutators are <see langword="protected"/>: an aggregate raises its own events from its behavior
/// methods, so callers cannot reach into another aggregate's event buffer. The read/clear members and the
/// <see cref="IDomainEventEmitter"/> / <see cref="IIntegrationEventEmitter"/> contracts stay accessible to the
/// infrastructure that collects, dispatches, and clears the buffers during a unit of work.
/// </remarks>
[PublicAPI]
public abstract class AggregateRoot : Entity, IAggregateRoot, IIntegrationEventEmitter, IDomainEventEmitter
{
    private List<EventOccurrence<IDomainEvent>>? _domainEvents;
    private List<EventOccurrence<IIntegrationEvent>>? _integrationEvents;

    /// <summary>Appends an integration event to the pending outbox for this aggregate.</summary>
    /// <remarks>Call from the aggregate's own behavior methods to raise integration events.</remarks>
    /// <param name="integrationEvent">The integration event to enqueue.</param>
    protected void AddIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(EventOccurrence.Capture<IIntegrationEvent>(integrationEvent));
    }

    /// <summary>Discards all pending integration events without dispatching them.</summary>
    public void ClearIntegrationEvents()
    {
        _integrationEvents?.Clear();
    }

    /// <summary>Returns the current list of pending integration events.</summary>
    /// <returns>A read-only snapshot of enqueued integration events; empty when none have been added.</returns>
    public IReadOnlyList<EventOccurrence<IIntegrationEvent>> GetIntegrationEvents()
    {
        return _integrationEvents?.ToArray() ?? [];
    }

    /// <summary>Appends a domain event to be dispatched within the current unit of work.</summary>
    /// <remarks>Call from the aggregate's own behavior methods to raise domain events.</remarks>
    /// <param name="domainEvent">The domain event to enqueue.</param>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(EventOccurrence.Capture<IDomainEvent>(domainEvent));
    }

    /// <summary>Returns the current list of pending domain events.</summary>
    /// <returns>A read-only snapshot of enqueued domain events; empty when none have been added.</returns>
    public IReadOnlyList<EventOccurrence<IDomainEvent>> GetDomainEvents()
    {
        return _domainEvents?.ToArray() ?? [];
    }

    /// <summary>Discards all pending domain events without dispatching them.</summary>
    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }

    /// <inheritdoc/>
    void IIntegrationEventEmitter.AddIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }

    /// <inheritdoc/>
    void IDomainEventEmitter.AddDomainEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }

    /// <summary>Preserves an occurrence already captured at its emission boundary.</summary>
    protected void AddDomainEvent(EventOccurrence<IDomainEvent> occurrence)
    {
        Argument.IsNotNull(occurrence);
        (_domainEvents ??= []).Add(occurrence);
    }

    /// <summary>Removes only occurrences included in a successfully saved batch.</summary>
    public void ClearDomainEvents(IReadOnlyList<EventOccurrence<IDomainEvent>> occurrences)
    {
        Argument.IsNotNull(occurrences);
        var ids = occurrences.Select(occurrence => occurrence.Context.EventId).ToHashSet(StringComparer.Ordinal);
        _domainEvents?.RemoveAll(occurrence => ids.Contains(occurrence.Context.EventId));
    }

    /// <inheritdoc/>
    void IDomainEventEmitter.AddDomainEvent(EventOccurrence<IDomainEvent> occurrence) => AddDomainEvent(occurrence);

    /// <summary>Preserves an occurrence already captured at its emission boundary.</summary>
    protected void AddIntegrationEvent(EventOccurrence<IIntegrationEvent> occurrence)
    {
        Argument.IsNotNull(occurrence);
        (_integrationEvents ??= []).Add(occurrence);
    }

    /// <summary>Removes only occurrences included in a successfully saved batch.</summary>
    public void ClearIntegrationEvents(IReadOnlyList<EventOccurrence<IIntegrationEvent>> occurrences)
    {
        Argument.IsNotNull(occurrences);
        var ids = occurrences.Select(occurrence => occurrence.Context.EventId).ToHashSet(StringComparer.Ordinal);
        _integrationEvents?.RemoveAll(occurrence => ids.Contains(occurrence.Context.EventId));
    }

    /// <inheritdoc/>
    void IIntegrationEventEmitter.AddIntegrationEvent(EventOccurrence<IIntegrationEvent> occurrence) =>
        AddIntegrationEvent(occurrence);
}
