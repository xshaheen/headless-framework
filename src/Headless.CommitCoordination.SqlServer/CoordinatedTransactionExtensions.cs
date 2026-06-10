// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.Data.SqlClient;

namespace Headless.CommitCoordination.SqlServer;

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
public static class CoordinatedTransactionExtensions
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
        public async Task ExecuteCoordinatedTransactionAsync(
            Func<SqlConnection, CancellationToken, Task> operation,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            var shouldClose = connection.State == ConnectionState.Closed;

            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var transaction = (SqlTransaction)
                    await connection.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false);

                // Enlist SYNCHRONOUSLY, in this frame, so the ambient coordinator flows to the operation's publishes.
                await using var _ = connection.EnlistCommitCoordination(transaction, services);

                await operation(connection, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
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
        public async Task ExecuteCoordinatedTransactionAsync<TArg>(
            Func<TArg, SqlConnection, CancellationToken, Task> operation,
            TArg arg,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            var shouldClose = connection.State == ConnectionState.Closed;

            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var transaction = (SqlTransaction)
                    await connection.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false);

                await using var _ = connection.EnlistCommitCoordination(transaction, services);

                await operation(arg, connection, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
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
        public async Task<TResult> ExecuteCoordinatedTransactionAsync<TResult>(
            Func<SqlConnection, CancellationToken, Task<TResult>> operation,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            var shouldClose = connection.State == ConnectionState.Closed;

            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var transaction = (SqlTransaction)
                    await connection.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false);

                await using var _ = connection.EnlistCommitCoordination(transaction, services);

                var result = await operation(connection, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return result;
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
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
        public async Task<TResult> ExecuteCoordinatedTransactionAsync<TResult, TArg>(
            Func<TArg, SqlConnection, CancellationToken, Task<TResult>> operation,
            TArg arg,
            IServiceProvider services,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            var shouldClose = connection.State == ConnectionState.Closed;

            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var transaction = (SqlTransaction)
                    await connection.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false);

                await using var _ = connection.EnlistCommitCoordination(transaction, services);

                var result = await operation(arg, connection, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return result;
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
}
