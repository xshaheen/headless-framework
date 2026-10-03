// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// One unit-of-work handle bound to the transaction-lock feature, returned by <c>unit.TransactionLocks</c>. A lock
/// taken through it lives inside that unit's transaction and is released when the unit completes or rolls back.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.TransactionLocks</c> and kept as unit-local state;
/// it owns nothing to dispose. The handle's liveness is checked when a lock runs, so a binding retained past the
/// unit's completion throws on its next call.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkTransactionLocks
{
    private readonly IUnitOfWorkTransactionLocks _locks;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkTransactionLocks(IUnitOfWorkTransactionLocks locks, IUnitOfWork unitOfWork)
    {
        _locks = locks;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on <paramref name="resource" /> inside the bound unit's
    /// transaction, waiting up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resource">The logical resource name.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> uses the provider's default (30
    /// seconds); <see cref="Timeout.InfiniteTimeSpan" /> waits without bound; <see cref="TimeSpan.Zero" /> is one
    /// attempt.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The held lock.</returns>
    /// <exception cref="LockAcquisitionTimeoutException">The lock was still held by another session when the wait elapsed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public ValueTask<TransactionLockHandle> AcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _locks.AcquireAsync(_unitOfWork, resource, acquireTimeout, cancellationToken);
    }

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on <paramref name="resource" /> inside the bound
    /// unit's transaction, waiting up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resource">The logical resource name.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> and <see cref="TimeSpan.Zero" /> are
    /// one attempt; <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The held lock, or <see langword="null" /> when another session still held it as the wait elapsed.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public ValueTask<TransactionLockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _locks.TryAcquireAsync(_unitOfWork, resource, acquireTimeout, cancellationToken);
    }
}
