// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// The PostgreSQL <c>unit.TransactionLocks</c> feature: takes <c>pg_advisory_xact_lock</c> /
/// <c>pg_try_advisory_xact_lock</c> on the unit's own Npgsql transaction, encoding each resource exactly as the
/// session-scoped provider does so both mutually exclude on one logical name.
/// </summary>
/// <remarks>
/// A bounded wait is a server-side <c>lock_timeout</c> that the unit's previous value replaces once the lock is held.
/// Every acquire runs inside a savepoint, and a set of two or more inside an outer one, so a timeout expiry (SqlState
/// <c>55P03</c>), a cancellation, or any other failed acquire rolls back to it and leaves the unit's transaction
/// usable instead of aborted, holding none of the set's new locks.
/// </remarks>
internal sealed class PostgreSqlUnitOfWorkTransactionLocks(IOptions<PostgreSqlDistributedLockOptions> options)
    : IUnitOfWorkTransactionLocks
{
    private const string _Operation = "transaction-scoped PostgreSQL lock";

    public ValueTask<bool> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<string> canonicalResources,
        TimeSpan acquireTimeout,
        CancellationToken cancellationToken
    )
    {
        var keys = _Keys(canonicalResources);
        var transaction = UnitOfWorkTransactions.RequireTransaction<NpgsqlTransaction>(unitOfWork, _Operation);

        return PostgreSqlDistributedLock.AcquireAllInSavepointAsync(
            keys,
            transaction,
            acquireTimeout,
            cancellationToken
        );
    }

    public bool TryAcquire(IUnitOfWork unitOfWork, IReadOnlyList<string> canonicalResources, TimeSpan acquireTimeout)
    {
        var keys = _Keys(canonicalResources);
        var transaction = UnitOfWorkTransactions.RequireTransaction<NpgsqlTransaction>(unitOfWork, _Operation);

        return PostgreSqlDistributedLock.AcquireAllInSavepoint(keys, transaction, acquireTimeout);
    }

    private PostgreSqlAdvisoryLockKey[] _Keys(IReadOnlyList<string> canonicalResources)
    {
        Argument.IsNotNullOrEmpty(canonicalResources);

        var prefix = options.Value.KeyPrefix;
        var keys = new PostgreSqlAdvisoryLockKey[canonicalResources.Count];

        for (var i = 0; i < keys.Length; i++)
        {
            // Same encoding as PostgresConnectionScopedLockStorage, so a session lock and a transaction lock on one
            // logical name derive the same advisory key and contend.
            keys[i] = PostgreSqlAdvisoryLockKey.FromString(
                prefix + Argument.IsNotNullOrWhiteSpace(canonicalResources[i], paramName: nameof(canonicalResources)),
                allowHashing: true
            );
        }

        return keys;
    }
}
