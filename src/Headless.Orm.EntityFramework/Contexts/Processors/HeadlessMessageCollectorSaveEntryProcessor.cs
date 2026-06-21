// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Contexts.Processors;

/// <summary>
/// Terminal save-entry processor that harvests pending integration and domain events from entities
/// into the <see cref="HeadlessSaveEntryContext"/> event buffers for the save pipeline to dispatch.
/// </summary>
/// <remarks>
/// This processor runs last (terminal stage) so all preceding processors — including
/// <see cref="HeadlessLocalEventSaveEntryProcessor"/> — can enqueue events before collection.
/// The save pipeline publishes domain events within the active transaction and writes integration
/// events to the outbox for post-commit delivery. Event buffers are cleared after each successful
/// save via <c>HeadlessSaveEntryContext.ClearEmitterMessages</c>.
/// </remarks>
[PublicAPI]
public sealed class HeadlessMessageCollectorSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    /// <summary>
    /// Collects pending integration and domain events from the entity entry into the save context
    /// event buffers. Detached entries are skipped.
    /// </summary>
    /// <param name="entry">The tracked entity entry to collect events from.</param>
    /// <param name="context">The per-save scratchpad that receives the collected emitter snapshots.</param>
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        if (entry.State == EntityState.Detached)
        {
            return;
        }

        if (entry.Entity is IIntegrationEventEmitter integrationEmitter)
        {
            var events = integrationEmitter.GetIntegrationEvents();

            if (events.Count > 0)
            {
                context.IntegrationEventEmitters.Add(new(integrationEmitter, events));
            }
        }

        if (entry.Entity is IDomainEventEmitter domainEmitter)
        {
            var events = domainEmitter.GetDomainEvents();

            if (events.Count > 0)
            {
                context.DomainEventEmitters.Add(new(domainEmitter, events));
            }
        }
    }
}
