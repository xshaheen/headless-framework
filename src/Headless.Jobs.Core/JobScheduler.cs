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
    private readonly IInternalJobManager _internalJobManager;
    private readonly IJobsHostScheduler _jobsHostScheduler;
    private readonly Func<Type, JobFunctionDescriptor?> _descriptorByRequestType;
    private readonly Func<string, JobFunctionDescriptor?> _descriptorByName;
    private readonly Func<string, JobFunctionDescriptor?> _canonicalDescriptorByName;

    public JobScheduler(
        ITimeJobManager<TTimeJob> timeJobManager,
        ICronJobManager<TCronJob> cronJobManager,
        JobFunctionRegistry functionRegistry,
        IInternalJobManager internalJobManager,
        IJobsHostScheduler jobsHostScheduler
    )
        : this(
            timeJobManager,
            cronJobManager,
            functionRegistry.DescriptorsByRequestType.GetValueOrDefault,
            functionRegistry.Descriptors.GetValueOrDefault,
            internalJobManager,
            jobsHostScheduler,
            functionRegistry.CanonicalDescriptors.GetValueOrDefault
        ) { }

    internal JobScheduler(
        ITimeJobManager<TTimeJob> timeJobManager,
        ICronJobManager<TCronJob> cronJobManager,
        Func<Type, JobFunctionDescriptor?> descriptorByRequestType,
        Func<string, JobFunctionDescriptor?> descriptorByName,
        IInternalJobManager internalJobManager,
        IJobsHostScheduler jobsHostScheduler,
        Func<string, JobFunctionDescriptor?>? canonicalDescriptorByName = null
    )
    {
        _timeJobManager = Argument.IsNotNull(timeJobManager);
        _cronJobManager = Argument.IsNotNull(cronJobManager);
        _internalJobManager = Argument.IsNotNull(internalJobManager);
        _jobsHostScheduler = Argument.IsNotNull(jobsHostScheduler);
        _descriptorByRequestType = Argument.IsNotNull(descriptorByRequestType);
        _descriptorByName = Argument.IsNotNull(descriptorByName);
        _canonicalDescriptorByName = canonicalDescriptorByName ?? descriptorByName;
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var accepted = await _internalJobManager
            .RequestTimeJobCancellationAsync(jobId, cancellationToken)
            .ConfigureAwait(false);
        if (accepted)
        {
            _jobsHostScheduler.Restart();
        }

        return accepted;
    }

    public async Task<bool> PauseCronAsync(Guid cronJobId, CancellationToken cancellationToken = default)
    {
        var accepted = await _internalJobManager.PauseCronJobAsync(cronJobId, cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            _jobsHostScheduler.Restart();
        }

        return accepted;
    }

    public async Task<bool> ResumeCronAsync(Guid cronJobId, CancellationToken cancellationToken = default)
    {
        var accepted = await _internalJobManager.ResumeCronJobAsync(cronJobId, cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            _jobsHostScheduler.Restart();
        }

        return accepted;
    }

    public Task<Guid> EnqueueAsync<TArgs>(
        TArgs request,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync(_GetDescriptor<TArgs>(), request, executionTime: null, options, cancellationToken);
    }

    public Task<Guid> EnqueueAsync(
        JobFunctionDescriptor descriptor,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync<object?>(
            _GetRequestlessDescriptor(descriptor),
            request: null,
            executionTime: null,
            options,
            cancellationToken
        );
    }

    public Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTime executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync(_GetDescriptor<TArgs>(), request, executionTime, options, cancellationToken);
    }

    public Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTime executionTime,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync<object?>(
            _GetRequestlessDescriptor(descriptor),
            request: null,
            executionTime,
            options,
            cancellationToken
        );
    }

    public Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleRecurringAsync(
            _GetDescriptor<TArgs>(),
            request,
            Argument.IsNotNullOrWhiteSpace(cronExpression),
            options,
            cancellationToken
        );
    }

    public Task<Guid> ScheduleRecurringAsync(
        JobFunctionDescriptor descriptor,
        string cronExpression,
        RecurringJobOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleRecurringAsync<object?>(
            _GetRequestlessDescriptor(descriptor),
            request: null,
            Argument.IsNotNullOrWhiteSpace(cronExpression),
            options,
            cancellationToken
        );
    }

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
