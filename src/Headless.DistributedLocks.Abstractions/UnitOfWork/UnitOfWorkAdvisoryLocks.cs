// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// One unit-of-work handle bound to the advisory-lock feature, returned by <c>unit.AdvisoryLocks</c>. A lock taken
/// through it lives inside that unit's transaction and is released when the unit completes or rolls back.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.AdvisoryLocks</c> and kept as unit-local state; it
/// owns nothing to dispose. The handle's liveness is checked when a lock runs, so a binding retained past the
/// unit's completion throws on its next call.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkAdvisoryLocks
{
    private readonly IUnitOfWorkAdvisoryLocks _locks;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkAdvisoryLocks(IUnitOfWorkAdvisoryLocks locks, IUnitOfWork unitOfWork)
    {
        _locks = locks;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on <paramref name="resource" /> inside the bound unit's
    /// transaction, waiting until the engine grants it. PostgreSQL waits without bound; SQL Server bounds the wait
    /// by the provider's default acquire timeout and throws <see cref="LockAcquisitionTimeoutException" /> past it.
    /// </summary>
    /// <param name="resource">The logical resource name.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public ValueTask AcquireAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _locks.AcquireAsync(_unitOfWork, resource, cancellationToken);
    }

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on <paramref name="resource" /> inside the bound
    /// unit's transaction without waiting.
    /// </summary>
    /// <param name="resource">The logical resource name.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns><see langword="true" /> when acquired; <see langword="false" /> when another session holds it.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public ValueTask<bool> TryAcquireAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _locks.TryAcquireAsync(_unitOfWork, resource, cancellationToken);
    }
}
