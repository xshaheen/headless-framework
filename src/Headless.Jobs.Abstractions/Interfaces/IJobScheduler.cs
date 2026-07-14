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
