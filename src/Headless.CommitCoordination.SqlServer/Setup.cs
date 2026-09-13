// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.CommitCoordination;

/// <summary>
/// Registers SQL Server commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupSqlServerCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the commit coordination services the SqlClient enlistment helpers resolve.
        /// </summary>
        /// <remarks>
        /// SQL Server uses an explicit (caller-driven) signal model: after committing the transaction the caller
        /// calls <see cref="ICommitScope.SignalAsync" /> on the scope returned by
        /// <c>SqlConnection.EnlistCommitCoordination</c>, or uses
        /// <c>SqlConnection.ExecuteCoordinatedTransactionAsync</c>, which signals for the caller. No provider
        /// service is needed beyond the core, so this registers <see cref="SetupCommitCoordination.AddCommitCoordination" />
        /// only; it exists so hosts declare the provider they enlist through. Idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddSqlServerCommitCoordination()
        {
            return services.AddCommitCoordination();
        }
    }
}
