// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Provides runtime management operations for scheduled jobs, including
/// querying, enabling, disabling, triggering, and deleting jobs.
/// </summary>
public interface IScheduledJobManager
{
    /// <summary>
    /// Retrieves all scheduled jobs.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A read-only list of all scheduled jobs.</returns>
    Task<IReadOnlyList<ScheduledJob>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a scheduled job by its unique name.
    /// </summary>
    /// <param name="name">The job name to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching job, or <c>null</c> if no job with that name exists.</returns>
    Task<ScheduledJob?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a previously disabled job and recomputes its next run time from
    /// the cron expression and time zone.
    /// </summary>
    /// <param name="name">The name of the job to enable.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no job with the specified name exists.</exception>
    Task EnableAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a job so it will not be picked up by the scheduler.
    /// </summary>
    /// <param name="name">The name of the job to disable.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no job with the specified name exists.</exception>
    Task DisableAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers an immediate execution of the specified job by setting its next run
    /// time to now, so the scheduler picks it up on the next poll cycle.
    /// </summary>
    /// <param name="name">The name of the job to trigger.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no job with the specified name exists.</exception>
    Task TriggerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a scheduled job by its name.
    /// </summary>
    /// <param name="name">The name of the job to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no job with the specified name exists.</exception>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
