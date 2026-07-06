// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Persistent backend contract for the distributed semaphore provider.
/// Semaphore slots are scored by their finite expiry timestamp in the backend, which allows
/// the backend to reclaim capacity automatically when a slot's TTL expires without an explicit
/// release. Unlike the mutex storage (<see cref="IDistributedLockStorage"/>), a slot must
/// always carry a finite TTL — <see cref="Timeout.InfiniteTimeSpan"/> is not valid.
/// </summary>
/// <remarks>
/// All methods must be safe to call concurrently from multiple threads. Implementations must
/// guarantee that <see cref="TryAcquireAsync"/> and <see cref="ReleaseAsync"/> are atomic with
/// respect to the slot count so the semaphore capacity limit is never exceeded under contention.
/// <para>
/// <b>Evolution policy — this backend SPI is frozen as of v1.0.</b> Custom semaphore providers implement it,
/// so adding a member is a breaking change for every implementer. New capability arrives as a C# default
/// interface member whenever a safe default exists (the framework precedent is
/// <c>IConnectionScopedLockStorage.BlocksServerSide</c>), or as a separate opt-in seam where no meaningful
/// default is possible, rather than as a required member here.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDistributedSemaphoreStorage
{
    /// <summary>
    /// Atomically acquires one semaphore slot for <paramref name="resource"/> when the number of active
    /// (non-expired) slots is below <paramref name="maxCount"/>.
    /// </summary>
    /// <param name="resource">The fully-scoped storage key for the semaphore resource.</param>
    /// <param name="leaseId">The unique identifier for this slot acquisition.</param>
    /// <param name="maxCount">
    /// The maximum number of concurrent slot holders. Must be &gt;= 1. The backend must prune
    /// expired slots before evaluating the count so TTL-expired holders do not consume capacity.
    /// </param>
    /// <param name="ttl">
    /// The finite slot duration. Must be a positive non-infinite value; a slot without expiry
    /// would hold capacity until explicitly released and break the capacity reclaim contract.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the storage call. Cancellation does not
    /// guarantee the slot was not stored — callers must perform best-effort cleanup.</param>
    /// <returns>
    /// A <see cref="DistributedLockAcquireResult"/> where <see cref="DistributedLockAcquireResult.Acquired"/>
    /// is <see langword="true"/> when a slot was granted; <see langword="false"/> when the semaphore
    /// is at capacity.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string leaseId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Extends the TTL of an existing slot identified by <paramref name="leaseId"/> within
    /// <paramref name="resource"/>.
    /// </summary>
    /// <param name="resource">The fully-scoped storage key for the semaphore resource.</param>
    /// <param name="leaseId">The lease identifier of the slot to extend.</param>
    /// <param name="ttl">The new TTL to apply from now.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// <see langword="true"/> when the slot still existed and its TTL was extended;
    /// <see langword="false"/> when the slot was not found (already released or expired).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<bool> TryExtendAsync(
        string resource,
        string leaseId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Confirms that the slot identified by <paramref name="leaseId"/> still exists and is
    /// non-expired within <paramref name="resource"/>. Used by the lease monitor's polling path.
    /// </summary>
    /// <param name="resource">The fully-scoped storage key for the semaphore resource.</param>
    /// <param name="leaseId">The lease identifier to check ownership for.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// <see langword="true"/> while ownership is confirmed; <see langword="false"/> when the slot
    /// is absent or expired (ownership lost).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<bool> ValidateAsync(string resource, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the slot identified by <paramref name="leaseId"/> from <paramref name="resource"/>.
    /// Safe to call on an already-released or expired slot (idempotent).
    /// </summary>
    /// <param name="resource">The fully-scoped storage key for the semaphore resource.</param>
    /// <param name="leaseId">The lease identifier of the slot to release.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// <see langword="true"/> when the slot was found and removed; <see langword="false"/> when it
    /// was already absent or expired.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<bool> ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of active (non-expired) slot holders for <paramref name="resource"/>.
    /// The backend must prune expired slots before counting so the result reflects live holders only.
    /// </summary>
    /// <param name="resource">The fully-scoped storage key for the semaphore resource.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default);
}
