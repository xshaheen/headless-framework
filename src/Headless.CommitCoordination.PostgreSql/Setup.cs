// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.CommitCoordination.PostgreSql;

/// <summary>
/// Registers PostgreSQL commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupPostgreSqlCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds PostgreSQL commit coordination services.
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddPostgreSqlCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<PostgreSqlCommitSignalSource>();
            services.TryAddSingleton<ICommitSignalSource>(sp => sp.GetRequiredService<PostgreSqlCommitSignalSource>());

            return services;
        }
    }
}
