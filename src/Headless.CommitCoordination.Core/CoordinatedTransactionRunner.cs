// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Headless.CommitCoordination;

/// <summary>
/// The one body behind every raw-ADO <c>ExecuteCoordinatedTransactionAsync</c> helper: open the connection when it
/// is closed, begin the transaction, enlist it in commit coordination synchronously, run the operation, commit, and
/// own both signals. The providers supply only what differs between drivers: how a transaction begins, how it
/// enlists, and the wording of the post-commit fault log.
/// </summary>
/// <remarks>
/// A raw driver has no commit edge to observe, so the runner signals <c>Committed</c> after the commit (without it
/// the un-signalled dispose would discard the enlisted work on every successful commit) and <c>RolledBack</c> when
/// the operation or the commit throws, so the failure path never reads as a forgotten signal.
/// </remarks>
internal static class CoordinatedTransactionRunner
{
    public static async Task<TResult> ExecuteAsync<TConnection, TTransaction, TResult>(
        TConnection connection,
        IsolationLevel isolation,
        Func<TConnection, IsolationLevel, CancellationToken, ValueTask<TTransaction>> beginTransaction,
        Func<TConnection, TTransaction, ICommitScope> enlist,
        Func<TConnection, CancellationToken, Task<TResult>> operation,
        ILogger logger,
        Action<ILogger, Exception> logPostCommitDrainFaulted,
        CancellationToken cancellationToken
    )
        where TConnection : DbConnection
        where TTransaction : DbTransaction
    {
        var shouldClose = connection.State == ConnectionState.Closed;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var transaction = await beginTransaction(connection, isolation, cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // Enlist SYNCHRONOUSLY, in this frame, so the ambient coordinator flows to the operation's publishes.
                var scope = enlist(connection, transaction);

                await using (scope.ConfigureAwait(false))
                {
                    TResult result;

                    try
                    {
                        result = await operation(connection, cancellationToken).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The physical rollback happens when the transaction disposes; the explicit signal discards
                        // the enlisted work now and keeps the scope's forgotten-signal warning for hand-rolled
                        // enlistments only. Nothing runs on rollback, so this cannot mask the caller's exception.
                        await scope.SignalAsync(CommitOutcome.RolledBack).ConfigureAwait(false);
                        throw;
                    }

                    try
                    {
                        // The drain runs to completion regardless of the caller's token: the commit is durable, and
                        // aborting the drain would only log a spurious fault for work that was going to run.
                        await scope.SignalAsync(CommitOutcome.Committed).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // The transaction is ALREADY durably committed. The drain is the dispatch accelerator; a
                        // fault here must not surface as a caller failure — a retry would re-run the operation and
                        // double-apply. The enlisted work is relay-recoverable (durable rows committed
                        // in-transaction + polling recovery), so log and return the committed result.
                        logPostCommitDrainFaulted(logger, ex);
                    }

                    return result;
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
