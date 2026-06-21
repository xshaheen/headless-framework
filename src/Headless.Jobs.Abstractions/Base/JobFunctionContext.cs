// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Base;

/// <summary>
/// Typed job execution context that carries a strongly-typed deserialized request payload alongside the
/// base scheduling metadata.
/// </summary>
/// <typeparam name="TRequest">The deserialized request type stored in the job row.</typeparam>
public class JobFunctionContext<TRequest> : JobFunctionContext
{
    /// <summary>
    /// Initializes a typed context by copying all base fields from <paramref name="jobFunctionContext"/>
    /// and attaching the deserialized <paramref name="request"/>.
    /// </summary>
    /// <param name="jobFunctionContext">The base context supplied by the scheduler.</param>
    /// <param name="request">The deserialized request payload for this execution.</param>
    public JobFunctionContext(JobFunctionContext jobFunctionContext, TRequest request)
    {
        Request = request;
        Id = jobFunctionContext.Id;
        Type = jobFunctionContext.Type;
        RetryCount = jobFunctionContext.RetryCount;
        IsDue = jobFunctionContext.IsDue;
        ScheduledFor = jobFunctionContext.ScheduledFor;
        RequestCancelOperationAction = jobFunctionContext.RequestCancelOperationAction;
        CronOccurrenceOperations = jobFunctionContext.CronOccurrenceOperations;
        FunctionName = jobFunctionContext.FunctionName;
    }

    /// <summary>The deserialized request payload for this job execution.</summary>
    public TRequest Request { get; set; }
}

/// <summary>
/// Runtime context passed to a job function method by the scheduler. Exposes scheduling metadata and
/// provides hooks for cooperative cancellation and cron-skip control.
/// </summary>
public class JobFunctionContext
{
    internal AsyncServiceScope ServiceScope { get; set; }

    /// <summary>
    /// Delegate invoked by <see cref="RequestCancellation"/> to signal the scheduler that this execution
    /// should be cancelled cooperatively.
    /// </summary>
    public required Action RequestCancelOperationAction { get; init; }

    /// <summary>Unique identifier of the job row (time job or cron occurrence) being executed.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Whether the execution is a time job or a cron occurrence.</summary>
    public JobType Type { get; internal set; }

    /// <summary>Number of times this job has been retried before the current attempt.</summary>
    public int RetryCount { get; internal set; }

    /// <summary>
    /// <see langword="true"/> when the job's execution time was in the past at dispatch time
    /// (i.e., the job was picked up from the stale-job backlog rather than dispatched live).
    /// </summary>
    public bool IsDue { get; internal set; }

    /// <summary>
    /// The time this job was scheduled to run (UTC). For time jobs this equals the row's
    /// <c>ExecutionTime</c>; for cron occurrences it equals the occurrence's <c>ExecutionTime</c>.
    /// </summary>
    public DateTime ScheduledFor { get; internal set; }

    /// <summary>The registered function name that identifies this job handler.</summary>
    public required string FunctionName { get; init; }

    /// <summary>Operations specific to cron occurrence execution (e.g., skip-if-already-running).</summary>
    public required CronOccurrenceOperations CronOccurrenceOperations { get; init; }

    /// <summary>
    /// Signals the scheduler to cancel this job's execution cooperatively. The cancellation token
    /// passed to the job function method will be cancelled, and the job's status will transition
    /// to <c>Cancelled</c>.
    /// </summary>
    public void RequestCancellation() => RequestCancelOperationAction();

    internal void SetServiceScope(AsyncServiceScope serviceScope) => ServiceScope = serviceScope;
}

/// <summary>
/// Cron-specific runtime operations available to a job function during execution.
/// </summary>
public class CronOccurrenceOperations
{
    /// <summary>
    /// Delegate invoked by <see cref="SkipIfAlreadyRunning"/> to mark the current occurrence as
    /// skipped when another occurrence of the same cron job is already executing on this node.
    /// </summary>
    public required Action SkipIfAlreadyRunningAction { get; init; }

    /// <summary>
    /// Marks this cron occurrence as <c>Skipped</c> and stops execution if another occurrence of the
    /// same cron job is currently running on this node. Use this to prevent overlapping executions for
    /// long-running cron jobs.
    /// </summary>
    public void SkipIfAlreadyRunning() => SkipIfAlreadyRunningAction();
}
