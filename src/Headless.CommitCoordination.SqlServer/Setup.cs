// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Registers SQL Server commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupSqlServerCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds SQL Server commit coordination services.
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddSqlServerCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<SqlServerCommitSignalSource>();
            services.TryAddSingleton<SqlServerCommitDiagnosticObserver>();
            services.TryAddSingleton<SqlServerCommitDiagnosticListenerObserver>();

            // The hosted service owns the SqlClientDiagnosticListener subscription lifetime (subscribe on start,
            // dispose on stop), so the out-of-band commit/rollback detection observes real provider edges.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, SqlServerCommitDiagnosticHostedService>()
            );

            return services;
        }
    }
}
