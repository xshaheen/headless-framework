// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Schedules generated <c>[Job]</c> classes without requiring callers to construct persistence entities or
/// copy durable job identities. A job with arguments is addressed by its argument type, and a job without arguments
/// by its class.
/// </summary>
[PublicAPI]
public interface IJobScheduler
{
    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Schedules one durable keyed intent at an absolute instant. Repeating the same intent observes its current run, including terminal runs.</summary>
    /// <remarks>
    /// Within the key scope, intent consists of contract version, exact durable request bytes after middleware,
    /// and the UTC due instant truncated to microseconds. Retry and node-death policy differences, including
    /// explicit overrides, observe the existing generation without changing its captured policy.
    /// Options validation still applies.
    /// </remarks>
    Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="ScheduleKeyedAsync{TArgs}(JobKey, TArgs, DateTimeOffset, CancellationToken)"/>
    Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Replaces or reschedules only a pending, unclaimed observed generation. A replay cannot advance another generation.</summary>
    /// <remarks>A successful replacement captures the call's resolved execution policy in generation N+1, even when its intent equals generation N.</remarks>
    Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="ReplaceKeyedAsync{TArgs}(JobKey, long, TArgs, DateTimeOffset, CancellationToken)"/>
    Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Cancels the observed current generation. Claimed cancellation is cooperative.</summary>
    Task<JobScheduleResult> CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Durably requests cooperative cancellation of the one-shot job identified by <paramref name="jobId"/>.
    /// </summary>
    /// <param name="jobId">The time-job identifier.</param>
    /// <param name="cancellationToken">Cancels only the durable request operation.</param>
    /// <returns><see langword="true"/> only when this call records a new cancellation transition.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Durably pauses one cron definition and prevents pending occurrences from starting.</summary>
    /// <param name="cronJobId">The cron-definition identifier.</param>
    /// <param name="cancellationToken">Cancels the durable pause operation.</param>
    /// <returns><see langword="true"/> only when this call changes the definition to paused.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<bool> PauseCronAsync(Guid cronJobId, CancellationToken cancellationToken = default);

    /// <summary>Durably resumes one cron definition and schedules exactly its first occurrence after resume time.</summary>
    /// <param name="cronJobId">The cron-definition identifier.</param>
    /// <param name="cancellationToken">Cancels the durable resume operation.</param>
    /// <returns><see langword="true"/> only when this call changes the definition to active.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<bool> ResumeCronAsync(Guid cronJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a <c>Failed</c> standalone time job back to <c>Idle</c> so it runs again as soon as a node claims it.
    /// </summary>
    /// <param name="timeJobId">The time-job identifier.</param>
    /// <param name="cancellationToken">Cancels only the durable requeue operation.</param>
    /// <returns>
    /// <see cref="JobRequeueOutcome.Requeued"/> when this call moved the row; otherwise the refusal reason, with the row
    /// unchanged.
    /// </returns>
    /// <remarks>
    /// The row restarts its retry budget: <c>RetryCount</c> returns to 0, the stored exception, owner, and lease are
    /// cleared, and its execution time moves to the store's current instant. Its stored retry count and intervals are
    /// kept. A chain member and a superseded keyed generation are refused. Requeue is one conditional transition from
    /// <c>Failed</c>, so repeating it, or racing it against another requeue, moves the row at most once.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<JobRequeueOutcome> RequeueAsync(Guid timeJobId, CancellationToken cancellationToken = default);

    /// <summary>Puts a <c>Failed</c> cron occurrence back to <c>Idle</c> so it runs again.</summary>
    /// <param name="occurrenceId">The cron-occurrence identifier.</param>
    /// <param name="cancellationToken">Cancels only the durable requeue operation.</param>
    /// <returns>
    /// <see cref="JobRequeueOutcome.Requeued"/> when this call moved the row; otherwise the refusal reason, with the row
    /// unchanged.
    /// </returns>
    /// <remarks>
    /// The occurrence keeps its execution time, which identifies the scheduled instant it stands for, so the fallback
    /// claim picks it up rather than the main peek. When its definition forbids overlapping runs, the request is
    /// refused while another occurrence of that definition is unfinished. The check runs under the same definition
    /// lock that occurrence creation takes, so a requeue cannot slip in beside a newly created run.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<JobRequeueOutcome> RequeueOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken = default);

    /// <summary>Enqueues a typed job for immediate execution and returns its persisted entity identifier.</summary>
    Task<Guid> EnqueueAsync<TArgs>(TArgs request, CancellationToken cancellationToken = default);

