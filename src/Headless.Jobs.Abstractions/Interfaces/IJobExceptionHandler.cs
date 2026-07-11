// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Optional hook for custom error handling logic when a job function throws or is cancelled. Register
/// an implementation via <c>JobsOptionsBuilder.SetExceptionHandler&lt;THandler&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HandleExceptionAsync"/> is invoked once per failed attempt — after that attempt's
/// durable retry state is persisted and before the next retry delay begins, and once more at final
/// failure — not only once per job. For a single once-per-job notification after the retry budget
/// is consumed, use <c>JobsRetryOptions.OnExhausted</c> instead.
/// </para>
/// <para>
/// Both methods are meant for side effects (alerting, logging sinks, compensating transactions)
/// rather than retry control — return normally to allow the scheduler to continue its standard
/// flow. Each invocation is bounded by <c>JobsRetryOptions.OnExhaustedTimeout</c>; a handler that
/// exceeds it is logged and orphaned so a misbehaving implementation cannot stall retry
/// progression.
/// </para>
/// </remarks>
public interface IJobExceptionHandler
{
    /// <summary>
    /// Called after each failed attempt in which the job function throws an unhandled exception
    /// that is not a cancellation (each retryable failure and the final one).
    /// </summary>
    /// <param name="exception">The exception thrown by the job function.</param>
    /// <param name="jobId">The identifier of the failing job row.</param>
    /// <param name="jobType">Whether the failing row is a time job or a cron occurrence.</param>
    /// <param name="cancellationToken">Token signalled on host shutdown; honor it in any async I/O.</param>
    Task HandleExceptionAsync(
        Exception exception,
        Guid jobId,
        JobType jobType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Called when a job is cancelled — either cooperatively via <c>JobFunctionContext.RequestCancellation</c>
    /// or by an external cancellation signal.
    /// </summary>
    /// <param name="exception">The <c>OperationCanceledException</c> or derived exception.</param>
    /// <param name="jobId">The identifier of the cancelled job row.</param>
    /// <param name="jobType">Whether the cancelled row is a time job or a cron occurrence.</param>
    /// <param name="cancellationToken">Token signalled on host shutdown; honor it in any async I/O.</param>
    Task HandleCanceledExceptionAsync(
        Exception exception,
        Guid jobId,
        JobType jobType,
        CancellationToken cancellationToken = default
    );
}
