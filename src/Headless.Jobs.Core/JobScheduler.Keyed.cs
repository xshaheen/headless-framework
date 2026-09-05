// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal sealed partial class JobScheduler<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public Task<JobScheduleResult> ScheduleKeyedAsync<TArgs>(
        JobKey key,
        TArgs request,
        DateTimeOffset executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleKeyedAsync(
            key,
            _GetKeyedDescriptor<TArgs>(),
            request,
            executionTime,
            options,
            expectedGeneration: null,
            cancellationToken
        );

    public Task<JobScheduleResult> ScheduleKeyedAsync(
        JobKey key,
        JobFunctionDescriptor descriptor,
        DateTimeOffset executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleKeyedAsync<object?>(
            key,
            _GetRequestlessDescriptor(descriptor),
            request: null,
            executionTime,
            options,
            expectedGeneration: null,
            cancellationToken
        );

    public Task<JobScheduleResult> ReplaceKeyedAsync<TArgs>(
        JobKey key,
        long expectedGeneration,
        TArgs request,
        DateTimeOffset executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleKeyedAsync(
            key,
            _GetKeyedDescriptor<TArgs>(),
            request,
            executionTime,
            options,
            expectedGeneration,
            cancellationToken
        );

    public Task<JobScheduleResult> ReplaceKeyedAsync(
        JobKey key,
        long expectedGeneration,
        JobFunctionDescriptor descriptor,
        DateTimeOffset executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleKeyedAsync<object?>(
            key,
            _GetRequestlessDescriptor(descriptor),
            request: null,
            executionTime,
            options,
            expectedGeneration,
            cancellationToken
        );

    public Task<JobScheduleResult> CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken = default
    ) => _timeJobManager.CancelKeyedAsync(scope, key, expectedGeneration, cancellationToken);

    private JobFunctionDescriptor _GetKeyedDescriptor<TArgs>()
    {
        if (typeof(TArgs) == typeof(JobChain))
        {
            throw new NotSupportedException(
                "Keyed JobChain scheduling and control are unsupported. A JobChain is a static conditional continuation tree."
            );
        }

        return _GetDescriptor<TArgs>();
    }

    private Task<JobScheduleResult> _ScheduleKeyedAsync<TArgs>(
        JobKey key,
        JobFunctionDescriptor descriptor,
        TArgs request,
        DateTimeOffset executionTime,
        EnqueueOptions? options,
        long? expectedGeneration,
        CancellationToken cancellationToken
    )
    {
        var entity = new TTimeJob
        {
            Function = descriptor.FunctionName,
            ContractVersion = descriptor.ContractVersion,
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Request = descriptor.RequestType is null
                ? null
                : JobsHelper.CreateJobRequest(request, _serializationOptions),
            ExecutionTime = executionTime.UtcDateTime,
            Description = options?.Description,
            Retries = options?.Retries ?? 0,
            RetryIntervals = options?.RetryIntervals?.ToArray(),
            OnNodeDeath = options?.OnNodeDeath ?? NodeDeathPolicy.Retry,
            TenantId = options?.TenantId,
            IsSystemJob = options?.IsSystemJob ?? false,
        };
        return _timeJobManager.ScheduleKeyedAsync(key, entity, expectedGeneration, cancellationToken);
    }
}
