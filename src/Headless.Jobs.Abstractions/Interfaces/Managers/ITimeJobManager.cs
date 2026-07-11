// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

/// <summary>
/// Application-facing manager for one-shot (time) jobs: enqueue, update, and delete a single job plus their
/// batch variants. Resolved from DI as <c>ITimeJobManager&lt;TTimeJob&gt;</c>, where
/// <typeparamref name="TTimeJob"/> is the application's concrete time job entity. The manager routes writes
/// through the active commit coordinator when one is present (see <c>AddAsync</c>) and otherwise persists
/// directly via the configured <c>IJobPersistenceProvider</c>.
/// </summary>
/// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
[PublicAPI]
public interface ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>
{
    /// <summary>Enqueues a time job and returns the persisted entity.</summary>
    /// <remarks>
    /// When a relational commit coordinator is active, the row is written inside the caller's ambient transaction and
    /// dispatch / scheduler-restart / notify are deferred to post-commit; the returned entity then means the row was
    /// enlisted into the transaction (it commits with it), not that dispatch ran. With no coordinator (or a coordinated
    /// scope exposing no relational capability) the row is inserted directly and the side effects run in-band. Any
    /// failure throws — so a coordinated caller's transaction rolls back rather than committing without the job row.
    /// (Update/Delete keep returning <see cref="JobResult{TTimeJob}" />; only the transaction-enlisting Add path throws.)
    /// </remarks>
    /// <exception cref="Headless.Jobs.Exceptions.JobValidatorException">The job failed validation (unknown function).</exception>
    /// <exception cref="InvalidOperationException">
    /// A relational coordinator is active but its transaction is dead/completed, or the configured persistence provider
    /// cannot write inside it (a mis-wire).
    /// </exception>
    Task<TTimeJob> AddAsync(TTimeJob entity, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing time job and returns the result.</summary>
    Task<JobResult<TTimeJob>> UpdateAsync(TTimeJob timeJob, CancellationToken cancellationToken = default);

    /// <summary>Deletes the time job with the given identifier and returns the result.</summary>
    Task<JobResult<TTimeJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations

    /// <inheritdoc cref="AddAsync" />
    /// <exception cref="Headless.Jobs.Exceptions.JobValidatorException">
    /// One or more jobs failed validation; <see cref="Headless.Jobs.Exceptions.JobValidatorException.Errors" /> lists each.
    /// </exception>
    Task<List<TTimeJob>> AddBatchAsync(List<TTimeJob> entities, CancellationToken cancellationToken = default);

    /// <summary>Updates a batch of time jobs and returns the aggregated result.</summary>
    Task<JobResult<List<TTimeJob>>> UpdateBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes the time jobs with the given identifiers and returns the aggregated result.</summary>
    Task<JobResult<TTimeJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
