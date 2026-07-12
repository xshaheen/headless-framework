// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using Headless.Checks;

namespace Headless.DistributedLocks.InMemory;

/// <summary>
/// In-process exclusive-lock storage backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/> and
/// <see cref="TimeProvider"/>-based TTL expiry.
/// </summary>
/// <remarks>
/// <para>
/// Suitable for unit tests, local development, and single-instance applications only. This implementation
/// does <b>not</b> coordinate across application instances or processes — it must not be used in
/// horizontally-scaled deployments where multiple processes share the same named resources.
/// </para>
/// <para>
/// Thread-safety is achieved with a per-resource <see langword="lock"/> that serialises the prune,
/// free-check, grant, and fencing-token increment so that the granted fencing token is always monotonic
/// with respect to grant order.
/// </para>
/// </remarks>
/// <param name="timeProvider">The time source used to compute and evaluate TTL expiry timestamps.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InMemoryDistributedLockStorage(TimeProvider timeProvider) : IDistributedLockStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    /// <summary>
    /// Atomically grants an exclusive lock on <paramref name="key"/> to the caller identified by
    /// <paramref name="leaseId"/>, issuing a monotonically increasing fencing token on success.
    /// </summary>
    /// <param name="key">The resource key to lock. Must not be <see langword="null"/> or empty.</param>
    /// <param name="leaseId">A caller-supplied identifier for this lease. Must not be <see langword="null"/> or empty.</param>
    /// <param name="ttl">
    /// Optional time-to-live for the lock. When <see langword="null"/> the lock does not expire
    /// until explicitly released via <see cref="RemoveIfEqualAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before acquiring.</param>
    /// <returns>
    /// A <see cref="DistributedLockAcquireResult"/> with <c>Acquired = true</c> and the new fencing
    /// token when the lock is granted; <see cref="DistributedLockAcquireResult.Failed"/> when the
    /// resource is already held.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> or <paramref name="leaseId"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<DistributedLockAcquireResult> InsertAsync(
        string key,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _resources.GetOrAdd(key, static _ => new ResourceState());

        // Serialize prune + free-check + grant + fence-increment so the granted fencing token always
        // corresponds to the live holder and is monotonic with respect to grant order. Splitting these
        // into separate ConcurrentDictionary operations let a stale caller (preempted across a TTL
        // expiry) hand back a token above the live holder for a key it no longer owned.
        lock (state)
        {
            _PruneExpired(state);

            if (state.Entry is not null)
            {
                return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
            }

            state.Entry = new LockEntry(leaseId, _GetExpiration(ttl));
            state.FencingToken++;

            return ValueTask.FromResult(new DistributedLockAcquireResult(Acquired: true, state.FencingToken));
        }
    }

    /// <summary>
    /// Atomically replaces the lease on <paramref name="key"/> when the currently stored lease ID
    /// matches <paramref name="expectedId"/> exactly, updating it to <paramref name="newId"/> and
    /// optionally resetting the TTL. Used by the lock-renewal path.
    /// </summary>
    /// <param name="key">The resource key. Must not be <see langword="null"/> or empty.</param>
    /// <param name="expectedId">The lease ID the caller currently holds. Must not be <see langword="null"/> or empty.</param>
    /// <param name="newId">The replacement lease ID. Must not be <see langword="null"/> or empty.</param>
    /// <param name="newTtl">
    /// Optional new TTL. When <see langword="null"/> the existing expiration is preserved.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the stored ID matched <paramref name="expectedId"/> and the entry
    /// was updated; <see langword="false"/> when the key does not exist, has expired, or the stored
    /// ID does not match.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/>, <paramref name="expectedId"/>, or <paramref name="newId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/>, <paramref name="expectedId"/>, or <paramref name="newId"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNullOrEmpty(expectedId);
        Argument.IsNotNullOrEmpty(newId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(key, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (
                state.Entry is not { } existing
                || !string.Equals(existing.LeaseId, expectedId, StringComparison.Ordinal)
            )
            {
                return ValueTask.FromResult(false);
            }

            var expiration = newTtl.HasValue ? _GetExpiration(newTtl) : existing.Expiration;
            state.Entry = new LockEntry(newId, expiration);

            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Atomically releases the lock on <paramref name="key"/> when the currently stored lease ID
    /// matches <paramref name="expectedId"/> exactly. Used by the lock-release path to prevent
    /// a caller from releasing a lock it no longer owns.
    /// </summary>
    /// <param name="key">The resource key. Must not be <see langword="null"/> or empty.</param>
    /// <param name="expectedId">The lease ID the caller holds. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the stored ID matched <paramref name="expectedId"/> and the entry
    /// was removed; <see langword="false"/> when the key does not exist, has expired, or the stored
    /// ID does not match.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="expectedId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> or <paramref name="expectedId"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNullOrEmpty(expectedId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(key, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (
                state.Entry is not { } existing
                || !string.Equals(existing.LeaseId, expectedId, StringComparison.Ordinal)
            )
            {
                return ValueTask.FromResult(false);
            }

            state.Entry = null;

            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Returns the remaining TTL for the lock held on <paramref name="key"/>, or <see langword="null"/> when
    /// the key does not exist, has no expiry, or has already expired.
    /// </summary>
    /// <param name="key">The resource key to query. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// The remaining time-to-live as a <see cref="TimeSpan"/> (clamped to <see cref="TimeSpan.Zero"/> if
    /// the entry is at or past its expiry), or <see langword="null"/> when the key has no expiry or does
    /// not exist.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(key, out var state))
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (state.Entry is not { Expiration: { } expiration })
            {
                return ValueTask.FromResult<TimeSpan?>(null);
            }

            var remaining = expiration - timeProvider.GetUtcNow();

            return ValueTask.FromResult<TimeSpan?>(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when a non-expired lock entry exists for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The resource key to check. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a live (non-expired) lock entry is present for <paramref name="key"/>;
    /// <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(key, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Entry is not null);
        }
    }

    /// <summary>Gets the lease ID stored for <paramref name="key"/>, or <see langword="null"/> when the key
    /// does not exist or has expired.</summary>
    /// <param name="key">The resource key to query. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// The current lease ID string, or <see langword="null"/> when no live entry exists for
    /// <paramref name="key"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(key, out var state))
        {
            return ValueTask.FromResult<string?>(null);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Entry?.LeaseId);
        }
    }

    /// <summary>Gets all live lock entries whose keys begin with <paramref name="prefix"/>.</summary>
    /// <remarks>
    /// Expired entries are pruned per-key during iteration. The snapshot is point-in-time; the
    /// dictionary may change concurrently after the call returns.
    /// </remarks>
    /// <param name="prefix">
    /// The key prefix to filter by. A <see langword="null"/> or empty value matches all keys.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// A read-only dictionary mapping each matching key to its current lease ID. Never
    /// <see langword="null"/>; empty when no live entries match the prefix.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, state) in _resources)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            lock (state)
            {
                _PruneExpired(state);

                if (state.Entry is { } entry)
                {
                    result[key] = entry.LeaseId;
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    /// <summary>
    /// Gets all live lock entries whose keys begin with <paramref name="prefix"/>, including their
    /// remaining TTL.
    /// </summary>
    /// <remarks>
    /// Expired entries are pruned per-key during iteration. The snapshot is point-in-time; the
    /// dictionary may change concurrently after the call returns. The TTL in each value is clamped
    /// to <see cref="TimeSpan.Zero"/> when the entry is at or past its expiry but has not yet been
    /// pruned; <see langword="null"/> means the entry has no expiry.
    /// </remarks>
    /// <param name="prefix">
    /// The key prefix to filter by. A <see langword="null"/> or empty value matches all keys.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>
    /// A read-only dictionary mapping each matching key to a tuple of its lease ID and remaining
    /// TTL. Never <see langword="null"/>; empty when no live entries match the prefix.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<IReadOnlyDictionary<string, (string LeaseId, TimeSpan? Ttl)>> GetAllWithExpirationByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var result = new Dictionary<string, (string LeaseId, TimeSpan? Ttl)>(StringComparer.Ordinal);

        foreach (var (key, state) in _resources)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            lock (state)
            {
                _PruneExpired(state);

                if (state.Entry is not { } entry)
                {
                    continue;
                }

                var remaining = entry.Expiration is { } expiration ? expiration - now : (TimeSpan?)null;
                var ttl =
                    remaining > TimeSpan.Zero ? remaining
                    : remaining.HasValue ? TimeSpan.Zero
                    : null;

                result[key] = (entry.LeaseId, ttl);
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, (string LeaseId, TimeSpan? Ttl)>>(result);
    }

    /// <summary>Gets the count of live lock entries whose keys match the given <paramref name="prefix"/>.</summary>
    /// <remarks>
    /// Expired entries are pruned per-key during counting. The result is a point-in-time snapshot and
    /// may change concurrently.
    /// </remarks>
    /// <param name="prefix">
    /// The key prefix to filter by. An empty string (the default) counts all live entries.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation before the operation.</param>
    /// <returns>The number of live lock entries matching <paramref name="prefix"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var count = 0L;

        foreach (var (key, state) in _resources)
        {
            if (!string.IsNullOrEmpty(prefix) && !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            lock (state)
            {
                _PruneExpired(state);

                if (state.Entry is not null)
                {
                    count++;
                }
            }
        }

        return ValueTask.FromResult(count);
    }

    private DateTimeOffset? _GetExpiration(TimeSpan? ttl)
    {
        return ttl is { } value ? timeProvider.GetUtcNow().Add(value) : null;
    }

    private void _PruneExpired(ResourceState state)
    {
        if (state.Entry is { } entry && _IsExpired(entry, timeProvider.GetUtcNow()))
        {
            state.Entry = null;
        }
    }

    private static bool _IsExpired(LockEntry entry, DateTimeOffset now)
    {
        return entry.Expiration is { } expiration && expiration <= now;
    }

    private sealed class ResourceState
    {
        public LockEntry? Entry { get; set; }

        public long FencingToken { get; set; }
    }

    private sealed record LockEntry(string LeaseId, DateTimeOffset? Expiration);
}
