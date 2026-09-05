// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal sealed partial class JobScheduler<TTimeJob, TCronJob> : IJobScheduler
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
    private readonly JobsRequestSerializationOptions _serializationOptions;
    private readonly int _maxChainDepth;
    private readonly TimeProvider? _timeProvider;
    private readonly JobSchedulingPolicies _policies;

    public JobScheduler(
        ITimeJobManager<TTimeJob> timeJobManager,
        ICronJobManager<TCronJob> cronJobManager,
        JobFunctionRegistry functionRegistry,
        IInternalJobManager internalJobManager,
        IJobsHostScheduler jobsHostScheduler,
        JobsRequestSerializationOptions serializationOptions,
        TimeProvider timeProvider,
        JobSchedulingPolicies policies,
        SchedulerOptionsBuilder? schedulerOptions = null
    )
        : this(
            timeJobManager,
            cronJobManager,
            functionRegistry.DescriptorsByRequestType.GetValueOrDefault,
            functionRegistry.Descriptors.GetValueOrDefault,
            internalJobManager,
            jobsHostScheduler,
            functionRegistry.CanonicalDescriptors.GetValueOrDefault,
            serializationOptions,
            schedulerOptions?.MaxChainDepth ?? SchedulerOptionsBuilder.DefaultMaxChainDepth,
            timeProvider,
            policies
        ) { }

    internal JobScheduler(
        ITimeJobManager<TTimeJob> timeJobManager,
        ICronJobManager<TCronJob> cronJobManager,
        Func<Type, JobFunctionDescriptor?> descriptorByRequestType,
        Func<string, JobFunctionDescriptor?> descriptorByName,
        IInternalJobManager internalJobManager,
        IJobsHostScheduler jobsHostScheduler,
        Func<string, JobFunctionDescriptor?>? canonicalDescriptorByName = null,
        JobsRequestSerializationOptions? serializationOptions = null,
        int maxChainDepth = SchedulerOptionsBuilder.DefaultMaxChainDepth,
        TimeProvider? timeProvider = null,
        JobSchedulingPolicies? policies = null
    )
    {
        _timeJobManager = Argument.IsNotNull(timeJobManager);
        _cronJobManager = Argument.IsNotNull(cronJobManager);
        _internalJobManager = Argument.IsNotNull(internalJobManager);
        _jobsHostScheduler = Argument.IsNotNull(jobsHostScheduler);
        _descriptorByRequestType = Argument.IsNotNull(descriptorByRequestType);
        _descriptorByName = Argument.IsNotNull(descriptorByName);
        _canonicalDescriptorByName = canonicalDescriptorByName ?? descriptorByName;
        _serializationOptions = serializationOptions ?? JobsRequestSerializationOptions.Default;
        _maxChainDepth = Argument.IsPositive(maxChainDepth);
        _timeProvider = timeProvider;
        _policies = policies ?? JobSchedulingPolicies.Empty;
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

    public Task<Guid> EnqueueAsync<TArgs>(TArgs request, CancellationToken cancellationToken = default) =>
        EnqueueAsync(request, options: null, cancellationToken);

    public Task<Guid> EnqueueAsync<TArgs>(
        TArgs request,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync(_GetDescriptor<TArgs>(), request, executionTime: null, options, cancellationToken);
    }

    public Task<Guid> EnqueueAsync(JobFunctionDescriptor descriptor, CancellationToken cancellationToken = default) =>
        EnqueueAsync(descriptor, options: null, cancellationToken);

    public Task<Guid> EnqueueAsync(
        JobFunctionDescriptor descriptor,
        JobOptions? options,
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

    public async Task<Guid> EnqueueAsync(JobChain chain, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(chain);

        // Validate the whole tree first (depth, then per-node descriptor resolution) so nothing is persisted when any
        // node is invalid — the manager's add path validates only the root, so per-node resolution closes that gap.
        // Depth was computed once when the builder froze the chain (JobChainBuilder.Build); read it instead of walking.
        var depth = chain.Depth;
        if (depth > _maxChainDepth)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The job chain has a depth of {depth} nodes, which exceeds the configured maximum chain depth of {_maxChainDepth} nodes (on-success and on-failure edges both count toward depth)."
                )
            );
        }

        // Map to a fresh TTimeJob tree on every call so re-enqueueing the same built chain yields independent trees;
        // one manager add persists the whole graph atomically via the existing tree-stamping + insert path.
        var root = _BuildChainEntity(chain.Root, runCondition: null);
        var persisted = await _timeJobManager.AddAsync(root, cancellationToken).ConfigureAwait(false);

        return persisted.Id;
    }

    public Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    ) => ScheduleAsync(request, executionTime, options: null, cancellationToken);

    public Task<Guid> ScheduleAsync<TArgs>(
        TArgs request,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync(
            _GetDescriptor<TArgs>(),
            request,
            executionTime.UtcDateTime,
            options,
            cancellationToken
        );
    }

    public Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTimeOffset executionTime,
        CancellationToken cancellationToken = default
    ) => ScheduleAsync(descriptor, executionTime, options: null, cancellationToken);

    public Task<Guid> ScheduleAsync(
        JobFunctionDescriptor descriptor,
        DateTimeOffset executionTime,
        JobOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _ScheduleTimeAsync<object?>(
            _GetRequestlessDescriptor(descriptor),
            request: null,
            executionTime.UtcDateTime,
            options,
            cancellationToken
        );
    }

    /// <summary>Schedules an ordinary one-shot job relative to the configured application clock; delay must be non-negative.</summary>
    public Task<Guid> ScheduleAfterAsync<TArgs>(
        TArgs request,
        TimeSpan delay,
        CancellationToken cancellationToken = default
    ) => ScheduleAfterAsync(request, delay, options: null, cancellationToken);

    /// <summary>Schedules an ordinary one-shot job relative to the configured application clock; delay must be non-negative.</summary>
    public Task<Guid> ScheduleAfterAsync<TArgs>(
        TArgs request,
        TimeSpan delay,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) => ScheduleAsync(request, _GetExecutionTime(delay), options, cancellationToken);

    /// <summary>Schedules an ordinary one-shot job relative to the configured application clock; delay must be non-negative.</summary>
    public Task<Guid> ScheduleAfterAsync(
        JobFunctionDescriptor descriptor,
        TimeSpan delay,
        CancellationToken cancellationToken = default
    ) => ScheduleAfterAsync(descriptor, delay, options: null, cancellationToken);

    /// <summary>Schedules an ordinary one-shot job relative to the configured application clock; delay must be non-negative.</summary>
    public Task<Guid> ScheduleAfterAsync(
        JobFunctionDescriptor descriptor,
        TimeSpan delay,
        JobOptions? options,
        CancellationToken cancellationToken = default
    ) => ScheduleAsync(descriptor, _GetExecutionTime(delay), options, cancellationToken);

    private DateTimeOffset _GetExecutionTime(TimeSpan delay)
    {
        Argument.IsGreaterThanOrEqualTo(delay, TimeSpan.Zero);
        var clock =
            _timeProvider ?? throw new InvalidOperationException("Job scheduling requires an injected TimeProvider.");
        return clock.GetUtcNow().Add(delay);
    }

    public Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        CancellationToken cancellationToken = default
    ) => ScheduleRecurringAsync(request, cronExpression, options: null, cancellationToken);

    public Task<Guid> ScheduleRecurringAsync<TArgs>(
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options,
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
        CancellationToken cancellationToken = default
    ) => ScheduleRecurringAsync(descriptor, cronExpression, options: null, cancellationToken);

    public Task<Guid> ScheduleRecurringAsync(
        JobFunctionDescriptor descriptor,
        string cronExpression,
        RecurringJobOptions? options,
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
        JobOptions? options,
        CancellationToken cancellationToken
    )
    {
        options = _policies.Resolve(descriptor, options);
        var entity = new TTimeJob
        {
            Function = descriptor.FunctionName,
            ContractVersion = descriptor.ContractVersion,
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Request =
                descriptor.RequestType == null ? null : JobsHelper.CreateJobRequest(request, _serializationOptions),
            ExecutionTime = executionTime,
            Description = options?.Description,
            Retries = options?.Retries ?? 0,
            RetryIntervals = options?.RetryIntervals,
            OnNodeDeath = options?.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
            TenantId = options?.TenantId,
            IsSystemJob = options?.IsSystemJob ?? false,
            RequireAtomicEnlistment = options?.RequireAtomicEnlistment ?? false,
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
        var policy = _policies.Resolve(
            descriptor,
            new JobOptions
            {
                Retries = options?.Retries,
                RetryIntervals = options?.RetryIntervals,
                OnNodeDeath = options?.OnNodeDeath,
            }
        );
        if (policy.RequireAtomicEnlistment)
        {
            throw new NotSupportedException(
                "Required atomic enlistment is not supported for recurring definitions. Configure required atomic policy only for one-shot functions."
            );
        }
        var entity = new TCronJob
        {
            Function = descriptor.FunctionName,
            ContractVersion = descriptor.ContractVersion,
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Request =
                descriptor.RequestType == null ? null : JobsHelper.CreateJobRequest(request, _serializationOptions),
            Expression = cronExpression,
            Description = options?.Description,
            TimeZoneId = options?.TimeZoneId,
            Retries = policy.Retries ?? 0,
            RetryIntervals = policy.RetryIntervals,
            OnNodeDeath = policy.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
        };

        var persisted = await _cronJobManager.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return persisted.Id;
    }

    private TTimeJob _BuildChainEntity(JobChainNode node, Enums.RunCondition? runCondition)
    {
        var descriptor = _ResolveNodeDescriptor(node);
        var options = _policies.Resolve(descriptor, node.Options);

        var entity = new TTimeJob
        {
            Function = descriptor.FunctionName,
            ContractVersion = descriptor.ContractVersion,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            Request = descriptor.RequestType is null
                ? null
                : JobsHelper.CreateJobRequest(node.Payload!, descriptor.RequestType, _serializationOptions),
            ExecutionTime = node.ExecutionTime?.UtcDateTime,
            Description = options.Description,
            Retries = options.Retries ?? 0,
            RetryIntervals = options.RetryIntervals,
            OnNodeDeath = options.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
            RequireAtomicEnlistment = options.RequireAtomicEnlistment,
            TenantId = options.TenantId,
            IsSystemJob = options.IsSystemJob,
            RunCondition = runCondition,
        };

        if (node.OnSuccess is not null)
        {
            entity.Children.Add(_BuildChainEntity(node.OnSuccess, Enums.RunCondition.OnSuccess));
        }

        if (node.OnFailure is not null)
        {
            entity.Children.Add(_BuildChainEntity(node.OnFailure, Enums.RunCondition.OnFailure));
        }

        return entity;
    }

    private JobFunctionDescriptor _ResolveNodeDescriptor(JobChainNode node)
    {
        if (node.Descriptor is not null)
        {
            return _GetRequestlessDescriptor(node.Descriptor);
        }

        var requestType = node.PayloadType!;
        return _descriptorByRequestType(requestType) ?? throw new JobFunctionNotFoundException(requestType);
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
