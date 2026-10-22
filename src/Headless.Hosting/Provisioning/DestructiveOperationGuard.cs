// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>Decides whether an operation that deletes data may run in the host's environment.</summary>
internal static class DestructiveOperationGuard
{
    /// <summary>
    /// Returns a refusal message naming the environment and how to proceed, or <see langword="null" /> when the operation
    /// may run. A container without a host environment (a test or a tool building its own services) is allowed.
    /// </summary>
    public static string? Refusal(IServiceProvider services, string operation, string remedy)
    {
        var environment = services.GetService<IHostEnvironment>();

        if (environment is null)
        {
            return null;
        }

        var allowed =
            services.GetService<IOptions<ResourceProvisioningOptions>>()?.Value.DestructiveEnvironments
            ?? new ResourceProvisioningOptions().DestructiveEnvironments;

        if (allowed.Exists(environment.IsEnvironment))
        {
            return null;
        }

        return $"Refusing to run {operation} in the '{environment.EnvironmentName}' environment: it deletes data, and only "
            + $"{string.Join(", ", allowed)} allow that without confirmation. {remedy}, or add the environment to "
            + "ResourceProvisioningOptions.DestructiveEnvironments.";
    }
}
