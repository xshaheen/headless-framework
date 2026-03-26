// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Constants;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>Extension methods for <see cref="IHostEnvironment"/>.</summary>
[PublicAPI]
public static class HostEnvironmentExtensions
{
    extension(IHostEnvironment hostEnvironment)
    {
        /// <summary>Checks if the current host environment name is "Test".</summary>
        /// <returns>True if the environment name is Test, otherwise false.</returns>
        public bool IsTest()
        {
            Argument.IsNotNull(hostEnvironment);

            return hostEnvironment.IsEnvironment(EnvironmentNames.Test);
        }

        /// <summary>Checks if the current host environment name is "Development" or "Test".</summary>
        /// <returns>True if the environment name is Development or Test, otherwise false.</returns>
        public bool IsDevelopmentOrTest()
        {
            Argument.IsNotNull(hostEnvironment);

            return hostEnvironment.IsEnvironment(EnvironmentNames.Test)
                || hostEnvironment.IsEnvironment(EnvironmentNames.Development);
        }
    }
}
