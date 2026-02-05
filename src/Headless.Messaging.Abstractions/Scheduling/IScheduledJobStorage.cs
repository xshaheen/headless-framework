// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Provides persistence operations for <see cref="ScheduledJob"/> and
/// <see cref="JobExecution"/> entities used by the scheduling infrastructure.
/// </summary>
/// <remarks>
/// Implementations must guarantee that <see cref="AcquireDueJobsAsync"/> atomically
/// transitions jobs to <see cref="ScheduledJobStatus.Running"/> and sets
/// <see cref="ScheduledJob.LockHolder"/> to prevent double-pickup by competing nodes.
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
    /// Inserts a new job or updates an existing job matched by <see cref="ScheduledJob.Name"/>.
    /// </summary>
    /// <param name="job">The job to insert or update.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertJobAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing scheduled job.
    /// </summary>
    /// <param name="job">The job with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
}
