// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>Ordering constants for Jobs middleware. Lower values run first.</summary>
public static class JobMiddlewarePriority
{
    /// <summary>Runs before the default middleware position.</summary>
    public const int Early = -1000;

    /// <summary>
    /// Position of the framework tenancy middleware. Ties with <see cref="Early"/>: no framework middleware registers
    /// at <see cref="Early"/> today, so a tie is possible only with consumer-declared middleware and resolves
    /// deterministically by ordinal middleware identity (<c>{assembly}:{fully-qualified-type}</c>). Relative order
    /// against consumer <see cref="Early"/> middleware is not contractual.
    /// </summary>
    public const int Tenancy = -1000;

    /// <summary>The default middleware position.</summary>
    public const int Default = 0;

    /// <summary>Runs after the default middleware position.</summary>
    public const int Late = 1000;
}

/// <summary>Declares schedule middleware globally or on a <c>[Job]</c> class in the same assembly.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class JobScheduleMiddlewareAttribute<TMiddleware> : Attribute
    where TMiddleware : IJobScheduleMiddleware
{
    /// <summary>
    /// Targets a function declared in another assembly. Omit for global assembly middleware or
    /// class-level middleware, whose target is the <c>[Job]</c> class it decorates.
    /// </summary>
    public string? Function { get; init; }

    /// <summary>Ordering priority; equal priorities are ordered by stable middleware identity.</summary>
    public int Priority { get; init; }
}

/// <summary>Declares execute middleware globally or on a <c>[Job]</c> class in the same assembly.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class JobExecuteMiddlewareAttribute<TMiddleware> : Attribute
    where TMiddleware : IJobExecuteMiddleware
{
    /// <summary>
    /// Targets a function declared in another assembly. Omit for global assembly middleware or
    /// class-level middleware, whose target is the <c>[Job]</c> class it decorates.
    /// </summary>
    public string? Function { get; init; }

    /// <summary>Ordering priority; equal priorities are ordered by stable middleware identity.</summary>
    public int Priority { get; init; }
}

/// <summary>Generated assembly metadata that exposes a durable job-function identity to consuming generators.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class JobFunctionDescriptorMetadataAttribute(
    string functionName,
    string contractVersion = JobContract.InitialVersion
) : Attribute
{
    /// <summary>Payload schema version of the generated durable contract.</summary>
    public string ContractVersion { get; } = JobContract.ValidateVersion(contractVersion);

    /// <summary>The generated durable function name.</summary>
    public string FunctionName { get; } = functionName ?? throw new ArgumentNullException(nameof(functionName));
}

/// <summary>Continuation for schedule middleware.</summary>
public delegate Task JobScheduleNext(CancellationToken cancellationToken = default);

/// <summary>Continuation for execute middleware.</summary>
public delegate Task JobExecuteNext(CancellationToken cancellationToken = default);

/// <summary>Generated callback for one schedule middleware declaration.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public delegate Task JobScheduleMiddlewareDispatch(
    JobScheduleContext context,
    JobScheduleNext next,
    CancellationToken cancellationToken
);

/// <summary>Generated callback for one execute middleware declaration.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public delegate Task JobExecuteMiddlewareDispatch(
    JobExecuteContext context,
    JobExecuteNext next,
    CancellationToken cancellationToken
);

/// <summary>Middleware invoked once for every submitted scheduling entity.</summary>
public interface IJobScheduleMiddleware
{
    /// <summary>Invokes this middleware and, when accepted, the next pipeline component.</summary>
    Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken = default);
}

/// <summary>Middleware invoked once for every handler execution attempt.</summary>
public interface IJobExecuteMiddleware
{
    /// <summary>Invokes this middleware and, when accepted, the next pipeline component.</summary>
    Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken = default);
}

/// <summary>Mutable scheduling state exposed to schedule middleware.</summary>
public sealed class JobScheduleContext(JobFunctionDescriptor descriptor, BaseJobEntity job, IServiceProvider services)
{
    /// <summary>The resolved immutable descriptor for the submitted job.</summary>
    public JobFunctionDescriptor Descriptor { get; } = descriptor;

    /// <summary>The time or cron entity that will be validated and persisted after the pipeline completes.</summary>
    public BaseJobEntity Job { get; } = job;

    /// <summary>The bounded scheduling-invocation service provider.</summary>
    public IServiceProvider Services { get; } = services;
}

