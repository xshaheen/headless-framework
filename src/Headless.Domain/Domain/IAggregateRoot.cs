// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    private List<IDomainEvent>? _domainEvents;
    private List<IIntegrationEvent>? _integrationEvents;

    /// <summary>Appends an integration event to the pending outbox for this aggregate.</summary>
    /// <remarks>Call from the aggregate's own behavior methods to raise integration events.</remarks>
    /// <param name="integrationEvent">The integration event to enqueue.</param>
    protected void AddIntegrationEvent(IIntegrationEvent integrationEvent) =>
        (_integrationEvents ??= []).Add(integrationEvent);

    /// <summary>Discards all pending integration events without dispatching them.</summary>
    public void ClearIntegrationEvents() => _integrationEvents?.Clear();

    /// <summary>Returns the current list of pending integration events.</summary>
    /// <returns>A read-only snapshot of enqueued integration events; empty when none have been added.</returns>
    public IReadOnlyList<IIntegrationEvent> GetIntegrationEvents() => _integrationEvents ?? [];

    /// <summary>Appends a domain event to be dispatched within the current unit of work.</summary>
    /// <remarks>Call from the aggregate's own behavior methods to raise domain events.</remarks>
    /// <param name="domainEvent">The domain event to enqueue.</param>
    protected void AddDomainEvent(IDomainEvent domainEvent) => (_domainEvents ??= []).Add(domainEvent);

    /// <summary>Returns the current list of pending domain events.</summary>
    /// <returns>A read-only snapshot of enqueued domain events; empty when none have been added.</returns>
    public IReadOnlyList<IDomainEvent> GetDomainEvents() => _domainEvents ?? [];

    /// <summary>Discards all pending domain events without dispatching them.</summary>
    public void ClearDomainEvents() => _domainEvents?.Clear();

    /// <inheritdoc/>
    void IIntegrationEventEmitter.AddIntegrationEvent(IIntegrationEvent integrationEvent) =>
        AddIntegrationEvent(integrationEvent);

    /// <inheritdoc/>
    void IDomainEventEmitter.AddDomainEvent(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
}
