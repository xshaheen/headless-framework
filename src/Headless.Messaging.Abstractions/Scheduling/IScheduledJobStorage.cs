// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Provides persistence operations for <see cref="ScheduledJob"/> and
/// <see cref="JobExecution"/> entities used by the scheduling infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Lifetime contract:</strong> Implementations must be registered as
/// <b>Singleton</b> or <b>Transient</b> — never Scoped. Singleton services
/// (ScheduledJobManager, SchedulerBackgroundService) capture this dependency
/// directly; a Scoped registration would create a captive dependency that
/// never disposes correctly. Use a connection-per-call pattern internally
/// (e.g. <c>IDbContextFactory</c>) to stay singleton-safe.
/// </para>
/// <para>
/// Implementations must guarantee that <see cref="AcquireDueJobsAsync"/> atomically
/// transitions jobs to <see cref="ScheduledJobStatus.Running"/> and sets
/// <see cref="ScheduledJob.LockHolder"/> to prevent double-pickup by competing nodes.
/// </para>
/// </remarks>
public interface IScheduledJobStorage
{
    /// <summary>
    /// Atomically acquires up to <paramref name="batchSize"/> due jobs by marking them
    /// as <see cref="ScheduledJobStatus.Running"/> and setting their
    /// <see cref="ScheduledJob.LockHolder"/> to <paramref name="lockHolder"/>.
    /// </summary>
    /// <param name="batchSize">Maximum number of jobs to acquire in a single batch.</param>
    /// <param name="lockHolder">
    /// The instance identifier claiming the jobs (e.g. machine name or process id).
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The list of jobs that were successfully acquired.</returns>
    /// <remarks>
    /// This operation must be atomic: only one caller should be able to claim a given
    /// job even when multiple nodes call this method concurrently.
    /// </remarks>
    Task<IReadOnlyList<ScheduledJob>> AcquireDueJobsAsync(
        int batchSize,
        string lockHolder,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves a scheduled job by its unique name.
    /// </summary>
    /// <param name="name">The job name to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching job, or <c>null</c> if no job with that name exists.</returns>
    Task<ScheduledJob?> GetJobByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all scheduled jobs.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of all scheduled jobs.</returns>
    Task<IReadOnlyList<ScheduledJob>> GetAllJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of stale jobs — jobs in <see cref="ScheduledJobStatus.Running"/>
    /// status whose <see cref="ScheduledJob.DateLocked"/> is older than <paramref name="threshold"/>.
    /// </summary>
    /// <param name="threshold">
    /// The absolute point in time before which a locked-and-running job is considered stale.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The count of stale jobs.</returns>
    Task<int> GetStaleJobCountAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new job or updates an existing job matched by <see cref="ScheduledJob.Name"/>.
    /// </summary>
    /// <param name="job">The job to insert or update.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertJobAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing scheduled job with optimistic concurrency control.
    /// </summary>
    /// <param name="job">The job with updated values. The <see cref="ScheduledJob.Version"/> is used for concurrency checking.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ScheduledJobConcurrencyException">
    /// Thrown when the job was modified by another process since it was read. The caller
    /// should re-read the job and retry the operation.
    /// </exception>
    Task UpdateJobAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a scheduled job by its identifier.
    /// </summary>
    /// <param name="jobId">The identifier of the job to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new execution record for a scheduled job.
    /// </summary>
    /// <param name="execution">The execution to persist.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreateExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing execution record.
    /// </summary>
    /// <param name="execution">The execution with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves execution records for a given job, ordered by most recent first.
    /// </summary>
    /// <param name="jobId">The job identifier to filter executions by.</param>
    /// <param name="limit">Maximum number of execution records to return.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of execution records for the specified job.</returns>
    Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(
        Guid jobId,
        int limit,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns execution counts grouped by UTC date and status for a given job,
    /// covering the last <paramref name="days"/> days. Implementations should push the
    /// aggregation to the storage engine (e.g. SQL GROUP BY) rather than fetching raw rows.
    /// </summary>
    /// <param name="jobId">The job identifier to filter executions by.</param>
    /// <param name="days">Number of past days to include (default 7).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of per-date, per-status counts ordered by date then status.</returns>
    Task<IReadOnlyList<ExecutionStatusCount>> GetExecutionStatusCountsAsync(
        Guid jobId,
        int days = 7,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Marks orphaned <see cref="JobExecution"/> records as <see cref="JobExecutionStatus.TimedOut"/>
    /// when their parent job is no longer in <see cref="ScheduledJobStatus.Running"/> status.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of execution records that were timed out.</returns>
    /// <remarks>
    /// Call this after <see cref="ReleaseStaleJobsAsync"/> to clean up executions
    /// that were left in <see cref="JobExecutionStatus.Running"/> status when the
    /// owning process crashed or became unresponsive.
    /// </remarks>
    Task<int> TimeoutStaleExecutionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases jobs that have been locked for longer than the specified staleness threshold
    /// by resetting their <see cref="ScheduledJobStatus"/> and clearing their lock holder.
    /// </summary>
    /// <param name="staleness">
    /// The age threshold for considering a job stale. Jobs with
    /// <see cref="ScheduledJobStatus.Running"/> status and a
    /// <see cref="ScheduledJob.DateLocked"/> timestamp older than
    /// <c>now - staleness</c> will be released.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of jobs that were released.</returns>
    Task<int> ReleaseStaleJobsAsync(TimeSpan staleness, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes execution records that completed longer ago than the specified retention period.
    /// </summary>
    /// <param name="retention">
    /// The retention period for completed executions. Execution records with a
    /// <see cref="JobExecution.DateCompleted"/> timestamp older than
    /// <c>now - retention</c> will be permanently deleted.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of execution records that were purged.</returns>
    Task<int> PurgeExecutionsAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}
