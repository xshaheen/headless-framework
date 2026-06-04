// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.DistributedLocks.InMemory;

/// <summary>Process-local lock storage for tests, local development, and single-instance apps.</summary>
/// <remarks>This storage is in-process only. It does not coordinate across application instances.</remarks>
[PublicAPI]
public sealed class InMemoryDistributedLockStorage(TimeProvider timeProvider) : IDistributedLockStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    public ValueTask<DistributedLockAcquireResult> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNullOrEmpty(lockId);
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

            state.Entry = new LockEntry(lockId, _GetExpiration(ttl));
            state.FencingToken++;

            return ValueTask.FromResult(new DistributedLockAcquireResult(Acquired: true, state.FencingToken));
        }
    }

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

            if (state.Entry is not { } existing || !string.Equals(existing.LockId, expectedId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            state.Entry = new LockEntry(newId, _GetExpiration(newTtl));

            return ValueTask.FromResult(true);
        }
    }

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

            if (state.Entry is not { } existing || !string.Equals(existing.LockId, expectedId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            state.Entry = null;

            return ValueTask.FromResult(true);
        }
    }

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

            return ValueTask.FromResult(state.Entry?.LockId);
        }
    }

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
                    result[key] = entry.LockId;
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public ValueTask<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>> GetAllWithExpirationByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var result = new Dictionary<string, (string LockId, TimeSpan? Ttl)>(StringComparer.Ordinal);

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
                var ttl = remaining > TimeSpan.Zero ? remaining : remaining.HasValue ? TimeSpan.Zero : null;

                result[key] = (entry.LockId, ttl);
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>>(result);
    }

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

    private sealed record LockEntry(string LockId, DateTimeOffset? Expiration);
}
