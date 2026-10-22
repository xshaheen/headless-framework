// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>
/// Runs the registered resource provisioners from a test or a tool, through the same runner as the <c>resources</c> verb.
/// </summary>
/// <remarks>
/// Call them on a built host's services, such as <c>host.Services</c> or a test server's <c>Services</c>. Each runs every
/// resource even after a failure, then throws one <see cref="ResourceProvisioningException" /> naming each failed resource.
/// An operation a resource does not support is skipped, not failed.
/// </remarks>
[PublicAPI]
public static class HeadlessResourceProvisioningServiceProviderExtensions
{
    /// <summary>Provisions every registered resource, in dependency order.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every resource is provisioned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="ResourceProvisioningException">At least one resource failed.</exception>
    public static Task ProvisionResourcesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(services, ResourceOperation.Provision, cancellationToken);
    }

    /// <summary>Purges the data of every registered resource and keeps its structure, for a test's clean start.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every resource is purged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="ResourceProvisioningException">At least one resource failed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The host's environment is not in <see cref="ResourceProvisioningOptions.DestructiveEnvironments" />.
    /// </exception>
    public static Task PurgeResourcesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);

        if (
            DestructiveOperationGuard.Refusal(
                services,
                "PurgeResourcesAsync",
                "Build the host in a Development or Test environment"
            ) is
            { } refusal
        )
        {
            throw new InvalidOperationException(refusal);
        }

        return _RunAsync(services, ResourceOperation.Purge, cancellationToken);
    }

    /// <summary>Deletes every registered resource, dependents before the resources they depend on.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every resource is deleted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="ResourceProvisioningException">At least one resource failed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The host's environment is not in <see cref="ResourceProvisioningOptions.DestructiveEnvironments" />.
    /// </exception>
    public static Task DeleteResourcesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);

        if (
            DestructiveOperationGuard.Refusal(
                services,
                "DeleteResourcesAsync",
                "Build the host in a Development or Test environment"
            ) is
            { } refusal
        )
        {
            throw new InvalidOperationException(refusal);
        }

        return _RunAsync(services, ResourceOperation.Delete, cancellationToken);
    }

    private static async Task _RunAsync(
        IServiceProvider services,
        ResourceOperation operation,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(services);

        var resources = await ResourceProvisioningRunner
            .GetProvisionersAsync(services, cancellationToken)
            .ConfigureAwait(false);
        var outcomes = await ResourceProvisioningRunner
            .RunAsync(resources, operation, timeout: null, cancellationToken)
            .ConfigureAwait(false);

        ResourceProvisioningRunner.ThrowIfFailed(outcomes, operation);
    }
}
