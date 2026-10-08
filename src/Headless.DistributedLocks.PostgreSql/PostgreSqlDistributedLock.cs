// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

#pragma warning disable CA2100 // Advisory SQL text is fixed; key values are supplied as parameters.
/// <summary>
/// Provides low-level helpers for acquiring PostgreSQL transaction-scoped advisory locks directly
/// against a caller-supplied <see cref="NpgsqlTransaction"/>. These helpers emit
/// <c>pg_advisory_xact_lock</c> / <c>pg_try_advisory_xact_lock</c> SQL and are the thin advisory-lock
/// façade used by application code that already owns a transaction and wants to couple lock lifetime to it.
/// </summary>
/// <remarks>
/// Transaction-scoped advisory locks are released automatically when the enclosing transaction commits or
/// rolls back — there is no explicit release step. Underlying Npgsql errors (for example
/// <see cref="Npgsql.NpgsqlException"/>) propagate to the caller.
/// <para>
/// The transaction is the lock's owner, so acquiring a key the transaction already holds succeeds at once; the
/// commit or rollback still releases it. Each acquire runs inside its own savepoint: an acquire that fails or is
/// cancelled is rolled back to that savepoint, which keeps the caller's transaction usable and releases a lock the
/// server granted before the client gave up.
/// </para>
/// <para>
/// Every method has a <see cref="DbTransaction"/> overload for callers that hold the transaction through an
/// abstraction, such as EF Core's <c>db.Database.CurrentTransaction.GetDbTransaction()</c>, and a synchronous
/// variant for the places EF Core only exposes synchronously, such as a <c>SavingChanges</c> interceptor.
/// </para>
/// </remarks>
[PublicAPI]
public static class PostgreSqlDistributedLock
{
    private const string _Savepoint = "headless_advisory_xact_lock";
    private const string _SetSavepoint = "headless_advisory_xact_lock_set";
    private const string _PriorLockTimeoutSetting = "headless.lock_timeout_before_acquire";

