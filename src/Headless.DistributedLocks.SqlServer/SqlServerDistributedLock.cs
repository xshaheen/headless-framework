// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// Static entry point for acquiring SQL Server transaction-scoped application locks directly against a
/// caller-managed <see cref="SqlTransaction"/>. This API targets callers that already hold an open
/// transaction and want the lock to be released automatically when the transaction commits or rolls back
/// (SQL Server <c>sp_getapplock @LockOwner = 'Transaction'</c> semantics).
/// </summary>
/// <remarks>
/// The resource encoding mirrors the session-provider's <c>KeyPrefix + resource</c> convention so both
/// the DI-managed <see cref="IDistributedLock"/> and these static methods mutually exclude on the same
/// logical resource name when using the same <see cref="SqlServerDistributedLockOptions.KeyPrefix"/>.
/// </remarks>
[PublicAPI]
public static class SqlServerDistributedLock
{
    /// <summary>Default acquire timeout, matching <c>ConnectionScopedDistributedLockProvider.DefaultAcquireTimeout</c>.</summary>
    private static readonly TimeSpan _DefaultAcquireTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default command timeout, matching <see cref="SqlServerDistributedLockOptions.CommandTimeout"/>.</summary>
    private static readonly TimeSpan _DefaultCommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Acquires an exclusive, transaction-scoped application lock on <paramref name="resource"/> via
    /// <c>sys.sp_getapplock @LockOwner = 'Transaction'</c>. Throws if the lock cannot be acquired within
    /// <paramref name="acquireTimeout"/>.
    /// </summary>
    /// <param name="resource">Logical resource name. Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="transaction">
    /// Open SQL Server transaction that will own the lock. The lock is automatically released when the
    /// transaction commits or rolls back. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="acquireTimeout">
    /// How long to wait for the lock. <see cref="TimeSpan.Zero"/> makes a single non-blocking attempt.
    /// <see langword="null"/> uses the 30-second default. Passed as <c>@LockTimeout</c> milliseconds to
    /// <c>sp_getapplock</c>.
    /// </param>
    /// <param name="commandTimeout">
    /// ADO.NET command timeout for the <c>sp_getapplock</c> call. <see langword="null"/> uses the
    /// 30-second default.
    /// </param>
    /// <param name="keyPrefix">
    /// Prefix prepended to <paramref name="resource"/> before encoding. Defaults to
    /// <see cref="DistributedLockOptions.DefaultKeyPrefix"/>. Use the same prefix as
    /// <see cref="SqlServerDistributedLockOptions.KeyPrefix"/> to interoperate with the DI provider.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquisition attempt.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="transaction"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace, or when SQL Server rejects the resource name or lock mode parameters (<c>sp_getapplock</c> returns -999).</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction has no associated open connection (already committed, rolled back, or
    /// disposed), or when SQL Server returns an unsupported lock mode result (code 104), or when an
    /// unexpected <c>sp_getapplock</c> return code is received.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">Thrown when the lock is not acquired within <paramref name="acquireTimeout"/>.</exception>
    /// <exception cref="DistributedLockDeadlockException">Thrown when SQL Server detects a deadlock (<c>sp_getapplock</c> returns -3).</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled or SQL Server cancels the acquisition (<c>sp_getapplock</c> returns -2).</exception>
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

    /// <summary>
    /// Attempts to acquire an exclusive, transaction-scoped application lock on <paramref name="resource"/>
    /// via <c>sys.sp_getapplock @LockOwner = 'Transaction'</c>. Returns <see langword="false"/> when the
    /// lock is held by another connection and the acquire timeout is reached without acquiring.
    /// </summary>
    /// <param name="resource">Logical resource name. Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="transaction">
    /// Open SQL Server transaction that will own the lock. The lock is automatically released when the
    /// transaction commits or rolls back. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="acquireTimeout">
    /// How long to wait for the lock. <see cref="TimeSpan.Zero"/> makes a single non-blocking attempt.
    /// <see langword="null"/> uses the 30-second default. Passed as <c>@LockTimeout</c> milliseconds to
    /// <c>sp_getapplock</c>.
    /// </param>
    /// <param name="commandTimeout">
    /// ADO.NET command timeout for the <c>sp_getapplock</c> call. <see langword="null"/> uses the
    /// 30-second default.
    /// </param>
    /// <param name="keyPrefix">
    /// Prefix prepended to <paramref name="resource"/> before encoding. Defaults to
    /// <see cref="DistributedLockOptions.DefaultKeyPrefix"/>. Use the same prefix as
    /// <see cref="SqlServerDistributedLockOptions.KeyPrefix"/> to interoperate with the DI provider.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquisition attempt.</param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if the lock is currently
    /// held in a conflicting mode and the acquire timeout elapsed without success.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="transaction"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace, or when SQL Server rejects the resource name or lock mode parameters (<c>sp_getapplock</c> returns -999).</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction has no associated open connection (already committed, rolled back, or
    /// disposed), or when SQL Server returns an unsupported lock mode result (code 104), or when an
    /// unexpected <c>sp_getapplock</c> return code is received.
    /// </exception>
    /// <exception cref="DistributedLockDeadlockException">Thrown when SQL Server detects a deadlock (<c>sp_getapplock</c> returns -3).</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled or SQL Server cancels the acquisition (<c>sp_getapplock</c> returns -2).</exception>
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
