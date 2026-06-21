// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ICronJobManager<TCronJob>
    where TCronJob : CronJobEntity
{
    /// <summary>Enqueues a cron job and returns the persisted entity.</summary>
    /// <remarks>
    /// When a relational commit coordinator is active, the row is written inside the caller's ambient transaction and
    /// cron-cache invalidation / scheduler-restart / notify are deferred to post-commit; the returned entity then means
    /// the row was enlisted into the transaction (it commits with it), not that the side effects ran. With no coordinator
    /// (or a coordinated scope exposing no relational capability) the row is inserted directly and the side effects run
    /// in-band. Any failure throws — so a coordinated caller's transaction rolls back rather than committing without the
    /// job row. (Update/Delete keep returning <see cref="JobResult{TCronJob}" />; only the Add path throws.)
    /// </remarks>
    /// <exception cref="Headless.Jobs.Exceptions.JobValidatorException">
    /// The job failed validation (unknown function or unparseable cron expression).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A relational coordinator is active but its transaction is dead/completed, or the configured persistence provider
    /// cannot write inside it (a mis-wire).
    /// </exception>
    Task<TCronJob> AddAsync(TCronJob entity, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing cron job definition and returns the result.</summary>
    Task<JobResult<TCronJob>> UpdateAsync(TCronJob cronJob, CancellationToken cancellationToken = default);

    /// <summary>Deletes the cron job definition with the given identifier and returns the result.</summary>
    Task<JobResult<TCronJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations

    /// <inheritdoc cref="AddAsync" />
    /// <exception cref="Headless.Jobs.Exceptions.JobValidatorException">
    /// One or more jobs failed validation; <see cref="Headless.Jobs.Exceptions.JobValidatorException.Errors" /> lists each.
    /// </exception>
    Task<List<TCronJob>> AddBatchAsync(List<TCronJob> entities, CancellationToken cancellationToken = default);

    /// <summary>Updates a batch of cron job definitions and returns the aggregated result.</summary>
    Task<JobResult<List<TCronJob>>> UpdateBatchAsync(
        List<TCronJob> cronJobs,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes the cron job definitions with the given identifiers and returns the aggregated result.</summary>
    Task<JobResult<TCronJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
