// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Base;

/// <summary>
/// Typed job execution context that carries a strongly-typed deserialized request payload alongside the
/// base scheduling metadata.
/// </summary>
/// <typeparam name="TRequest">The deserialized request type stored in the job row.</typeparam>
/// <remarks>
/// Initializes a typed context by copying every base member from <paramref name="jobFunctionContext"/>
/// through the base copy constructor — so a member added to <see cref="JobFunctionContext"/> is never
/// silently dropped here — and attaching the deserialized <paramref name="request"/>.
/// </remarks>
/// <param name="jobFunctionContext">The base context supplied by the scheduler.</param>
/// <param name="request">The deserialized request payload for this execution.</param>
[PublicAPI]
[method: SetsRequiredMembers]
public class JobFunctionContext<TRequest>(JobFunctionContext jobFunctionContext, TRequest request)
    : JobFunctionContext(jobFunctionContext)
{
    /// <summary>The deserialized request payload for this job execution.</summary>
    public TRequest Request { get; set; } = request;
}

/// <summary>
/// Runtime context passed to a job function method by the scheduler. Exposes scheduling metadata and
/// provides hooks for cooperative cancellation and cron-skip control.
/// </summary>
[PublicAPI]
public class JobFunctionContext
{
    /// <summary>Initializes a new context; the scheduler populates its members via an object initializer.</summary>
    public JobFunctionContext() { }

    /// <summary>
    /// Copy constructor used by the typed <see cref="JobFunctionContext{TRequest}"/> to clone an existing
    /// context. Every base member is copied here in one place, so a member added to this base is never silently
    /// dropped when a typed context wraps a base one.
    /// </summary>
    /// <param name="other">The context to copy from.</param>
    [SetsRequiredMembers]
    protected JobFunctionContext(JobFunctionContext other)
    {
        ServiceScope = other.ServiceScope;
        Id = other.Id;
        Type = other.Type;
        RetryCount = other.RetryCount;
        IsDue = other.IsDue;
        ScheduledFor = other.ScheduledFor;
        FunctionName = other.FunctionName;
        CronOccurrenceOperations = other.CronOccurrenceOperations;
    }

    internal AsyncServiceScope ServiceScope { get; set; }

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
    /// Durably requests cooperative cancellation of this time job. The current owner observes the persisted request;
    /// this method does not directly signal process-local execution state.
    /// </summary>
    /// <param name="cancellationToken">Cancels only the durable request operation.</param>
    /// <returns><see langword="true"/> only when a new durable request was recorded.</returns>
    /// <exception cref="InvalidOperationException">This context represents a cron occurrence.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<bool> RequestCancellationAsync(CancellationToken cancellationToken = default)
    {
        if (Type != JobType.TimeJob)
        {
            throw new InvalidOperationException("Durable cancellation is supported only for time jobs.");
        }

        return ServiceScope.ServiceProvider.GetRequiredService<IJobScheduler>().CancelAsync(Id, cancellationToken);
    }

    internal void SetServiceScope(AsyncServiceScope serviceScope)
    {
        ServiceScope = serviceScope;
    }
}

/// <summary>
/// Cron-specific runtime operations available to a job function during execution. Instances are
/// constructed by the scheduler runtime, which wires the skip callback; consumers only call
/// <see cref="SkipIfAlreadyRunning"/>.
/// </summary>
[PublicAPI]
public sealed class CronOccurrenceOperations
{
    private readonly Action _skipIfAlreadyRunning;

    /// <summary>
    /// Wires the delegate invoked by <see cref="SkipIfAlreadyRunning"/> to mark the current occurrence
    /// as skipped when another occurrence of the same cron job is already executing on this node.
    /// Internal: the scheduler runtime is the only producer of execution contexts.
    /// </summary>
    /// <param name="skipIfAlreadyRunning">Callback that skips the occurrence when a sibling is running.</param>
    internal CronOccurrenceOperations(Action skipIfAlreadyRunning)
    {
        _skipIfAlreadyRunning = skipIfAlreadyRunning;
    }

    /// <summary>
    /// Marks this cron occurrence as <c>Skipped</c> and stops execution if another occurrence of the
    /// same cron job is currently running on this node. Use this to prevent overlapping executions for
    /// long-running cron jobs.
    /// </summary>
    public void SkipIfAlreadyRunning()
    {
        _skipIfAlreadyRunning();
    }
}
