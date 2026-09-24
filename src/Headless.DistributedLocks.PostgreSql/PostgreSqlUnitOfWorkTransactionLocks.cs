// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

#pragma warning disable CA2100 // Lock SQL is fixed text; the key binds as parameters and the timeout is a formatted integer.
/// <summary>
/// The PostgreSQL <c>unit.TransactionLocks</c> feature: takes <c>pg_advisory_xact_lock</c> /
/// <c>pg_try_advisory_xact_lock</c> on the unit's own Npgsql transaction, encoding the resource exactly as the
/// session-scoped provider does so both mutually exclude on one logical name.
/// </summary>
/// <remarks>
/// A bounded wait is a server-side <c>lock_timeout</c>. The lock runs inside the caller's transaction, where a plain
/// <c>SET LOCAL</c> would stay in force for the rest of the unit, so the timed acquire runs inside a savepoint:
/// the setting is released with the savepoint on both outcomes, and a timeout expiry (SqlState <c>55P03</c>) rolls
/// the savepoint back so the transaction stays usable instead of aborting.
/// </remarks>
internal sealed class PostgreSqlUnitOfWorkTransactionLocks(IOptions<PostgreSqlDistributedLockOptions> options)
    : IUnitOfWorkTransactionLocks
{
    private const string _ProviderName = "PostgreSQL";
    private const string _LockNotAvailable = "55P03";
    private const string _Savepoint = "headless_txn_lock";

    // Matches the SQL Server feature and the static helpers so one wait budget applies across providers.
    private static readonly TimeSpan _DefaultAcquireTimeout = TimeSpan.FromSeconds(30);

    public async ValueTask<TransactionLockHandle> AcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await _AcquireAsync(
                unitOfWork,
                resource,
                acquireTimeout ?? _DefaultAcquireTimeout,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (acquired)
        {
            return new TransactionLockHandle(resource);
        }

        throw acquireTimeout == TimeSpan.Zero
            ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
            : new LockAcquisitionTimeoutException(resource);
    }

    public async ValueTask<TransactionLockHandle?> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await _AcquireAsync(unitOfWork, resource, acquireTimeout ?? TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);

        return acquired ? new TransactionLockHandle(resource) : null;
    }

    private async ValueTask<bool> _AcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan acquireTimeout,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var transaction = UnitOfWorkTransactions.RequireTransaction<NpgsqlTransaction>(unitOfWork, _ProviderName);

        // Same encoding as PostgresConnectionScopedLockStorage, so a session lock and a transaction lock on one
        // logical name derive the same advisory key and contend.
        var key = PostgreSqlAdvisoryLockKey.FromString(options.Value.KeyPrefix + resource, allowHashing: true);

        if (acquireTimeout == TimeSpan.Zero)
        {
            return await PostgreSqlDistributedLock
                .TryAcquireWithTransactionAsync(key, transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            await PostgreSqlDistributedLock
                .AcquireWithTransactionAsync(key, transaction, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }

        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );

        var lockTimeoutMs = (long)Math.Ceiling(acquireTimeout.TotalMilliseconds);

        await transaction.SaveAsync(_Savepoint, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = string.Create(
                CultureInfo.InvariantCulture,
                $"SET LOCAL lock_timeout = {lockTimeoutMs}; SELECT pg_catalog.pg_advisory_xact_lock({key.AddKeyParameters(command)})"
            );
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException e) when (string.Equals(e.SqlState, _LockNotAvailable, StringComparison.Ordinal))
        {
            // The expiry aborted the statement; the savepoint rollback lifts the abort so the unit stays usable.
            await transaction.RollbackAsync(_Savepoint, CancellationToken.None).ConfigureAwait(false);

            return false;
        }

        // Releasing the savepoint keeps the lock (it belongs to the transaction) and drops the SET LOCAL scope.
        await transaction.ReleaseAsync(_Savepoint, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
#pragma warning restore CA2100
