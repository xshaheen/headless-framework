// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Thrown from a job function body to set the job's terminal status explicitly rather than letting the
/// scheduler infer it from a generic exception. The default status is <see cref="JobStatus.Skipped"/>.
/// </summary>
/// <remarks>
/// Throwing this exception from a job function bypasses the built-in retry logic: the scheduler
/// stamps the requested status directly and does not re-enqueue the job. To keep cron occurrences from overlapping,
/// set the definition's overlap policy instead of checking for a running sibling here; the policy is enforced
/// cluster-wide before the occurrence starts.
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

/// <summary>
/// The exception a failed attempt stores: the outer exception's type and message, and everything under them, so an
/// operator reads the whole failure, inner exceptions included, and not only where the innermost one threw.
/// </summary>
internal sealed class ExceptionDetailClassForSerialization
{
    /// <summary>Gets or sets the outer exception's full type name, such as <c>System.TimeoutException</c>.</summary>
    public string? Type { get; set; }

    public required string Message { get; set; }

    /// <summary>
    /// Gets or sets the outer exception's frames, each starting <c>at</c>, then each inner exception as a
    /// <c>Type: message</c> line followed by its own frames.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>Returns <paramref name="exception"/> as the JSON a failed attempt stores.</summary>
    public static string Serialize(Exception exception)
    {
        // ExpandMessage writes the inner chain (and a flattened AggregateException) the way Messaging stores its
        // failures, bounded to a few levels so a deep chain cannot grow a row without limit.
        var stackTrace =
            exception is AggregateException
                ? exception.ExpandMessage()
                : string.Join(
                    Environment.NewLine,
                    new[] { exception.StackTrace?.TrimEnd(), exception.InnerException?.ExpandMessage() }.Where(
                        static part => !string.IsNullOrEmpty(part)
                    )
                );

        var fullStackTrace = stackTrace.Length == 0 ? null : stackTrace;
        var detail = new ExceptionDetailClassForSerialization
        {
            Type = exception.GetType().FullName,
            Message = exception.Message,
            StackTrace = fullStackTrace,
        };

        // Bound the fields, not the JSON text: cutting the serialized string would leave JSON the dashboard can no
        // longer read. The message keeps the first quarter of the budget so a long stack cannot crowd it out, and the
        // stack takes what is left. JSON escaping can make the text longer than its fields (a newline is two
        // characters, a control character six), so each pass cuts the stack, then the message, by its share of the overshoot.
        var messageLimit = JobExceptionText.StoredMaxLength / 4;
        var stackLimit = JobExceptionText.StoredMaxLength;

        while (true)
        {
            detail.Message = exception.Message.TruncateEnd(messageLimit, JobExceptionText.TruncationMarker);
            detail.StackTrace = fullStackTrace.TruncateEnd(stackLimit, JobExceptionText.TruncationMarker);
            var json = JsonSerializer.Serialize(
                detail,
                JobsExceptionJsonContext.Default.ExceptionDetailClassForSerialization
            );
            var overshoot = json.Length - JobExceptionText.StoredMaxLength;

            if (overshoot <= 0 || (messageLimit == 0 && stackLimit == 0))
            {
                return json;
            }

            if (detail.StackTrace is { Length: > 0 } stack)
            {
                stackLimit = _Shrink(stack.Length, overshoot, json.Length);
            }
            else
            {
                stackLimit = 0;
                messageLimit = _Shrink(detail.Message.Length, overshoot, json.Length);
            }
        }
    }

    // Cuts a field by its share of the overshoot: a field that escapes to six characters each shrinks by a sixth of the
    // overshoot, not by all of it, so it keeps as much text as fits. Always cuts at least one character, so the loop ends.
    private static int _Shrink(int length, int overshoot, int jsonLength) =>
        Math.Max(0, length - Math.Max(1, (int)Math.Ceiling((double)overshoot * length / jsonLength)));
}