    /// <summary>
    /// Acquires a transaction-scoped exclusive advisory lock for <paramref name="key"/> on the connection
    /// associated with <paramref name="transaction"/>, blocking until the lock is granted by the server.
    /// </summary>
    /// <param name="key">The advisory-lock key to acquire.</param>
    /// <param name="transaction">
    /// The active transaction whose connection will execute the <c>pg_advisory_xact_lock</c> command.
    /// The lock is held until this transaction commits or rolls back.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="transaction"/> has no associated open connection (already committed,
    /// rolled back, or disposed).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the server grants the lock.
    /// </exception>
    public static async ValueTask AcquireWithTransactionAsync(
        PostgreSqlAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        await AcquireInSavepointAsync(key, transaction, Timeout.InfiniteTimeSpan, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to acquire a transaction-scoped exclusive advisory lock for <paramref name="key"/> on the
    /// connection associated with <paramref name="transaction"/> using a non-blocking
    /// <c>pg_try_advisory_xact_lock</c> call.
    /// </summary>
    /// <param name="key">The advisory-lock key to acquire.</param>
    /// <param name="transaction">
    /// The active transaction whose connection will execute the <c>pg_try_advisory_xact_lock</c> command.
    /// The lock is held until this transaction commits or rolls back when acquired.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if another session
    /// currently holds a conflicting lock.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="transaction"/> has no associated open connection (already committed,
    /// rolled back, or disposed).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the command completes.
    /// </exception>
    public static ValueTask<bool> TryAcquireWithTransactionAsync(
        PostgreSqlAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        return AcquireInSavepointAsync(key, transaction, TimeSpan.Zero, cancellationToken);
    }

    /// <inheritdoc cref="AcquireWithTransactionAsync(PostgreSqlAdvisoryLockKey, NpgsqlTransaction, CancellationToken)"/>
    /// <exception cref="ArgumentException">Thrown when <paramref name="transaction"/> is not an <see cref="NpgsqlTransaction"/>.</exception>
    public static ValueTask AcquireWithTransactionAsync(
        PostgreSqlAdvisoryLockKey key,
        DbTransaction transaction,
        CancellationToken cancellationToken = default
    ) => AcquireWithTransactionAsync(key, _RequireNpgsql(transaction), cancellationToken);

    /// <inheritdoc cref="TryAcquireWithTransactionAsync(PostgreSqlAdvisoryLockKey, NpgsqlTransaction, CancellationToken)"/>
    /// <exception cref="ArgumentException">Thrown when <paramref name="transaction"/> is not an <see cref="NpgsqlTransaction"/>.</exception>
    public static ValueTask<bool> TryAcquireWithTransactionAsync(
        PostgreSqlAdvisoryLockKey key,
        DbTransaction transaction,
        CancellationToken cancellationToken = default
    ) => TryAcquireWithTransactionAsync(key, _RequireNpgsql(transaction), cancellationToken);

    /// <summary>
    /// Synchronous form of <see cref="AcquireWithTransactionAsync(PostgreSqlAdvisoryLockKey, NpgsqlTransaction, CancellationToken)"/>
    /// for callers on a synchronous path, such as an EF Core <c>SavingChanges</c> interceptor. Blocks the calling
    /// thread until the server grants the lock.
    /// </summary>
    /// <param name="key">The advisory-lock key to acquire.</param>
    /// <param name="transaction">The active transaction whose connection executes <c>pg_advisory_xact_lock</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="transaction"/> has no associated open connection (already committed,
    /// rolled back, or disposed).
    /// </exception>
    public static void AcquireWithTransaction(PostgreSqlAdvisoryLockKey key, NpgsqlTransaction transaction)
    {
        _AcquireInSavepoint(key, transaction, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc cref="AcquireWithTransaction(PostgreSqlAdvisoryLockKey, NpgsqlTransaction)"/>
    /// <exception cref="ArgumentException">Thrown when <paramref name="transaction"/> is not an <see cref="NpgsqlTransaction"/>.</exception>
    public static void AcquireWithTransaction(PostgreSqlAdvisoryLockKey key, DbTransaction transaction) =>
        AcquireWithTransaction(key, _RequireNpgsql(transaction));

    /// <summary>
    /// Synchronous form of <see cref="TryAcquireWithTransactionAsync(PostgreSqlAdvisoryLockKey, NpgsqlTransaction, CancellationToken)"/>
    /// for callers on a synchronous path, such as an EF Core <c>SavingChanges</c> interceptor.
    /// </summary>
    /// <param name="key">The advisory-lock key to acquire.</param>
    /// <param name="transaction">The active transaction whose connection executes <c>pg_try_advisory_xact_lock</c>.</param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if another session
    /// currently holds a conflicting lock.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="transaction"/> has no associated open connection (already committed,
    /// rolled back, or disposed).
    /// </exception>
    public static bool TryAcquireWithTransaction(PostgreSqlAdvisoryLockKey key, NpgsqlTransaction transaction)
    {
        return _AcquireInSavepoint(key, transaction, TimeSpan.Zero);
    }

    /// <inheritdoc cref="TryAcquireWithTransaction(PostgreSqlAdvisoryLockKey, NpgsqlTransaction)"/>
    /// <exception cref="ArgumentException">Thrown when <paramref name="transaction"/> is not an <see cref="NpgsqlTransaction"/>.</exception>
    public static bool TryAcquireWithTransaction(PostgreSqlAdvisoryLockKey key, DbTransaction transaction) =>
        TryAcquireWithTransaction(key, _RequireNpgsql(transaction));

    /// <summary>
    /// Takes the transaction-scoped advisory lock for every key in <paramref name="keys"/>, in the order given and
    /// under one wait budget, all-or-nothing. A set of two or more runs inside an outer savepoint around the per-key
    /// savepoints: when any key fails, times out, or is cancelled, rolling back to it drops every lock this call took
    /// and keeps the ones the transaction held before.
    /// </summary>
    /// <param name="keys">One or more advisory-lock keys in acquisition order.</param>
    /// <param name="transaction">The caller's transaction, which owns the locks once acquired.</param>
    /// <param name="timeout">
    /// The budget for the whole set, with the sentinels of <see cref="AcquireInSavepointAsync"/>; each key waits only
    /// for what the earlier keys left.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquire commands.</param>
    /// <returns><see langword="false"/> when a key was still held by another session as the budget ran out.</returns>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and rolling back to its savepoint failed too, so the transaction may still hold some keys.
    /// </exception>
    internal static async ValueTask<bool> AcquireAllInSavepointAsync(
        IReadOnlyList<PostgreSqlAdvisoryLockKey> keys,
        NpgsqlTransaction transaction,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (keys.Count == 1)
        {
            return await AcquireInSavepointAsync(keys[0], transaction, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        var connection = _RequireConnection(transaction);
        await transaction.SaveAsync(_SetSavepoint, cancellationToken).ConfigureAwait(false);
        var budget = TransactionLockBudget.Start(timeout);

        try
        {
            foreach (var key in keys)
            {
                if (
                    !budget.TryGetRemaining(out var wait)
                    || !await AcquireInSavepointAsync(key, transaction, wait, cancellationToken).ConfigureAwait(false)
                )
                {
                    await _RollBackSetAsync(transaction).ConfigureAwait(false);

                    return false;
                }
            }

            await transaction.ReleaseAsync(_SetSavepoint, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (connection.State == ConnectionState.Open)
        {
            try
            {
                await _RollBackSetAsync(transaction).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw _SetCleanupFailed(exception, cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>Synchronous form of <see cref="AcquireAllInSavepointAsync"/>.</summary>
    /// <param name="keys">One or more advisory-lock keys in acquisition order.</param>
    /// <param name="transaction">The caller's transaction, which owns the locks once acquired.</param>
    /// <param name="timeout">The budget for the whole set.</param>
    /// <returns><see langword="false"/> when a key was still held by another session as the budget ran out.</returns>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and rolling back to its savepoint failed too, so the transaction may still hold some keys.
    /// </exception>
    internal static bool AcquireAllInSavepoint(
        IReadOnlyList<PostgreSqlAdvisoryLockKey> keys,
        NpgsqlTransaction transaction,
        TimeSpan timeout
    )
    {
        if (keys.Count == 1)
        {
            return _AcquireInSavepoint(keys[0], transaction, timeout);
        }

        var connection = _RequireConnection(transaction);
        transaction.Save(_SetSavepoint);
        var budget = TransactionLockBudget.Start(timeout);

        try
        {
            foreach (var key in keys)
            {
                if (!budget.TryGetRemaining(out var wait) || !_AcquireInSavepoint(key, transaction, wait))
                {
                    _RollBackSet(transaction);

                    return false;
                }
            }

            transaction.Release(_SetSavepoint);

            return true;
        }
        catch (Exception exception) when (connection.State == ConnectionState.Open)
        {
            try
            {
                _RollBackSet(transaction);
            }
            catch (Exception cleanupFailure)
            {
                throw _SetCleanupFailed(exception, cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>
    /// Takes the transaction-scoped advisory lock for <paramref name="key"/> inside a savepoint of
    /// <paramref name="transaction"/>. A failed or cancelled acquire rolls back to the savepoint, which lifts the
    /// statement's abort from the caller's transaction and releases a lock the server granted before the client gave
    /// up (a transaction-level advisory lock taken in a rolled-back subtransaction goes with it).
    /// </summary>
    /// <param name="key">The advisory-lock key to acquire.</param>
    /// <param name="transaction">The caller's transaction, which owns the lock once acquired.</param>
    /// <param name="timeout">
    /// <see cref="TimeSpan.Zero"/> makes one non-blocking attempt; <see cref="Timeout.InfiniteTimeSpan"/> waits under
    /// the transaction's own <c>lock_timeout</c>; any other value bounds the wait with a <c>lock_timeout</c> that the
    /// transaction's previous value replaces once the lock is held.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquire command.</param>
    /// <returns>
    /// <see langword="false"/> when the attempt or the bounded wait found the key held by another session.
    /// </returns>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and rolling back to its savepoint failed too, so the transaction may still hold the lock.
    /// </exception>
    internal static async ValueTask<bool> AcquireInSavepointAsync(
        PostgreSqlAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var connection = _RequireConnection(transaction);
        await using var command = _CreateAcquireCommand(connection, transaction, key, timeout);

        try
        {
            if (timeout == TimeSpan.Zero)
            {
                return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (connection.State == ConnectionState.Open)
        {
            try
            {
                await using var rollBack = _CreateRollBackCommand(connection, transaction);
                await rollBack.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                _ThrowUnlessSavepointWasNeverSet(exception, cleanupFailure);
            }

            if (_IsBoundedWaitExpiry(exception, timeout))
            {
                return false;
            }

            throw;
        }
    }

    private static bool _AcquireInSavepoint(
        PostgreSqlAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        TimeSpan timeout
    )
    {
        var connection = _RequireConnection(transaction);
        using var command = _CreateAcquireCommand(connection, transaction, key, timeout);

        try
        {
            if (timeout == TimeSpan.Zero)
            {
                return (bool)(command.ExecuteScalar() ?? false);
            }

            command.ExecuteNonQuery();

            return true;
        }
        catch (Exception exception) when (connection.State == ConnectionState.Open)
        {
            try
            {
                using var rollBack = _CreateRollBackCommand(connection, transaction);
                rollBack.ExecuteNonQuery();
            }
            catch (Exception cleanupFailure)
            {
                _ThrowUnlessSavepointWasNeverSet(exception, cleanupFailure);
            }

            if (_IsBoundedWaitExpiry(exception, timeout))
            {
                return false;
            }

            throw;
        }
    }

    private static async ValueTask _RollBackSetAsync(NpgsqlTransaction transaction)
    {
        // Never cancelled: the rollback is what releases the set's locks, so it must run after a cancelled acquire too.
        await transaction.RollbackAsync(_SetSavepoint, CancellationToken.None).ConfigureAwait(false);
        await transaction.ReleaseAsync(_SetSavepoint, CancellationToken.None).ConfigureAwait(false);
    }

    private static void _RollBackSet(NpgsqlTransaction transaction)
    {
        transaction.Rollback(_SetSavepoint);
        transaction.Release(_SetSavepoint);
    }

    private static LockCleanupFailedException _SetCleanupFailed(Exception acquireFailure, Exception cleanupFailure)
    {
        return new LockCleanupFailedException(
            [acquireFailure, cleanupFailure],
            "The advisory-lock set acquire failed and rolling back to its savepoint failed too; the transaction may "
                + "hold some of the set's locks until it ends."
        );
    }

    private static NpgsqlCommand _CreateAcquireCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgreSqlAdvisoryLockKey key,
        TimeSpan timeout
    )
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;

        var isTry = timeout == TimeSpan.Zero;
        var isBounded = !isTry && timeout != Timeout.InfiniteTimeSpan;

        // One round trip: a failing statement stops the rest of the batch, so RELEASE runs only after the lock is held.
        var text = new StringBuilder();
        text.Append("SAVEPOINT ").Append(_Savepoint).AppendLine(";");

        if (isBounded)
        {
            // SET LOCAL survives RELEASE SAVEPOINT, so the transaction's own lock_timeout is parked in a
            // transaction-local custom setting and put back once the lock is held. A failed acquire needs neither:
            // rolling back to the savepoint undoes both settings.
            text.Append("SELECT pg_catalog.set_config('")
                .Append(_PriorLockTimeoutSetting)
                .AppendLine("', pg_catalog.current_setting('lock_timeout'), true);");
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"SET LOCAL lock_timeout = {(long)Math.Ceiling(timeout.TotalMilliseconds)};"
            );
        }

        text.Append("SELECT pg_catalog.pg")
            .Append(isTry ? "_try" : string.Empty)
            .Append("_advisory_xact_lock(")
            .Append(key.AddKeyParameters(command))
            .AppendLine(");");

        if (isBounded)
        {
            text.Append("SELECT pg_catalog.set_config('lock_timeout', pg_catalog.current_setting('")
                .Append(_PriorLockTimeoutSetting)
                .AppendLine("'), true);");
        }

        text.Append("RELEASE SAVEPOINT ").Append(_Savepoint);
        command.CommandText = text.ToString();

        return command;
    }

    private static NpgsqlCommand _CreateRollBackCommand(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ROLLBACK TO SAVEPOINT {_Savepoint}; RELEASE SAVEPOINT {_Savepoint}";

        return command;
    }

    private static bool _IsBoundedWaitExpiry(Exception exception, TimeSpan timeout)
    {
        // Only a lock_timeout this acquire set is a "not acquired" answer; one the caller set stays the caller's error.
        return timeout != TimeSpan.Zero
            && timeout != Timeout.InfiniteTimeSpan
            && exception is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable };
    }

    private static void _ThrowUnlessSavepointWasNeverSet(Exception acquireFailure, Exception cleanupFailure)
    {
        // The SAVEPOINT itself failed (the caller's transaction was already aborted), so the acquire changed nothing
        // and its own failure is the one to report.
        if (cleanupFailure is PostgresException { SqlState: PostgresErrorCodes.InvalidSavepointSpecification })
        {
            return;
        }

        throw new LockCleanupFailedException(
            [acquireFailure, cleanupFailure],
            "The advisory-lock acquire failed and rolling back to its savepoint failed too; the transaction may hold "
                + "the lock until it ends."
        );
    }

    private static NpgsqlConnection _RequireConnection(NpgsqlTransaction transaction)
    {
        Argument.IsNotNull(transaction);

        return transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );
    }

    private static NpgsqlTransaction _RequireNpgsql(DbTransaction transaction)
    {
        Argument.IsNotNull(transaction);

        return transaction as NpgsqlTransaction
            ?? throw new ArgumentException(
                $"The transaction must be an {nameof(NpgsqlTransaction)}; got '{transaction.GetType().FullName}'. "
                    + "EF Core callers pass db.Database.CurrentTransaction.GetDbTransaction() from a Npgsql-backed context.",
                nameof(transaction)
            );
    }
}
#pragma warning restore CA2100
