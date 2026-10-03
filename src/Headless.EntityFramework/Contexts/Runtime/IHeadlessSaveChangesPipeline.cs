// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework.Contexts.Processors;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Coordinates the per-<c>SaveChanges</c> work of a <see cref="HeadlessDbContext"/>: runs the ordered
/// chain of <see cref="IHeadlessSaveEntryProcessor"/> stages, captures audit entries, dispatches local
/// messages within the active transaction, persists the entity batch, and enqueues distributed messages
/// post-success before committing.
/// </summary>
/// <remarks>
/// Implementations own the transaction boundary. When an explicit transaction is already on the context
/// the pipeline reuses it; otherwise it opens a transaction wrapped by the execution strategy, enlists it in a
/// unit of work for the save's duration, and completes that unit after the commit so audit and message-emitter
/// work commits atomically with the entity batch and after-commit work drains once the outcome is durable.
/// <para>
/// A completed local drain is not repeated by subsequent persistence retries within an owned save.
/// Handler failures can repeat handler entry; there are no per-handler checkpoints. Local handlers must
/// remain replay-safe and avoid rollback-unsafe external effects. Once a participant prevents retry
/// (such as a job write enlisted in the unit of work), any failure propagates and requires a fresh context and
/// graph. Caller-owned successful saves clear only their saved batches before physical commit; a known outer
/// rollback requires a fresh context and graph. Outbox storage can enlist atomically; delivery and external
/// effects remain at-least-once.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHeadlessSaveChangesPipeline
{
    /// <summary>
    /// Asynchronously executes the full Headless save pipeline: runs processors, captures audit entries,
    /// dispatches domain events, persists the entity batch, enqueues integration events, and commits.
    /// </summary>
    /// <param name="context">The EF Core context being saved.</param>
    /// <param name="baseSaveChangesAsync">The base <c>SaveChangesAsync</c> delegate from the context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept all changes on success.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Synchronously executes the full Headless save pipeline.
    /// </summary>
    /// <param name="context">The EF Core context being saved.</param>
    /// <param name="baseSaveChanges">The base <c>SaveChanges</c> delegate from the context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept all changes on success.</param>
    /// <returns>The number of state entries written to the database.</returns>
    int SaveChanges(DbContext context, Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess);
}
