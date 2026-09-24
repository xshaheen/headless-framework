// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// The SQL Server <c>unit.AdvisoryLocks</c> feature: takes <c>sp_getapplock @LockOwner = 'Transaction'</c> on the
/// unit's own <see cref="SqlTransaction" />, encoding the resource exactly as the session-scoped provider does so
/// both mutually exclude on one logical name.
/// </summary>
internal sealed class SqlServerUnitOfWorkAdvisoryLocks(IOptions<SqlServerDistributedLockOptions> options)
    : IUnitOfWorkAdvisoryLocks
{
    private const string _ProviderName = "SQL Server";

    public ValueTask AcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var transaction = UnitOfWorkTransactionResolver.Require<SqlTransaction>(unitOfWork, _ProviderName);
        var value = options.Value;

        return SqlServerDistributedLock.AcquireWithTransactionAsync(
            resource,
            transaction,
            acquireTimeout: null,
            value.CommandTimeout,
            value.KeyPrefix,
            cancellationToken
        );
    }

    public ValueTask<bool> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var transaction = UnitOfWorkTransactionResolver.Require<SqlTransaction>(unitOfWork, _ProviderName);
        var value = options.Value;

        // TimeSpan.Zero is sp_getapplock's single non-blocking attempt, matching pg_try_advisory_xact_lock.
        return SqlServerDistributedLock.TryAcquireWithTransactionAsync(
            resource,
            transaction,
            TimeSpan.Zero,
            value.CommandTimeout,
            value.KeyPrefix,
            cancellationToken
        );
    }
}
