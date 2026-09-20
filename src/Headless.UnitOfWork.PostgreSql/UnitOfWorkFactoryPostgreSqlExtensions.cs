// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.UnitOfWork.Internal;
using Npgsql;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.UnitOfWork;

/// <summary>
/// Npgsql entry points for the scoped unit of work: <c>BeginAsync(connection)</c> (owned mode — the unit begins
/// the transaction eagerly and owns commit), <c>Enlist(connection, transaction)</c> (observed mode — the caller
/// commits, then calls <c>CompleteAsync</c>), and <c>RunAsync(connection, …)</c> (begin → operation → complete
/// in one call).
/// </summary>
/// <remarks>
/// Npgsql exposes no commit edge to observe, so observed mode is explicit: after committing the transaction the
/// caller calls <see cref="IUnitOfWork.CompleteAsync" />, after rolling it back <see cref="IUnitOfWork.RollbackAsync" />.
/// A unit disposed without either after its transaction completed is logged by the factory as a forgotten
/// completion. There is no execution-strategy retry for raw ADO (an EF Core concept); a throwing operation rolls
/// the unit back and discards the enlisted work. A closed connection is opened for the unit's duration and closed
/// again afterwards; an already-open connection is left open.
/// </remarks>
[PublicAPI]
public static class UnitOfWorkFactoryPostgreSqlExtensions
{
    extension(IUnitOfWorkFactory factory)
    {
        /// <summary>
        /// Begins an owned unit of work on <paramref name="connection" />: the transaction starts on this line and
        /// <c>CompleteAsync</c> commits it, then drains.
        /// </summary>
        /// <param name="connection">The connection whose transaction the unit owns; opened when closed.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted" />.</param>
        /// <param name="cancellationToken">Propagates the caller's cancellation to the open and begin.</param>
        /// <returns>The begun unit of work; the caller completes or disposes it.</returns>
        public ValueTask<IUnitOfWork> BeginAsync(
            NpgsqlConnection connection,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(factory);
            Argument.IsNotNull(connection);

            return factory.BeginAsync(
                ct => _BeginOwnedAsync(connection, isolation, ct),
                options: null,
                cancellationToken
            );
        }

        /// <summary>
        /// Enlists an already-open Npgsql transaction in observed mode: the caller commits (or rolls back) the
        /// transaction and then calls <c>CompleteAsync</c> (or <c>RollbackAsync</c>) on the returned unit.
        /// </summary>
        /// <param name="connection">The connection owning <paramref name="transaction" />.</param>
        /// <param name="transaction">The open transaction the unit observes.</param>
        /// <returns>The enlisted unit of work.</returns>
        public IUnitOfWork Enlist(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            Argument.IsNotNull(factory);
            Argument.IsNotNull(connection);
            Argument.IsNotNull(transaction);

            return factory.Enlist(new PostgreSqlUnitOfWorkResource(connection, transaction, owned: false));
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as an owned unit of work on <paramref name="connection" />:
        /// begin → operation → <c>CompleteAsync</c>. A throwing operation rolls the unit back and rethrows; a
        /// drain fault after a durable commit is logged, never surfaced.
        /// </summary>
        /// <param name="connection">The connection to operate on; opened when closed.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted" />.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, the operation, and commit.</param>
        public Task RunAsync(
            NpgsqlConnection connection,
            Func<IUnitOfWork, CancellationToken, Task> operation,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(factory);
            Argument.IsNotNull(connection);
            Argument.IsNotNull(operation);

            return UnitOfWorkRunner.RunAsync(
                factory,
                ct => _BeginOwnedAsync(connection, isolation, ct),
                async (unitOfWork, ct) =>
                {
                    await operation(unitOfWork, ct).ConfigureAwait(false);

                    return true;
                },
                UnitOfWorkRunner.LoggerFor(factory),
                cancellationToken
            );
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as an owned unit of work on <paramref name="connection" /> and returns
        /// its result, with the same semantics as the result-less <c>RunAsync</c> overload.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by <paramref name="operation" />.</typeparam>
        /// <param name="connection">The connection to operate on; opened when closed.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token, returning a result.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted" />.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, the operation, and commit.</param>
        /// <returns>The result produced by <paramref name="operation" />.</returns>
        public Task<TResult> RunAsync<TResult>(
            NpgsqlConnection connection,
            Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(factory);
            Argument.IsNotNull(connection);
            Argument.IsNotNull(operation);

            return UnitOfWorkRunner.RunAsync(
                factory,
                ct => _BeginOwnedAsync(connection, isolation, ct),
                operation,
                UnitOfWorkRunner.LoggerFor(factory),
                cancellationToken
            );
        }
    }

    private static async ValueTask<IUnitOfWorkResource> _BeginOwnedAsync(
        NpgsqlConnection connection,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        var shouldClose = connection.State == ConnectionState.Closed;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var transaction = await connection
                .BeginTransactionAsync(isolation, cancellationToken)
                .ConfigureAwait(false);

            return new PostgreSqlUnitOfWorkResource(connection, transaction, owned: true, closeConnection: shouldClose);
        }
        catch
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }

            throw;
        }
    }
}
