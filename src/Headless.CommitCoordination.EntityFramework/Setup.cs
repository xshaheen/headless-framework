// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

            // EF Core resolves IInterceptor services from the application service provider (wired by AddDbContext)
            // and applies them to every context, so the commit/rollback edges are observed without per-context setup.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IInterceptor, CommitCoordinationTransactionInterceptor>()
            );

            return services;
        }
    }
}
