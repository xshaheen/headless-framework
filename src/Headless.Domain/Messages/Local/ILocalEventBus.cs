// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Publishes in-process domain events to their <see cref="IDomainEventHandler{TEvent}"/> handlers.
/// Dispatched synchronously within the active unit of work / transaction.
/// </summary>
[PublicAPI]
public interface ILocalEventBus
{
    /// <summary>Publishes a domain event to its handlers, blocking the calling thread until they all complete.</summary>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <remarks>
    /// Dispatches the async handlers synchronously (sync-over-async). Do not call from a thread that carries a
    /// synchronization context (classic ASP.NET, Blazor Server, WPF), or it may deadlock; prefer
    /// <c>PublishAsync</c> in asynchronous code.
    /// </remarks>
    void Publish<T>(T domainEvent)
        where T : class, IDomainEvent;

    /// <summary>Publishes a domain event resolved by its runtime type, blocking the calling thread until all handlers complete.</summary>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <remarks>Sync-over-async; see <c>Publish</c> for the synchronization-context deadlock caveat.</remarks>
    void Publish(IDomainEvent domainEvent);

    /// <summary>Publishes a domain event to its handlers asynchronously.</summary>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <param name="cancellationToken">Token to cancel the dispatch operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all handlers have finished.</returns>
    ValueTask PublishAsync<T>(T domainEvent, CancellationToken cancellationToken = default)
        where T : class, IDomainEvent;

    /// <summary>Publishes a domain event resolved by its runtime type asynchronously.</summary>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <param name="cancellationToken">Token to cancel the dispatch operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all handlers have finished.</returns>
    ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
