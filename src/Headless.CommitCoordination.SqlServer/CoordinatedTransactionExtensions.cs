// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Data.SqlClient;

/// <summary>
/// Single-call coordinated-transaction helpers for a raw-ADO <see cref="SqlConnection"/>: open a
/// transaction, enlist it in commit coordination, run the operation, and commit — so deferred work buffered
/// inside the operation drains atomically on commit and is discarded on rollback. The enlist cannot be
/// forgotten because it is welded into the helper.
/// </summary>
/// <remarks>
/// A raw connection cannot expose a resolving scope, so these overloads require an explicit
/// <c>IServiceProvider</c> (the request scope) for the post-commit drain. There is no execution-strategy
/// retry for raw ADO (that is an EF Core concept); a throwing operation rolls the transaction back and
/// discards the enlisted buffer via un-signalled dispose. If the connection is closed it is opened for the
/// duration and closed again afterward; an already-open connection is left open.
/// </remarks>
[PublicAPI]
public static class HeadlessSqlServerCoordinatedTransactionExtensions
{
    extension(SqlConnection connection)
    {
        /// <summary>
        /// Executes <paramref name="operation"/> inside a commit-coordinated transaction.
        /// </summary>
        /// <param name="operation">An asynchronous delegate receiving the connection and a cancellation token.</param>
        /// <param name="services">The scoped (request) service provider captured for the post-commit drain.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task ExecuteCoordinatedTransactionAsync(
            Func<SqlConnection, CancellationToken, Task> operation,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            return _ExecuteCoreAsync(
                connection,
                services,
                isolation,
                async (c, ct) =>
                {
                    await operation(c, ct).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            );
        }

        /// <summary>
        /// Executes <paramref name="operation"/> inside a commit-coordinated transaction, forwarding <paramref name="arg"/>.
        /// </summary>
        /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
        /// <param name="operation">An asynchronous delegate receiving <paramref name="arg"/>, the connection, and a cancellation token.</param>
        /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
        /// <param name="services">The scoped (request) service provider captured for the post-commit drain.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task ExecuteCoordinatedTransactionAsync<TArg>(
            Func<TArg, SqlConnection, CancellationToken, Task> operation,
            TArg arg,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            return _ExecuteCoreAsync(
                connection,
                services,
                isolation,
                async (c, ct) =>
                {
                    await operation(arg, c, ct).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            );
        }

        /// <summary>
        /// Executes <paramref name="operation"/> inside a commit-coordinated transaction and returns its result.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
        /// <param name="operation">An asynchronous delegate receiving the connection and a cancellation token, returning a result.</param>
        /// <param name="services">The scoped (request) service provider captured for the post-commit drain.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result produced by <paramref name="operation"/>.</returns>
        public Task<TResult> ExecuteCoordinatedTransactionAsync<TResult>(
            Func<SqlConnection, CancellationToken, Task<TResult>> operation,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            return _ExecuteCoreAsync(connection, services, isolation, operation, cancellationToken);
        }

        /// <summary>
        /// Executes <paramref name="operation"/> inside a commit-coordinated transaction, forwarding <paramref name="arg"/>, and returns its result.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by the operation.</typeparam>
        /// <typeparam name="TArg">Type of the argument passed to <paramref name="operation"/>.</typeparam>
        /// <param name="operation">An asynchronous delegate receiving <paramref name="arg"/>, the connection, and a cancellation token, returning a result.</param>
        /// <param name="arg">Argument forwarded to <paramref name="operation"/>.</param>
        /// <param name="services">The scoped (request) service provider captured for the post-commit drain.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result produced by <paramref name="operation"/>.</returns>
        public Task<TResult> ExecuteCoordinatedTransactionAsync<TResult, TArg>(
            Func<TArg, SqlConnection, CancellationToken, Task<TResult>> operation,
            TArg arg,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            return _ExecuteCoreAsync(
                connection,
                services,
                isolation,
                (c, ct) => operation(arg, c, ct),
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Shared body for every <c>ExecuteCoordinatedTransactionAsync</c> overload: opens the connection when
    /// closed, begins the transaction, enlists commit coordination, runs <paramref name="operation"/>, commits,
    /// signals the scope, and closes the connection if it was opened here. SqlServer commit detection is
    /// out-of-band — the SqlClient diagnostic raises <c>SignalCommittedAsync</c> within <c>CommitAsync</c> — but
    /// this helper also signals explicitly so the drain still runs when the diagnostic is disabled or degraded;
    /// when the diagnostic already claimed the scope the explicit signal is a logged no-op.
    /// </summary>
    private static async Task<TResult> _ExecuteCoreAsync<TResult>(
        SqlConnection connection,
        IServiceProvider services,
        IsolationLevel isolation,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        // Resolve the post-commit-fault logger up front (fail loud here, at a safe point) rather than with a
        // null-conditional inside the catch: a missing ILoggerFactory is a host misconfiguration, and surfacing it
        // before any commit is safe, whereas resolving it inside the post-commit catch could throw after the
        // transaction is already durable — exactly the caller-failure the catch exists to prevent.
        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Headless.CommitCoordination.SqlServer.CoordinatedTransaction");

        var shouldClose = connection.State == ConnectionState.Closed;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var transaction = (SqlTransaction)
                await connection.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // Enlist SYNCHRONOUSLY, in this frame, so the ambient coordinator flows to the operation's publishes.
                var scope = connection.EnlistCommitCoordination(transaction, services, cancellationToken);

                await using (scope.ConfigureAwait(false))
                {
                    var result = await operation(connection, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    try
                    {
                        await scope.SignalAsync(CommitOutcome.Committed).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // The transaction is ALREADY durably committed. The explicit signal is the degraded-mode
                        // fallback (diagnostic disabled or missed); a drain fault here must not surface as a
                        // caller failure — a retry would re-run the operation and double-apply. The enlisted work
                        // is relay-recoverable, so log and return the committed result.
                        logger.LogError(
                            ex,
                            "Post-commit drain faulted after a successful SQL Server commit; the relay will recover any uncommitted work."
                        );
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
