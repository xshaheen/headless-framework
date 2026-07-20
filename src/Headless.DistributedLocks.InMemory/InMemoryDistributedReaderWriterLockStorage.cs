// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.DistributedLocks.InMemory;

/// <summary>
/// In-process reader-writer lock storage backed by per-resource state dictionaries and
/// <see cref="TimeProvider"/>-based TTL expiry.
/// </summary>
/// <remarks>
/// <para>
/// Suitable for unit tests, local development, and single-instance applications only. This implementation
/// does <b>not</b> coordinate across application instances or processes — it must not be used in
/// horizontally-scaled deployments where multiple processes share the same named resources.
/// </para>
/// <para>
/// Thread-safety is achieved with a per-resource <see langword="lock"/> that serialises all read/write
/// state mutations. The implementation enforces writer-preference (design decision D8): when a writer is
/// waiting, new readers are blocked until the writer acquires and releases the resource.
/// </para>
/// <para>
/// Lease IDs must not contain the <c>':'</c> character because it is used as the delimiter for the
/// writer-waiting marker suffix.
/// </para>
/// </remarks>
/// <param name="timeProvider">The time source used to compute and evaluate TTL expiry timestamps.</param>
internal sealed class InMemoryDistributedReadWriteLockStorage(TimeProvider timeProvider)
    : IDistributedReadWriteLockStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    /// <summary>
    /// Atomically acquires a shared (read) lease on <paramref name="resource"/> for the caller
    /// identified by <paramref name="leaseId"/> when no writer holds the resource and no
    /// writer-waiting marker is present (writer-preference; D8).
    /// </summary>
    /// <param name="resource">The resource name to read-lock. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">
    /// A caller-supplied identifier for this read lease. Must not be <see langword="null"/>,
    /// empty, or contain <c>':'</c>.
    /// </param>
    /// <param name="ttl">
    /// Optional time-to-live for the read lease. Read leases always carry a finite TTL in practice;
    /// the higher-level provider clamps <see langword="null"/> to a default before reaching this
    /// method.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before acquiring.</param>
    /// <returns>
    /// <see langword="true"/> when the read lease is granted; <see langword="false"/> when a writer
    /// holds the resource or a writer-waiting marker is present.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            _PruneExpired(state);

            if (state.Writer is not null || state.WriterWaitingMarker is not null)
            {
                return ValueTask.FromResult(false);
            }

            state.Readers[leaseId] = new LeaseEntry(_GetExpiration(ttl));

            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Atomically refreshes the TTL on the caller's read lease for <paramref name="resource"/>,
    /// provided no writer-waiting marker is present. The TTL is never shortened (only extended).
    /// Read leases may not be promoted to non-expiring: a <see langword="null"/> <paramref name="ttl"/>
    /// keeps the existing finite expiry rather than making the lease infinite.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The caller's current read-lease ID. Must not be <see langword="null"/>, empty, or contain <c>':'</c>.</param>
    /// <param name="ttl">
    /// The desired new TTL. When <see langword="null"/> the existing expiry is preserved rather than
    /// promoting the lease to infinite.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was found and the TTL was refreshed;
    /// <see langword="false"/> when the key does not exist, the lease has expired or been released,
    /// or a writer-waiting marker is present.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> TryExtendReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (!state.Readers.TryGetValue(leaseId, out var existing))
            {
                return ValueTask.FromResult(false);
            }

            if (state.WriterWaitingMarker is not null)
            {
                return ValueTask.FromResult(false);
            }

            // Readers must always carry a finite TTL: a null ttl keeps the existing finite expiry rather than
            // promoting the lease to infinite, which would let a zombie reader block writers forever.
            state.Readers[leaseId] = new LeaseEntry(
                Expiration: _ExtendExpiration(existing.Expiration, _GetExpiration(ttl), allowInfinite: false)
            );

            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Removes the caller's read lease for <paramref name="resource"/>. Idempotent — calling on a
    /// lease that has already been released, expired, or never existed succeeds without throwing.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The caller's read-lease ID. Must not be <see langword="null"/>, empty, or contain <c>':'</c>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask ReleaseReadAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                _PruneExpired(state);
                state.Readers.Remove(leaseId);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Atomically acquires an exclusive write lease on <paramref name="resource"/> when no readers
    /// and no other writer hold the resource. When readers are present, plants or refreshes the
    /// writer-waiting marker derived from <paramref name="waitingId"/> (writer-preference; D8), using
    /// <paramref name="markerTtl"/> so that an abandoned writer does not block readers for the full
    /// lease window.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">
    /// A caller-supplied identifier for this write lease. Must not be <see langword="null"/>,
    /// empty, or contain <c>':'</c>.
    /// </param>
    /// <param name="waitingId">
    /// The writer-waiting marker ID to plant when readers are present. Must not be
    /// <see langword="null"/> or empty.
    /// </param>
    /// <param name="ttl">Optional TTL for the write lease itself. <see langword="null"/> means no expiry.</param>
    /// <param name="markerTtl">Optional TTL for the writer-waiting marker. <see langword="null"/> means no expiry.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before acquiring.</param>
    /// <returns>
    /// <see langword="true"/> when the exclusive write lease is granted; <see langword="false"/> when
    /// another writer already holds the resource or when readers are present (in which case the
    /// writer-waiting marker is planted and the caller should retry).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/>, <paramref name="leaseId"/>, or <paramref name="waitingId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/>, <paramref name="leaseId"/>, or <paramref name="waitingId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string leaseId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        Argument.IsNotNullOrEmpty(waitingId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            _PruneExpired(state);

            if (state.Writer is not null)
            {
                return ValueTask.FromResult(false);
            }

            if (state.Readers.Count is 0)
            {
                state.Writer = new WriterEntry(leaseId, _GetExpiration(ttl));
                // A successful claim drops any pending waiting marker (mirrors Redis's single-key overwrite).
                state.WriterWaitingMarker = null;

                return ValueTask.FromResult(true);
            }

            // Only plant when no waiter holds the slot so concurrent writers don't clobber the first waiter.
            state.WriterWaitingMarker ??= new WriterEntry(waitingId, _GetExpiration(markerTtl));

            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    /// Atomically refreshes the TTL on the caller's exclusive write lease for <paramref name="resource"/>.
    /// The TTL is never shortened (only extended). Writers may hold non-expiring leases: a
    /// <see langword="null"/> <paramref name="ttl"/> promotes the lease to infinite.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The caller's current write-lease ID. Must not be <see langword="null"/>, empty, or contain <c>':'</c>.</param>
    /// <param name="ttl">
    /// The desired new TTL. When <see langword="null"/> the lease becomes non-expiring (allowed for
    /// writers unlike readers).
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the stored writer ID matches <paramref name="leaseId"/> and the
    /// TTL was refreshed; <see langword="false"/> when the key does not exist, the lease has expired
    /// or been released, or the stored writer ID does not match.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (state.Writer is not { } existing || !string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            // Writers may hold infinite leases, so a null ttl is allowed to extend to non-expiring.
            state.Writer = existing with
            {
                Expiration = _ExtendExpiration(existing.Expiration, _GetExpiration(ttl), allowInfinite: true),
            };

            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Releases the caller's exclusive write lease for <paramref name="resource"/> and clears both
    /// the held writer ID and the writer-waiting marker derived from the same
    /// <paramref name="leaseId"/> (D8) so a cancelled queued writer does not strand the resource
    /// until TTL expiry. Idempotent — does not throw when the stored ID does not match or the key
    /// no longer exists.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The caller's write-lease ID. Must not be <see langword="null"/>, empty, or contain <c>':'</c>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask ReleaseWriteAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                _PruneExpired(state);

                if (state.Writer is { } writer && string.Equals(writer.LeaseId, leaseId, StringComparison.Ordinal))
                {
                    state.Writer = null;
                }

                var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(leaseId);
                if (
                    state.WriterWaitingMarker is { } marker
                    && string.Equals(marker.LeaseId, waitingId, StringComparison.Ordinal)
                )
                {
                    state.WriterWaitingMarker = null;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="leaseId"/> is currently present in the
    /// reader set for <paramref name="resource"/>. Intended for monitoring self-validation by an
    /// existing lease holder — callers must treat the result as advisory because the TTL can expire
    /// between this read and any subsequent action.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The read-lease ID to check. Must not be <see langword="null"/>, empty, or contain <c>':'</c>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="leaseId"/> is in the reader set and has not
    /// expired; <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> ValidateReadAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Readers.ContainsKey(leaseId));
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the stored writer ID for <paramref name="resource"/>
    /// matches <paramref name="leaseId"/> exactly. The writer-waiting marker is excluded — only a
    /// fully promoted writer satisfies this check. Intended for monitoring self-validation; advisory
    /// because the TTL can expire between this read and any subsequent action.
    /// </summary>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The write-lease ID to check. Must not be <see langword="null"/>, empty, or contain <c>':'</c>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the live writer matches <paramref name="leaseId"/>;
    /// <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="leaseId"/> contains <c>':'</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> ValidateWriteAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(
                state.Writer is { } writer && string.Equals(writer.LeaseId, leaseId, StringComparison.Ordinal)
            );
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one reader currently holds
    /// <paramref name="resource"/>. Point-in-time inspection only — do not use for correctness
    /// decisions; use <see cref="TryAcquireWriteAsync"/> or a held lease's validate methods instead.
    /// </summary>
    /// <remarks>
    /// Expired reader entries are pruned before checking, so the result reflects only live leases
    /// at the moment of the call.
    /// </remarks>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when one or more non-expired readers are present;
    /// <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Readers.Count > 0);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> only when a real writer (not the writer-waiting marker)
    /// currently holds <paramref name="resource"/>. Point-in-time inspection only — do not use for
    /// correctness decisions.
    /// </summary>
    /// <remarks>
    /// Expired writer entries are pruned before checking, so the result reflects only the live
    /// writer at the moment of the call.
    /// </remarks>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a non-expired promoted writer is present;
    /// <see langword="false"/> otherwise (including when only a writer-waiting marker is set).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Writer is not null);
        }
    }

    /// <summary>
    /// Returns the current non-expired reader count for <paramref name="resource"/>. Point-in-time
    /// inspection only — the count can change concurrently; do not use for correctness decisions.
    /// </summary>
    /// <remarks>
    /// Expired reader entries are pruned before counting, so the result reflects only live leases
    /// at the moment of the call.
    /// </remarks>
    /// <param name="resource">The resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>The number of non-expired readers currently holding <paramref name="resource"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(0L);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult((long)state.Readers.Count);
        }
    }

    private DateTimeOffset? _GetExpiration(TimeSpan? ttl)
    {
        return ttl is { } value ? timeProvider.GetUtcNow().Add(value) : null;
    }

    private static DateTimeOffset? _ExtendExpiration(
        DateTimeOffset? existing,
        DateTimeOffset? candidate,
        bool allowInfinite
    )
    {
        if (existing is null)
        {
            return existing;
        }

        // An infinite (null) candidate only wins where infinite leases are allowed (writers); for readers it
        // collapses to "keep the existing finite expiry" so the lease never becomes non-expiring.
        if (candidate is null)
        {
            return allowInfinite ? null : existing;
        }

        return candidate > existing ? candidate : existing;
    }

    private void _PruneExpired(ResourceState state)
    {
        var now = timeProvider.GetUtcNow();

        List<string>? expiredReaders = null;

        foreach (var (leaseId, entry) in state.Readers)
        {
            if (_IsExpired(entry.Expiration, now))
            {
                expiredReaders ??= [];
                expiredReaders.Add(leaseId);
            }
        }

        if (expiredReaders is not null)
        {
            foreach (var leaseId in expiredReaders)
            {
                state.Readers.Remove(leaseId);
            }
        }

        if (state.Writer is { } writer && _IsExpired(writer.Expiration, now))
        {
            state.Writer = null;
        }

        if (state.WriterWaitingMarker is { } marker && _IsExpired(marker.Expiration, now))
        {
            state.WriterWaitingMarker = null;
        }
    }

    private static bool _IsExpired(DateTimeOffset? expiration, DateTimeOffset now)
    {
        return expiration is { } exp && exp <= now;
    }

    private static void _ValidateLockId(string leaseId)
    {
        Argument.IsNotNullOrEmpty(leaseId);
        Ensure.False(
            leaseId.Contains(':', StringComparison.Ordinal),
            "Reader-writer lock ids cannot contain ':' because it conflicts with the writer-waiting suffix delimiter."
        );
    }

    private sealed class ResourceState
    {
        public Dictionary<string, LeaseEntry> Readers { get; } = new(StringComparer.Ordinal);

        public WriterEntry? Writer { get; set; }

        public WriterEntry? WriterWaitingMarker { get; set; }
    }

    private sealed record LeaseEntry(DateTimeOffset? Expiration);

    private sealed record WriterEntry(string LeaseId, DateTimeOffset? Expiration);
}
