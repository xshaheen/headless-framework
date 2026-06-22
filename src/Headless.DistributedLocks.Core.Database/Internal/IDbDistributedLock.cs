// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// There are several strategies for implementing SQL-based locks (multiplexed-optimistic, dedicated connection,
/// transaction-scoped). This interface abstracts between them so the connection-scoped storage that drives the
/// acquire/release loop stays independent of the chosen connection-management strategy.
/// </summary>
internal interface IDbDistributedLock
{
    /// <summary>
    /// Attempts to acquire the lock for <paramref name="strategy"/>, returning <see langword="null"/> on failure or a
    /// handle on success. The <paramref name="contextHandle"/> argument is used when acquiring a nested lock (such as
    /// upgrading an upgradeable read lock to a write lock); it lets the implementation reuse the connection that
    /// already holds the outer lock.
    /// </summary>
    /// <typeparam name="TLockCookie">The strategy's opaque acquire/release state.</typeparam>
    /// <param name="timeout">Maximum time to wait for the strategy's native lock acquisition.</param>
    /// <param name="strategy">The SQL synchronization strategy that emits acquire/release SQL.</param>
    /// <param name="contextHandle">
    /// When non-<see langword="null"/>, a nested acquire: the implementation reuses the connection from this
    /// existing handle. <see langword="null"/> opens or borrows a connection from the pool.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A live <see cref="IDistributedLease"/> on success, or <see langword="null"/> on failure.</returns>
    ValueTask<IDistributedLease?> TryAcquireAsync<TLockCookie>(
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        IDistributedLease? contextHandle,
        CancellationToken cancellationToken
    )
        where TLockCookie : class;
}
