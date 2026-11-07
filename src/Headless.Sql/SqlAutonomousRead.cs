// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Sql;

/// <summary>
/// Runs one read-only store call on its own connection with no transaction, and retries any transient fault
/// <see cref="RelationalTransientFaults" /> classifies.
/// </summary>
/// <remarks>
/// <para>
/// The body runs one statement. A single statement already reads one consistent view of the rows it reads on every
/// engine (PostgreSQL's statement snapshot, SQL Server's statement-level READ COMMITTED, SQLite's autocommit read), so
/// wrapping it in a transaction adds a <c>BEGIN</c> and a <c>COMMIT</c> and changes nothing it decides. Those round
/// trips cut a one-row read's throughput by about 1.7x on PostgreSQL and 2.9x on SQL Server at 32 concurrent callers.
/// On SQLite an autonomous transaction also takes the write lock at begin, so a read in one waits behind a writer and
/// blocks it; a read with no transaction takes only a shared lock.
/// </para>
/// <para>
/// A body that runs two statements whose results must agree needs
/// <see cref="SqlAutonomousTransaction.RunAsync{T}" /> instead. The call writes nothing, so every transient fault is
/// retried, however late it was raised.
/// </para>
/// </remarks>
[PublicAPI]
public static class SqlAutonomousRead
{
    /// <summary>Runs <paramref name="body" /> on a new connection, retrying a transient fault.</summary>
    /// <param name="operation">
    /// The store call this is, in <c>feature.verb</c> form (<c>fencing.status</c>); the span and every metric carry it
    /// as <c>headless.sql.operation</c>. Pass a constant: each distinct value is a separate metric series.
    /// </param>
    /// <param name="createConnection">Creates an unopened connection; each attempt gets a new one.</param>
    /// <param name="body">The one statement and the reading of its result; it must write nothing.</param>
    /// <param name="timeProvider">The clock retries wait on.</param>
    /// <param name="cancellationToken">Token used to cancel the call and the waits between attempts.</param>
    /// <returns>The successful attempt's result.</returns>
    public static ValueTask<T> RunAsync<T>(
        string operation,
        Func<DbConnection> createConnection,
        Func<DbConnection, CancellationToken, Task<T>> body,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(operation);
        Argument.IsNotNull(createConnection);
        Argument.IsNotNull(body);

        // No attempt ever marks a commit as started, so the retry treats every transient fault as safe to repeat.
        return SqlAutonomousTransaction.RetryAsync(
            operation,
            (_, ct) => _RunOnceAsync(createConnection, body, ct),
            timeProvider,
            onRetry: null,
            cancellationToken
        );
    }

    private static async Task<T> _RunOnceAsync<T>(
        Func<DbConnection> createConnection,
        Func<DbConnection, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        // Microsoft.Data.Sqlite completes every call synchronously, waiting out an exclusive lock on the calling thread;
        // yielding first hands the caller a pending task instead of blocking it through that wait.
        await Task.Yield();
        await using var connection = createConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await body(connection, cancellationToken).ConfigureAwait(false);
    }
}
