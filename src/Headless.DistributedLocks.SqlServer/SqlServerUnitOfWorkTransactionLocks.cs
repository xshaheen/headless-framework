// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// The SQL Server <c>unit.TransactionLocks</c> feature: takes <c>sp_getapplock @LockOwner = 'Transaction'</c> on
/// the unit's own <see cref="SqlTransaction" />, encoding each resource exactly as the session-scoped provider does
/// so both mutually exclude on one logical name.
/// </summary>
/// <remarks>
/// A set of two or more is all-or-nothing: when a resource is contended past the budget or its acquire fails, the
/// locks the call newly granted are released with <c>sp_releaseapplock</c> in reverse order. A savepoint would not
/// do here, because rolling back to one does not release the locks taken after it.
/// </remarks>
internal sealed class SqlServerUnitOfWorkTransactionLocks(IOptions<SqlServerDistributedLockOptions> options)
    : IUnitOfWorkTransactionLocks
{
    private const string _Operation = "transaction-scoped SQL Server lock";

    public ValueTask<bool> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<string> canonicalResources,
        TimeSpan acquireTimeout,
        CancellationToken cancellationToken
    )
    {
        var value = options.Value;
        var resources = _Encode(canonicalResources, value.KeyPrefix);
        var transaction = UnitOfWorkTransactions.RequireTransaction<SqlTransaction>(unitOfWork, _Operation);

        return SqlServerApplicationLock.TryAcquireTransactionSetAsync(
            transaction,
            resources,
            acquireTimeout,
            value.CommandTimeout,
            cancellationToken
        );
    }

    public bool TryAcquire(IUnitOfWork unitOfWork, IReadOnlyList<string> canonicalResources, TimeSpan acquireTimeout)
    {
        var value = options.Value;
        var resources = _Encode(canonicalResources, value.KeyPrefix);
        var transaction = UnitOfWorkTransactions.RequireTransaction<SqlTransaction>(unitOfWork, _Operation);

        return SqlServerApplicationLock.TryAcquireTransactionSet(
            transaction,
            resources,
            acquireTimeout,
            value.CommandTimeout
        );
    }

    private static string[] _Encode(IReadOnlyList<string> canonicalResources, string keyPrefix)
    {
        Argument.IsNotNullOrEmpty(canonicalResources);

        var resources = new string[canonicalResources.Count];

        for (var i = 0; i < resources.Length; i++)
        {
            // Mirror the session provider's encoding (KeyPrefix + resource) so both derive an identical @Resource
            // and mutually exclude on the same logical resource name.
            resources[i] = SqlServerResourceName.Encode(
                keyPrefix + Argument.IsNotNullOrWhiteSpace(canonicalResources[i], paramName: nameof(canonicalResources))
            );
        }

        return resources;
    }
}
