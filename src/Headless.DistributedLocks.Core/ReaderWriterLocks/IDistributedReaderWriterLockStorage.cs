// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Storage contract for atomic distributed reader-writer lock operations.</summary>
public interface IDistributedReaderWriterLockStorage
{
    /// <summary>
    /// Derives the writer-waiting marker id from a writer's <paramref name="lockId"/>. The provider
    /// passes the derived marker into <see cref="TryAcquireWriteAsync"/> so the storage can plant
    /// a placeholder while readers drain (writer-preference; see D8). Implementations MUST be
    /// deterministic for a given <paramref name="lockId"/> so the same marker is produced for the
    /// later cleanup-or-release round trip.
    /// </summary>
    [Pure]
    string GetWaitingId(string lockId);

    /// <summary>
    /// Atomically acquires a shared (read) lease on <paramref name="resource"/> for the caller's
    /// <paramref name="lockId"/> when no writer holds the resource and no writer-waiting marker is
    /// present (writer preference; see D8). Implementations MUST guarantee atomicity of the
    /// inspect-then-acquire update so that concurrent writers observing readers cannot acquire
    /// exclusivity, and so concurrent readers cannot bypass a queued writer.
    /// Returns <see langword="true"/> when the lease is granted, <see langword="false"/> on contention.
    /// </summary>
    ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically refreshes the TTL on the caller's read lease for <paramref name="resource"/>.
    /// Returns <see langword="true"/> when the caller's <paramref name="lockId"/> is still recorded
    /// as a reader and the TTL was extended; <see langword="false"/> when the lease has been lost
    /// (expired, evicted, or never granted). Implementations MUST never shorten an existing TTL.
    /// </summary>
    ValueTask<bool> TryExtendReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes the caller's <paramref name="lockId"/> from the reader set for
    /// <paramref name="resource"/>. Idempotent — calling on a lease that has already been
    /// released, expired, or never existed MUST succeed without throwing.
    /// </summary>
    ValueTask ReleaseReadAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically acquires an exclusive (write) lease on <paramref name="resource"/> when no
    /// readers and no other writer hold the resource. When readers are present, implementations
    /// MUST plant or refresh the writer-waiting marker derived from <paramref name="waitingId"/>
    /// (see <see cref="GetWaitingId"/> — required for writer-preference per D8) so subsequent
    /// readers are blocked until the queued writer promotes. The marker is planted with
    /// <paramref name="markerTtl"/> rather than the lease TTL so an abandoned/cancelled writer
    /// does not keep readers blocked for the full lease window. Returns <see langword="true"/>
    /// when the exclusive lease is granted, <see langword="false"/> when the writer is queued or
    /// another writer already holds the resource.
    /// </summary>
    ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string lockId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically refreshes the TTL on the caller's exclusive write lease for
    /// <paramref name="resource"/>. Returns <see langword="true"/> only when the stored writer id
    /// matches <paramref name="lockId"/>; <see langword="false"/> when the lease has been lost.
    /// Implementations MUST never shorten an existing TTL.
    /// </summary>
    ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases the caller's exclusive write lease for <paramref name="resource"/>. The
    /// implementation MUST clear both the held writer id AND the writer-waiting marker derived
    /// from the same <paramref name="lockId"/> (per D8) so a cancelled queued writer doesn't
    /// strand the resource until TTL expiry. Idempotent — must not throw when the stored id
    /// doesn't match or the key no longer exists.
    /// </summary>
    ValueTask ReleaseWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="lockId"/> is currently present in the
    /// reader set for <paramref name="resource"/>. Intended for self-validation by an existing
    /// lease holder (monitoring loop) — callers MUST treat the result as advisory because the
    /// TTL can expire between this read and any subsequent action.
    /// </summary>
    ValueTask<bool> ValidateReadAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when the stored writer id for <paramref name="resource"/>
    /// matches <paramref name="lockId"/> exactly (the writer-waiting marker is excluded — only a
    /// promoted writer satisfies this check). Intended for monitoring self-validation; advisory
    /// because the TTL can expire between this read and any subsequent action.
    /// </summary>
    ValueTask<bool> ValidateWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when at least one reader currently holds
    /// <paramref name="resource"/>. Point-in-time inspection only — callers MUST NOT rely on this
    /// for correctness decisions; use <see cref="TryAcquireWriteAsync"/> or a held lease's
    /// validate methods instead.
    /// </summary>
    ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> only when a real writer (not the writer-waiting marker)
    /// currently holds <paramref name="resource"/>. Point-in-time inspection only — callers MUST
    /// NOT rely on this for correctness decisions.
    /// </summary>
    ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current reader count for <paramref name="resource"/>. Point-in-time inspection
    /// only — the count can change concurrently and entries can expire, so callers MUST NOT rely
    /// on this for correctness decisions.
    /// </summary>
    ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default);
}
