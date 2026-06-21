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
        /// Adds the PostgreSQL (Npgsql) commit signal source and the core commit coordination services.
        /// </summary>
        /// <remarks>
        /// PostgreSQL uses an inline (caller-driven) signal model: after committing the transaction the caller
        /// must call <see cref="ICommitScope.SignalAsync" /> on the scope returned by
        /// <c>NpgsqlConnection.EnlistCommitCoordination</c>. Use
        /// <c>NpgsqlConnection.ExecuteCoordinatedTransactionAsync</c> to handle this automatically.
        /// Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddPostgreSqlCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<PostgreSqlCommitSignalSource>();
            services.TryAddSingleton<ICommitSignalSource>(sp => sp.GetRequiredService<PostgreSqlCommitSignalSource>());

            return services;
        }
    }
}
