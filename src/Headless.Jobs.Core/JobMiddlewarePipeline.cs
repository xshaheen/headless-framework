// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

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
