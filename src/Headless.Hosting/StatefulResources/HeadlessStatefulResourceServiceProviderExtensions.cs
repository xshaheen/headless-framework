// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Runs the registered stateful resources from a test or a tool, through the same runner as the <c>resources</c> verb.
/// </summary>
/// <remarks>
/// Call them on a built host's services, such as <c>host.Services</c> or a test server's <c>Services</c>. Each runs every
/// resource even after a failure, then throws one <see cref="StatefulResourceException" /> naming each failed resource.
/// An operation a resource does not support is skipped, not failed.
/// </remarks>
[PublicAPI]
public static class HeadlessStatefulResourceServiceProviderExtensions
{
    /// <summary>Sets up every registered stateful resource, in dependency order.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every resource is set up.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="StatefulResourceException">At least one resource failed.</exception>
    public static Task SetupResourcesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(services, StatefulResourceOperation.Setup, cancellationToken);
    }

    /// <summary>Clears the data of every registered stateful resource and keeps its structure, for a test's clean start.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every resource is cleared.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="StatefulResourceException">At least one resource failed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The host's environment is not in <see cref="StatefulResourceOptions.DestructiveEnvironments" />.
    /// </exception>
    public static Task ResetResourceStateAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);

        if (
            DestructiveOperationGuard.Refusal(
                services,
                "ResetResourceStateAsync",
                "Build the host in a Development or Test environment"
            ) is
            { } refusal
        )
        {
            throw new InvalidOperationException(refusal);
        }

        return _RunAsync(services, StatefulResourceOperation.Clear, cancellationToken);
    }

    /// <summary>Deletes every registered stateful resource, dependents before the resources they depend on.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every resource is deleted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="StatefulResourceException">At least one resource failed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The host's environment is not in <see cref="StatefulResourceOptions.DestructiveEnvironments" />.
    /// </exception>
    public static Task TeardownResourcesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);

        if (
            DestructiveOperationGuard.Refusal(
                services,
                "TeardownResourcesAsync",
                "Build the host in a Development or Test environment"
            ) is
            { } refusal
        )
        {
            throw new InvalidOperationException(refusal);
        }

        return _RunAsync(services, StatefulResourceOperation.Teardown, cancellationToken);
    }

    private static async Task _RunAsync(
        IServiceProvider services,
        StatefulResourceOperation operation,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(services);

        var resources = await StatefulResourceRunner
            .GetResourcesAsync(services, cancellationToken)
            .ConfigureAwait(false);
        var outcomes = await StatefulResourceRunner
            .RunAsync(resources, operation, timeout: null, cancellationToken)
            .ConfigureAwait(false);

        StatefulResourceRunner.ThrowIfFailed(outcomes, operation);
    }
}
