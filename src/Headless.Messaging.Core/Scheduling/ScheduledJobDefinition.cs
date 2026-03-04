// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Represents a discovered scheduled job definition collected during consumer registration.
/// Used for startup reconciliation with persistent storage.
/// </summary>
internal sealed record ScheduledJobDefinition
{
    /// <summary>
    /// Gets the unique job name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the consumer type that handles this job.
    /// </summary>
    public required Type ConsumerType { get; init; }

    /// <summary>
    /// Gets the 6-field cron expression defining the recurrence schedule.
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>
    /// Gets the IANA time-zone identifier, or <c>null</c> for UTC.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// Gets the retry intervals in seconds between successive retry attempts.
    /// </summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>
    /// Gets whether a new occurrence should be skipped if the previous execution is still running.
    /// </summary>
    public bool SkipIfRunning { get; init; } = true;

    /// <summary>
    /// Gets the timeout for job execution, or <c>null</c> for no timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets the strategy for handling missed scheduled executions.
    /// </summary>
    public MisfireStrategy MisfireStrategy { get; init; } = MisfireStrategy.FireImmediately;
}
