// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Changes the deployment settings of one declared job, identified by its <c>[Job]</c> identity. Tuning cannot declare
/// a job or change its identity, argument type, or cron schedule; the job's attribute owns those.
/// </summary>
/// <remarks>
/// Each call replaces the previous value of that setting for this tuning. Middleware accumulates. When several
/// <c>Tune</c> calls name the same identity, they apply in registration order, so a later value wins, and
/// <c>Headless:Jobs:Jobs:{identity}</c> configuration applies after all of them.
/// </remarks>
[PublicAPI]
public sealed class JobTuningBuilder
{
    private readonly List<JobScheduleMiddlewareRegistration> _schedule = [];
    private readonly List<JobExecuteMiddlewareRegistration> _execute = [];
    private int? _maxConcurrency;
    private JobPriority? _priority;
    private JobOptions? _options;
    private FailurePolicyDefinition? _failurePolicy;

    internal JobTuningBuilder(string identity)
    {
        Identity = identity;
    }

    internal string Identity { get; }

    /// <summary>
    /// Sets the maximum number of concurrent executions of this job on one node. <c>0</c> removes the job's own limit,
    /// leaving only the scheduler-wide limit.
    /// </summary>
    /// <param name="maxConcurrency">The per-node limit; <c>0</c> means no per-job limit.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is negative.</exception>
    public JobTuningBuilder Concurrency(int maxConcurrency)
    {
        _maxConcurrency = Argument.IsPositiveOrZero(maxConcurrency);
        return this;
    }

    /// <summary>Sets the thread-pool priority this job runs at on this host.</summary>
    /// <param name="priority">The priority to run at.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="priority"/> is not a defined value.</exception>
    public JobTuningBuilder Priority(JobPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new InvalidEnumArgumentException(nameof(priority), (int)priority, typeof(JobPriority));
        }

        _priority = priority;
        return this;
    }

    /// <summary>
    /// Replaces the failure policy the job declares, or the host default when it declares none, on this host.
    /// <c>Headless:Jobs:Jobs:{identity}:FailurePolicy</c> configuration still adjusts its retry counts and delays, and a
    /// scheduling call's <c>WithRetries</c> still overrides the stored retry count of that run.
    /// </summary>
    /// <typeparam name="TPolicy">The policy type; it is built once, during this call.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public JobTuningBuilder FailurePolicy<TPolicy>()
        where TPolicy : Reliability.FailurePolicy, new()
    {
        _failurePolicy = new TPolicy().Build();
        return this;
    }

    /// <summary>
    /// Replaces the job's failure policy with one described inline. See <see cref="FailurePolicy{TPolicy}"/>.
    /// </summary>
    /// <param name="configure">Describes the policy; it runs once, synchronously, during this call.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public JobTuningBuilder FailurePolicy([InstantHandle] Action<FailurePolicyBuilder> configure)
    {
        Argument.IsNotNull(configure);

        var builder = new FailurePolicyBuilder();
        configure(builder);
        _failurePolicy = builder.Build();
        return this;
    }

    /// <summary>
    /// Overrides the host's node-death default for this job. A value supplied on a scheduling call still wins over this
    /// one. Retries are not accepted here: the job's failure policy owns them, so tune it with
    /// <see cref="FailurePolicy{TPolicy}"/> instead.
    /// </summary>
    /// <param name="options">Startup policy settings; retries and invocation metadata are not accepted.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The options set retries or retry intervals, contain invalid settings, or carry invocation metadata.
    /// </exception>
    public JobTuningBuilder Options(JobOptions options)
    {
        _options = JobSchedulingPolicies.Snapshot(options);
        return this;
    }

    /// <summary>Authors the node-death override for this job. See <see cref="Options(JobOptions)"/>.</summary>
    /// <remarks>Invokes the callback once, synchronously, with a fresh builder.</remarks>
    /// <param name="configure">Authors startup policy settings; retries and invocation metadata are not accepted.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The authored options set retries or retry intervals, contain invalid settings, or carry invocation metadata.
    /// </exception>
    public JobTuningBuilder Options(Action<JobOptionsBuilder> configure)
    {
        Argument.IsNotNull(configure);
        var builder = new JobOptionsBuilder();
        configure(builder);
        return Options(builder.Build());
    }

    /// <summary>
    /// Runs <typeparamref name="TMiddleware"/> around every execution attempt of this job on this host. The middleware
    /// is resolved from the attempt's service scope, so register it in DI.
    /// </summary>
    /// <typeparam name="TMiddleware">The execute middleware type.</typeparam>
    /// <param name="priority">Ordering priority; see <see cref="JobMiddlewarePriority"/>.</param>
    /// <returns>This builder, for chaining.</returns>
    public JobTuningBuilder UseExecuteMiddleware<TMiddleware>(int priority = JobMiddlewarePriority.Default)
        where TMiddleware : IJobExecuteMiddleware
    {
        _execute.Add(
            new(
                _MiddlewareIdentity(typeof(TMiddleware)),
                Identity,
                priority,
                static (context, next, cancellationToken) =>
                    context.Services.GetRequiredService<TMiddleware>().InvokeAsync(context, next, cancellationToken)
            )
        );
        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TMiddleware"/> around every scheduling call for this job on this host. The middleware
    /// is resolved from the scheduling call's service scope, so register it in DI.
    /// </summary>
    /// <typeparam name="TMiddleware">The schedule middleware type.</typeparam>
    /// <param name="priority">Ordering priority; see <see cref="JobMiddlewarePriority"/>.</param>
    /// <returns>This builder, for chaining.</returns>
    public JobTuningBuilder UseScheduleMiddleware<TMiddleware>(int priority = JobMiddlewarePriority.Default)
        where TMiddleware : IJobScheduleMiddleware
    {
        _schedule.Add(
            new(
                _MiddlewareIdentity(typeof(TMiddleware)),
                Identity,
                priority,
                static (context, next, cancellationToken) =>
                    context.Services.GetRequiredService<TMiddleware>().InvokeAsync(context, next, cancellationToken)
            )
        );
        return this;
    }

    internal JobTuning Build() =>
        new(Identity, _maxConcurrency, _priority, _options, [.. _schedule], [.. _execute], _failurePolicy);

    // Prefixed with the job identity so the tuned registration orders deterministically next to generated middleware
    // and a type attached to two jobs keeps two distinct registrations.
    private string _MiddlewareIdentity(Type middlewareType) => $"tune:{Identity}:{middlewareType.FullName}";
}

/// <summary>One immutable <c>Tune</c> call, applied to the host's catalog when its job registry is built.</summary>
internal sealed record JobTuning(
    string Identity,
    int? MaxConcurrency,
    JobPriority? Priority,
    JobOptions? Options,
    JobScheduleMiddlewareRegistration[] ScheduleMiddleware,
    JobExecuteMiddlewareRegistration[] ExecuteMiddleware,
    FailurePolicyDefinition? FailurePolicy
);
