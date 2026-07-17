// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework.Contexts.Processors;

/// <summary>
/// Per-<c>SaveChanges</c> scratchpad threaded through the processor chain. Holds the active
/// <see cref="DbContext"/> and the message-emitter snapshots collected this round.
/// </summary>
[PublicAPI]
public sealed class HeadlessSaveEntryContext(DbContext dbContext, string? tenantId = null)
{
    // Constructor parameters are positional — documented via the type summary and property docs.
    /// <summary>Active EF Core context for this save.</summary>
    public DbContext DbContext { get; } = dbContext;

    /// <summary>Ambient tenant identifier captured for this save round.</summary>
    public string? TenantId { get; } = tenantId;

    /// <summary>
    /// Domain-event emitters collected during processing. Internal plumbing between the terminal
    /// collector processor and the save pipeline; the pipeline publishes the events within the active
    /// transaction and then clears them via <see cref="ClearEmitterMessages"/>.
    /// </summary>
    internal List<EmitterDomainEvents> DomainEventEmitters { get; } = [];

    /// <summary>
    /// Integration-event emitters collected during processing. Internal plumbing consumed by the save
    /// pipeline to enqueue events into the outbox for post-commit delivery.
    /// </summary>
    internal List<EmitterIntegrationEvents> IntegrationEventEmitters { get; } = [];

    /// <summary>Clears the event buffers on each captured emitter once they have been dispatched.</summary>
    internal void ClearEmitterMessages()
    {
        foreach (var emitter in IntegrationEventEmitters)
        {
            emitter.Emitter.ClearIntegrationEvents();
        }

        foreach (var emitter in DomainEventEmitters)
        {
            emitter.Emitter.ClearDomainEvents();
        }
    }
}
