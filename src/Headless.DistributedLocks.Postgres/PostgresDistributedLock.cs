// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Npgsql;

namespace Headless.DistributedLocks.Postgres;

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
/// </remarks>
[PublicAPI]
public static class PostgresDistributedLock
{
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
        PostgresAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_advisory_xact_lock({key.AddKeyParameters(command)})";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
    public static async ValueTask<bool> TryAcquireWithTransactionAsync(
        PostgresAdvisoryLockKey key,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_xact_lock({key.AddKeyParameters(command)})";

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }
}
#pragma warning restore CA2100
