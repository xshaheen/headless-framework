// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data.Common;
using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.UnitOfWork;

/// <summary>
/// Runs a store's EF Core work inside a caller's unit of work: a context from the store's
/// <see cref="IDbContextFactory{TContext}" /> borrows the unit's open connection and transaction for one call, then gets
/// its own connection back.
/// </summary>
/// <remarks>
/// A seam for storage packages whose writes accept an <see cref="IUnitOfWork" /> (permissions, settings, features), not
/// an application API. The unit may be an EF or a raw-ADO unit on the same database. The context's own connection is
/// restored before it is disposed, because a pooled context keeps a connection it does not own for its next lease.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityFrameworkEnlistment
{
    /// <summary>Runs <paramref name="work" /> on a context bound to <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="dbFactory">The store's context factory.</param>
    /// <param name="unitOfWork">The caller's unit; it must be active and carry a live relational transaction.</param>
    /// <param name="owner">The store, named in refusal messages.</param>
    /// <param name="operation">The operation, named in refusal messages.</param>
    /// <param name="work">The work to run. It must not commit, roll back, or change the context's connection.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of <paramref name="work" />.</returns>
    /// <exception cref="InvalidOperationException">
    /// The unit cannot host the call: it is not active, carries no live relational transaction, targets another
    /// database, or the context retries on failure outside an execution strategy that is already running.
    /// </exception>
    public static async Task<TResult> RunAsync<TContext, TResult>(
        IDbContextFactory<TContext> dbFactory,
        IUnitOfWork unitOfWork,
        string owner,
        string operation,
        Func<TContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken
    )
        where TContext : DbContext
    {
        Argument.IsNotNull(dbFactory);
        Argument.IsNotNull(work);

        var (connection, transaction) = RelationalEnlistment.RequireLive(
            unitOfWork,
            typeof(DbConnection),
            typeof(DbTransaction),
            owner,
            operation
        );

        var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await using (db.ConfigureAwait(false))
        {
            var configured = RelationalOptionsExtension.Extract(db.GetService<IDbContextOptions>());

            if (configured.Connection is not null && configured.IsConnectionOwned)
            {
                throw new InvalidOperationException(
                    $"{owner} cannot run an enlisted {operation} call on {typeof(TContext).Name}, because the context owns "
                        + "the connection its options supply and would dispose it when it borrows the unit's connection. "
                        + "Configure the context with a connection string, or with contextOwnsConnection: false."
                );
            }

            // A retrying strategy refuses a transaction it did not begin unless it is already running, which is what
            // RunAsync(db, …) does for the whole unit; check first so the refusal names that remedy.
            if (db.Database.CreateExecutionStrategy().RetriesOnFailure && ExecutionStrategy.Current is null)
            {
                throw new InvalidOperationException(
                    $"{owner} cannot run an enlisted {operation} call on {typeof(TContext).Name}, because the context "
                        + "retries on failure and cannot join a transaction it did not begin. Run the unit of work with "
                        + "IUnitOfWorkFactory.RunAsync(db, …) so the whole unit replays under the execution strategy."
                );
            }

            var connectionString = db.Database.GetConnectionString();
            RelationalEnlistment.RequireSameDatabase(db.Database.GetDbConnection(), connection, owner, operation);

            db.Database.SetDbConnection(connection, contextOwnsConnection: false);

            try
            {
                await db.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);

                return await work(db, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Not the caller's token: the context must let go of the unit's transaction even when the work was cancelled.
                await db.Database.UseTransactionAsync(transaction: null, CancellationToken.None).ConfigureAwait(false);

                if (configured.Connection is { } supplied)
                {
                    db.Database.SetDbConnection(supplied, contextOwnsConnection: false);
                }
                else
                {
                    db.Database.SetDbConnection(connection: null, contextOwnsConnection: true);
                    db.Database.SetConnectionString(connectionString);
                }
            }
        }
    }
}
