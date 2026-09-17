// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Headless.UnitOfWork;

/// <summary>
/// The SqlClient unit-of-work resource: exposes the live connection and transaction so participants (outbox and
/// job writers) place durable rows inside the caller's transaction.
/// </summary>
/// <remarks>
/// Owned mode began the transaction itself and commits or rolls it back on the unit's verbs, disposing the
/// transaction afterwards and closing the connection when the provider opened it. Observed mode wraps a
/// caller-committed transaction: both verbs are no-ops and the unit only coordinates the commit edge.
/// </remarks>
internal sealed class SqlServerUnitOfWorkResource(
    SqlConnection connection,
    SqlTransaction transaction,
    bool owned,
    bool closeConnection = false
) : IRelationalUnitOfWorkResource
{
    public bool IsOwned { get; } = owned;

    public DbConnection Connection => connection;

    public DbTransaction Transaction => transaction;

    /// <summary>
    /// SqlClient detaches a transaction from its connection once it commits, rolls back, or is disposed.
    /// </summary>
    public bool IsTransactionCompleted => transaction.Connection is null;

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
