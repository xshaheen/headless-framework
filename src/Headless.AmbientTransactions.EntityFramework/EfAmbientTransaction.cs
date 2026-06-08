// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.AmbientTransactions.EntityFramework;

/// <summary>
/// Adapts an EF Core relational transaction to <see cref="IAmbientTransaction" />.
/// </summary>
[PublicAPI]
public sealed class EfAmbientTransaction(ICurrentAmbientTransaction currentAmbientTransaction)
    : AmbientTransactionBase(currentAmbientTransaction)
{
    public EfAmbientTransaction(
        IDbContextTransaction dbContextTransaction,
        ICurrentAmbientTransaction currentAmbientTransaction,
        bool autoCommit = false
    )
        : this(currentAmbientTransaction)
    {
        DbTransaction = dbContextTransaction;
        AutoCommit = autoCommit;
    }

    public override void Commit()
    {
        if (DbTransaction is IDbContextTransaction dbContextTransaction)
        {
            dbContextTransaction.Commit();
        }

        DrainCommitWork();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (DbTransaction is IDbContextTransaction dbContextTransaction)
        {
            await dbContextTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await DrainCommitWorkAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Rollback()
    {
        if (DbTransaction is IDbContextTransaction dbContextTransaction)
        {
            dbContextTransaction.Rollback();
        }

        DiscardCommitWork();
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (DbTransaction is IDbContextTransaction dbContextTransaction)
        {
            await dbContextTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        DiscardCommitWork();
    }
}
