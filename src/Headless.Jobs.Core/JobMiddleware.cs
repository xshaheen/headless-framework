// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>Ordering constants for Jobs middleware. Lower values run first.</summary>
public static class JobMiddlewarePriority
{
    /// <summary>Runs before the default middleware position.</summary>
    public const int Early = -1000;

    /// <summary>The default middleware position.</summary>
    public const int Default = 0;

    /// <summary>Runs after the default middleware position.</summary>
    public const int Late = 1000;
}

/// <summary>Declares schedule middleware globally or beside a local <c>[JobFunction]</c> method.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method, AllowMultiple = true)]
public sealed class JobScheduleMiddlewareAttribute<TMiddleware> : Attribute
    where TMiddleware : IJobScheduleMiddleware
{
    /// <summary>
    /// Targets a function declared in another assembly. Omit for global assembly middleware or
    /// method-local middleware, whose target is derived from its neighboring <c>[JobFunction]</c>.
    /// </summary>
    public string? Function { get; init; }

    /// <summary>Ordering priority; equal priorities are ordered by stable middleware identity.</summary>
    public int Priority { get; init; }
}

/// <summary>Declares execute middleware globally or beside a local <c>[JobFunction]</c> method.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method, AllowMultiple = true)]
public sealed class JobExecuteMiddlewareAttribute<TMiddleware> : Attribute
    where TMiddleware : IJobExecuteMiddleware
{
    /// <summary>
    /// Targets a function declared in another assembly. Omit for global assembly middleware or
    /// method-local middleware, whose target is derived from its neighboring <c>[JobFunction]</c>.
    /// </summary>
    public string? Function { get; init; }

    /// <summary>Ordering priority; equal priorities are ordered by stable middleware identity.</summary>
    public int Priority { get; init; }
}

/// <summary>Generated assembly metadata that exposes a durable job-function identity to consuming generators.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class JobFunctionDescriptorMetadataAttribute(string functionName) : Attribute
{
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
    JobFunctionContext functionContext,
    int attempt,
    IServiceProvider services
)
{
    /// <summary>The resolved immutable descriptor for the executing job.</summary>
    public JobFunctionDescriptor Descriptor { get; } = descriptor;

    /// <summary>The mutable execution state owned by the existing execution flow.</summary>
    public JobExecutionState Execution { get; } = execution;

    /// <summary>The existing handler context for this attempt.</summary>
    public JobFunctionContext FunctionContext { get; } = functionContext;

    /// <summary>Zero-based retry attempt number.</summary>
    public int Attempt { get; } = attempt;

    /// <summary>The bounded attempt service provider.</summary>
    public IServiceProvider Services { get; } = services;
}

/// <summary>Frozen generated callback registry for Jobs middleware dispatch.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class JobMiddlewareRegistry
{
    private static bool _frozen;
    private static readonly List<ScheduleRegistration> _ScheduleRegistrations = [];
    private static readonly List<ExecuteRegistration> _ExecuteRegistrations = [];
    private static ScheduleRegistration[] _schedule = [];
    private static ExecuteRegistration[] _execute = [];

    /// <summary>Registers generated schedule dispatch before <see cref="JobFunctionProvider.Build"/>.</summary>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    public static void RegisterSchedule(
        string identity,
        string? function,
        int priority,
        JobScheduleMiddlewareDispatch dispatch
    )
    {
        JobFunctionProvider.RegisterMiddleware(() =>
            _ScheduleRegistrations.Add(new(identity, function, priority, dispatch))
        );
    }

    /// <summary>Registers generated execute dispatch before <see cref="JobFunctionProvider.Build"/>.</summary>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    public static void RegisterExecute(
        string identity,
        string? function,
        int priority,
        JobExecuteMiddlewareDispatch dispatch
    )
    {
        JobFunctionProvider.RegisterMiddleware(() =>
            _ExecuteRegistrations.Add(new(identity, function, priority, dispatch))
        );
    }

    internal static Task DispatchScheduleAsync(
        JobScheduleContext context,
        JobScheduleNext next,
        CancellationToken cancellationToken
    ) => _DispatchSchedule(context, next, cancellationToken);

    internal static Task DispatchExecuteAsync(
        JobExecuteContext context,
        JobExecuteNext next,
        CancellationToken cancellationToken
    ) => _DispatchExecute(context, next, cancellationToken);

    internal static void FreezeUnderProviderLock()
    {
        _schedule = _Order(_ScheduleRegistrations);
        _execute = _Order(_ExecuteRegistrations);
        _frozen = true;
    }

    // The generated registrations are process-global. Unit tests that exercise alternate generated chains reset this
    // state inside the non-parallel Jobs helper collection, keeping the production API frozen after startup.
    internal static void ResetUnderProviderLock()
    {
        _frozen = false;
        _ScheduleRegistrations.Clear();
        _ExecuteRegistrations.Clear();
        _schedule = [];
        _execute = [];
    }

    private static Task _DispatchSchedule(
        JobScheduleContext context,
        JobScheduleNext next,
        CancellationToken cancellationToken
    )
    {
        var registrations = _frozen ? _schedule : _Order(_ScheduleRegistrations);
        JobScheduleNext current = next;
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            var registration = registrations[index];
            var previous = current;
            current = token =>
                registration.Function is null
                || string.Equals(registration.Function, context.Descriptor.FunctionName, StringComparison.Ordinal)
                    ? registration.Dispatch(context, previous, token)
                    : previous(token);
        }

        return current(cancellationToken);
    }

    private static Task _DispatchExecute(
        JobExecuteContext context,
        JobExecuteNext next,
        CancellationToken cancellationToken
    )
    {
        var registrations = _frozen ? _execute : _Order(_ExecuteRegistrations);
        JobExecuteNext current = next;
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            var registration = registrations[index];
            var previous = current;
            current = token =>
                registration.Function is null
                || string.Equals(registration.Function, context.Descriptor.FunctionName, StringComparison.Ordinal)
                    ? registration.Dispatch(context, previous, token)
                    : previous(token);
        }

        return current(cancellationToken);
    }

    private static T[] _Order<T>(IEnumerable<T> registrations)
        where T : IRegistration =>
        [.. registrations.OrderBy(x => x.Priority).ThenBy(x => x.Identity, StringComparer.Ordinal)];

    private interface IRegistration
    {
        string Identity { get; }
        int Priority { get; }
    }

    private sealed record ScheduleRegistration(
        string Identity,
        string? Function,
        int Priority,
        JobScheduleMiddlewareDispatch Dispatch
    ) : IRegistration;

    private sealed record ExecuteRegistration(
        string Identity,
        string? Function,
        int Priority,
        JobExecuteMiddlewareDispatch Dispatch
    ) : IRegistration;
}
