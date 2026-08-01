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
    /// Initializes a new instance that stamps a specific terminal <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The terminal status to stamp on the job row. Must be terminal — see
    /// <see cref="_EnsureTerminal"/>.</param>
    /// <param name="message">Human-readable reason stored in the job's skip/fail reason field.</param>
    public TerminateExecutionException(JobStatus status, string message)
        : base(message) => Status = _EnsureTerminal(status);

    /// <summary>
    /// Initializes a new instance that marks the job as <see cref="JobStatus.Skipped"/>, preserving
    /// an inner exception for diagnostic purposes.
    /// </summary>
    /// <param name="message">Human-readable reason stored in the job's skip-reason field.</param>
    /// <param name="innerException">The underlying cause, for logging context.</param>
    public TerminateExecutionException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance that stamps a specific terminal <paramref name="status"/>,
    /// preserving an inner exception for diagnostic purposes.
    /// </summary>
    /// <param name="status">The terminal status to stamp on the job row. Must be terminal — see
    /// <see cref="_EnsureTerminal"/>.</param>
    /// <param name="message">Human-readable reason stored in the job's skip/fail reason field.</param>
    /// <param name="innerException">The underlying cause, for logging context.</param>
    public TerminateExecutionException(JobStatus status, string message, Exception innerException)
        : base(message, innerException) => Status = _EnsureTerminal(status);

    // The handler stamps this status verbatim as the job's final state. A NON-terminal status (Idle/Queued/
    // InProgress) would leave the row's owner and lease intact while making it satisfy the claim predicates
    // again — the same node re-claims and re-runs it immediately with no backoff and no retry accounting: an
    // unbounded hot loop. Reject at construction, where the mistake is written.
    private static JobStatus _EnsureTerminal(JobStatus status)
    {
        return
            status
                is JobStatus.Succeeded
                    or JobStatus.DueDone
                    or JobStatus.Failed
                    or JobStatus.Cancelled
                    or JobStatus.Skipped
            ? status
            : throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "TerminateExecutionException requires a terminal JobStatus (Succeeded, DueDone, Failed, "
                    + "Cancelled, or Skipped); a non-terminal status would make the row immediately re-claimable "
                    + "by the same node in an unbounded hot loop."
            );
    }
}

internal sealed class ExceptionDetailClassForSerialization
{
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
}
