// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Pairs an <see cref="IDomainEventEmitter"/> with the deduplicated snapshot of domain events collected
/// from it during the current <c>SaveChanges</c>. Internal bookkeeping for collection and post-save
/// clearing — not part of the public dispatch contract.
/// </summary>
internal sealed record EmitterDomainEvents(IDomainEventEmitter Emitter, IReadOnlyList<IDomainEvent> Events)
{
    /// <summary>
    /// Returns a snapshot of the constructor argument deduplicated by <see cref="IDomainEvent.UniqueId"/>,
    /// isolating the dispatch from subsequent mutation of the emitter's source list.
    /// </summary>
    public IReadOnlyList<IDomainEvent> Events { get; } = EmitterEventsSnapshot.Snapshot(Events, static e => e.UniqueId);
}

/// <summary>
/// Pairs an <see cref="IIntegrationEventEmitter"/> with the deduplicated snapshot of integration events
/// collected from it during the current <c>SaveChanges</c>. Internal bookkeeping — not public contract.
/// </summary>
internal sealed record EmitterIntegrationEvents(
    IIntegrationEventEmitter Emitter,
    IReadOnlyList<IIntegrationEvent> Events
)
{
    /// <summary>
    /// Returns a snapshot of the constructor argument deduplicated by
    /// <see cref="IIntegrationEvent.UniqueId"/>, isolated from subsequent emitter mutation.
    /// </summary>
    public IReadOnlyList<IIntegrationEvent> Events { get; } =
        EmitterEventsSnapshot.Snapshot(Events, static e => e.UniqueId);
}

internal static class EmitterEventsSnapshot
{
    // Snapshots the caller's list so subsequent mutation on the emitter doesn't leak into the pipeline,
    // and deduplicates by the supplied UniqueId accessor.
    public static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> events, Func<T, string> uniqueId)
    {
        return events.Count switch
        {
            0 => [],
            1 => [events[0]],
            _ => events.DistinctBy(uniqueId, StringComparer.Ordinal).ToArray(),
        };
    }
}
