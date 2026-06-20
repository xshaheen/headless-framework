// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

[PublicAPI]
public static class SqlServerDistributedLock
{
    /// <summary>Default acquire timeout, matching <c>ConnectionScopedDistributedLockProvider.DefaultAcquireTimeout</c>.</summary>
    private static readonly TimeSpan _DefaultAcquireTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default command timeout, matching <see cref="SqlServerDistributedLockOptions.CommandTimeout"/>.</summary>
    private static readonly TimeSpan _DefaultCommandTimeout = TimeSpan.FromSeconds(30);

    public static async ValueTask AcquireWithTransactionAsync(
        string resource,
        SqlTransaction transaction,
        TimeSpan? acquireTimeout = null,
        TimeSpan? commandTimeout = null,
        string keyPrefix = DistributedLockOptions.DefaultKeyPrefix,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveAcquireTimeout = acquireTimeout ?? _DefaultAcquireTimeout;

        var acquired = await TryAcquireWithTransactionAsync(
                resource,
                transaction,
                effectiveAcquireTimeout,
                commandTimeout,
                keyPrefix,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!acquired)
        {
            throw effectiveAcquireTimeout == TimeSpan.Zero
                ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                : new LockAcquisitionTimeoutException(resource);
        }
    }

    public static async ValueTask<bool> TryAcquireWithTransactionAsync(
        string resource,
        SqlTransaction transaction,
        TimeSpan? acquireTimeout = null,
        TimeSpan? commandTimeout = null,
        string keyPrefix = DistributedLockOptions.DefaultKeyPrefix,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNull(transaction);

        return await SqlServerApplicationLock
            .TryAcquireTransactionAsync(
                transaction,
                // Mirror the session provider's encoding (KeyPrefix + resource) so both APIs derive an identical
                // @Resource and therefore mutually exclude on the same logical resource name.
                SqlServerResourceName.Encode(keyPrefix + resource),
                isShared: false,
                acquireTimeout ?? _DefaultAcquireTimeout,
                commandTimeout: commandTimeout ?? _DefaultCommandTimeout,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
