// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents a concurrency violation when updating a scheduled job.
/// </summary>
public sealed class ScheduledJobConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobConcurrencyException"/>.
    /// </summary>
    public ScheduledJobConcurrencyException()
        : base("Scheduled job was modified by another process.")
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobConcurrencyException"/> with a custom message.
    /// </summary>
    public ScheduledJobConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobConcurrencyException"/> with a custom message
    /// and inner exception.
    /// </summary>
    public ScheduledJobConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobConcurrencyException"/> for a specific job.
    /// </summary>
    public ScheduledJobConcurrencyException(Guid jobId, long expectedVersion)
        : base(
            $"Scheduled job '{jobId}' was modified by another process. Expected version {expectedVersion}."
        )
    {
        JobId = jobId;
        ExpectedVersion = expectedVersion;
    }

    /// <summary>
    /// Gets the job identifier associated with the concurrency violation.
    /// </summary>
    public Guid JobId { get; }

    /// <summary>
    /// Gets the expected version when the update was attempted.
    /// </summary>
    public long ExpectedVersion { get; }
}
