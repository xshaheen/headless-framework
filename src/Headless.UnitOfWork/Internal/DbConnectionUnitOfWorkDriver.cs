// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Polly.Retry;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The one body behind every raw-ADO provider's <c>BeginAsync</c>, <c>Enlist</c>, and <c>RunAsync</c>: refuse a
/// connection that already carries a live unit, open a closed connection for the unit's duration, begin the
/// transaction, wrap it in a <see cref="DbConnectionUnitOfWorkResource" />, and record the unit in the connection
/// binding so a later <c>RunAsync(connection, …)</c> joins it. Each provider keeps its own driver-typed public
/// members and supplies only what its driver does differently.
/// </summary>
/// <typeparam name="TConnection">The driver's connection type, which the per-attempt block receives.</typeparam>
/// <param name="isTransactionCompleted">
/// The driver's completion probe; <see langword="null" /> uses <see cref="DbTransaction.Connection" /> being
/// cleared, which holds for every driver that detaches a transaction from its connection once it ends.
/// </param>
/// <param name="yieldBeforeBegin">
/// Set for a driver that blocks its caller's thread inside the open or begin (SQLite waits for the database write
/// lock synchronously): yielding first hands the caller a pending task instead of blocking it through that wait.
/// </param>
internal sealed class DbConnectionUnitOfWorkDriver<TConnection>(
    Func<DbTransaction, bool>? isTransactionCompleted = null,
    bool yieldBeforeBegin = false
)
    where TConnection : DbConnection
{
    private readonly Func<DbTransaction, bool> _isTransactionCompleted =
        isTransactionCompleted ?? (static transaction => transaction.Connection is null);

    /// <summary>
    /// Begins an owned unit on <paramref name="connection" />. The refusal runs inside the factory's begin so it
    /// precedes any connection or transaction effect, and the binding is recorded once the unit exists.
    /// </summary>
    public async ValueTask<IUnitOfWork> BeginAsync(
        IUnitOfWorkFactory factory,
        TConnection connection,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        var unit = await factory
            .BeginAsync(
                async ct =>
                {
                    // Awaited, not fire-and-forget: a stale unit the binding abandons here closes the connection it
                    // opened, and that close must land before the begin below reads the connection's state.
                    await DbConnectionUnitOfWorkBinding.ThrowIfBoundAsync(connection).ConfigureAwait(false);

                    return await _BeginOwnedAsync(connection, isolation, ct).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        DbConnectionUnitOfWorkBinding.Bind(connection, unit);

        return unit;
    }

    /// <summary>Enlists the caller's open <paramref name="transaction" /> in observed mode and records the binding.</summary>
    public IUnitOfWork Enlist(IUnitOfWorkFactory factory, TConnection connection, DbTransaction transaction)
    {
        DbConnectionUnitOfWorkBinding.ThrowIfBound(connection);

        var unit = factory.Enlist(
            new DbConnectionUnitOfWorkResource(connection, transaction, _isTransactionCompleted, owned: false)
        );
        DbConnectionUnitOfWorkBinding.Bind(connection, unit);

        return unit;
    }

    /// <summary>
    /// Runs <paramref name="operation" /> on the caller's <paramref name="connection" />, joining the unit already
    /// bound there or beginning one. Never replays: the caller owns the connection.
    /// </summary>
    public Task RunAsync(
        IUnitOfWorkFactory factory,
        TConnection connection,
        Func<IUnitOfWork, CancellationToken, Task> operation,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        return RunAsync(
            factory,
            connection,
            async (unitOfWork, ct) =>
            {
                await operation(unitOfWork, ct).ConfigureAwait(false);

                return true;
            },
            isolation,
            cancellationToken
        );
    }

    /// <inheritdoc cref="RunAsync(IUnitOfWorkFactory, TConnection, Func{IUnitOfWork, CancellationToken, Task}, IsolationLevel, CancellationToken)" />
    public Task<TResult> RunAsync<TResult>(
        IUnitOfWorkFactory factory,
        TConnection connection,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        return UnitOfWorkRunner.RunAsync(
            () => DbConnectionUnitOfWorkBinding.TryGetAsync(connection),
            ct => BeginAsync(factory, connection, isolation, ct),
            operation,
            NoReplayUnitOfWorkExecutionStrategy.Instance,
            UnitOfWorkRunner.LoggerFor(factory),
            cancellationToken
        );
    }

    /// <summary>
    /// Runs <paramref name="operation" /> on a connection of its own per attempt, taken from
    /// <paramref name="connectionFactory" /> and opened when it arrives closed, under the call's or the host's
    /// replay policy.
    /// </summary>
    public Task RunPerAttemptConnectionAsync(
        IUnitOfWorkFactory factory,
        Func<CancellationToken, ValueTask<TConnection>> connectionFactory,
        Func<IUnitOfWork, TConnection, CancellationToken, Task> operation,
        IsolationLevel isolation,
        RetryStrategyOptions? retry,
        CancellationToken cancellationToken
    )
    {
        return RunPerAttemptConnectionAsync(
            factory,
            connectionFactory,
            async (unitOfWork, connection, ct) =>
            {
                await operation(unitOfWork, connection, ct).ConfigureAwait(false);

                return true;
            },
            isolation,
            retry,
            cancellationToken
        );
    }

    /// <inheritdoc cref="RunPerAttemptConnectionAsync(IUnitOfWorkFactory, Func{CancellationToken, ValueTask{TConnection}}, Func{IUnitOfWork, TConnection, CancellationToken, Task}, IsolationLevel, RetryStrategyOptions?, CancellationToken)" />
    public Task<TResult> RunPerAttemptConnectionAsync<TResult>(
        IUnitOfWorkFactory factory,
        Func<CancellationToken, ValueTask<TConnection>> connectionFactory,
        Func<IUnitOfWork, TConnection, CancellationToken, Task<TResult>> operation,
        IsolationLevel isolation,
        RetryStrategyOptions? retry,
        CancellationToken cancellationToken
    )
    {
        return UnitOfWorkRunner.RunPerAttemptConnectionAsync(
            factory,
            ct => _OpenAttemptConnectionAsync(connectionFactory, ct),
            (connection, ct) => BeginAsync(factory, connection, isolation, ct),
            operation,
            retry,
            cancellationToken
        );
    }

    private static async ValueTask<TConnection> _OpenAttemptConnectionAsync(
        Func<CancellationToken, ValueTask<TConnection>> connectionFactory,
        CancellationToken cancellationToken
    )
    {
        var connection =
            await connectionFactory(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The {typeof(TConnection).Name} factory passed to RunAsync returned null. Return a new connection for each attempt."
            );

        if (connection.State != ConnectionState.Closed)
        {
            return connection;
        }

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return connection;
    }

    private async ValueTask<IUnitOfWorkResource> _BeginOwnedAsync(
        TConnection connection,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        if (yieldBeforeBegin)
        {
            await Task.Yield();
        }

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

            return new DbConnectionUnitOfWorkResource(
                connection,
                transaction,
                _isTransactionCompleted,
                owned: true,
                closeConnection: shouldClose
            );
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
