// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork.Internal;
using Npgsql;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.UnitOfWork;

/// <summary>
/// Npgsql entry points for the unit of work: <c>BeginAsync(connection)</c> (owned mode — the unit begins
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
/// <para>
/// Begin and enlist bind the unit to the connection (<see cref="HeadlessDbConnectionUnitOfWorkExtensions.UnitOfWork" />), and
/// <c>RunAsync(connection, …)</c> on a connection that already carries a live unit joins it: the block runs
/// inside the owner's unit and neither commits nor rolls back, so a service that wraps its own work in
/// <c>RunAsync</c> composes under a caller that already opened the transaction. A second <c>BeginAsync</c> or
/// <c>Enlist</c> on a bound connection is refused instead, because two units cannot own one transaction.
/// </para>
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
        /// <exception cref="InvalidOperationException">
        /// The connection already carries an active unit of work: join it with <c>RunAsync</c> or pass it along.
        /// </exception>
        public ValueTask<IUnitOfWork> BeginAsync(
            NpgsqlConnection connection,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(factory);
            Argument.IsNotNull(connection);

            return BoundConnectionUnitOfWork.BeginAsync(
                factory,
                connection,
                ct => _BeginOwnedAsync(connection, isolation, ct),
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
        /// <exception cref="InvalidOperationException">The connection already carries an active unit of work.</exception>
        public IUnitOfWork Enlist(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            Argument.IsNotNull(factory);
            Argument.IsNotNull(connection);
            Argument.IsNotNull(transaction);

            return BoundConnectionUnitOfWork.Enlist(
                factory,
                connection,
                new PostgreSqlUnitOfWorkResource(connection, transaction, owned: false)
            );
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as an owned unit of work on <paramref name="connection" />:
        /// begin → operation → <c>CompleteAsync</c>. A throwing operation rolls the unit back and rethrows; a
        /// drain fault after a durable commit is logged, never surfaced. When the connection already carries a
        /// live unit, the block joins it instead: it receives that unit, and commit or rollback stay with its
        /// owner; a block that ends the unit itself is refused once it returns.
        /// </summary>
        /// <param name="connection">The connection to operate on; opened when closed.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token.</param>
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Defaults to <see cref="IsolationLevel.ReadCommitted" />.
        /// Ignored when the call joins an already-bound unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, the operation, and commit.</param>
        /// <exception cref="InvalidOperationException">A joined block completed, rolled back, or disposed the owner's unit.</exception>
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
                connection.UnitOfWork(),
                ct =>
                    BoundConnectionUnitOfWork.BeginAsync(
                        factory,
                        connection,
                        beginCt => _BeginOwnedAsync(connection, isolation, beginCt),
                        ct
                    ),
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
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Defaults to <see cref="IsolationLevel.ReadCommitted" />.
        /// Ignored when the call joins an already-bound unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, the operation, and commit.</param>
        /// <returns>The result produced by <paramref name="operation" />.</returns>
        /// <exception cref="InvalidOperationException">A joined block completed, rolled back, or disposed the owner's unit.</exception>
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
                connection.UnitOfWork(),
                ct =>
                    BoundConnectionUnitOfWork.BeginAsync(
                        factory,
                        connection,
                        beginCt => _BeginOwnedAsync(connection, isolation, beginCt),
                        ct
                    ),
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
