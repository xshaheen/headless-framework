// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.CommitCoordination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Opens EF Core transactions that participate in commit coordination: enlisted work drains when the
/// transaction commits and is discarded when it rolls back, surfaced through the relational capability.
/// </summary>
[PublicAPI]
public static class BeginCoordinatedTransactionExtensions
{
    extension(DatabaseFacade database)
    {
        /// <summary>
        /// Opens a coordinated EF transaction with the default isolation level.
        /// </summary>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The coordinated transaction.</returns>
        public Task<IDbContextTransaction> BeginCoordinatedTransactionAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            return database.BeginCoordinatedTransactionAsync(IsolationLevel.Unspecified, services, cancellationToken);
        }

        /// <summary>
        /// Opens a coordinated EF transaction with a specific isolation level.
        /// </summary>
        /// <param name="isolationLevel">The isolation level.</param>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The coordinated transaction.</returns>
        public async Task<IDbContextTransaction> BeginCoordinatedTransactionAsync(
            IsolationLevel isolationLevel,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(services);

            var transaction = await database
                .BeginTransactionAsync(isolationLevel, cancellationToken)
                .ConfigureAwait(false);

            return _Coordinate(database, services, transaction, cancellationToken);
        }

        /// <summary>
        /// Opens a coordinated EF transaction synchronously with the default isolation level.
        /// </summary>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <returns>The coordinated transaction.</returns>
        public IDbContextTransaction BeginCoordinatedTransaction(IServiceProvider services)
        {
            return database.BeginCoordinatedTransaction(IsolationLevel.Unspecified, services);
        }

        /// <summary>
        /// Opens a coordinated EF transaction synchronously with a specific isolation level.
        /// </summary>
        /// <param name="isolationLevel">The isolation level.</param>
        /// <param name="services">The scoped service provider captured for the post-commit drain.</param>
        /// <returns>The coordinated transaction.</returns>
        public IDbContextTransaction BeginCoordinatedTransaction(IsolationLevel isolationLevel, IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            var transaction = database.BeginTransaction(isolationLevel);

            return _Coordinate(database, services, transaction, CancellationToken.None);
        }
    }

    private static IDbContextTransaction _Coordinate(
        DatabaseFacade database,
        IServiceProvider services,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var signalSource = services.GetRequiredService<EntityFrameworkCommitSignalSource>();
            var dbTransaction = transaction.GetDbTransaction();

            // Attach stores the scope in the signal source's registry keyed by dbTransaction; the interceptor
            // (commit/rollback) and the wrapper's dispose safety-net both drive it through that registry, so the
            // returned reference is redundant here.
            _ = signalSource.Attach(
                new CommitCoordinatorBindings
                {
                    Services = services,
                    Connection = database.GetDbConnection(),
                    Transaction = dbTransaction,
                    ProviderTransactionKey = dbTransaction,
                },
                cancellationToken
            );

            return new CoordinatedDbContextTransaction(transaction, signalSource, dbTransaction);
        }
        catch
        {
            transaction.Dispose();

            throw;
        }
    }
}
