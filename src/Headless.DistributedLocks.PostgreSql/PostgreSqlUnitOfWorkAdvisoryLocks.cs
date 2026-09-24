// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// The PostgreSQL <c>unit.AdvisoryLocks</c> feature: takes <c>pg_advisory_xact_lock</c> /
/// <c>pg_try_advisory_xact_lock</c> on the unit's own Npgsql transaction, encoding the resource exactly as the
/// session-scoped provider does so both mutually exclude on one logical name.
/// </summary>
internal sealed class PostgreSqlUnitOfWorkAdvisoryLocks(IOptions<PostgreSqlDistributedLockOptions> options)
    : IUnitOfWorkAdvisoryLocks
{
    private const string _ProviderName = "PostgreSQL";

    public ValueTask AcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        var (key, transaction) = _Bind(unitOfWork, resource);

        return PostgreSqlDistributedLock.AcquireWithTransactionAsync(key, transaction, cancellationToken);
    }

    public ValueTask<bool> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        var (key, transaction) = _Bind(unitOfWork, resource);

        return PostgreSqlDistributedLock.TryAcquireWithTransactionAsync(key, transaction, cancellationToken);
    }

    private (PostgreSqlAdvisoryLockKey Key, NpgsqlTransaction Transaction) _Bind(
        IUnitOfWork unitOfWork,
        string resource
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var transaction = UnitOfWorkTransactionResolver.Require<NpgsqlTransaction>(unitOfWork, _ProviderName);

        // Same encoding as PostgresConnectionScopedLockStorage, so a session lock and a transaction lock on one
        // logical name derive the same advisory key and contend.
        var key = PostgreSqlAdvisoryLockKey.FromString(options.Value.KeyPrefix + resource, allowHashing: true);

        return (key, transaction);
    }
}
