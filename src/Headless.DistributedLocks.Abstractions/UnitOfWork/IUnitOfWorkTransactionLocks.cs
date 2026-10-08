// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// The enlisted lock surface behind <c>unit.TransactionLocks</c>: every call takes transaction-scoped locks on the
/// unit's own transaction, so the engine releases them at that unit's commit or rollback and nothing else.
/// </summary>
/// <remarks>
/// A singleton registered by a lock provider whose engine has transaction-scoped locks (PostgreSQL advisory
/// locks, SQL Server application locks with <c>@LockOwner = 'Transaction'</c>). It holds no unit: the caller's
/// handle arrives per call, and the call refuses, before any command runs, a unit that is no longer active, carries
/// no relational resource, or carries a transaction from another provider. The TTL leases of
/// <see cref="IDistributedLock" /> are a different contract and never enlist.
/// <para>
/// Callers use <see cref="UnitOfWorkTransactionLocks" />, which owns everything every provider must do alike: it
/// validates, deduplicates, and ordinal-sorts the set, applies the default wait, and turns a <see langword="false" />
/// into <see langword="null" /> or <see cref="LockAcquisitionTimeoutException" />. An implementation receives the
/// canonical set and acquires it in the order given, under one wait budget for the whole set.
/// </para>
/// <para>
/// The unit's transaction is the owner, so acquiring a resource the unit already holds succeeds at once on every
/// wait shape. The set is all-or-nothing: an acquire that times out, is cancelled, or fails leaves the unit's
/// transaction usable and holding nothing it did not hold before the call.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkTransactionLocks : IUnitOfWorkFeature
{
    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on every resource in
    /// <paramref name="canonicalResources" />, in order, inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction owns the locks.</param>
    /// <param name="canonicalResources">
    /// One or more distinct, non-blank logical resource names in ordinal order, each encoded exactly as the provider's
    /// session locks encode it.
    /// </param>
    /// <param name="acquireTimeout">
    /// The wait budget for the whole set. <see cref="TimeSpan.Zero" /> is one attempt per resource;
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>
    /// <see langword="true" /> when every resource is held; <see langword="false" /> when another session still held
    /// one of them as the budget ran out, in which case none of the set's new locks remain held.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, exposes no relational resource, or its transaction belongs to another provider.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> fired before the set was held.</exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may hold some of the set.
    /// </exception>
    ValueTask<bool> TryAcquireAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<string> canonicalResources,
        TimeSpan acquireTimeout,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Synchronous form of <see cref="TryAcquireAsync" /> for callers that cannot await, such as an EF Core
    /// <c>SavingChanges</c> interceptor. Blocks the calling thread for up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction owns the locks.</param>
    /// <param name="canonicalResources">
    /// One or more distinct, non-blank logical resource names in ordinal order.
    /// </param>
    /// <param name="acquireTimeout">The wait budget for the whole set, with the same sentinels as the async form.</param>
    /// <returns>
    /// <see langword="true" /> when every resource is held; <see langword="false" /> when another session still held
    /// one of them as the budget ran out.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The unit is no longer active, exposes no relational resource, or its transaction belongs to another provider.
    /// </exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may hold some of the set.
    /// </exception>
    bool TryAcquire(IUnitOfWork unitOfWork, IReadOnlyList<string> canonicalResources, TimeSpan acquireTimeout);
}
