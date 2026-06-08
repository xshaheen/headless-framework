// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

            return services;
        }
    }
}
