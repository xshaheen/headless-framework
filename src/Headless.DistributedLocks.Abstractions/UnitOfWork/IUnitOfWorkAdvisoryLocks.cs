// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// The enlisted advisory-lock surface behind <c>unit.AdvisoryLocks</c>: every call takes a transaction-scoped lock on
/// the unit's own transaction, so the lock is released by that unit's commit or rollback and nothing else.
/// </summary>
/// <remarks>
/// A singleton registered by a lock provider whose engine has transaction-scoped locks (PostgreSQL advisory
/// locks, SQL Server application locks with <c>@LockOwner = 'Transaction'</c>). It holds no unit: the caller's
/// handle arrives per call, and the call refuses, before any command runs, a unit that is no longer active, carries
/// no relational resource, or carries a transaction from another provider. The TTL leases of
/// <see cref="IDistributedLock" /> are a different contract and never enlist.
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkAdvisoryLocks : IUnitOfWorkFeature
{
    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on <paramref name="resource" /> inside
    /// <paramref name="unitOfWork" />'s transaction, waiting until the engine grants it.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction owns the lock.</param>
    /// <param name="resource">The logical resource name, encoded exactly as the provider's session locks encode it.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, exposes no relational resource, or its transaction belongs to another provider.
    /// </exception>
    ValueTask AcquireAsync(IUnitOfWork unitOfWork, string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on <paramref name="resource" /> inside
    /// <paramref name="unitOfWork" />'s transaction without waiting.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction owns the lock.</param>
    /// <param name="resource">The logical resource name, encoded exactly as the provider's session locks encode it.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns><see langword="true" /> when acquired; <see langword="false" /> when another session holds it.</returns>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, exposes no relational resource, or its transaction belongs to another provider.
    /// </exception>
    ValueTask<bool> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        string resource,
        CancellationToken cancellationToken = default
    );
}
