// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents a single execution attempt of a <see cref="ScheduledJob"/>.
/// </summary>
/// <remarks>
/// Each time a scheduled job fires, a <see cref="JobExecution"/> row is created to
/// track the outcome, duration, and any error information for that attempt.
/// </remarks>
public sealed class JobExecution
{
    /// <summary>
    /// Gets or sets the unique identifier for this execution.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the parent <see cref="ScheduledJob"/>.
    /// </summary>
    public required Guid JobId { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this execution was scheduled to run.
    /// </summary>
    public required DateTimeOffset ScheduledTime { get; set; }

    /// <summary>
    /// Gets or sets the UTC time the execution actually started.
    /// </summary>
    /// <value>
    /// The start time, or <c>null</c> if the execution has not yet started.
    /// </value>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time the execution completed.
    /// </summary>
    /// <value>
    /// The completion time, or <c>null</c> if the execution has not yet finished.
    /// </value>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the current status of this execution.
    /// </summary>
    public required JobExecutionStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the execution duration in milliseconds.
    /// </summary>
    /// <value>
    /// Elapsed milliseconds, or <c>null</c> if the execution has not yet completed.
    /// </value>
    public long? Duration { get; set; }

    /// <summary>
    /// Gets or sets the zero-based retry attempt number.
    /// </summary>
    /// <value>
    /// 0 for the first attempt, incremented on each retry.
    /// </value>
    public required int RetryAttempt { get; set; }

    /// <summary>
    /// Gets or sets the error message if the execution failed.
    /// </summary>
    /// <value>
    /// The exception or error description, or <c>null</c> when the execution succeeded.
    /// </value>
    public string? Error { get; set; }
}
