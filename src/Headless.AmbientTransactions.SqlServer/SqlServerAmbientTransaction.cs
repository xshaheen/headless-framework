// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.AmbientTransactions;

namespace Headless.AmbientTransactions.SqlServer;

/// <summary>
/// SQL Server ambient transaction coordinator.
/// </summary>
[PublicAPI]
public sealed class SqlServerAmbientTransaction(ICurrentAmbientTransaction currentAmbientTransaction)
    : AmbientTransactionBase(currentAmbientTransaction)
{
    public override void Commit()
    {
        if (DbTransaction is IDbTransaction dbTransaction)
        {
            dbTransaction.Commit();
        }
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (DbTransaction is DbTransaction dbTransaction)
        {
            await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (DbTransaction is IDbTransaction syncTransaction)
        {
            syncTransaction.Commit();
        }
    }

    public override void Rollback()
    {
        if (DbTransaction is IDbTransaction dbTransaction)
        {
            dbTransaction.Rollback();
        }

        DiscardCommitWork();
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (DbTransaction is DbTransaction dbTransaction)
        {
            await dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (DbTransaction is IDbTransaction syncTransaction)
        {
            syncTransaction.Rollback();
        }

        DiscardCommitWork();
    }
}
