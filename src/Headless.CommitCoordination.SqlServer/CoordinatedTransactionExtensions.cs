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
/// <c>IServiceProvider</c> that resolves the commit coordination services. SQL Server signaling is explicit
/// (this helper signals after its own <c>Commit</c>), so a caller that commits a raw <c>SqlTransaction</c>
/// outside this helper must signal the enlisted scope itself. There is no execution-strategy retry for raw ADO
/// (that is an EF Core concept); a throwing operation rolls the transaction back and discards the enlisted work.
/// If the connection is closed it is opened for the duration and closed again afterward; an already-open
/// connection is left open.
/// </remarks>
[PublicAPI]
public static partial class HeadlessSqlServerCoordinatedTransactionExtensions
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
    /// Resolves the post-commit-fault logger up front (fail loud here, at a safe point) rather than with a
    /// null-conditional inside the catch: a missing ILoggerFactory is a host misconfiguration, and surfacing it
    /// before any commit is safe, whereas resolving it inside the post-commit catch could throw after the
    /// transaction is already durable — exactly the caller-failure the catch exists to prevent. The transaction
    /// body itself is the shared <see cref="CoordinatedTransactionRunner" />.
    /// </summary>
    private static Task<TResult> _ExecuteCoreAsync<TResult>(
        SqlConnection connection,
        IServiceProvider services,
        IsolationLevel isolation,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Headless.CommitCoordination.SqlServer.CoordinatedTransaction");

        return CoordinatedTransactionRunner.ExecuteAsync(
            connection,
            isolation,
            static async (c, iso, ct) => (SqlTransaction)await c.BeginTransactionAsync(iso, ct).ConfigureAwait(false),
            (c, t) => c.EnlistCommitCoordination(t, services, cancellationToken),
            operation,
            logger,
            static (l, ex) => LogPostCommitDrainFaulted(l, ex),
            cancellationToken
        );
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Post-commit drain faulted after a successful SQL Server commit; the relay will recover any uncommitted work."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogPostCommitDrainFaulted(ILogger logger, Exception exception);
}
