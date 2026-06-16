// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>
{
    /// <summary>Enqueues a time job.</summary>
    /// <remarks>
    /// When a relational commit coordinator is active, the row is written inside the caller's ambient transaction and
    /// dispatch / scheduler-restart / notify are deferred to post-commit — a succeeded <see cref="JobResult{TTimeJob}" />
    /// then means the row committed with the transaction, not that dispatch ran. With no coordinator (or a coordinated
    /// scope exposing no relational capability) the row is inserted directly and the side effects run in-band.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A relational coordinator is active but its transaction is dead/completed, or the configured persistence provider
    /// cannot write inside it (a mis-wire). Validation and direct-path persistence errors are surfaced through the
    /// returned <see cref="JobResult{TTimeJob}" /> instead of thrown.
    /// </exception>
    Task<JobResult<TTimeJob>> AddAsync(TTimeJob entity, CancellationToken cancellationToken = default);
    Task<JobResult<TTimeJob>> UpdateAsync(TTimeJob timeJob, CancellationToken cancellationToken = default);
    Task<JobResult<TTimeJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations

    /// <inheritdoc cref="AddAsync" />
    Task<JobResult<List<TTimeJob>>> AddBatchAsync(
        List<TTimeJob> entities,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<List<TTimeJob>>> UpdateBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<TTimeJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
