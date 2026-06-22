// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Defines a handler for a specific domain event type. Register implementations with the DI container;
/// <c>ILocalEventBus</c> resolves and invokes them when the matching event is published.
/// </summary>
/// <remarks>
/// Apply <c>DomainEventHandlerOrderAttribute</c> to control the invocation order when multiple handlers
/// are registered for the same event type.
/// </remarks>
/// <typeparam name="TEvent">The concrete domain event type this handler processes.</typeparam>
[PublicAPI]
public interface IDomainEventHandler<in TEvent>
    where TEvent : class, IDomainEvent
{
    /// <summary>Handler handles the event by implementing this method.</summary>
    /// <param name="domainEvent">Event data</param>
    /// <param name="cancellationToken">Abort token</param>
    ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
