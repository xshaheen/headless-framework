// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

/// <summary>
/// Application-facing manager for cron job definitions: create, update, and delete a definition plus their
/// batch variants. The scheduler materializes recurring occurrences from these definitions. Resolved from DI
/// as <c>ICronJobManager&lt;TCronJob&gt;</c>, where <typeparamref name="TCronJob"/> is the application's
/// concrete cron job entity. Writes route through the scope's active unit of work when one is present (see
/// <c>AddAsync</c>) and otherwise persist directly via the configured <c>IJobPersistenceProvider</c>.
/// </summary>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
[PublicAPI]
public interface ICronJobManager<TCronJob>
    where TCronJob : CronJobEntity
{
    /// <summary>Enqueues a cron job and returns the persisted entity.</summary>
    /// <remarks>
    /// When a joinable, compatible unit of work is active in this scope, the row is written inside its transaction
    /// and cron-cache invalidation / scheduler-restart / notify are deferred to post-commit; the returned entity
    /// then means the row was enlisted into the transaction (it commits with it), not that the side effects ran.
    /// With no active unit of work (or one with no joinable relational resource) the row is inserted directly and
    /// the side effects run in-band. Any failure throws — so an enlisted caller's unit of work rolls back rather
    /// than completing without the job row. (Update/Delete keep returning <see cref="JobResult{TCronJob}" />; only
    /// the Add path throws.)
    /// </remarks>
    /// <exception cref="Headless.Jobs.Exceptions.JobValidatorException">
    /// The job failed validation (unknown function or unparseable cron expression).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Headless.UnitOfWork.TransactionEnlistment.Required" /> is resolved with no active unit of work,
    /// the active unit of work's resource is dead or belongs to another database, or the configured persistence
    /// provider cannot write inside it (a mis-wire).
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
