// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.DistributedLocks.InMemory;

/// <summary>
/// In-process semaphore storage backed by per-resource holder dictionaries and
/// <see cref="TimeProvider"/>-based TTL expiry.
/// </summary>
/// <remarks>
/// <para>
/// Suitable for unit tests, local development, and single-instance applications only. This implementation
/// does <b>not</b> coordinate across application instances or processes — it must not be used in
/// horizontally-scaled deployments where multiple processes share the same named resources.
/// </para>
/// <para>
/// Thread-safety is achieved with a per-resource <see langword="lock"/> that serialises holder-count
/// checks, acquisitions, TTL mutations, and the fencing-token increment.
/// </para>
/// <para>
/// Expiry follows the <see cref="TimeProvider"/>'s monotonic timestamp, so a wall-clock step does not move
/// it. A resource entry with no live holder is removed under its lock and flagged, and any caller that then
/// locks the flagged entry re-fetches the current one, so idle resources do not accumulate. Fencing tokens
/// come from one counter per storage instance rather than per resource, so a token issued after an entry is
/// removed and recreated is still greater than every earlier one; tokens are therefore gappy per resource.
/// </para>
/// </remarks>
/// <param name="timeProvider">The time source used to compute and evaluate TTL expiry timestamps.</param>
internal sealed class InMemoryDistributedSemaphoreStorage(TimeProvider timeProvider) : IDistributedSemaphoreStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    /// <summary>The number of per-resource entries currently held in memory, live or not yet pruned.</summary>
    internal int EntryCount => _resources.Count;

    private long _fencingToken;

    /// <summary>
    /// Atomically acquires a semaphore slot on <paramref name="resource"/> for the caller identified
    /// by <paramref name="leaseId"/>, issuing a monotonically increasing fencing token on success.
    /// </summary>
    /// <param name="resource">The semaphore resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">A caller-supplied identifier for this slot. Must not be <see langword="null"/> or empty.</param>
    /// <param name="maxCount">The maximum number of concurrent holders. Must be at least 1.</param>
    /// <param name="ttl">The time-to-live for this slot. Must be greater than <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before acquiring.</param>
    /// <returns>
    /// A <see cref="DistributedLockAcquireResult"/> with <c>Acquired = true</c> and the new fencing
    /// token when a slot is granted; <see cref="DistributedLockAcquireResult.Failed"/> when the
    /// semaphore is at capacity or <paramref name="leaseId"/> already holds a slot.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCount"/> is less than 1 or <paramref name="ttl"/> is not greater than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string leaseId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

            lock (state)
            {
                if (state.Removed)
                {
                    // Another caller dropped this idle entry between GetOrAdd and the lock; a grant here
                    // would live in a state no one else can see.
                    continue;
                }

                _PruneExpired(state);

                if (state.Holders.Count >= maxCount || state.Holders.ContainsKey(leaseId))
                {
                    return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
                }

                state.Holders[leaseId] = new HolderEntry(LeaseDeadline.StartNow(timeProvider, ttl));
                var fencingToken = Interlocked.Increment(ref _fencingToken);

                return ValueTask.FromResult(
                    new DistributedLockAcquireResult(Acquired: true, new LockFencingToken(fencingToken))
                );
            }
        }
    }

    /// <summary>
    /// Atomically refreshes the TTL on the caller's semaphore slot for <paramref name="resource"/>.
    /// The expiry is only advanced, never shortened: if the computed new expiry is not later than
    /// the existing one the update is silently skipped.
    /// </summary>
    /// <param name="resource">The semaphore resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The caller's current slot ID. Must not be <see langword="null"/> or empty.</param>
    /// <param name="ttl">The desired new TTL measured from now. Must be greater than <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the slot was found (even if the expiry was not advanced because
    /// the new value was not later than the existing one); <see langword="false"/> when the slot
    /// does not exist or has already expired.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ttl"/> is not greater than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> TryExtendAsync(
        string resource,
        string leaseId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        while (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                if (state.Removed)
                {
                    continue;
                }

                _PruneExpired(state);

                if (!state.Holders.TryGetValue(leaseId, out var existing))
                {
                    _RemoveIfIdle(resource, state);

                    return ValueTask.FromResult(false);
                }

                var candidate = LeaseDeadline.StartNow(timeProvider, ttl);

                if (candidate.IsLaterThan(existing.Deadline, timeProvider))
                {
                    state.Holders[leaseId] = new HolderEntry(candidate);
                }

                return ValueTask.FromResult(true);
            }
        }

        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="leaseId"/> is currently present in the
    /// holder set for <paramref name="resource"/> and has not expired. Intended for monitoring
    /// self-validation — callers must treat the result as advisory.
    /// </summary>
    /// <param name="resource">The semaphore resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The slot ID to check. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a live slot exists for <paramref name="leaseId"/>;
    /// <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> ValidateAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);
            _RemoveIfIdle(resource, state);

            return ValueTask.FromResult(state.Holders.ContainsKey(leaseId));
        }
    }

    /// <summary>
    /// Releases the caller's semaphore slot for <paramref name="resource"/> by removing
    /// <paramref name="leaseId"/> from the holder set.
    /// </summary>
    /// <param name="resource">The semaphore resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">The slot ID to release. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="leaseId"/> was found in the holder set and
    /// removed; <see langword="false"/> when the resource does not exist, the slot has already
    /// expired, or <paramref name="leaseId"/> was never a holder.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        while (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                if (state.Removed)
                {
                    continue;
                }

                _PruneExpired(state);
                var released = state.Holders.Remove(leaseId);
                _RemoveIfIdle(resource, state);

                return ValueTask.FromResult(released);
            }
        }

        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// Returns the number of non-expired slots currently held on <paramref name="resource"/>.
    /// Point-in-time inspection only — the count can change concurrently.
    /// </summary>
    /// <param name="resource">The semaphore resource name. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>The current live holder count; 0 when the resource does not exist or has no holders.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default)
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
            _RemoveIfIdle(resource, state);

            return ValueTask.FromResult((long)state.Holders.Count);
        }
    }

    private void _PruneExpired(ResourceState state)
    {
        List<string>? expired = null;

        foreach (var (leaseId, entry) in state.Holders)
        {
            if (entry.Deadline.IsExpired(timeProvider))
            {
                expired ??= [];
                expired.Add(leaseId);
            }
        }

        if (expired is not null)
        {
            foreach (var leaseId in expired)
            {
                state.Holders.Remove(leaseId);
            }
        }
    }

    /// <summary>Drops an entry that holds no slot. The caller must hold the lock on <paramref name="state"/>.</summary>
    private void _RemoveIfIdle(string resource, ResourceState state)
    {
        if (state.Holders.Count > 0 || state.Removed)
        {
            return;
        }

        // Flag first so a caller that already fetched this instance retries against the dictionary instead
        // of granting into a detached state. Removal is by key and instance, so a newer entry is never hit.
        state.Removed = true;
        _resources.TryRemove(new KeyValuePair<string, ResourceState>(resource, state));
    }

    private sealed class ResourceState
    {
        public Dictionary<string, HolderEntry> Holders { get; } = new(StringComparer.Ordinal);

        public bool Removed { get; set; }
    }

    private sealed record HolderEntry(LeaseDeadline Deadline);
}