/// <summary>Per-attempt execution state exposed to execute middleware.</summary>
public sealed class JobExecuteContext(
    JobFunctionDescriptor descriptor,
    JobExecutionState execution,
    JobContext functionContext,
    int attempt,
    IServiceProvider services
)
{
    /// <summary>The resolved immutable descriptor for the executing job.</summary>
    public JobFunctionDescriptor Descriptor { get; } = descriptor;

    /// <summary>The mutable execution state owned by the existing execution flow.</summary>
    public JobExecutionState Execution { get; } = execution;

    /// <summary>The existing handler context for this attempt.</summary>
    public JobContext FunctionContext { get; } = functionContext;

    /// <summary>Zero-based retry attempt number.</summary>
    public int Attempt { get; } = attempt;

    /// <summary>The bounded attempt service provider.</summary>
    public IServiceProvider Services { get; } = services;
}

/// <summary>
/// One host's frozen middleware chain, ordered by priority and then by stable middleware identity. Built once with the
/// host's job registry, so two hosts in one process never share or reorder each other's middleware.
/// </summary>
internal sealed class JobMiddlewarePipeline
{
    internal static readonly JobMiddlewarePipeline Empty = new([], []);

    private readonly ApplicableMiddleware<JobScheduleMiddlewareRegistration> _schedule;
    private readonly ApplicableMiddleware<JobExecuteMiddlewareRegistration> _execute;

    private JobMiddlewarePipeline(
        JobScheduleMiddlewareRegistration[] schedule,
        JobExecuteMiddlewareRegistration[] execute
    )
    {
        _schedule = new(schedule);
        _execute = new(execute);
    }

    internal static JobMiddlewarePipeline Create(
        IEnumerable<JobScheduleMiddlewareRegistration> schedule,
        IEnumerable<JobExecuteMiddlewareRegistration> execute
    ) => new(_Order(schedule), _Order(execute));

    internal Task DispatchScheduleAsync(
        JobScheduleContext context,
        JobScheduleNext next,
        CancellationToken cancellationToken
    )
    {
        var registrations = _schedule.For(context.Descriptor.FunctionName);
        var current = next;
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            var registration = registrations[index];
            var previous = current;
            current = token => registration.Dispatch(context, previous, token);
        }

        return current(cancellationToken);
    }

    internal Task DispatchExecuteAsync(
        JobExecuteContext context,
        JobExecuteNext next,
        CancellationToken cancellationToken
    )
    {
        var registrations = _execute.For(context.Descriptor.FunctionName);
        var current = next;
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            var registration = registrations[index];
            var previous = current;
            current = token => registration.Dispatch(context, previous, token);
        }

        return current(cancellationToken);
    }

    private static T[] _Order<T>(IEnumerable<T> registrations)
        where T : IJobMiddlewareRegistration =>
        [.. registrations.OrderBy(x => x.Priority).ThenBy(x => x.Identity, StringComparer.Ordinal)];

    /// <summary>
    /// The ordered middleware that applies to each job, resolved once when the pipeline is built so a dispatch neither
    /// filters nor wraps middleware limited to other jobs.
    /// </summary>
    private sealed class ApplicableMiddleware<T>(T[] ordered)
        where T : IJobMiddlewareRegistration
    {
        // A job no middleware names gets only the global middleware, so only the named jobs need their own chain.
        private readonly T[] _global = [.. ordered.Where(x => x.Function is null)];

        private readonly FrozenDictionary<string, T[]> _byFunction = ordered
            .Where(x => x.Function is not null)
            .Select(x => x.Function!)
            .Distinct(StringComparer.Ordinal)
            .ToFrozenDictionary(
                function => function,
                function =>
                    ordered
                        .Where(x => x.Function is null || string.Equals(x.Function, function, StringComparison.Ordinal))
                        .ToArray(),
                StringComparer.Ordinal
            );

        public T[] For(string function) =>
            _byFunction.TryGetValue(function, out var registrations) ? registrations : _global;
    }
}

internal interface IJobMiddlewareRegistration
{
    string Identity { get; }
    string? Function { get; }
    int Priority { get; }
}

internal sealed record JobScheduleMiddlewareRegistration(
    string Identity,
    string? Function,
    int Priority,
    JobScheduleMiddlewareDispatch Dispatch
) : IJobMiddlewareRegistration;

internal sealed record JobExecuteMiddlewareRegistration(
    string Identity,
    string? Function,
    int Priority,
    JobExecuteMiddlewareDispatch Dispatch
) : IJobMiddlewareRegistration;
