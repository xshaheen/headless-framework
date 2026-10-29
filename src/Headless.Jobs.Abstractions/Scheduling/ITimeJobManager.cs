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
    /// Same receiver-decided routing as <see cref="AddAsync(TTimeJob, CancellationToken)"/> (inside the unit's transaction for the manager from
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

    /// <summary>
    /// Enqueues a time job due <paramref name="delay"/> after the store's instant at write, and returns the persisted
    /// entity.
    /// </summary>
    /// <remarks>
    /// The store resolves the due time inside the write, as its own instant plus <paramref name="delay"/>, so the
    /// calling node's clock skew never moves when the job becomes due; an enlisted write is stamped when the row is
    /// written, not when the unit commits. The entity's <c>ExecutionTime</c> must be null on entry; a schedule
    /// middleware that sets an absolute <c>ExecutionTime</c> wins over the delay. Otherwise the same receiver-decided
    /// routing and throw-on-failure contract as <see cref="AddAsync(TTimeJob, CancellationToken)"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="delay"/> is negative (thrown synchronously), or the store's instant plus the delay overflows
    /// (thrown before any row is written).
    /// </exception>
    /// <exception cref="Headless.Jobs.JobValidatorException">The job failed validation (unknown function).</exception>
    /// <exception cref="ArgumentException">The entity already carries an <c>ExecutionTime</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// On the enlisted manager only: the unit carries no live relational resource, its resource is dead or belongs
    /// to another database, or the configured persistence provider cannot write inside it (a mis-wire).
    /// </exception>
    Task<TTimeJob> AddAsync(TTimeJob entity, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a time job behind an idempotency reservation, due <paramref name="delay"/> after the store's instant
    /// at write; a live reservation is observed and its job returned without inserting anything.
    /// </summary>
    /// <remarks>
    /// The idempotent form of <see cref="AddAsync(TTimeJob, TimeSpan, CancellationToken)"/>, with the reservation
    /// semantics of <see cref="AddIdempotentAsync(TTimeJob, string, TimeSpan, CancellationToken)"/>. Only a creation
    /// is stamped; a dedup hit writes nothing and keeps the reserved job's due time.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="delay"/> is negative (thrown synchronously), or the store's instant plus the delay overflows
    /// (thrown before any row is written).
    /// </exception>
    /// <exception cref="Headless.Jobs.JobValidatorException">The job failed validation (unknown function).</exception>
    /// <exception cref="ArgumentException">
    /// The entity already carries an <c>ExecutionTime</c>, the key is not a valid Jobs name, or the TTL is outside 1
    /// second to 30 days.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// On the enlisted manager only: the unit carries no live relational resource, its resource is dead, belongs to
    /// another database, or cannot create savepoints, or the configured persistence provider cannot write inside it
    /// (a mis-wire).
    /// </exception>
    Task<TTimeJob> AddIdempotentAsync(
        TTimeJob entity,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        TimeSpan delay,
        CancellationToken cancellationToken = default
    );

    /// <summary>Updates an existing time job and returns the result.</summary>
    /// <remarks>
    /// Only a job no node has started can be edited: one that is idle and unclaimed, or queued. Editing a queued job
    /// releases its claim, so rescheduling the next-due job still works. A running or finished job, or one whose
    /// loaded descendants belong to a claimed chain, returns a failed result and nothing changes; use
    /// <c>IJobScheduler.RequeueAsync</c> to run a failed job again. The job's run state (status, owner, retry count,
    /// outcome) is never taken from the supplied entity.
    /// </remarks>
    Task<JobResult<TTimeJob>> UpdateAsync(TTimeJob timeJob, CancellationToken cancellationToken = default);

    /// <summary>Deletes the time job with the given identifier and returns the result.</summary>
    Task<JobResult<TTimeJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations

    /// <inheritdoc cref="AddAsync(TTimeJob, CancellationToken)" />
    /// <exception cref="Headless.Jobs.JobValidatorException">
    /// One or more jobs failed validation; <see cref="Headless.Jobs.JobValidatorException.Errors" /> lists each.
    /// </exception>
    Task<List<TTimeJob>> AddBatchAsync(List<TTimeJob> entities, CancellationToken cancellationToken = default);

    /// <summary>Updates a batch of time jobs and returns the aggregated result.</summary>
    /// <remarks>
    /// Each job follows the <see cref="UpdateAsync"/> rule, and the batch is all-or-nothing: one job that cannot be
    /// edited fails the result and leaves every job unchanged.
    /// </remarks>
    Task<JobResult<List<TTimeJob>>> UpdateBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes the time jobs with the given identifiers and returns the aggregated result.</summary>
    Task<JobResult<TTimeJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
