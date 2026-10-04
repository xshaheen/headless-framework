// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root entity.
/// </summary>
/// <remarks>
/// Aggregate roots may have single, composite, or non-default key schemes. Repositories typically constrain operations to aggregate roots.
/// </remarks>
[PublicAPI]
public interface IAggregateRoot : IEntity;

/// <summary>Provides a base implementation for aggregate roots that emit domain and integration events.</summary>
/// <remarks>
/// Event mutation methods are <see langword="protected"/> so aggregates raise events through their own domain methods.
/// Read and clear members remain accessible to infrastructure that collects, dispatches, and clears buffers during a unit of work.
/// </remarks>
[PublicAPI]
public abstract class AggregateRoot : Entity, IAggregateRoot, IIntegrationEventEmitter, IDomainEventEmitter
{
    private EventBuffer? _domainEvents;
    private EventBuffer? _integrationEvents;

    /// <summary>Appends an integration event to the pending outbox for this aggregate.</summary>
    /// <param name="integrationEvent">The integration event to enqueue.</param>
    protected void AddIntegrationEvent(object integrationEvent)
    {
        AddIntegrationEvent(EventContext.Capture(integrationEvent));
    }

    /// <summary>Discards all pending integration events without dispatching them.</summary>
    public void ClearIntegrationEvents()
    {
        _integrationEvents?.Clear();
    }

    /// <summary>Returns the current list of pending integration events.</summary>
    /// <returns>A read-only snapshot of enqueued integration events, or an empty list when none are present.</returns>
    public IReadOnlyList<EventContext<object>> GetIntegrationEvents()
    {
        return _integrationEvents?.Snapshot() ?? [];
    }

    /// <summary>Appends a domain event to be dispatched within the current unit of work.</summary>
    /// <param name="domainEvent">The domain event to enqueue.</param>
    protected void AddDomainEvent(object domainEvent)
    {
        AddDomainEvent(EventContext.Capture(domainEvent));
    }

    /// <summary>Returns the current list of pending domain events.</summary>
    /// <returns>A read-only snapshot of enqueued domain events, or an empty list when none are present.</returns>
    public IReadOnlyList<EventContext<object>> GetDomainEvents()
    {
        return _domainEvents?.Snapshot() ?? [];
    }

    /// <summary>Discards all pending domain events without dispatching them.</summary>
    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }

    /// <inheritdoc/>
    void IIntegrationEventEmitter.AddIntegrationEvent(object integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }

    /// <inheritdoc/>
    void IDomainEventEmitter.AddDomainEvent(object domainEvent)
    {
        AddDomainEvent(domainEvent);
    }

    /// <summary>Preserves an occurrence already captured at its emission boundary.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="context">The captured event context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    protected void AddDomainEvent<TPayload>(EventContext<TPayload> context)
        where TPayload : class
    {
        Argument.IsNotNull(context);
        (_domainEvents ??= new()).Add(context);
    }

    /// <summary>Removes only occurrences included in a successfully saved batch.</summary>
    /// <param name="occurrences">The list of occurrences to remove from the buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="occurrences"/> is <see langword="null"/>.</exception>
    public void ClearDomainEvents(IReadOnlyList<EventContext<object>> occurrences)
    {
        Argument.IsNotNull(occurrences);
        _domainEvents?.Clear(occurrences);
    }

    /// <inheritdoc/>
    void IDomainEventEmitter.AddDomainEvent<TPayload>(EventContext<TPayload> occurrence) => AddDomainEvent(occurrence);

    /// <summary>Preserves an occurrence already captured at its emission boundary.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="context">The captured event context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    protected void AddIntegrationEvent<TPayload>(EventContext<TPayload> context)
        where TPayload : class
    {
        Argument.IsNotNull(context);
        (_integrationEvents ??= new()).Add(context);
    }

    /// <summary>Removes only occurrences included in a successfully saved batch.</summary>
    /// <param name="occurrences">The list of occurrences to remove from the buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="occurrences"/> is <see langword="null"/>.</exception>
    public void ClearIntegrationEvents(IReadOnlyList<EventContext<object>> occurrences)
    {
        Argument.IsNotNull(occurrences);
        _integrationEvents?.Clear(occurrences);
    }

    /// <inheritdoc/>
    void IIntegrationEventEmitter.AddIntegrationEvent<TPayload>(EventContext<TPayload> occurrence) =>
        AddIntegrationEvent(occurrence);
}
