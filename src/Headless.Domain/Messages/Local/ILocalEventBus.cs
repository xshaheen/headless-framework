// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Publishes in-process domain events to their <see cref="IDomainEventHandler{TEvent}"/> handlers.
/// Dispatched inline within the active unit of work / transaction.
/// </summary>
/// <remarks>
/// The contract is async-only by design: a public synchronous publish would invite sync-over-async
/// dispatch of the async handlers, which can deadlock on threads that carry a synchronization context
/// (classic ASP.NET, Blazor Server, WPF). Infrastructure that must publish from a synchronous code path
/// owns and contains that bridge itself.
/// </remarks>
[PublicAPI]
public interface ILocalEventBus
{
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
