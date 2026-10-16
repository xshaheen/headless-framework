// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>Collects the registered resources in dependency order and runs one operation over them.</summary>
internal static class StatefulResourceRunner
{
    /// <summary>
    /// Returns every registered resource, direct registrations first and then each source's, ordered so that every
    /// resource follows the resources it depends on. Otherwise registration order holds.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two resources depend on each other.</exception>
    public static async Task<IReadOnlyList<IStatefulResource>> GetResourcesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        List<IStatefulResource> resources = [.. services.GetServices<IStatefulResource>()];

        foreach (var source in services.GetServices<IStatefulResourceSource>())
        {
            resources.AddRange(await source.GetResourcesAsync(cancellationToken).ConfigureAwait(false));
        }

        return _Order(resources);
    }

    /// <summary>
    /// Runs <paramref name="operation" /> over <paramref name="resources" /> one at a time: teardown in reverse order,
    /// every other operation in the given order. A failure does not stop the run, so the report names every resource.
    /// </summary>
    /// <param name="resources">The resources, in dependency order.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="timeout">The budget of the whole run; resources not reached in time are reported as timed out.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    public static async Task<IReadOnlyList<StatefulResourceOutcome>> RunAsync(
        IReadOnlyList<IStatefulResource> resources,
        StatefulResourceOperation operation,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        using var budget = BudgetedStep.CreateBudget(timeout, cancellationToken);

        var ordered = operation == StatefulResourceOperation.Teardown ? resources.Reverse() : resources;
        var outcomes = new List<StatefulResourceOutcome>(resources.Count);

        foreach (var resource in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (budget.IsCancellationRequested)
            {
                outcomes.Add(
                    new(
                        resource,
                        StatefulResourceOutcomeStatus.TimedOut,
                        TimeSpan.Zero,
                        new TimeoutException("Not run: the timeout elapsed before this resource was reached."),
                        []
                    )
                );

                continue;
            }

            IReadOnlyList<KeyValuePair<string, string>> properties = [];
            var result = await BudgetedStep
                .RunAsync(
                    async token => properties = await _InvokeAsync(resource, operation, token).ConfigureAwait(false),
                    budget.Token,
                    cancellationToken
                )
                .ConfigureAwait(false);

            outcomes.Add(
                new(
                    resource,
                    result.Status switch
                    {
                        BudgetedStepStatus.Ok => StatefulResourceOutcomeStatus.Ok,
                        BudgetedStepStatus.Unsupported => StatefulResourceOutcomeStatus.Unsupported,
                        BudgetedStepStatus.TimedOut => StatefulResourceOutcomeStatus.TimedOut,
                        _ => StatefulResourceOutcomeStatus.Failed,
                    },
                    result.Duration,
                    result.Error,
                    result.Status == BudgetedStepStatus.Ok ? properties : []
                )
            );
        }

        return outcomes;
    }

    /// <summary>Throws when any outcome failed or timed out; an unsupported operation is not a failure.</summary>
    /// <exception cref="StatefulResourceException">At least one outcome failed or timed out.</exception>
    public static void ThrowIfFailed(
        IReadOnlyList<StatefulResourceOutcome> outcomes,
        StatefulResourceOperation operation
    )
    {
        var failures = outcomes.Where(static outcome => outcome.IsFailure).ToList();

        if (failures.Count == 0)
        {
            return;
        }

        var details = string.Join(
            "; ",
            failures.Select(static outcome =>
                $"{outcome.Resource.Type} {outcome.Resource.Name}: {outcome.Error?.Message ?? outcome.Status.ToString()}"
            )
        );

        throw new StatefulResourceException(
            $"Stateful resource {operation.ToString().ToLowerInvariant()} failed for {failures.Count} resource(s): {details}",
            failures[0].Error
        )
        {
            Failures = [.. failures.Select(static outcome => outcome.Error!)],
        };
    }

    private static async Task<IReadOnlyList<KeyValuePair<string, string>>> _InvokeAsync(
        IStatefulResource resource,
        StatefulResourceOperation operation,
        CancellationToken cancellationToken
    )
    {
        switch (operation)
        {
            case StatefulResourceOperation.Setup:
                await resource.SetupAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case StatefulResourceOperation.Check:
                await resource.CheckAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case StatefulResourceOperation.Clear:
                await resource.ClearStateAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case StatefulResourceOperation.Teardown:
                await resource.TeardownAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case StatefulResourceOperation.Statistics:
                return await resource.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown resource operation.");
        }
    }

    private static List<IStatefulResource> _Order(List<IStatefulResource> resources)
    {
        var ordered = new List<IStatefulResource>(resources.Count);
        var registered = new HashSet<IStatefulResource>(resources, ReferenceEqualityComparer.Instance);
        var done = new HashSet<IStatefulResource>(ReferenceEqualityComparer.Instance);
        var visiting = new List<IStatefulResource>();

        foreach (var resource in resources)
        {
            visit(resource);
        }

        return ordered;

        void visit(IStatefulResource resource)
        {
            if (done.Contains(resource))
            {
                return;
            }

            if (visiting.Contains(resource, ReferenceEqualityComparer.Instance))
            {
                var cycle = visiting
                    .SkipWhile(x => !ReferenceEquals(x, resource))
                    .Append(resource)
                    .Select(static x => $"{x.Type} {x.Name}");

                throw new InvalidOperationException(
                    $"Stateful resources depend on each other: {string.Join(" -> ", cycle)}."
                );
            }

            visiting.Add(resource);

            if (resource is IStatefulResourceWithDependencies dependent)
            {
                // A dependency that is not registered is not this host's to set up, so it does not join the order.
                foreach (var dependency in dependent.GetDependencies(resources).Where(registered.Contains))
                {
                    visit(dependency);
                }
            }

            visiting.RemoveAt(visiting.Count - 1);
            done.Add(resource);
            ordered.Add(resource);
        }
    }
}

/// <summary>An operation the resource runner applies to every selected resource.</summary>
internal enum StatefulResourceOperation
{
    Setup = 0,
    Check = 1,
    Clear = 2,
    Teardown = 3,
    Statistics = 4,
}

/// <summary>How one resource's operation ended.</summary>
internal enum StatefulResourceOutcomeStatus
{
    Ok = 0,
    Failed = 1,
    Unsupported = 2,
    TimedOut = 3,
}

/// <summary>What one operation did to one resource.</summary>
internal sealed record StatefulResourceOutcome(
    IStatefulResource Resource,
    StatefulResourceOutcomeStatus Status,
    TimeSpan Duration,
    Exception? Error,
    IReadOnlyList<KeyValuePair<string, string>> Properties
)
{
    public bool IsFailure => Status is StatefulResourceOutcomeStatus.Failed or StatefulResourceOutcomeStatus.TimedOut;
}
