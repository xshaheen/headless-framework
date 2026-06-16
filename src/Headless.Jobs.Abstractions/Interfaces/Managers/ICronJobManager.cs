// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ICronJobManager<TCronJob>
    where TCronJob : CronJobEntity
{
    /// <summary>Enqueues a cron job.</summary>
    /// <remarks>
    /// When a relational commit coordinator is active, the row is written inside the caller's ambient transaction and
    /// cron-cache invalidation / scheduler-restart / notify are deferred to post-commit — a succeeded
    /// <see cref="JobResult{TCronJob}" /> then means the row committed with the transaction, not that the side effects
    /// ran. With no coordinator (or a coordinated scope exposing no relational capability) the row is inserted directly
    /// and the side effects run in-band.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A relational coordinator is active but its transaction is dead/completed, or the configured persistence provider
    /// cannot write inside it (a mis-wire). Validation and direct-path persistence errors are surfaced through the
    /// returned <see cref="JobResult{TCronJob}" /> instead of thrown.
    /// </exception>
    Task<JobResult<TCronJob>> AddAsync(TCronJob entity, CancellationToken cancellationToken = default);
    Task<JobResult<TCronJob>> UpdateAsync(TCronJob cronJob, CancellationToken cancellationToken = default);
    Task<JobResult<TCronJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations

    /// <inheritdoc cref="AddAsync" />
    Task<JobResult<List<TCronJob>>> AddBatchAsync(
        List<TCronJob> entities,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<List<TCronJob>>> UpdateBatchAsync(
        List<TCronJob> cronJobs,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<TCronJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
