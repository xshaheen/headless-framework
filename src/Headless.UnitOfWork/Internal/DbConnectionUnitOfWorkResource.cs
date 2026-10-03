// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The raw-ADO unit-of-work resource: exposes the live connection and transaction so participants (outbox, job,
/// and store writers) place their rows inside the caller's transaction.
/// </summary>
/// <remarks>
/// Owned mode began the transaction itself and commits or rolls it back on the unit's verbs, disposing the
/// transaction afterwards and closing the connection when the provider opened it. Observed mode wraps a
/// caller-committed transaction: both verbs are no-ops and the unit only coordinates the commit edge. Completion is
/// read through the driver's probe, because the drivers disagree on what a completed transaction still exposes.
/// </remarks>
internal sealed class DbConnectionUnitOfWorkResource(
    DbConnection connection,
    DbTransaction transaction,
    Func<DbTransaction, bool> isTransactionCompleted,
    bool owned,
    bool closeConnection = false
) : IRelationalUnitOfWorkResource
{
    public bool IsOwned { get; } = owned;

    public DbConnection Connection => connection;

    public DbTransaction Transaction => transaction;

    public bool IsTransactionCompleted => isTransactionCompleted(transaction);

    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (!IsOwned)
        {
            return;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _FinishAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        if (!IsOwned)
        {
            return;
        }

        try
        {
            if (!IsTransactionCompleted)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await _FinishAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask _FinishAsync()
    {
        await transaction.DisposeAsync().ConfigureAwait(false);

        if (closeConnection)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}
