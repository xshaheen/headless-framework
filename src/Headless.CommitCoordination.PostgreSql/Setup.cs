// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.CommitCoordination;

/// <summary>
/// Registers PostgreSQL commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupPostgreSqlCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the commit coordination services the Npgsql enlistment helpers resolve.
        /// </summary>
        /// <remarks>
        /// PostgreSQL uses an explicit (caller-driven) signal model: after committing the transaction the caller
        /// calls <see cref="ICommitScope.SignalAsync" /> on the scope returned by
        /// <c>NpgsqlConnection.EnlistCommitCoordination</c>, or uses
        /// <c>NpgsqlConnection.ExecuteCoordinatedTransactionAsync</c>, which signals for the caller. No provider
        /// service is needed beyond the core, so this registers <see cref="SetupCommitCoordination.AddCommitCoordination" />
        /// only; it exists so hosts declare the provider they enlist through. Idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddPostgreSqlCommitCoordination()
        {
            return services.AddCommitCoordination();
        }
    }
}
