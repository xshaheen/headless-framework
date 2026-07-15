// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal sealed class JobScheduler<TTimeJob, TCronJob> : IJobScheduler
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly ITimeJobManager<TTimeJob> _timeJobManager;
    private readonly ICronJobManager<TCronJob> _cronJobManager;
    private readonly Func<Type, JobFunctionDescriptor?> _descriptorByRequestType;
    private readonly Func<string, JobFunctionDescriptor?> _descriptorByName;
    private readonly Func<string, JobFunctionDescriptor?> _canonicalDescriptorByName;

    public JobScheduler(
        ITimeJobManager<TTimeJob> timeJobManager,
        ICronJobManager<TCronJob> cronJobManager,
        JobFunctionRegistry functionRegistry
    )
        : this(
            timeJobManager,
            cronJobManager,
            functionRegistry.DescriptorsByRequestType.GetValueOrDefault,
            functionRegistry.Descriptors.GetValueOrDefault,
            functionRegistry.CanonicalDescriptors.GetValueOrDefault
        ) { }

    internal JobScheduler(
        ITimeJobManager<TTimeJob> timeJobManager,
        ICronJobManager<TCronJob> cronJobManager,
        Func<Type, JobFunctionDescriptor?> descriptorByRequestType,
        Func<string, JobFunctionDescriptor?> descriptorByName,
        Func<string, JobFunctionDescriptor?>? canonicalDescriptorByName = null
    )
    {
        _timeJobManager = Argument.IsNotNull(timeJobManager);
        _cronJobManager = Argument.IsNotNull(cronJobManager);
        _descriptorByRequestType = Argument.IsNotNull(descriptorByRequestType);
        _descriptorByName = Argument.IsNotNull(descriptorByName);
        _canonicalDescriptorByName = canonicalDescriptorByName ?? descriptorByName;
    }

    public Task<Guid> EnqueueAsync<TArgs>(
        TArgs request,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _ScheduleTimeAsync(_GetDescriptor<TArgs>(), request, null, options, cancellationToken);

    public Task<Guid> EnqueueAsync(
        JobFunctionDescriptor descriptor,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _ScheduleTimeAsync<object?>(_GetRequestlessDescriptor(descriptor), null, null, options, cancellationToken);

    public Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _ScheduleTimeAsync(_GetDescriptor<TArgs>(), request, executionTime, options, cancellationToken);

    public Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleTimeAsync<object?>(
            _GetRequestlessDescriptor(descriptor),
            null,
            executionTime,
            options,
            cancellationToken
        );

    public Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleRecurringAsync(
            _GetDescriptor<TArgs>(),
            request,
            Argument.IsNotNullOrWhiteSpace(cronExpression),
            options,
            cancellationToken
        );

    public Task<Guid> ScheduleRecurringAsync(
        JobFunctionDescriptor descriptor,
        string cronExpression,
        RecurringJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _ScheduleRecurringAsync<object?>(
            _GetRequestlessDescriptor(descriptor),
            null,
            Argument.IsNotNullOrWhiteSpace(cronExpression),
            options,
            cancellationToken
        );

    private async Task<Guid> _ScheduleTimeAsync<TArgs>(
        JobFunctionDescriptor descriptor,
        TArgs request,
        DateTime? executionTime,
        EnqueueOptions? options,
        CancellationToken cancellationToken
    )
    {
        var entity = new TTimeJob
        {
            Function = descriptor.FunctionName,
            Request = descriptor.RequestType == null ? null : JobsHelper.CreateJobRequest(request),
            ExecutionTime = executionTime,
            Description = options?.Description,
            Retries = options?.Retries ?? 0,
            RetryIntervals = options?.RetryIntervals is { } intervals ? [.. intervals] : null,
            OnNodeDeath = options?.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
        };

        var persisted = await _timeJobManager.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return persisted.Id;
    }

    private async Task<Guid> _ScheduleRecurringAsync<TArgs>(
        JobFunctionDescriptor descriptor,
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options,
        CancellationToken cancellationToken
    )
    {
        var entity = new TCronJob
        {
            Function = descriptor.FunctionName,
            Request = descriptor.RequestType == null ? null : JobsHelper.CreateJobRequest(request),
            Expression = cronExpression,
            Description = options?.Description,
            Retries = options?.Retries ?? 0,
            RetryIntervals = options?.RetryIntervals is { } intervals ? [.. intervals] : null,
            OnNodeDeath = options?.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
        };

        var persisted = await _cronJobManager.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return persisted.Id;
    }

    private JobFunctionDescriptor _GetDescriptor<TArgs>()
    {
        var requestType = typeof(TArgs);
        return _descriptorByRequestType(requestType) ?? throw new JobFunctionNotFoundException(requestType);
    }

    private JobFunctionDescriptor _GetRequestlessDescriptor(JobFunctionDescriptor descriptor)
    {
        Argument.IsNotNull(descriptor);

        if (descriptor.RequestType != null)
        {
            throw new ArgumentException(
                "Typed job functions must be scheduled through a typed request overload.",
                nameof(descriptor)
            );
        }

        var registered = _descriptorByName(descriptor.FunctionName);
        var canonical = _canonicalDescriptorByName(descriptor.FunctionName);
        return canonical == descriptor && registered != null
            ? registered
            : throw new JobFunctionNotFoundException(descriptor.FunctionName);
    }
}
