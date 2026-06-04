// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Publishes in-process domain events to their <see cref="IDomainEventHandler{TEvent}"/> handlers.
/// Dispatched synchronously within the active unit of work / transaction.
/// </summary>
public interface ILocalEventBus
{
    void Publish<T>(T domainEvent)
        where T : class, IDomainEvent;

    /// <summary>Publishes a domain event resolved by its runtime type.</summary>
    void Publish(IDomainEvent domainEvent);

    ValueTask PublishAsync<T>(T domainEvent, CancellationToken cancellationToken = default)
        where T : class, IDomainEvent;

    /// <summary>Publishes a domain event resolved by its runtime type.</summary>
    ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
