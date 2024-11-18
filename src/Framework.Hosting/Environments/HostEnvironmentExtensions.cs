// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>Extension methods for <see cref="IHostEnvironment"/>.</summary>
[PublicAPI]
public static class HostEnvironmentExtensions
{
    /// <summary>Checks if the current host environment name is "Test".</summary>
    /// <param name="hostEnvironment">An instance of <see cref="IHostEnvironment"/>.</param>
    /// <returns>True if the environment name is Test, otherwise false.</returns>
    public static bool IsTest(this IHostEnvironment hostEnvironment)
    {
        Argument.IsNotNull(hostEnvironment);

        return hostEnvironment.IsEnvironment(EnvironmentNames.Test);
    }

    /// <summary>Checks if the current host environment name is "Development" or "Test".</summary>
    /// <param name="hostEnvironment">An instance of <see cref="IHostEnvironment"/>.</param>
    /// <returns>True if the environment name is Development or Test, otherwise false.</returns>
    public static bool IsDevelopmentOrTest(this IHostEnvironment hostEnvironment)
    {
        Argument.IsNotNull(hostEnvironment);

        return hostEnvironment.IsEnvironment(EnvironmentNames.Test)
            || hostEnvironment.IsEnvironment(EnvironmentNames.Development);
    }
}
