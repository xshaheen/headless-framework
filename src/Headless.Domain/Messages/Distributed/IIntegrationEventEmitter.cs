// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Exposes the integration-event outbox of a domain object, allowing infrastructure layers to collect
/// and dispatch pending integration events after a transaction commits.
/// </summary>
[PublicAPI]
public interface IIntegrationEventEmitter
{
    /// <summary>Appends an integration event to the pending outbox.</summary>
    /// <remarks>
    /// This is the infrastructure enqueue contract. Domain code raises events through an aggregate's own
    /// behavior methods (the base <c>AggregateRoot.AddIntegrationEvent</c> is <see langword="protected"/>);
    /// this member stays public so infrastructure that cannot derive from the aggregate can enqueue across
    /// assemblies.
    /// </remarks>
    /// <param name="integrationEvent">The integration event to enqueue.</param>
    void AddIntegrationEvent(IIntegrationEvent integrationEvent);

    /// <summary>Discards all pending integration events without dispatching them.</summary>
    void ClearIntegrationEvents();

    /// <summary>Returns the current list of pending integration events.</summary>
    /// <returns>A read-only snapshot of enqueued integration events; empty when none have been added.</returns>
    IReadOnlyList<IIntegrationEvent> GetIntegrationEvents();
}