    Task<Guid> EnqueueAsync<TArgs>(TArgs request, JobOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a typed <see cref="JobChain"/>: resolves every node's generated descriptor, enforces the configured
    /// maximum chain depth, and persists the root together with its whole descendant tree atomically through the
    /// existing manager add path. Returns the persisted root job identifier.
    /// </summary>
    /// <param name="chain">The immutable chain produced by <c>JobChainBuilder.Build()</c>.</param>
    /// <param name="cancellationToken">Cancels the enqueue operation.</param>
    /// <returns>The persisted identifier of the chain's root job.</returns>
    /// <remarks>
    /// Every node is validated before any row is written: an unmapped payload or a descriptor/step mismatch fails
    /// before persistence, and a chain deeper than the configured limit is rejected naming that limit. Each call
    /// materializes fresh entities, so re-enqueueing the same built chain yields independent trees.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The chain is deeper than the configured maximum chain depth.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<Guid> EnqueueAsync(JobChain chain, CancellationToken cancellationToken = default);

    /// <summary>Schedules a typed one-shot job and returns its persisted entity identifier.</summary>
    Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    );

    Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Schedules an ordinary one-shot job relative to the configured application clock; delay must be non-negative.</summary>
    Task<Guid> ScheduleAfterAsync<TArgs>(TArgs request, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Schedules an ordinary one-shot job relative to the configured application clock; delay must be non-negative.</summary>
    Task<Guid> ScheduleAfterAsync<TArgs>(
        TArgs request,
        TimeSpan delay,
        JobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates a typed recurring definition and returns the persisted cron-definition identifier.</summary>
    Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        CancellationToken cancellationToken = default
    );

    Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<Guid> ScheduleAsync<TJob>(DateTime executionTime, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<Guid> ScheduleAsync<TJob>(
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ScheduleKeyedAsync<TJob>(
        JobKey key,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ScheduleKeyedAsync<TJob>(
        JobKey key,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ReplaceKeyedAsync<TJob>(
        JobKey key,
        long expectedGeneration,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    Task<JobScheduleResult> ReplaceKeyedAsync<TJob>(
        JobKey key,
        long expectedGeneration,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Enqueues a job that takes no arguments for immediate execution and returns its persisted entity identifier.</summary>
    /// <typeparam name="TJob">The <c>[Job]</c> class to run.</typeparam>
    /// <exception cref="Exceptions.JobFunctionNotFoundException"><typeparamref name="TJob"/> is not a registered job.</exception>
    Task<Guid> EnqueueAsync<TJob>(CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <inheritdoc cref="EnqueueAsync{TJob}(CancellationToken)"/>
    Task<Guid> EnqueueAsync<TJob>(JobOptions? options, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>Schedules a one-shot run of a job that takes no arguments and returns its persisted entity identifier.</summary>
    /// <typeparam name="TJob">The <c>[Job]</c> class to run.</typeparam>
    /// <exception cref="Exceptions.JobFunctionNotFoundException"><typeparamref name="TJob"/> is not a registered job.</exception>
    Task<Guid> ScheduleAsync<TJob>(DateTimeOffset executionTime, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <inheritdoc cref="ScheduleAsync{TJob}(DateTimeOffset, CancellationToken)"/>
    Task<Guid> ScheduleAsync<TJob>(
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Schedules a one-shot run of a job that takes no arguments relative to the configured application clock; delay must be non-negative.</summary>
    /// <typeparam name="TJob">The <c>[Job]</c> class to run.</typeparam>
    Task<Guid> ScheduleAfterAsync<TJob>(TimeSpan delay, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <inheritdoc cref="ScheduleAfterAsync{TJob}(TimeSpan, CancellationToken)"/>
    Task<Guid> ScheduleAfterAsync<TJob>(
        TimeSpan delay,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Creates a recurring definition for a job that takes no arguments and returns the persisted cron-definition identifier.</summary>
    /// <typeparam name="TJob">The <c>[Job]</c> class to run.</typeparam>
    Task<Guid> ScheduleRecurringAsync<TJob>(string cronExpression, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <inheritdoc cref="ScheduleRecurringAsync{TJob}(string, CancellationToken)"/>
    Task<Guid> ScheduleRecurringAsync<TJob>(
        string cronExpression,
        RecurringJobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Schedules a durable keyed intent for a job that takes no arguments at an absolute instant.</summary>
    /// <remarks>
    /// Within the key scope, intent consists of contract version, exact durable request bytes after middleware,
    /// and the UTC due instant truncated to microseconds.
    /// Retry and node-death policy differences, including explicit overrides, preserve the existing generation's
    /// captured policy. Options validation still applies.
    /// </remarks>
    Task<JobScheduleResult> ScheduleKeyedAsync<TJob>(
        JobKey key,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <inheritdoc cref="ScheduleKeyedAsync{TJob}(JobKey, DateTimeOffset, CancellationToken)"/>
    Task<JobScheduleResult> ScheduleKeyedAsync<TJob>(
        JobKey key,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <summary>Replaces or reschedules a pending, unclaimed observed generation of a job that takes no arguments.</summary>
    /// <remarks>A successful replacement captures the call's resolved execution policy in generation N+1, even when its intent equals generation N.</remarks>
    Task<JobScheduleResult> ReplaceKeyedAsync<TJob>(
        JobKey key,
        long expectedGeneration,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;

    /// <inheritdoc cref="ReplaceKeyedAsync{TJob}(JobKey, long, DateTimeOffset, CancellationToken)"/>
    Task<JobScheduleResult> ReplaceKeyedAsync<TJob>(
        JobKey key,
        long expectedGeneration,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
        where TJob : IJob;
}
