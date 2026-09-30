// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Headless.Threading;

namespace Headless.Sql;

/// <summary>
/// Runs one store call on its own connection and READ COMMITTED transaction, commits it, and retries only the failures
/// a fresh transaction can clear: deadlocks and serialization conflicts, as the dialect classifies them.
/// </summary>
/// <remarks>
/// Only autonomous calls retry. A call running inside a caller's transaction must never be retried by the store: a
/// deadlock has already rolled that transaction back (on SQL Server later statements on the connection would even
/// autocommit one by one), so only the transaction's owner can run the whole unit again.
/// </remarks>
[PublicAPI]
public static class SqlAutonomousTransaction
{
    /// <summary>Runs <paramref name="body" /> in a new transaction, retrying on a transient conflict.</summary>
    /// <param name="dialect">Classifies the engine's failures.</param>
    /// <param name="createConnection">Creates an unopened connection; each attempt gets a new one.</param>
    /// <param name="body">The statements; they must decide everything the result reports.</param>
    /// <param name="timeProvider">The clock retries wait on.</param>
    /// <param name="cancellationToken">Token used to cancel the call and the waits between attempts.</param>
    /// <returns>The committed attempt's result.</returns>
    public static ValueTask<T> RunAsync<T>(
        ISqlDialect dialect,
        Func<DbConnection> createConnection,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(dialect);
        Argument.IsNotNull(createConnection);
        Argument.IsNotNull(body);
        Argument.IsNotNull(timeProvider);

        // Each attempt opens its own connection and transaction, so the victim's rolled-back transaction is already
        // disposed and the retry starts clean.
        return TransientRetry.RunAsync(
            ct => _RunOnceAsync(createConnection, body, ct),
            ex => IsTransient(dialect, ex),
            timeProvider,
            cancellationToken
        );
    }

    /// <summary>Returns whether a fresh transaction can clear <paramref name="exception" />.</summary>
    public static bool IsTransient(ISqlDialect dialect, Exception exception)
    {
        Argument.IsNotNull(dialect);

        return dialect.Classify(exception) is SqlErrorKind.Deadlock or SqlErrorKind.SerializationConflict;
    }

    private static async ValueTask<T> _RunOnceAsync<T>(
        Func<DbConnection> createConnection,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        await using var connection = createConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Explicit so the call runs at READ COMMITTED whatever the server default or a pooled session last used: a
        // stricter level turns a lock wait or a lost insert race into a conflict, and SQL Server refuses READPAST.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var result = await body(connection, transaction, cancellationToken).ConfigureAwait(false);

        // The statements already decided and the result describes them; a late cancel must not roll back a write the
        // caller is about to be told happened.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return result;
    }
}
