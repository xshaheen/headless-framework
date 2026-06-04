// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Backend seam for connection-scoped (session- or transaction-held) lock storage. A custom database
/// lock provider implements this to map its native primitive (for example <c>pg_advisory_lock</c> or
/// <c>sp_getapplock</c>) onto <see cref="ConnectionScopedDistributedLockProvider"/>. Locks live for the
/// lifetime of the underlying connection and have no TTL; loss of the connection releases the lock and is
/// surfaced through <see cref="ConnectionScopedLockHandle.ConnectionLostToken"/>.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent use across acquirers. The provider performs all retry,
/// timeout, and waiter accounting; storage only needs to attempt acquisition once per call and release.
/// </remarks>
[PublicAPI]
public interface IConnectionScopedLockStorage
{
    /// <summary>
    /// Attempts a single, non-blocking acquisition of <paramref name="resource"/>. Returns a live
    /// <see cref="ConnectionScopedLockHandle"/> on success, or <see langword="null"/> if the resource is
    /// currently held in a conflicting mode (the provider then retries per its timeout policy). Must not
    /// block waiting for the lock — blocking-with-timeout is owned by the provider.
    /// </summary>
    /// <param name="resource">The resource name to lock.</param>
    /// <param name="lockId">Provider-generated identifier stamped onto the handle for ownership tracking.</param>
    /// <param name="isShared"><see langword="true"/> for a shared (reader) lock; <see langword="false"/> for an exclusive lock.</param>
    ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
        string resource,
        string lockId,
        bool isShared,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases the lock represented by <paramref name="handle"/>. Must be idempotent: the provider may
    /// call this during cleanup paths, and the handle itself de-duplicates release.
    /// </summary>
    ValueTask ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the lock identified by <paramref name="resource"/> and <paramref name="lockId"/> without a
    /// live handle (the out-of-band release path). A no-op if no matching lock is held.
    /// </summary>
    ValueTask ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether <paramref name="resource"/> is currently locked. When <paramref name="isShared"/> is
    /// <see langword="null"/>, any mode counts; otherwise the query is scoped to the given mode.
    /// </summary>
    ValueTask<bool> IsLockedAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns how many holders currently own <paramref name="resource"/> (for example the reader count of a
    /// shared lock). When <paramref name="isShared"/> is <see langword="null"/>, all modes are counted.
    /// </summary>
    ValueTask<long> GetLocksCountAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the lock id held for <paramref name="resource"/> by this process/connection, or
    /// <see langword="null"/> if this process does not hold it.
    /// </summary>
    ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Lists all locks currently observable in the backing store.</summary>
    ValueTask<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the total count of locks currently observable in the backing store.</summary>
    ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default);
}
