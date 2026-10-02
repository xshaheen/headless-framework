// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// The PostgreSQL <c>unit.TransactionLocks</c> feature: takes <c>pg_advisory_xact_lock</c> /
/// <c>pg_try_advisory_xact_lock</c> on the unit's own Npgsql transaction, encoding the resource exactly as the
/// session-scoped provider does so both mutually exclude on one logical name.
/// </summary>
/// <remarks>
/// A bounded wait is a server-side <c>lock_timeout</c> that the unit's previous value replaces once the lock is held.
/// Every acquire runs inside a savepoint, so a timeout expiry (SqlState <c>55P03</c>), a cancellation, or any other
/// failed acquire rolls back to it and leaves the unit's transaction usable instead of aborted.
/// </remarks>
internal sealed class PostgreSqlUnitOfWorkTransactionLocks(IOptions<PostgreSqlDistributedLockOptions> options)
    : IUnitOfWorkTransactionLocks
{
    private const string _Operation = "transaction-scoped PostgreSQL lock";

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

        var transaction = UnitOfWorkTransactions.RequireTransaction<NpgsqlTransaction>(unitOfWork, _Operation);

        // Same encoding as PostgresConnectionScopedLockStorage, so a session lock and a transaction lock on one
        // logical name derive the same advisory key and contend.
        var key = PostgreSqlAdvisoryLockKey.FromString(options.Value.KeyPrefix + resource, allowHashing: true);

        return await PostgreSqlDistributedLock
            .AcquireInSavepointAsync(key, transaction, acquireTimeout, cancellationToken)
            .ConfigureAwait(false);
    }
}
