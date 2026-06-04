// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

[PublicAPI]
public static class SqlServerDistributedLock
{
    public static async ValueTask AcquireWithTransactionAsync(
        string resource,
        SqlTransaction transaction,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await TryAcquireWithTransactionAsync(
                resource,
                transaction,
                acquireTimeout ?? Timeout.InfiniteTimeSpan,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!acquired)
        {
            throw acquireTimeout == TimeSpan.Zero
                ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                : new LockAcquisitionTimeoutException(resource);
        }
    }

    public static async ValueTask<bool> TryAcquireWithTransactionAsync(
        string resource,
        SqlTransaction transaction,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNull(transaction);

        return await SqlServerApplicationLock
            .TryAcquireTransactionAsync(
                transaction,
                SqlServerResourceName.Encode(resource),
                isShared: false,
                acquireTimeout ?? TimeSpan.Zero,
                commandTimeout: TimeSpan.FromSeconds(30),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
