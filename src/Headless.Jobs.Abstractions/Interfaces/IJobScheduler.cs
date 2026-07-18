// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Schedules generated <c>[JobFunction]</c> handlers without requiring callers to construct persistence entities or
/// copy durable function-name strings.
/// </summary>
[PublicAPI]
public interface IJobScheduler
{
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

    /// <summary>Enqueues a typed job for immediate execution and returns its persisted entity identifier.</summary>
    Task<Guid> EnqueueAsync<TArgs>(
        TArgs request,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enqueues a requestless job for immediate execution and returns its persisted entity identifier.</summary>
    Task<Guid> EnqueueAsync(
        JobFunctionDescriptor descriptor,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Schedules a typed one-shot job and returns its persisted entity identifier.</summary>
    Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Schedules a requestless one-shot job and returns its persisted entity identifier.</summary>
    Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates a typed recurring definition and returns the persisted cron-definition identifier.</summary>
    Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates a requestless recurring definition and returns the persisted cron-definition identifier.</summary>
    Task<Guid> ScheduleRecurringAsync(
        JobFunctionDescriptor descriptor,
        string cronExpression,
        RecurringJobOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
