// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Persistent backend contract for the mutex (exclusive) distributed-lock provider.
/// Each operation maps to a single atomic backend command; the provider layer owns retry
/// logic, key scoping, and lease-loss monitoring on top of this interface.
/// </summary>
/// <remarks>
/// All methods must be safe to call concurrently from multiple threads. Implementations must
/// guarantee that <see cref="InsertAsync"/> and <see cref="RemoveIfEqualAsync"/> are
/// compare-and-set atomic operations so the mutual-exclusion guarantee holds under concurrent
/// callers.
/// </remarks>
[PublicAPI]
public interface IDistributedLockStorage
{
    /// <summary>
    /// Atomically sets <paramref name="key"/> to <paramref name="leaseId"/> with the given
    /// <paramref name="ttl"/> if and only if the key does not already exist (SET NX semantics).
    /// </summary>
    /// <param name="key">The fully-scoped storage key for the resource.</param>
    /// <param name="leaseId">The unique identifier to store as the lock value.</param>
    /// <param name="ttl">
    /// The lease duration. <see langword="null"/> or <see cref="Timeout.InfiniteTimeSpan"/>
    /// stores the key with no expiration (use only for resources that are explicitly released).
    /// </param>
    /// <param name="cancellationToken">Token to cancel the storage call. Cancellation does not
    /// guarantee the write was not committed — callers must perform best-effort cleanup.</param>
    /// <returns>
    /// A <see cref="DistributedLockAcquireResult"/> where <see cref="DistributedLockAcquireResult.Acquired"/>
    /// is <see langword="true"/> when the key was created and ownership was granted; <see langword="false"/>
    /// when the key already existed and the lock is held by another caller.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<DistributedLockAcquireResult> InsertAsync(
        string key,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically replaces the value at <paramref name="key"/> from <paramref name="expectedId"/> to
    /// <paramref name="newId"/> if and only if the current value equals <paramref name="expectedId"/>
    /// (compare-and-swap semantics). Used by the lock renewal (extend-TTL) path.
    /// </summary>
    /// <param name="key">The fully-scoped storage key for the resource.</param>
    /// <param name="expectedId">The current lease identifier that must match for the swap to proceed.</param>
    /// <param name="newId">The new lease identifier to write (may equal <paramref name="expectedId"/> for a TTL-only refresh).</param>
    /// <param name="newTtl">
    /// The new TTL to apply. <see langword="null"/> leaves the existing TTL unchanged.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// <see langword="true"/> when the value matched and the swap succeeded; <see langword="false"/>
    /// when the value differed (lock lost or expired).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically removes <paramref name="key"/> if and only if its current value equals
    /// <paramref name="expectedId"/> (compare-and-delete semantics). Used by the lock release path.
    /// </summary>
    /// <param name="key">The fully-scoped storage key for the resource.</param>
    /// <param name="expectedId">The lease identifier that must match for the key to be deleted.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// <see langword="true"/> when the value matched and the key was deleted; <see langword="false"/>
    /// when the value differed (already released or expired).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId, CancellationToken cancellationToken = default);

    /// <summary>Gets the remaining TTL for the lock key, or <see langword="null"/> if the key does not exist or has no expiration.</summary>
    /// <param name="key">The fully-scoped storage key for the resource.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>The remaining TTL, or <see langword="null"/> if the key is absent or has no expiration.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Returns <see langword="true"/> when the lock key exists (resource is currently locked).</summary>
    /// <param name="key">The fully-scoped storage key for the resource.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the lock ID stored for the given key, or <see langword="null"/> if not found.</summary>
    /// <param name="key">The fully-scoped storage key for the resource.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>The current lease identifier, or <see langword="null"/> when the key does not exist.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets all lock keys and their IDs matching the given prefix.</summary>
    /// <param name="prefix">Key prefix to filter by; an empty string returns all keys in scope.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// A dictionary mapping each matching key (with prefix stripped if the implementation normalizes it)
    /// to its current lease identifier.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all lock keys, their IDs, and their remaining TTL matching the given prefix.</summary>
    /// <param name="prefix">Key prefix to filter by; an empty string returns all keys in scope.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>
    /// A dictionary mapping each matching key to a tuple of its current lease identifier and its
    /// remaining TTL (<see langword="null"/> TTL means no expiration is set).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<IReadOnlyDictionary<string, (string LeaseId, TimeSpan? Ttl)>> GetAllWithExpirationByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the count of locks matching the given prefix.</summary>
    /// <param name="prefix">Key prefix to filter by; an empty string counts all keys in scope.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);
}
