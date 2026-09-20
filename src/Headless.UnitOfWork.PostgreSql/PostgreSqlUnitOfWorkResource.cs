// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Npgsql;

namespace Headless.UnitOfWork;

/// <summary>
/// The Npgsql unit-of-work resource: exposes the live connection and transaction so participants (outbox and job
/// writers) place durable rows inside the caller's transaction.
/// </summary>
/// <remarks>
/// Owned mode began the transaction itself and commits or rolls it back on the unit's verbs, disposing the
/// transaction afterwards and closing the connection when the provider opened it. Observed mode wraps a
/// caller-committed transaction: both verbs are no-ops and the unit only coordinates the commit edge.
/// </remarks>
internal sealed class PostgreSqlUnitOfWorkResource(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    bool owned,
    bool closeConnection = false
) : IRelationalUnitOfWorkResource
{
    public bool IsOwned { get; } = owned;

    public DbConnection Connection => connection;

    public DbTransaction Transaction => transaction;

    /// <summary>
    /// Npgsql keeps its completion flag internal and leaves <c>Connection</c> populated after commit, so the only
    /// public observable is the readiness guard on <see cref="NpgsqlTransaction.IsolationLevel" />, which throws
    /// once the transaction has committed, rolled back, or been disposed. Evaluated lazily (the factory reads it
    /// only on an un-completed dispose), so the exception cost never lands on a healthy commit; a driver that stops
    /// throwing degrades to "no warning", never to a false one.
    /// </summary>
    public bool IsTransactionCompleted
    {
        get
        {
            try
            {
                _ = transaction.IsolationLevel;

                return false;
            }
            catch (InvalidOperationException)
            {
                // Completed ("no longer usable") or disposed (ObjectDisposedException derives from this type).
                return true;
            }
        }
    }

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
