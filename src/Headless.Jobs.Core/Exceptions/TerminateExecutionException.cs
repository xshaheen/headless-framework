// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Exceptions;

/// <summary>
/// Thrown from a job function body to set the job's terminal status explicitly rather than letting the
/// scheduler infer it from a generic exception. The default status is <see cref="JobStatus.Skipped"/>.
/// </summary>
/// <remarks>
/// This is the mechanism used by <c>CronOccurrenceOperations.SkipIfAlreadyRunning</c> to mark an
/// occurrence as <see cref="JobStatus.Skipped"/> when a sibling is already executing on the same node.
/// Throwing this exception from a job function bypasses the built-in retry logic: the scheduler
/// stamps the requested status directly and does not re-enqueue the job.
/// </remarks>
public sealed class TerminateExecutionException : Exception
{
    internal JobStatus Status { get; } = JobStatus.Skipped;

    /// <summary>
    /// Initializes a new instance that marks the job as <see cref="JobStatus.Skipped"/>.
    /// </summary>
    /// <param name="message">Human-readable reason stored in the job's skip-reason field.</param>
    public TerminateExecutionException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance that stamps a specific terminal <paramref name="jobType"/> status.
    /// </summary>
    /// <param name="jobType">The terminal status to stamp on the job row.</param>
    /// <param name="message">Human-readable reason stored in the job's skip/fail reason field.</param>
    public TerminateExecutionException(JobStatus jobType, string message)
        : base(message) => Status = jobType;

    /// <summary>
    /// Initializes a new instance that marks the job as <see cref="JobStatus.Skipped"/>, preserving
    /// an inner exception for diagnostic purposes.
    /// </summary>
    /// <param name="message">Human-readable reason stored in the job's skip-reason field.</param>
    /// <param name="innerException">The underlying cause, for logging context.</param>
    public TerminateExecutionException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance that stamps a specific terminal <paramref name="jobType"/> status,
    /// preserving an inner exception for diagnostic purposes.
    /// </summary>
    /// <param name="jobType">The terminal status to stamp on the job row.</param>
    /// <param name="message">Human-readable reason stored in the job's skip/fail reason field.</param>
    /// <param name="innerException">The underlying cause, for logging context.</param>
    public TerminateExecutionException(JobStatus jobType, string message, Exception innerException)
        : base(message, innerException) => Status = jobType;
}

internal sealed class ExceptionDetailClassForSerialization
{
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
}
