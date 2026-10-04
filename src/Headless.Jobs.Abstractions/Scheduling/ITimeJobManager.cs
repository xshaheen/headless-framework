// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Application-facing manager for one-shot (time) jobs: enqueue, update, and delete a single job plus their
/// batch variants. Resolved from DI as <c>ITimeJobManager&lt;TTimeJob&gt;</c>, where
/// <typeparamref name="TTimeJob"/> is the application's concrete time job entity. The injected manager writes
/// autonomously via the configured <c>IJobPersistenceProvider</c>; the manager returned by
/// <c>unit.TimeJobs&lt;TTimeJob&gt;()</c> writes inside that unit's transaction (see <c>AddAsync</c>).
/// </summary>
/// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
[PublicAPI]
public interface ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>
{
    /// <summary>Applies scheduling policy, then atomically creates, observes, or replaces a standalone keyed job.</summary>
    Task<JobScheduleResult> ScheduleKeyedAsync(
        JobKey key,
        TTimeJob entity,
        long? expectedGeneration = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Requests cancellation of exactly the observed current keyed generation.</summary>
    Task<JobScheduleResult> CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enqueues a time job and returns the persisted entity.</summary>
    /// <remarks>
    /// The receiver decides enlistment. The manager from <c>unit.TimeJobs&lt;TTimeJob&gt;()</c> writes the row, and
    /// every chain descendant, inside the unit's transaction and defers dispatch / scheduler-restart / notify to
    /// post-commit; the returned entity then means the rows commit with the unit, not that dispatch ran. The injected
    /// manager never enlists: it inserts the whole tree atomically in its own transaction and runs the side effects
    /// in-band. Any failure throws — so an enlisted caller's unit of work rolls back rather than completing without
    /// the job row. (Update/Delete keep returning <see cref="JobResult{TTimeJob}" />; only the Add path throws.)
    /// </remarks>
    /// <exception cref="Headless.Jobs.JobValidatorException">The job failed validation (unknown function).</exception>
    /// <exception cref="InvalidOperationException">
    /// On the enlisted manager only: the unit carries no live relational resource, its resource is dead or belongs
    /// to another database, or the configured persistence provider cannot write inside it (a mis-wire).
    /// </exception>
    Task<TTimeJob> AddAsync(TTimeJob entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a time job behind an idempotency reservation: creates the job and its reservation atomically, or
    /// observes a live reservation and returns its job without inserting anything.
    /// </summary>
    /// <remarks>
    /// Same receiver-decided routing as <see cref="AddAsync"/> (inside the unit's transaction for the manager from
    /// <c>unit.TimeJobs&lt;TTimeJob&gt;()</c>, a direct insert for the injected manager), with the reservation
    /// mutation written in the same transaction as the job so a rollback removes both. Tenant capture and the schedule pipeline run before the reservation identity is formed. On a
    /// dedup hit the returned entity's identifier is the reserved job's ID (overwritten from the reservation) and no
    /// dispatch/restart/notify side effects are armed. The entity's identifier becomes the reserved job ID on
    /// creation.
    /// </remarks>
    /// <exception cref="Headless.Jobs.JobValidatorException">The job failed validation (unknown function).</exception>
    /// <exception cref="ArgumentException">The key is not a valid Jobs name or the TTL is outside 1 second to 30 days.</exception>
    /// <exception cref="InvalidOperationException">
    /// On the enlisted manager only: the unit carries no live relational resource, its resource is dead, belongs to
    /// another database, or cannot create savepoints, or the configured persistence provider cannot write inside it
    /// (a mis-wire).
    /// </exception>
    Task<TTimeJob> AddIdempotentAsync(
        TTimeJob entity,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        CancellationToken cancellationToken = default
    );

    /// <summary>Updates an existing time job and returns the result.</summary>
    Task<JobResult<TTimeJob>> UpdateAsync(TTimeJob timeJob, CancellationToken cancellationToken = default);

    /// <summary>Deletes the time job with the given identifier and returns the result.</summary>
    Task<JobResult<TTimeJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations

    /// <inheritdoc cref="AddAsync" />
    /// <exception cref="Headless.Jobs.JobValidatorException">
    /// One or more jobs failed validation; <see cref="Headless.Jobs.JobValidatorException.Errors" /> lists each.
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
