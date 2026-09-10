// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal sealed partial class JobScheduler<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ScheduleKeyedAsync(
        JobKey key,
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ScheduleKeyedAsync(
        JobKey key,
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ReplaceKeyedAsync(
        JobKey key,
        long expectedGeneration,
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );

    /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
    [Obsolete(
        "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
        error: true
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<JobScheduleResult> ReplaceKeyedAsync(
        JobKey key,
        long expectedGeneration,
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
        );
}
