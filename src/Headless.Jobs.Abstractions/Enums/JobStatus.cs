// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Lifecycle state of a job row (time job or cron occurrence).
/// </summary>
public enum JobStatus
{
    /// <summary>The job has been persisted but has not yet been picked up for execution.</summary>
    Idle,

    /// <summary>The job has been selected by the scheduler and is waiting for a worker thread.</summary>
    Queued,

    /// <summary>A worker thread is currently executing the job function.</summary>
    InProgress,

    /// <summary>The job function completed without error.</summary>
    Succeeded,

    /// <summary>
    /// The job completed successfully but its execution time was already in the past at dispatch time
    /// (picked up from the stale-job backlog).
    /// </summary>
    DueDone,

    /// <summary>The job function threw an unhandled exception and exhausted its retry budget.</summary>
    Failed,

    /// <summary>Execution was cancelled cooperatively via <c>JobFunctionContext.RequestCancellation</c> or an external cancel signal.</summary>
    Cancelled,

    /// <summary>
    /// Execution was intentionally skipped — for example via <c>CronOccurrenceOperations.SkipIfAlreadyRunning</c>
    /// or by throwing <c>TerminateExecutionException</c> with this status.
    /// </summary>
    Skipped,
}
