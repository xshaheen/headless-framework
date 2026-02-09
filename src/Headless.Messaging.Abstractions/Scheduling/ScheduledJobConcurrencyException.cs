// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Thrown when an optimistic concurrency conflict is detected during a scheduled job update.
/// This indicates that another process modified the job between the time it was read and updated.
/// </summary>
public sealed class ScheduledJobConcurrencyException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledJobConcurrencyException"/> class.
    /// </summary>
    /// <param name="jobId">The identifier of the job that failed the concurrency check.</param>
    /// <param name="expectedVersion">The version that was expected but no longer matches.</param>
    public ScheduledJobConcurrencyException(Guid jobId, long expectedVersion)
        : base(
            $"Concurrency conflict on scheduled job '{jobId}': expected version {expectedVersion} but the row was modified by another process."
        )
    {
        JobId = jobId;
        ExpectedVersion = expectedVersion;
    }

    /// <summary>
    /// Gets the identifier of the job that caused the concurrency conflict.
    /// </summary>
    public Guid JobId { get; }

    /// <summary>
    /// Gets the version that was expected at the time of the update.
    /// </summary>
    public long ExpectedVersion { get; }
}
