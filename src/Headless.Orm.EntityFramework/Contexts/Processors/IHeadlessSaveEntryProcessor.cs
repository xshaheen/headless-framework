// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Processors;

/// <summary>
/// Stage in the ordered processor chain run by <see cref="HeadlessSaveChangesPipeline"/> against every
/// tracked entry before <c>SaveChanges</c> dispatches to the database.
/// </summary>
/// <remarks>
/// Processors execute in registration order, once per tracked entity per <c>SaveChanges</c> call. Stamp
/// audit fields, generate IDs, or enqueue messages on the <see cref="HeadlessSaveEntryContext"/> — do
/// not call <c>context.DbContext.SaveChanges</c> from within a processor.
/// </remarks>
public interface IHeadlessSaveEntryProcessor
{
    void Process(EntityEntry entry, HeadlessSaveEntryContext context);
}

/// <summary>
/// Per-<c>SaveChanges</c> scratchpad threaded through the processor chain. Holds the active
/// <see cref="DbContext"/> and the message-emitter snapshots collected this round.
/// </summary>
public sealed class HeadlessSaveEntryContext(DbContext dbContext)
{
    /// <summary>Active EF Core context for this save.</summary>
    public DbContext DbContext { get; } = dbContext;

    /// <summary>
    /// Local message emitters collected during processing.
    /// </summary>
    /// <remarks>
    /// Processors append to this list during <see cref="IHeadlessSaveEntryProcessor.Process"/>; framework
    /// terminal processors consume the collected contents to publish messages within the active
    /// transaction. Replacing entries from a custom processor is permitted but signals an unusual
    /// contract.
    /// </remarks>
    public List<EmitterLocalMessages> LocalEmitters { get; } = [];

    /// <summary>
    /// Distributed message emitters collected during processing.
    /// </summary>
    /// <remarks>
    /// Processors append to this list during <see cref="IHeadlessSaveEntryProcessor.Process"/>; framework
    /// terminal processors consume the collected contents to enqueue messages for post-commit delivery.
    /// Replacing entries from a custom processor is permitted but signals an unusual contract.
    /// </remarks>
    public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

    /// <summary>Clears the message buffers on each captured emitter once they have been dispatched.</summary>
    public void ClearEmitterMessages()
    {
        foreach (var emitter in DistributedEmitters)
        {
            emitter.Emitter.ClearDistributedMessages();
        }

        foreach (var emitter in LocalEmitters)
        {
            emitter.Emitter.ClearLocalMessages();
        }
    }
}
