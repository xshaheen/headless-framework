// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Messaging;

/// <summary>
/// Provides runtime management operations for scheduled jobs, including
/// querying, enabling, disabling, triggering, and deleting jobs.
/// </summary>
public interface IScheduledJobManager
{
    /// <summary>
    /// Lists all scheduled jobs.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A read-only list of all scheduled jobs.</returns>
    Task<IReadOnlyList<ScheduledJob>> ListJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recent executions for a scheduled job.
    /// </summary>
    /// <param name="name">The scheduled job name.</param>
    /// <param name="limit">Maximum number of executions to return.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A read-only list of job executions ordered by most recent first.</returns>
    Task<IReadOnlyList<JobExecution>> ListExecutionsAsync(
        string name,
        int limit = 20,
        CancellationToken cancellationToken = default
    );

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
    /// <returns>A <see cref="NotFoundError"/> if no job with the specified name exists.</returns>
    Task<Result<ResultError>> EnableAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a job so it will not be picked up by the scheduler.
    /// </summary>
    /// <param name="name">The name of the job to disable.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="NotFoundError"/> if no job with the specified name exists.</returns>
    Task<Result<ResultError>> DisableAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers an immediate execution of the specified job by setting its next run
    /// time to now, so the scheduler picks it up on the next poll cycle.
    /// </summary>
    /// <param name="name">The name of the job to trigger.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="NotFoundError"/> if no job with the specified name exists.</returns>
    Task<Result<ResultError>> TriggerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a scheduled job by its name.
    /// </summary>
    /// <param name="name">The name of the job to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="NotFoundError"/> if no job with the specified name exists.</returns>
    Task<Result<ResultError>> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a one-time job to run at a specific time with a consumer resolved at runtime.
    /// </summary>
    /// <param name="name">The unique name for this job.</param>
    /// <param name="runAt">The UTC time when the job should execute.</param>
    /// <param name="consumerType">The consumer type that will handle the job. Will be resolved via keyed DI or ActivatorUtilities at runtime.</param>
    /// <param name="payload">Optional payload to pass to the consumer.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentException">Thrown when runAt is in the past.</exception>
    Task ScheduleOnceAsync(
        string name,
        DateTimeOffset runAt,
        Type consumerType,
        string? payload = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Schedules a one-time job to run at a specific time for a class-based consumer.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type handling <see cref="ScheduledTrigger"/>.</typeparam>
    /// <param name="name">The unique name for this job.</param>
    /// <param name="runAt">The UTC time when the job should execute.</param>
    /// <param name="payload">Optional serialized payload.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ScheduleOnceAsync<TConsumer>(
        string name,
        DateTimeOffset runAt,
        string? payload = null,
        CancellationToken cancellationToken = default
    )
        where TConsumer : class, IConsume<ScheduledTrigger>;

    /// <summary>
    /// Schedules a one-time job to run at a specific time with a typed payload.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type handling <see cref="ScheduledTrigger"/>.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="name">The unique name for this job.</param>
    /// <param name="runAt">The UTC time when the job should execute.</param>
    /// <param name="payload">The payload to serialize as JSON.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ScheduleOnceAsync<TConsumer, TPayload>(
        string name,
        DateTimeOffset runAt,
        TPayload payload,
        CancellationToken cancellationToken = default
    )
        where TConsumer : class, IConsume<ScheduledTrigger>;
}
