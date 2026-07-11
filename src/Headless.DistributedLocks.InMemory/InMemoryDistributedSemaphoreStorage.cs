// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
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
/// checks, acquisitions, and TTL mutations. Fencing tokens are tracked in a separate
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> and incremented atomically on each successful
/// acquisition.
/// </para>
/// </remarks>
/// <param name="timeProvider">The time source used to compute and evaluate TTL expiry timestamps.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InMemoryDistributedSemaphoreStorage(TimeProvider timeProvider) : IDistributedSemaphoreStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _fencingTokens = new(StringComparer.Ordinal);

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

        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            _PruneExpired(state);

            if (state.Holders.Count >= maxCount || state.Holders.ContainsKey(leaseId))
            {
                return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
            }

            state.Holders[leaseId] = new HolderEntry(timeProvider.GetUtcNow().Add(ttl));
            var fencingToken = _fencingTokens.AddOrUpdate(resource, static _ => 1, static (_, current) => current + 1);

            return ValueTask.FromResult(new DistributedLockAcquireResult(Acquired: true, fencingToken));
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

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (!state.Holders.TryGetValue(leaseId, out var existing))
            {
                return ValueTask.FromResult(false);
            }

            var newExpiry = timeProvider.GetUtcNow().Add(ttl);

            if (newExpiry > existing.Expires)
            {
                state.Holders[leaseId] = new HolderEntry(newExpiry);
            }

            return ValueTask.FromResult(true);
        }
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

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Holders.Remove(leaseId));
        }
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

            return ValueTask.FromResult((long)state.Holders.Count);
        }
    }

    private void _PruneExpired(ResourceState state)
    {
        var now = timeProvider.GetUtcNow();

        List<string>? expired = null;

        foreach (var (leaseId, entry) in state.Holders)
        {
            if (entry.Expires <= now)
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

    private sealed class ResourceState
    {
        public Dictionary<string, HolderEntry> Holders { get; } = new(StringComparer.Ordinal);
    }

    private sealed record HolderEntry(DateTimeOffset Expires);
}
