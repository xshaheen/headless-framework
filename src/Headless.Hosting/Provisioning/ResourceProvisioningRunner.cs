// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>Collects the registered provisioners in dependency order and runs one operation over them.</summary>
internal static class ResourceProvisioningRunner
{
    /// <summary>
    /// Returns every registered resource, direct registrations first and then each source's, ordered so that every
    /// resource follows the resources it depends on. Otherwise registration order holds.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two resources depend on each other.</exception>
    public static async Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        List<IResourceProvisioner> resources = [.. services.GetServices<IResourceProvisioner>()];

        foreach (var source in services.GetServices<IResourceProvisionerSource>())
        {
            resources.AddRange(await source.GetProvisionersAsync(cancellationToken).ConfigureAwait(false));
        }

        return DependencyOrder.Sort<IResourceProvisioner>(
            resources,
            static (p, all) => p.DependsOn(all),
            static p => $"{p.Kind} {p.Name}",
            "Resource provisioners"
        );
    }

    /// <summary>
    /// Runs <paramref name="operation" /> over <paramref name="resources" /> one at a time: delete and purge in reverse order,
    /// every other operation in the given order. A failure does not stop the run, so the report names every resource.
    /// </summary>
    /// <param name="resources">The resources, in dependency order.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="timeout">The budget of the whole run; resources not reached in time are reported as timed out.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    public static async Task<IReadOnlyList<ResourceOutcome>> RunAsync(
        IReadOnlyList<IResourceProvisioner> resources,
        ResourceOperation operation,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        using var budget = BudgetedStep.CreateBudget(timeout, cancellationToken);

        // Delete and purge go dependents first, so a row or object another resource references is removed after it.
        var ordered = operation is ResourceOperation.Delete or ResourceOperation.Purge
            ? resources.Reverse()
            : resources;
        var outcomes = new List<ResourceOutcome>(resources.Count);

        foreach (var resource in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_Supports(resource, operation))
            {
                outcomes.Add(new(resource, ResourceOutcomeStatus.Unsupported, TimeSpan.Zero, Error: null, []));

                continue;
            }

            if (budget.IsCancellationRequested)
            {
                outcomes.Add(
                    new(
                        resource,
                        ResourceOutcomeStatus.TimedOut,
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
                        BudgetedStepStatus.Ok => ResourceOutcomeStatus.Ok,
                        BudgetedStepStatus.TimedOut => ResourceOutcomeStatus.TimedOut,
                        _ => ResourceOutcomeStatus.Failed,
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
    /// <exception cref="ResourceProvisioningException">At least one outcome failed or timed out.</exception>
    public static void ThrowIfFailed(IReadOnlyList<ResourceOutcome> outcomes, ResourceOperation operation)
    {
        var failures = outcomes.Where(static outcome => outcome.IsFailure).ToList();

        if (failures.Count == 0)
        {
            return;
        }

        var details = string.Join(
            "; ",
            failures.Select(static outcome =>
                $"{outcome.Resource.Kind} {outcome.Resource.Name}: {outcome.Error?.Message ?? outcome.Status.ToString()}"
            )
        );

        throw new ResourceProvisioningException(
            $"Resource {operation.ToString().ToLowerInvariant()} failed for {failures.Count} resource(s): {details}",
            failures[0].Error
        )
        {
            Failures = [.. failures.Select(static outcome => outcome.Error!)],
        };
    }

    private static bool _Supports(IResourceProvisioner resource, ResourceOperation operation) =>
        operation switch
        {
            ResourceOperation.Purge => resource.CanPurge,
            ResourceOperation.Delete => resource.CanDelete,
            _ => true,
        };

    private static async Task<IReadOnlyList<KeyValuePair<string, string>>> _InvokeAsync(
        IResourceProvisioner resource,
        ResourceOperation operation,
        CancellationToken cancellationToken
    )
    {
        switch (operation)
        {
            case ResourceOperation.Provision:
                await resource.ProvisionAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case ResourceOperation.Verify:
                await resource.VerifyAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case ResourceOperation.Purge:
                await resource.PurgeAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case ResourceOperation.Delete:
                await resource.DeleteAsync(cancellationToken).ConfigureAwait(false);

                return [];
            case ResourceOperation.Status:
                return await resource.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown resource operation.");
        }
    }
}

/// <summary>An operation the resource runner applies to every selected resource.</summary>
internal enum ResourceOperation
{
    Provision = 0,
    Verify = 1,
    Purge = 2,
    Delete = 3,
    Status = 4,
}

/// <summary>How one resource's operation ended.</summary>
internal enum ResourceOutcomeStatus
{
    Ok = 0,
    Failed = 1,
    Unsupported = 2,
    TimedOut = 3,
}

/// <summary>What one operation did to one resource.</summary>
internal sealed record ResourceOutcome(
    IResourceProvisioner Resource,
    ResourceOutcomeStatus Status,
    TimeSpan Duration,
    Exception? Error,
    IReadOnlyList<KeyValuePair<string, string>> Properties
)
{
    public bool IsFailure => Status is ResourceOutcomeStatus.Failed or ResourceOutcomeStatus.TimedOut;
}
