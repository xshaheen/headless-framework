// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Optional hook for custom error handling logic when a job function throws or is cancelled. Register
/// an implementation via <c>JobsOptionsBuilder.SetExceptionHandler&lt;THandler&gt;</c>.
/// </summary>
/// <remarks>
/// Both methods are called by the scheduler after the built-in retry and status-update logic runs. They
/// are meant for side effects (alerting, logging sinks, compensating transactions) rather than retry
/// control — return normally to allow the scheduler to continue its standard flow.
/// </remarks>
public interface IJobExceptionHandler
{
    /// <summary>
    /// Called when a job function throws an unhandled exception that is not a cancellation.
    /// </summary>
    /// <param name="exception">The exception thrown by the job function.</param>
    /// <param name="jobId">The identifier of the failing job row.</param>
    /// <param name="jobType">Whether the failing row is a time job or a cron occurrence.</param>
    Task HandleExceptionAsync(Exception exception, Guid jobId, JobType jobType);

    /// <summary>
    /// Called when a job is cancelled — either cooperatively via <c>JobFunctionContext.RequestCancellation</c>
    /// or by an external cancellation signal.
    /// </summary>
    /// <param name="exception">The <c>OperationCanceledException</c> or derived exception.</param>
    /// <param name="jobId">The identifier of the cancelled job row.</param>
    /// <param name="jobType">Whether the cancelled row is a time job or a cron occurrence.</param>
    Task HandleCanceledExceptionAsync(Exception exception, Guid jobId, JobType jobType);
}
