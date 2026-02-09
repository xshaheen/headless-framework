// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents a scheduled job definition stored in the scheduling infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// A scheduled job can be either <see cref="ScheduledJobType.Recurring"/> (cron-based)
/// or <see cref="ScheduledJobType.OneTime"/>. The scheduler uses this entity to track
/// next run times, lock ownership, retry state, and execution history.
/// </para>
/// <para>
/// This is a mutable class (not a record) because properties such as
/// <see cref="Status"/>, <see cref="NextRunTime"/>, <see cref="LockHolder"/>, etc.
/// are updated by the scheduling engine and EF Core during normal operation.
/// </para>
/// </remarks>
public sealed class ScheduledJob
{
    /// <summary>
    /// Gets or sets the unique identifier for this job.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name that identifies this job.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the scheduling type (recurring or one-time).
    /// </summary>
    public required ScheduledJobType Type { get; set; }

    /// <summary>
    /// Gets or sets the cron expression defining the recurrence schedule.
    /// </summary>
    /// <value>
    /// A cron expression (e.g. <c>0 */5 * * *</c>) for recurring jobs, or <c>null</c> for one-time jobs.
    /// </value>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Gets or sets the IANA time-zone identifier used to evaluate the cron expression.
    /// </summary>
    /// <value>Defaults to UTC when not specified.</value>
    public required string TimeZone { get; set; }

    /// <summary>
    /// Gets or sets an optional opaque payload associated with the job.
    /// </summary>
    /// <value>
    /// A free-form string (typically JSON) carrying job-specific data, or <c>null</c>.
    /// </value>
    public string? Payload { get; set; }

    /// <summary>
    /// Gets or sets the current status of this job.
    /// </summary>
    public required ScheduledJobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the next scheduled run time.
    /// </summary>
    /// <value>
    /// The next UTC time the job should execute, or <c>null</c> if not yet computed
    /// or the job is one-time and has already run.
    /// </value>
    public DateTimeOffset? NextRunTime { get; set; }

    /// <summary>
    /// Gets or sets the time of the last execution.
    /// </summary>
    /// <value>
    /// The UTC time the job last started, or <c>null</c> if the job has never run.
    /// </value>
    public DateTimeOffset? LastRunTime { get; set; }

    /// <summary>
    /// Gets or sets the duration of the last execution in milliseconds.
    /// </summary>
    /// <value>
    /// Elapsed milliseconds, or <c>null</c> if the job has never run.
    /// </value>
    public long? LastRunDuration { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts allowed for this job.
    /// </summary>
    public required int MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the retry intervals (in seconds) between successive retry attempts.
    /// </summary>
    /// <value>
    /// An array of intervals, or <c>null</c> to use the default retry policy.
    /// </value>
    public int[]? RetryIntervals { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a new occurrence should be skipped
    /// if the previous execution is still running.
    /// </summary>
    public required bool SkipIfRunning { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the node that currently holds the execution lock.
    /// </summary>
    /// <value>
    /// A machine or instance identifier, or <c>null</c> when no lock is held.
    /// </value>
    public string? LockHolder { get; set; }

    /// <summary>
    /// Gets or sets the time the lock was acquired.
    /// </summary>
    /// <value>
    /// The UTC time the lock was taken, or <c>null</c> when no lock is held.
    /// </value>
    public DateTimeOffset? DateLocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this job is enabled and eligible for execution.
    /// </summary>
    public required bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this job was created.
    /// </summary>
    public required DateTimeOffset DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this job was last updated.
    /// </summary>
    public required DateTimeOffset DateUpdated { get; set; }

    /// <summary>
    /// Gets or sets the timeout for job execution.
    /// </summary>
    /// <value>
    /// The maximum duration allowed for job execution, or <c>null</c> for no timeout.
    /// </value>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the strategy for handling missed scheduled executions.
    /// </summary>
    public required MisfireStrategy MisfireStrategy { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the consumer for one-time jobs.
    /// Used for runtime resolution when the consumer is not pre-registered via keyed DI.
    /// </summary>
    public string? ConsumerTypeName { get; set; }

    /// <summary>
    /// Gets or sets the optimistic concurrency version token.
    /// Incremented on every successful update to detect concurrent modifications.
    /// </summary>
    public long Version { get; set; }
}
