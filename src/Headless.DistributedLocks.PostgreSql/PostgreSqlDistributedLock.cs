// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// Every method has a <see cref="DbTransaction"/> overload for callers that hold the transaction through an
/// abstraction, such as EF Core's <c>db.Database.CurrentTransaction.GetDbTransaction()</c>, and a synchronous
/// variant for the places EF Core only exposes synchronously, such as a <c>SavingChanges</c> interceptor.
/// </para>
/// </remarks>
[PublicAPI]
public static class PostgreSqlDistributedLock
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
        PostgreSqlAdvisoryLockKey key,
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
        PostgreSqlAdvisoryLockKey key,
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
        var connection = _RequireConnection(transaction);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_advisory_xact_lock({key.AddKeyParameters(command)})";
        command.ExecuteNonQuery();
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
        var connection = _RequireConnection(transaction);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_xact_lock({key.AddKeyParameters(command)})";

        return (bool)(command.ExecuteScalar() ?? false);
    }

    /// <inheritdoc cref="TryAcquireWithTransaction(PostgreSqlAdvisoryLockKey, NpgsqlTransaction)"/>
    /// <exception cref="ArgumentException">Thrown when <paramref name="transaction"/> is not an <see cref="NpgsqlTransaction"/>.</exception>
    public static bool TryAcquireWithTransaction(PostgreSqlAdvisoryLockKey key, DbTransaction transaction) =>
        TryAcquireWithTransaction(key, _RequireNpgsql(transaction));

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
