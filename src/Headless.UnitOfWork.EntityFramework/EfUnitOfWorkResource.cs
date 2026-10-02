// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.UnitOfWork;

/// <summary>
/// The EF Core unit-of-work resource: exposes the context's live connection and transaction so participants
/// (outbox writers, job writers) place durable rows inside the caller's transaction.
/// </summary>
/// <remarks>
/// Owned mode began the transaction through <c>DatabaseFacade.BeginTransactionAsync</c> and commits
/// or rolls it back on the unit's verbs, disposing the <see cref="IDbContextTransaction" /> after either —
/// the shape an <c>await using</c> block would give an application. Observed mode wraps a caller-committed
/// transaction: both verbs are no-ops, and the unit only coordinates the commit edge.
/// </remarks>
internal sealed class EfUnitOfWorkResource(DbContext db, IDbContextTransaction transaction, bool owned)
    : IRelationalUnitOfWorkResource
{
    public bool IsOwned { get; } = owned;

    public DbConnection Connection => db.Database.GetDbConnection();

    public DbTransaction Transaction => transaction.GetDbTransaction();

    /// <summary>
    /// True once the context no longer holds this transaction: the caller (observed mode) committed or
    /// rolled it back, or the unit itself (owned mode) finished it. Shapes the forgotten-completion
    /// warning for an observed unit disposed without a completion verb.
    /// </summary>
    public bool IsTransactionCompleted =>
        db.Database.CurrentTransaction is null || !ReferenceEquals(db.Database.CurrentTransaction, transaction);

    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (!IsOwned)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        if (!IsOwned)
        {
            return;
        }

        try
        {
            // The transaction may already have finished (a race with the caller, or a second rollback attempt);
            // a dispose of a finished IDbContextTransaction is safe, a rollback of one is not.
            if (!IsTransactionCompleted)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Always: the dispose is what clears the context's CurrentTransaction. A rollback that throws on a
            // broken connection would otherwise leave the dead transaction on the context, and a replaying
            // RunAsync would then refuse its next begin as "already has an active transaction".
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
