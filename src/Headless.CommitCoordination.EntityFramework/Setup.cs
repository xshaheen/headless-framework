// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Registers EF Core commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupEntityFrameworkCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds EF Core commit coordination services.
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddEntityFrameworkCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<EntityFrameworkCommitSignalSource>();

            return services;
        }
    }
}
