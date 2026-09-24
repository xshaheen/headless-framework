// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// The SQL Server <c>unit.TransactionLocks</c> feature: takes <c>sp_getapplock @LockOwner = 'Transaction'</c> on
/// the unit's own <see cref="SqlTransaction" />, encoding the resource exactly as the session-scoped provider does
/// so both mutually exclude on one logical name.
/// </summary>
internal sealed class SqlServerUnitOfWorkTransactionLocks(IOptions<SqlServerDistributedLockOptions> options)
    : IUnitOfWorkTransactionLocks
{
    private const string _ProviderName = "SQL Server";

    // Matches the static helpers' default so a caller moving between the two surfaces keeps one wait budget.
    private static readonly TimeSpan _DefaultAcquireTimeout = TimeSpan.FromSeconds(30);

    public async ValueTask<TransactionLockHandle> AcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var transaction = UnitOfWorkTransactions.RequireTransaction<SqlTransaction>(unitOfWork, _ProviderName);
        var value = options.Value;

        await SqlServerDistributedLock
            .AcquireWithTransactionAsync(
                resource,
                transaction,
                acquireTimeout ?? _DefaultAcquireTimeout,
                value.CommandTimeout,
                value.KeyPrefix,
                cancellationToken
            )
            .ConfigureAwait(false);

        return new TransactionLockHandle(resource);
    }

    public async ValueTask<TransactionLockHandle?> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var transaction = UnitOfWorkTransactions.RequireTransaction<SqlTransaction>(unitOfWork, _ProviderName);
        var value = options.Value;

        // TimeSpan.Zero is sp_getapplock's single non-blocking attempt, the try-acquire default on every provider.
        var acquired = await SqlServerDistributedLock
            .TryAcquireWithTransactionAsync(
                resource,
                transaction,
                acquireTimeout ?? TimeSpan.Zero,
                value.CommandTimeout,
                value.KeyPrefix,
                cancellationToken
            )
            .ConfigureAwait(false);

        return acquired ? new TransactionLockHandle(resource) : null;
    }
}
