// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root. It's primary key may not be "Id" or it may have a composite primary key
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
[PublicAPI]
public interface IAggregateRoot : IEntity;

/// <summary>Base class for aggregate roots that may emit domain (in-process) and integration (distributed) events.</summary>
[PublicAPI]
public abstract class AggregateRoot : Entity, IAggregateRoot, IIntegrationEventEmitter, IDomainEventEmitter
{
    private List<IDomainEvent>? _domainEvents;
    private List<IIntegrationEvent>? _integrationEvents;

    public void AddIntegrationEvent(IIntegrationEvent integrationEvent) =>
        (_integrationEvents ??= []).Add(integrationEvent);

    public void ClearIntegrationEvents() => _integrationEvents?.Clear();

    public IReadOnlyList<IIntegrationEvent> GetIntegrationEvents() => _integrationEvents ?? [];

    public void AddDomainEvent(IDomainEvent domainEvent) => (_domainEvents ??= []).Add(domainEvent);

    public IReadOnlyList<IDomainEvent> GetDomainEvents() => _domainEvents ?? [];

    public void ClearDomainEvents() => _domainEvents?.Clear();
}
