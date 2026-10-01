// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Headless.Threading;
using Headless.UnitOfWork;

namespace Headless.Sql;

/// <summary>
/// Runs one store call on its own connection and READ COMMITTED transaction, commits it, and retries a transient
/// fault raised before the commit started: whatever <see cref="RelationalTransientFaults" /> classifies as transient
/// (a deadlock, a serialization conflict, a lock timeout, a dropped connection, a capacity fault).
/// </summary>
/// <remarks>
/// <para>
/// A fault raised once the commit started is never retried, whatever its classification: the commit may have
/// succeeded on the server before it failed on the wire, and a retry would apply the call twice. It surfaces
/// unchanged. This is the rule the unit-of-work runner applies to its own replays.
/// </para>
/// <para>
/// Only autonomous calls retry. A call running inside a caller's transaction must never be retried by the store: a
/// deadlock has already rolled that transaction back (on SQL Server later statements on the connection would even
/// autocommit one by one), so only the transaction's owner can run the whole unit again.
/// </para>
/// </remarks>
[PublicAPI]
public static class SqlAutonomousTransaction
{
    /// <summary>Runs <paramref name="body" /> in a new transaction, retrying a transient fault raised before the commit.</summary>
    /// <param name="createConnection">Creates an unopened connection; each attempt gets a new one.</param>
    /// <param name="body">The statements; they must decide everything the result reports.</param>
    /// <param name="timeProvider">The clock retries wait on.</param>
    /// <param name="cancellationToken">Token used to cancel the call and the waits between attempts.</param>
    /// <returns>The committed attempt's result.</returns>
    public static ValueTask<T> RunAsync<T>(
        Func<DbConnection> createConnection,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(createConnection);
        Argument.IsNotNull(body);

        // Each attempt opens its own connection and transaction, so the victim's rolled-back transaction is already
        // disposed and the retry starts clean.
        return RetryAsync(
            (attempt, ct) => _RunOnceAsync(createConnection, body, attempt, ct),
            timeProvider,
            onRetry: null,
            cancellationToken
        );
    }

    /// <summary>
    /// Runs <paramref name="attempt" />, and runs it again when it fails with a transient fault before it marked its
    /// commit as started, up to <see cref="TransientRetry.MaxAttempts" /> attempts in total.
    /// </summary>
    /// <param name="attempt">
    /// One self-contained attempt: it opens its own connection and transaction, and calls
    /// <see cref="SqlAutonomousAttempt.MarkCommitStarted" /> immediately before it commits.
    /// </param>
    /// <param name="timeProvider">The clock retries wait on.</param>
    /// <param name="onRetry">
    /// Called with the fault and the number of the attempt about to run, once per retry; for logging.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the call and the waits between attempts.</param>
    /// <returns>The successful attempt's result.</returns>
    /// <remarks>
    /// The fault that ends the call, a non-transient one, one raised after the commit started, or the last
    /// attempt's, propagates unchanged.
    /// </remarks>
    public static ValueTask<T> RetryAsync<T>(
        Func<SqlAutonomousAttempt, CancellationToken, Task<T>> attempt,
        TimeProvider timeProvider,
        Action<Exception, int>? onRetry = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(attempt);
        Argument.IsNotNull(timeProvider);

        // TransientRetry runs attempts one at a time and asks about a failure before starting the next, so the
        // attempt being judged is always the latest one.
        SqlAutonomousAttempt current = null!;
        var attemptNumber = 0;

        return TransientRetry.RunAsync(
            ct =>
            {
                current = new SqlAutonomousAttempt();
                attemptNumber++;

                return new ValueTask<T>(attempt(current, ct));
            },
            ex =>
            {
                if (current.CommitStarted || !RelationalTransientFaults.IsTransient(ex, cancellationToken))
                {
                    return false;
                }

                // TransientRetry asks only when another attempt will run, so this reports each retry once.
                onRetry?.Invoke(ex, attemptNumber + 1);

                return true;
            },
            timeProvider,
            cancellationToken
        );
    }

    private static async Task<T> _RunOnceAsync<T>(
        Func<DbConnection> createConnection,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        SqlAutonomousAttempt attempt,
        CancellationToken cancellationToken
    )
    {
        // Microsoft.Data.Sqlite completes every call synchronously, waiting out another writer's lock on the calling
        // thread; yielding first hands the caller a pending task instead of blocking it through that wait.
        await Task.Yield();
        await using var connection = createConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Explicit so the call runs at READ COMMITTED whatever the server default or a pooled session last used: a
        // stricter level turns a lock wait or a lost insert race into a conflict, and SQL Server refuses READPAST.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var result = await body(connection, transaction, cancellationToken).ConfigureAwait(false);

        attempt.MarkCommitStarted();
        // The statements already decided and the result describes them; a late cancel must not roll back a write the
        // caller is about to be told happened.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return result;
    }
}
