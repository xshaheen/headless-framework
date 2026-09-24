// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// The enlisted lock surface behind <c>unit.TransactionLocks</c>: every call takes a transaction-scoped lock on the
/// unit's own transaction, so the engine releases it at that unit's commit or rollback and nothing else.
/// </summary>
/// <remarks>
/// A singleton registered by a lock provider whose engine has transaction-scoped locks (PostgreSQL advisory
/// locks, SQL Server application locks with <c>@LockOwner = 'Transaction'</c>). It holds no unit: the caller's
/// handle arrives per call, and the call refuses, before any command runs, a unit that is no longer active, carries
/// no relational resource, or carries a transaction from another provider. The TTL leases of
/// <see cref="IDistributedLock" /> are a different contract and never enlist.
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkTransactionLocks : IUnitOfWorkFeature
{
    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on <paramref name="resource" /> inside
    /// <paramref name="unitOfWork" />'s transaction, waiting up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction owns the lock.</param>
    /// <param name="resource">The logical resource name, encoded exactly as the provider's session locks encode it.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> uses the provider's default;
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits without bound; <see cref="TimeSpan.Zero" /> is one attempt.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The held lock.</returns>
    /// <exception cref="LockAcquisitionTimeoutException">The lock was still held by another session when the wait elapsed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, exposes no relational resource, or its transaction belongs to another provider.
    /// </exception>
    ValueTask<TransactionLockHandle> AcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on <paramref name="resource" /> inside
    /// <paramref name="unitOfWork" />'s transaction, waiting up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction owns the lock.</param>
    /// <param name="resource">The logical resource name, encoded exactly as the provider's session locks encode it.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> and <see cref="TimeSpan.Zero" /> are
    /// one attempt; <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The held lock, or <see langword="null" /> when another session still held it as the wait elapsed.</returns>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, exposes no relational resource, or its transaction belongs to another provider.
    /// </exception>
    ValueTask<TransactionLockHandle?> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    );
}
