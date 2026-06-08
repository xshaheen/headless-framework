// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.AmbientTransactions;

namespace Headless.AmbientTransactions.PostgreSql;

/// <summary>
/// PostgreSQL ambient transaction coordinator.
/// </summary>
[PublicAPI]
public sealed class PostgreSqlAmbientTransaction(ICurrentAmbientTransaction currentAmbientTransaction)
    : AmbientTransactionBase(currentAmbientTransaction)
{
    public override void Commit()
    {
        if (DbTransaction is IDbTransaction dbTransaction)
        {
            dbTransaction.Commit();
        }

        DrainCommitWork();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (DbTransaction is DbTransaction dbTransaction)
        {
            await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (DbTransaction is IDbTransaction syncTransaction)
        {
            syncTransaction.Commit();
        }

        await DrainCommitWorkAsync(cancellationToken).ConfigureAwait(false);
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
