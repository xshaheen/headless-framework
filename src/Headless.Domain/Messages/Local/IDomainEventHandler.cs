// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

[PublicAPI]
public interface IDomainEventHandler<in TEvent>
    where TEvent : class, IDomainEvent
{
    /// <summary>Handler handles the event by implementing this method.</summary>
    /// <param name="domainEvent">Event data</param>
    /// <param name="cancellationToken">Abort token</param>
    ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
