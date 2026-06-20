// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Contexts.Processors;

/// <summary>
/// Stage in the ordered processor chain run by <see cref="Runtime.HeadlessSaveChangesPipeline"/> against every
/// tracked entry before <c>SaveChanges</c> dispatches to the database.
/// </summary>
/// <remarks>
/// Processors execute in registration order, once per tracked entity per <c>SaveChanges</c> call. Stamp
/// audit fields, generate IDs, or enqueue messages on the <see cref="HeadlessSaveEntryContext"/> — do
/// not call <c>context.DbContext.SaveChanges</c> from within a processor.
/// </remarks>
[PublicAPI]
public interface IHeadlessSaveEntryProcessor
{
    void Process(EntityEntry entry, HeadlessSaveEntryContext context);
}
